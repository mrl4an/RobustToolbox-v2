using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Robust.Shared.Utility;

namespace Robust.Shared.ContentPack
{
    internal interface IResourceManagerInternal : IResourceManager
    {
        /// <summary>
        ///     Performs one-time startup of the resource manager. Call it before anything is mounted.
        /// </summary>
        /// <param name="userData">
        ///     Path of an on-disk directory that backs the writable user-data area.
        ///     If <see langword="null"/>, a purely in-memory (virtual) writable area is used instead.
        /// </param>
        /// <param name="hideUserDataDir">
        ///     If true, the real root directory of the writable provider is not exposed
        ///     through the <c>UserData</c> property of <see cref="IResourceManager"/>.
        /// </param>
        void Initialize(string? userData, bool hideUserDataDir);

        /// <summary>
        ///     Mounts a single stream as a content file. Useful for unit testing.
        /// </summary>
        /// <param name="stream">The stream to mount.</param>
        /// <param name="path">The path that the file will be mounted at.</param>
        void MountStreamAt(MemoryStream stream, ResPath path);

        /// <summary>
        ///     Mounts the content pack that the configuration names as the default, at the standard
        ///     (root) prefix. Takes no parameters. If the configuration names no pack, nothing
        ///     harmful happens: a warning is logged and no exception is thrown.
        /// </summary>
        void MountDefaultContentPack();

        /// <summary>
        ///     Mounts a zip-style content pack found on disk, adding its files to the virtual file
        ///     system beneath <paramref name="prefix"/>.
        /// </summary>
        /// <param name="pack">
        ///     Path of the pack file. A relative path is resolved against the directory of the
        ///     running executable.
        /// </param>
        /// <param name="prefix">
        ///     Rooted virtual path under which the pack's files appear. If <see langword="null"/>,
        ///     the files appear at the virtual root.
        /// </param>
        /// <exception cref="FileNotFoundException">No file exists at <paramref name="pack"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="prefix"/> is supplied but is not a rooted path.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="pack"/> is <see langword="null"/>.</exception>
        void MountContentPack(string pack, ResPath? prefix = null);

        void MountContentPack(Stream zipStream, ResPath? prefix = null);

        /// <summary>
        ///     Adds a plain on-disk directory as a source of content files, exposing its contents in
        ///     the virtual file system beneath <paramref name="prefix"/>.
        /// </summary>
        /// <param name="path">
        ///     Path of the directory. A relative path is resolved against the directory of the
        ///     running executable.
        /// </param>
        /// <param name="prefix">
        ///     Rooted virtual path under which the contents appear. If <see langword="null"/>,
        ///     the contents appear at the virtual root.
        /// </param>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        /// <exception cref="ArgumentException"><paramref name="prefix"/> is supplied but is not a rooted path.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
        void MountContentDirectory(string path, ResPath? prefix = null);

        /// <summary>
        ///     Attempts to get an on-disk path absolute file path for the specified resource path.
        /// </summary>
        /// <remarks>
        /// <para>
        ///     This only works if the resource is mounted as a direct directory,
        ///     so this obviously fails if the resource is mounted in another way such as a zip file.
        /// </para>
        /// <para>
        ///     This can be used for optimizations such as assembly loading, where an on-disk path is better.
        /// </para>
        /// </remarks>
        bool TryGetDiskFilePath(ResPath path, [NotNullWhen(true)] out string? diskPath);

        new IEnumerable<string> GetContentRoots();
    }
}
