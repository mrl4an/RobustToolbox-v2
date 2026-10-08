using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenTK.Audio.OpenAL;
using Robust.Shared.Utility;

namespace Robust.Client.Audio.Music;

/// <summary>
/// The implementation of <see cref="IMusicStream"/>. A thread of its own reads and decodes the file in pieces of half a second into a queue (a few
/// pieces ahead, so a slow frame does not make the music stutter); the main thread, in <see cref="Update"/>, hands the pieces to OpenAL buffers one
/// by one and takes back the buffers that have been played. Only the main thread touches OpenAL; the decoding thread only touches the decoder.
/// </summary>
internal sealed class MusicStream : IMusicStream
{
    /// <summary>How many OpenAL buffers go round (each holds one piece).</summary>
    private const int BufferCount = 4;

    /// <summary>How long a piece is, seconds.</summary>
    private const float PieceSeconds = 0.5f;

    /// <summary>How many decoded pieces wait for a free buffer at most.</summary>
    private const int MaxWaiting = 4;

    private readonly AudioManager _manager;
    private readonly string _name;
    private readonly ConcurrentQueue<short[]> _waiting = new();
    private readonly CancellationTokenSource _cancel = new();
    private readonly int _source;
    private readonly int[] _buffers = new int[BufferCount];
    private readonly int[] _bufferFrames = new int[BufferCount];
    private readonly ALFormat _format;
    private readonly int _sampleRate;
    private readonly int _channels;

