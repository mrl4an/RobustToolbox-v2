using System;

namespace Robust.Client.Audio;

/// <summary>
/// A long piece of audio (music) that is read and decoded piece by piece on a thread of its own while it plays, so that only a couple of seconds of it are
/// ever in memory and opening it never stalls a frame (unlike <see cref="AudioStream"/>, which is decoded whole). MP3 and Ogg Vorbis files can be played.
/// It plays to the listener (no position, no distance falloff), at the master volume of the game.
/// Get one from <see cref="IAudioManager.OpenMusic"/>; call <see cref="Update"/> every frame from the main thread; <see cref="Dispose"/> it when done (it
/// has no finalizer and it must be disposed on the main thread). Nothing here throws once it is open: what goes wrong is told by <see cref="Error"/> and
/// the stream is then <see cref="Finished"/>.
/// </summary>
[NotContentImplementable]
public interface IMusicStream : IDisposable
{
    /// <summary>Volume, 0 (silent) to 1 (as recorded).</summary>
    float Volume { get; set; }

    /// <summary>The length of the piece; zero until it is known (the file is measured on the decoding thread).</summary>
    TimeSpan Length { get; }

    /// <summary>How far into the piece it has played.</summary>
    TimeSpan Position { get; }

    /// <summary>It has played to the end, or it failed (see <see cref="Error"/>). A finished stream has nothing more to do.</summary>
    bool Finished { get; }

    /// <summary>What went wrong while reading or playing it; null if nothing.</summary>
    string? Error { get; }

    /// <summary>Starts it playing (it plays from the beginning when it is opened and played for the first time).</summary>
    void Play();

    /// <summary>Hands what has been decoded to the sound device and notices when it has ended. Call it every frame, on the main thread.</summary>
    void Update();
}
