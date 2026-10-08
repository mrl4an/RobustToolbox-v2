using System;
using System.IO;
using NLayer;
using NVorbis;

namespace Robust.Client.Audio.Music;

/// <summary>Reads audio piece by piece as 32 bit floats, interleaved by channel. Used by one thread only (the decoding thread of a <see cref="MusicStream"/>).</summary>
internal interface IMusicDecoder : IDisposable
{
    int SampleRate { get; }
    int Channels { get; }

    /// <summary>The length of the piece, or zero if it cannot be told.</summary>
    TimeSpan GetLength();

    /// <summary>Reads up to <paramref name="buffer"/>.Length values; returns how many were read, 0 at the end.</summary>
    int Read(Span<float> buffer);
}

internal sealed class Mp3MusicDecoder : IMusicDecoder
{
    private readonly MpegFile _file;
    private readonly Stream _stream;
    private float[] _scratch = new float[8192];

    public Mp3MusicDecoder(Stream stream)
    {
        _stream = stream;
        _file = new MpegFile(stream);
    }

    public int SampleRate => _file.SampleRate;
    public int Channels => _file.Channels;

    public TimeSpan GetLength() => _file.Duration;

    public int Read(Span<float> buffer)
    {
        if (_scratch.Length < buffer.Length)
            _scratch = new float[buffer.Length];

        var read = _file.ReadSamples(_scratch, 0, buffer.Length);
        _scratch.AsSpan(0, read).CopyTo(buffer);
        return read;
    }

    public void Dispose()
    {
        _file.Dispose();
        _stream.Dispose();
    }
}

internal sealed class OggMusicDecoder : IMusicDecoder
{
    private readonly VorbisReader _reader;

    public OggMusicDecoder(Stream stream)
    {
        _reader = new VorbisReader(stream, true);
        _reader.Initialize();
    }

    public int SampleRate => _reader.SampleRate;
    public int Channels => _reader.Channels;

    public TimeSpan GetLength() => _reader.TotalTime;

    public int Read(Span<float> buffer) => _reader.ReadSamples(buffer);

    public void Dispose() => _reader.Dispose();
}
