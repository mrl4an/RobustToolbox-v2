using System;
using System.IO;
using Robust.Shared.Audio.Sources;

namespace Robust.Client.Audio;

/// <summary>
/// Public audio API for stuff that can't go through <see cref="AudioSystem"/>
/// </summary>
[NotContentImplementable]
public interface IAudioManager
{
    IAudioSource? CreateAudioSource(AudioStream stream);

    AudioStream LoadAudioOggVorbis(Stream stream, string? name = null);

    AudioStream LoadAudioWav(Stream stream, string? name = null);

    AudioStream LoadAudioRaw(ReadOnlySpan<short> samples, int channels, int sampleRate, string? name = null);

    /// <summary>
    /// Opens a long piece of music (MP3 or Ogg Vorbis) that is read and decoded piece by piece while it plays: see <see cref="IMusicStream"/>.
    /// The stream is owned by the result and closed with it. Null (and an error in the log, never an exception) if it cannot be opened or there is no
    /// sound device.
    /// </summary>
    IMusicStream? OpenMusic(Stream stream, string? name = null);

    void SetMasterGain(float gain);
}
