using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using Robust.Shared.Log;
using Robust.Shared.Utility;

namespace Robust.Shared.ContentPack
{
    internal partial class ResourceManager
    {
        /// <summary>
        ///     Mounts a zip archive as a content root of the virtual file system.
        /// </summary>
        sealed class PackLoader : IContentRoot
        {
            private readonly FileInfo? _pack;
            private readonly ISawmill _sawmill;
            private readonly Stream? _stream;
            private ZipArchive _zip = default!;

            /// <summary>
            ///     Creates a loader that will read a zip archive from a file on disk.
            ///     The archive is not opened until <see cref="Mount"/> is called.
            /// </summary>
            /// <param name="pack">The zip file to expose through the virtual file system.</param>
            /// <param name="sawmill">The logger used for diagnostic messages.</param>
            public PackLoader(FileInfo pack, ISawmill sawmill)
            {
                _pack = pack;
                _sawmill = sawmill;
            }

            public PackLoader(Stream stream, ISawmill sawmill)
            {
                _stream = stream;
                _sawmill = sawmill;
            }

            /// <inheritdoc />
            public void Mount()
            {
                if (_pack != null)
                {
                    // File mode: open the archive read-only straight from disk.
                    _sawmill.Info("Loading ContentPack: " + _pack.FullName + "...");

                    _zip = ZipFile.OpenRead(_pack.FullName);
                }
                else
                {
                    // Stream mode: wrap the caller-supplied stream.
                    DebugTools.AssertNotNull(_stream);

                    _zip = new ZipArchive(_stream!, ZipArchiveMode.Read);
                }
            }

            /// <inheritdoc />
            public bool TryGetFile(ResPath relPath, [NotNullWhen(true)] out Stream? stream)
            {
                // Exact, case-sensitive lookup by the path's canonical text.
                var entry = _zip.GetEntry(relPath.ToString());

                if (entry == null)
                {
                    stream = null;
                    return false;
                }

                var memory = new MemoryStream();

                // ZipArchive entry reads are not thread-safe, so serialise them on the archive.
                lock (_zip)
                {
                    using var entryStream = entry.Open();
                    entryStream.CopyTo(memory);
                }

                memory.Position = 0;
                stream = memory;
                return true;
            }

            public bool FileExists(ResPath relPath)
            {
                return _zip.GetEntry(relPath.ToString()) != null;
            }

            /// <inheritdoc />
            public IEnumerable<ResPath> FindFiles(ResPath path)
            {
                // The trailing slash ensures "Foo" never matches entries under "Foobar/".
                var prefix = path.ToString() + "/";

                foreach (var entry in _zip.Entries)
                {
                    // Directory nodes have an empty short name; skip them.
                    if (entry.Name == "")
                    {
                        continue;
                    }

                    if (!entry.FullName.StartsWith(prefix))
                    {
                        continue;
                    }

                    // Report the full pack-relative path, recursively for all nested files.
                    yield return new ResPath(entry.FullName).ToRelativePath();
                }
            }

            public IEnumerable<string> GetRelativeFilePaths()
            {
                foreach (var entry in _zip.Entries)
                {
                    if (entry.Name == "")
                    {
                        // Dir node.
                        continue;
                    }

                    yield return new ResPath(entry.FullName).ToRootedPath().ToString();
                }
            }
        }
    }
}
