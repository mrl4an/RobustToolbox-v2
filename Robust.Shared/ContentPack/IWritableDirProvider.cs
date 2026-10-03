using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;
using Robust.Shared.Utility;

namespace Robust.Shared.ContentPack
{
    /// <summary>
    /// File and directory operations that are restricted to one root folder.
    /// </summary>
    [PublicAPI]
    [NotContentImplementable]
    public interface IWritableDirProvider
    {
        /// <summary>
        /// The root path of this provider.
        /// Can be null if it's a virtual provider or the path is protected (e.g. on the client).
        /// </summary>
        string? RootDir { get; }

        /// <summary>
        ///     Makes sure a directory exists at the given path, creating any missing parent directories as well.
        ///     If the directory already exists, nothing happens.
        /// </summary>
        /// <param name="path">Rooted virtual path of the directory to create.</param>
        void CreateDir(ResPath path);

        /// <summary>
        ///     Removes whatever is located at the given path. A directory is removed together with all of its contents,
        ///     a file is deleted. If nothing exists at the path, the call does nothing.
        /// </summary>
        /// <param name="path">Rooted virtual path of the file or directory to remove.</param>
        void Delete(ResPath path);

        /// <summary>
        ///     Checks whether a file or a directory is present at the given path.
        /// </summary>
        /// <param name="path">Rooted virtual path to check.</param>
        /// <returns>True if a file or directory exists at the path, false if neither does.</returns>
        bool Exists(ResPath path);

        /// <summary>
        ///     Searches the root of this provider for files and directories whose names match a wildcard pattern.
        /// </summary>
        /// <param name="pattern">Wildcard pattern (<c>*</c> and <c>?</c>) that entry names are matched against.</param>
        /// <param name="recursive">
        ///     If true (the default), subfolders are searched too. If false, only the top level is searched.
        /// </param>
        /// <returns>
        ///     A tuple with the matching files and the matching directories (including empty ones).
        ///     All returned paths are rooted virtual paths relative to the root of this provider.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        ///     The pattern tries to leave the root (it contains two consecutive dots).
        /// </exception>
        (IEnumerable<ResPath> files, IEnumerable<ResPath> directories) Find(string pattern,
            bool recursive = true);

        IEnumerable<string> DirectoryEntries(ResPath path);

        /// <summary>
        ///     Checks whether the given path names a directory, as opposed to a file.
        /// </summary>
        /// <param name="path">Rooted virtual path to check.</param>
        /// <returns>True if the path is an existing directory.</returns>
        bool IsDir(ResPath path);

        /// <summary>
        ///     Opens or creates the file at the given path, depending on <paramref name="fileMode"/>,
        ///     and returns a stream for it. The caller owns the stream and must dispose it.
        /// </summary>
        /// <param name="path">Rooted virtual path of the file.</param>
        /// <param name="fileMode">Whether to open, create, truncate or append to the file.</param>
        /// <param name="access">Which operations the returned stream allows: read, write or both.</param>
        /// <param name="share">What other users of the same file are allowed to do while it is open.</param>
        /// <returns>A stream for the file.</returns>
        /// <exception cref="FileNotFoundException">
        ///     The file does not exist and <paramref name="fileMode"/> requires it to exist.
        /// </exception>
        Stream Open(ResPath path, FileMode fileMode, FileAccess access, FileShare share);

        /// <summary>
        ///     Convenience overload of <see cref="Open(ResPath, FileMode, FileAccess, FileShare)"/> that picks the
        ///     access and sharing itself. With <see cref="FileMode.Append"/> the stream is write-only; with every other
        ///     mode it allows reading and writing. The file is always opened for exclusive use
        ///     (<see cref="FileShare.None"/>).
        /// </summary>
        /// <param name="path">Rooted virtual path of the file.</param>
        /// <param name="fileMode">Whether to open, create, truncate or append to the file.</param>
        /// <returns>A stream for the file. The caller owns it and must dispose it.</returns>
        Stream Open(ResPath path, FileMode fileMode)
        {
            return Open(
                path,
                fileMode,
                fileMode == FileMode.Append ? FileAccess.Write : FileAccess.ReadWrite,
                FileShare.None);
        }

        /// <summary>
        /// Returns a new <see cref="IWritableDirProvider"/> that points to a subdirectory.
        /// </summary>
        /// <param name="path">Path of directory to open.</param>
        IWritableDirProvider OpenSubdirectory(ResPath path);

        /// <summary>
        ///     Renames or moves a file to a new path. Both paths are inside the root of this provider.
        /// </summary>
        /// <param name="oldPath">Rooted virtual path of the existing file.</param>
        /// <param name="newPath">Rooted virtual path the file should have afterwards.</param>
        void Rename(ResPath oldPath, ResPath newPath);

        /// <summary>
        /// Opens up an external file window for some subdirectory. For example, this could be used to create a button
        /// that opens up the screenshot directory using the operating system's file explorer.
        /// </summary>
        void OpenOsWindow(ResPath path);
    }
}