    private readonly Stack<int> _free = new();
    private float _volume = 1f;
    private bool _playRequested;
    private bool _disposed;
    private bool _finished;
    private long _playedFrames;
    private long _lengthTicks;
    private volatile bool _decodingDone;
    private volatile string? _error;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(float.IsFinite(value) ? value : 0f, 0f, 1f);
            if (!_disposed)
                AL.Source(_source, ALSourcef.Gain, _volume);
        }
    }

    public TimeSpan Length => TimeSpan.FromTicks(Interlocked.Read(ref _lengthTicks));

    public TimeSpan Position
    {
        get
        {
            if (_disposed)
                return TimeSpan.Zero;

            AL.GetSource(_source, ALSourcef.SecOffset, out var inBuffer);
            return TimeSpan.FromSeconds(_playedFrames / (double) _sampleRate + inBuffer);
        }
    }

    public bool Finished => _finished;

    public string? Error => _error;

    private MusicStream(AudioManager manager, IMusicDecoder decoder, string name)
    {
        _manager = manager;
        _name = name;
        _sampleRate = decoder.SampleRate;
        _channels = decoder.Channels;

        _format = _channels switch
        {
            1 => ALFormat.Mono16,
            2 => ALFormat.Stereo16,
            _ => throw new NotSupportedException($"Music with {_channels} channels (only mono and stereo can be played)"),
        };

        _source = AL.GenSource();
        if (!AL.IsSource(_source))
            throw new InvalidOperationException("No free sound source for the music");

        AL.GenBuffers(_buffers);
        foreach (var buffer in _buffers)
        {
            _free.Push(buffer);
        }

        // Plays to the listener, not from a place in the world.
        AL.Source(_source, ALSourceb.SourceRelative, true);
        AL.Source(_source, ALSource3f.Position, 0f, 0f, 0f);
        AL.Source(_source, ALSourcef.RolloffFactor, 0f);
        AL.Source(_source, ALSourcef.Gain, _volume);
        _manager._checkAlError();

        Task.Factory.StartNew(() => Decode(decoder), _cancel.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    /// <summary>
    /// Opens a piece of music from a stream (MP3 or Ogg Vorbis, told apart by the first bytes) and starts reading it. The stream is owned by the result
    /// from now on, and is closed when it is disposed (or here, when it cannot be opened, with the exception).
    /// </summary>
    public static MusicStream Open(AudioManager manager, Stream stream, string name)
    {
        IMusicDecoder? decoder = null;
        try
        {
            if (!stream.CanSeek)
                stream = stream.CopyToMemoryStream();

            var head = new byte[4];
            var read = stream.Read(head, 0, head.Length);
            stream.Seek(0, SeekOrigin.Begin);
            if (read < head.Length)
                throw new InvalidDataException("The file is too short to be music");

            if (head[0] == 'O' && head[1] == 'g' && head[2] == 'g' && head[3] == 'S')
                decoder = new OggMusicDecoder(stream);
            else if ((head[0] == 'I' && head[1] == 'D' && head[2] == '3') || (head[0] == 0xFF && (head[1] & 0xE0) == 0xE0))
                decoder = new Mp3MusicDecoder(stream);
            else
                throw new NotSupportedException("Not an MP3 or Ogg Vorbis file");

            return new MusicStream(manager, decoder, name);
        }
        catch
        {
            decoder?.Dispose();
            stream.Dispose();
            throw;
        }
    }

    /// <summary>The decoding thread: reads the file piece by piece until it ends, is cancelled, or fails.</summary>
    private void Decode(IMusicDecoder decoder)
    {
        try
        {
            // The length may have to be found by reading through the file; that is done here, not on the main thread.
            Interlocked.Exchange(ref _lengthTicks, decoder.GetLength().Ticks);

            var values = Math.Max(1, (int) (_sampleRate * PieceSeconds)) * _channels;
            var floats = new float[values];

            while (!_cancel.IsCancellationRequested)
            {
                if (_waiting.Count >= MaxWaiting)
                {
                    Thread.Sleep(20);
                    continue;
                }

                var count = decoder.Read(floats);
                if (count <= 0)
                    break;

                var piece = new short[count - count % _channels];
                for (var i = 0; i < piece.Length; i++)
                {
                    piece[i] = (short) (Math.Clamp(floats[i], -1f, 1f) * short.MaxValue);
                }

                if (piece.Length > 0)
                    _waiting.Enqueue(piece);
            }
        }
        catch (Exception e)
        {
            _error = $"Cannot read the music {_name}: {e.Message}";
        }
        finally
        {
            decoder.Dispose();
            _decodingDone = true;
        }
    }

    public void Play()
    {
        if (_disposed || _finished)
            return;

        _playRequested = true;
        Update();
    }

    public void Update()
    {
        if (_disposed || _finished)
            return;

        try
        {
            // Take back the buffers that have been played.
            AL.GetSource(_source, ALGetSourcei.BuffersProcessed, out var processed);
            for (; processed > 0; processed--)
            {
                var buffer = AL.SourceUnqueueBuffer(_source);
                _free.Push(buffer);
                _playedFrames += _bufferFrames[Array.IndexOf(_buffers, buffer)];
            }

            // Fill the free ones with what has been decoded.
            while (_free.Count > 0 && _waiting.TryDequeue(out var piece))
            {
                var buffer = _free.Pop();
                unsafe
                {
                    fixed (short* ptr = piece)
                    {
                        AL.BufferData(buffer, _format, (IntPtr) ptr, piece.Length * sizeof(short), _sampleRate);
                    }
                }

                _bufferFrames[Array.IndexOf(_buffers, buffer)] = piece.Length / _channels;
                AL.SourceQueueBuffer(_source, buffer);
            }

            _manager._checkAlError();

            // It plays while there is something to play; a source that has run dry (the file was slow to read) stops by itself and is started again.
            var state = (ALSourceState) AL.GetSource(_source, ALGetSourcei.SourceState);
            var queued = BufferCount - _free.Count;
            if (_playRequested && state != ALSourceState.Playing && queued > 0)
                AL.SourcePlay(_source);

            if (_playRequested && queued == 0 && _waiting.IsEmpty && _decodingDone)
                _finished = true;
        }
        catch (Exception e)
        {
            _error ??= $"Cannot play the music {_name}: {e.Message}";
            _finished = true;
        }

        if (_error != null)
            _finished = true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _finished = true;
        _cancel.Cancel();

        // The decoding thread closes the decoder (and the file) itself when it sees the cancel.
        AL.SourceStop(_source);
        AL.DeleteSource(_source);
        AL.DeleteBuffers(_buffers);
        _manager._checkAlError();
    }
}
