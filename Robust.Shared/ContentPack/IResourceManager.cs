using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using Robust.Shared.Utility;
using YamlDotNet.RepresentationModel;

namespace Robust.Shared.ContentPack
{
    /// <summary>
    ///     Gives access to the virtual file system that merges resource folders and content packs.
    /// </summary>
    [NotContentImplementable]
    public interface IResourceManager
    {
        /// <summary>
        ///     Entry point to the writable user-data area. This is the only place where content may create,
        ///     modify or delete files; it is separate from the read-only mounted content roots.
        /// </summary>
        /// <remarks>
        ///     Once the manager has been initialized, the same provider instance is returned for its whole lifetime.
        /// </remarks>
        IWritableDirProvider UserData { get; }

        /// <summary>
        ///     Provides a way to mount a <see cref="IContentRoot"/> implementation to the VFS.
        /// </summary>
        /// <param name="prefix"></param>
        /// <param name="loader"></param>
        void AddRoot(ResPath prefix, IContentRoot loader);

        /// <summary>
        ///     Opens a file from the mounted content roots and returns a readable stream over its contents.
        ///     If several roots contain the file, the earliest mounted one wins.
        /// </summary>
        /// <remarks>
        ///     The returned stream is a memory stream. The caller owns it and should dispose it.
        ///     To avoid an exception for a missing file, use <see cref="TryContentFileRead(ResPath?, out Stream?)"/>.
        /// </remarks>
        /// <param name="path">Absolute (rooted) VFS path of the file to open.</param>
        /// <returns>A readable stream with the file contents.</returns>
        /// <exception cref="FileNotFoundException">No mounted content root contains the file.</exception>
        /// <exception cref="ArgumentException"><paramref name="path"/> is not rooted.</exception>
        Stream ContentFileRead(ResPath path);

        /// <summary>
        ///     String overload of <see cref="ContentFileRead(ResPath)"/>. The text is interpreted as a
        ///     <see cref="ResPath"/> and the same behavior applies.
        /// </summary>
        /// <param name="path">Absolute (rooted) VFS path of the file to open.</param>
        /// <returns>A readable stream with the file contents; the caller should dispose it.</returns>
        /// <exception cref="FileNotFoundException">No mounted content root contains the file.</exception>
        /// <exception cref="ArgumentException"><paramref name="path"/> is not rooted.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
        Stream ContentFileRead(string path);

        /// <summary>
        ///     Checks whether any mounted content root contains a file at the given path.
        /// </summary>
        /// <param name="path">Absolute (rooted) VFS path to check.</param>
        /// <returns>
        ///     True if a file exists at the path, otherwise false. A path that names a folder
        ///     (for example one ending in a separator) yields false.
        /// </returns>
        /// <exception cref="ArgumentException"><paramref name="path"/> is not rooted.</exception>
        bool ContentFileExists(ResPath path);

        /// <summary>
        ///     String overload of <see cref="ContentFileExists(ResPath)"/>. The text is interpreted as a
        ///     <see cref="ResPath"/> and the same result is produced.
        /// </summary>
        /// <param name="path">Absolute (rooted) VFS path to check.</param>
        /// <returns>True if a file exists at the path, otherwise false.</returns>
        /// <exception cref="ArgumentException"><paramref name="path"/> is not rooted.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
        bool ContentFileExists(string path);

        /// <summary>
        ///     Attempts to open a file from the mounted content roots without throwing when it is absent.
        /// </summary>
        /// <param name="path">Absolute (rooted) VFS path of the file to try.</param>
        /// <param name="fileStream">
        ///     On success, a readable in-memory stream with the file contents; otherwise null.
        ///     It is non-null exactly when this method returns true.
        /// </param>
        /// <returns>True if the file was found and loaded, false if it does not exist.</returns>
        /// <exception cref="ArgumentException"><paramref name="path"/> is not rooted.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
        bool TryContentFileRead(ResPath? path, [NotNullWhen(true)] out Stream? fileStream);

