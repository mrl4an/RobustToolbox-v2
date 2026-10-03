using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Robust.Shared.Utility;

namespace Robust.Shared.ContentPack
{
    /// <summary>
    ///     Common interface for mounting various things in the VFS.
    /// </summary>
    public interface IContentRoot
    {
        /// <summary>
        ///     Prepares this root for use. This is the first call made on an instance, before any of the
        ///     query members are used.
        /// </summary>
        /// <remarks>
        ///     Failure to mount is reported by throwing; the exception type is not fixed by this interface.
        ///     An implementation that has nothing to set up may do nothing. Callers invoke this once and from a
        ///     single thread, so implementations need not be idempotent or thread-safe.
        /// </remarks>
        void Mount();

        /// <summary>
        ///     Attempts to open a file from this root.
        /// </summary>
        /// <param name="relPath">
        ///     Location of the wanted file, relative to this root. Path semantics such as case sensitivity and
        ///     handling of a leading slash are defined by the implementation. A root never looks outside itself.
        /// </param>
        /// <param name="stream">
        ///     On success, a readable stream holding the content of the file; otherwise <see langword="null"/>.
        ///     The content is fully loaded, so the stream is independent of the root, seekable and positioned at
        ///     its start. Every call produces a new stream; the caller owns it and must dispose it.
        /// </param>
        /// <returns>
        ///     <see langword="true"/> if the file exists in this root and a stream was produced;
        ///     <see langword="false"/> if the root does not contain it. A plain miss does not throw.
        /// </returns>
        bool TryGetFile(ResPath relPath, [NotNullWhen(true)] out Stream? stream);

        /// <summary>
        ///     Returns true if the given file exists.
        /// </summary>
        public bool FileExists(ResPath relPath);

        /// <summary>
        ///     Finds all files inside a directory of this root, including those in nested directories.
        /// </summary>
        /// <param name="path">The directory, relative to this root, whose contents are searched.</param>
        /// <returns>
        ///     One path per file found. Directories are not returned. Each path is relative to the root itself
        ///     (it includes the searched directory part), not relative to <paramref name="path"/>. The order is
        ///     unspecified. A directory that does not exist yields an empty sequence rather than an exception.
        ///     The sequence may be evaluated lazily, so it should be enumerated only once, and errors (for example
        ///     a path that escapes the root) may only appear during enumeration.
        /// </returns>
        IEnumerable<ResPath> FindFiles(ResPath path);

        /// <summary>
        ///     Recursively returns relative paths to resource files.
        /// </summary>
        /// <returns>Enumeration of all relative file paths.</returns>
        IEnumerable<string> GetRelativeFilePaths();

        IEnumerable<string> GetEntries(ResPath path)
        {
            var countDirs = path == ResPath.Self ? 0 : path.CanonPath.Split('/').Count();

            var options = FindFiles(path).Select(c =>
            {
                var segment = c.CanonPath.Split('/');
                var segCount = segment.Count();
                var newPath = segment.Skip(countDirs).First();
                if (segCount > countDirs + 1)
                    newPath += "/";

                return newPath;
            }).Distinct();

            return options;
        }
    }
}