        /// <summary>
        ///     String overload of <see cref="TryContentFileRead(ResPath?, out Stream?)"/>. The text is
        ///     interpreted as a <see cref="ResPath"/>.
        /// </summary>
        /// <param name="path">Absolute (rooted) VFS path of the file to try.</param>
        /// <param name="fileStream">
        ///     On success, a readable in-memory stream with the file contents; otherwise null.
        ///     It is non-null exactly when this method returns true.
        /// </param>
        /// <returns>True if the file was found and loaded, false if it does not exist.</returns>
        /// <exception cref="ArgumentException"><paramref name="path"/> is not rooted.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
        bool TryContentFileRead(string path, [NotNullWhen(true)] out Stream? fileStream);

        /// <summary>
        ///     Recursively lists all files below a directory, across every mounted content root,
        ///     including those in nested subdirectories.
        /// </summary>
        /// <remarks>
        ///     A file present in several roots is listed once (the earliest mounted root wins).
        /// </remarks>
        /// <param name="path">Absolute (rooted) VFS path of the directory to search.</param>
        /// <returns>
        ///     A sequence of absolute paths, each naming a file rather than a directory.
        ///     A directory that does not exist produces an empty sequence.
        /// </returns>
        /// <exception cref="ArgumentException"><paramref name="path"/> is not rooted.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
        IEnumerable<ResPath> ContentFindFiles(ResPath? path);

        IEnumerable<ResPath> ContentFindRelativeFiles(ResPath path)
        {
            foreach (var absPath in ContentFindFiles(path))
            {
                if (!absPath.TryRelativeTo(path, out var rel))
                {
                    DebugTools.Assert("Past must be relative to be returned, unreachable.");
                    throw new InvalidOperationException("This is unreachable");
                }

                yield return rel.Value;
            }
        }

        /// <summary>
        ///     String overload of <see cref="ContentFindFiles(ResPath?)"/>. The text is interpreted as a
        ///     <see cref="ResPath"/> and the same results are produced.
        /// </summary>
        /// <param name="path">Absolute (rooted) VFS path of the directory to search.</param>
        /// <returns>
        ///     A sequence of absolute file paths; empty if the directory does not exist.
        /// </returns>
        /// <exception cref="ArgumentException"><paramref name="path"/> is not rooted.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
        IEnumerable<ResPath> ContentFindFiles(string path);

        /// <summary>
        /// Gets entries in a content directory.
        /// </summary>
        /// <remarks>
        /// This is not a performant API; the VFS does not work natively with these kinds of directory entries.
        /// This is intended for development tools such as console command completions,
        /// not for general-purpose resource management.
        /// </remarks>
        /// <param name="path"></param>
        /// <returns>A sequence of entry names. If the entry name ends in a slash, it's a directory.</returns>
        IEnumerable<string> ContentGetDirectoryEntries(ResPath path);

        /// <summary>
        ///     Returns a list of paths to all top-level content directories
        /// </summary>
        /// <returns></returns>
        [Obsolete("This API is no longer content-accessible")]
        IEnumerable<ResPath> GetContentRoots();

        /// <summary>
        ///     Read a file from the mounted content paths to a string.
        /// </summary>
        /// <param name="path">Path of the file to read.</param>
        string ContentFileReadAllText(string path)
        {
            return ContentFileReadAllText(new ResPath(path));
        }

        /// <summary>
        ///     Read a file from the mounted content paths to a string.
        /// </summary>
        /// <param name="path">Path of the file to read.</param>
        string ContentFileReadAllText(ResPath path)
        {
            return ContentFileReadAllText(path, EncodingHelpers.UTF8);
        }

        /// <summary>
        ///     Read a file from the mounted content paths to a string.
        /// </summary>
        /// <param name="path">Path of the file to read.</param>
        /// <param name="encoding">Text encoding to use when reading.</param>
        string ContentFileReadAllText(ResPath path, Encoding encoding)
        {
            using var stream = ContentFileRead(path);
            using var reader = new StreamReader(stream, encoding);

            return reader.ReadToEnd();
        }

        public YamlStream ContentFileReadYaml(ResPath path)
        {
            using var reader = ContentFileReadText(path);

            var yamlStream = new YamlStream();
            yamlStream.Load(reader);

            return yamlStream;
        }

        public StreamReader ContentFileReadText(ResPath path)
        {
            return ContentFileReadText(path, EncodingHelpers.UTF8);
        }

        public StreamReader ContentFileReadText(ResPath path, Encoding encoding)
        {
            var stream = ContentFileRead(path);
            return new StreamReader(stream, encoding);
        }
    }
}
