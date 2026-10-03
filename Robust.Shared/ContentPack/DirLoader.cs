using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Robust.Shared.Log;
using Robust.Shared.Utility;

namespace Robust.Shared.ContentPack
{
    internal partial class ResourceManager
    {
        /// <summary>
        ///     A directory on disk that is mounted into the virtual file system.
        /// </summary>
        sealed class DirLoader : IContentRoot
        {
            private readonly DirectoryInfo _directory;
            private readonly ISawmill _sawmill;
            private readonly bool _checkCasing;

            /// <summary>
            ///     Creates a loader for the given directory.
            /// </summary>
            /// <param name="directory">The directory on disk that becomes this content root.</param>
            /// <param name="sawmill">Logger used for diagnostics such as casing warnings.</param>
            /// <param name="checkCasing">Whether resource path casing is compared against the files on disk.</param>
            public DirLoader(DirectoryInfo directory, ISawmill sawmill, bool checkCasing)
            {
                _directory = directory;
                _sawmill = sawmill;
                _checkCasing = checkCasing;
            }

            /// <inheritdoc />
            public void Mount()
            {
                // Nothing to verify here: permission checks are performed by the owning ResourceManager.
            }

            /// <inheritdoc />
            public bool TryGetFile(ResPath relPath, [NotNullWhen(true)] out Stream? stream)
            {
                var path = GetPath(relPath);
                CheckPathCasing(relPath);

                var ret = FileHelper.TryOpenFileRead(path, out var fStream);
                stream = fStream;
                return ret;
            }

            public bool FileExists(ResPath relPath)
            {
                var path = GetPath(relPath);
                return File.Exists(path);
            }

            internal string GetPath(ResPath relPath)
            {
                // All disk access in this class goes through this call, so the helper's
                // check against escaping the mount directory covers every caller.
                return PathHelpers.SafeGetResourcePath(_directory.FullName, relPath);
            }

            /// <inheritdoc />
            public IEnumerable<ResPath> FindFiles(ResPath path)
            {
                // Lazy iterator: the path is resolved (and validated) on the first MoveNext.
                var fullPath = GetPath(path);
                if (!Directory.Exists(fullPath))
                    yield break;

                var rootLength = _directory.FullName.Length;
                foreach (var file in PathHelpers.GetFiles(fullPath))
                {
                    // Results are relative to the mount root, not to the searched directory.
                    yield return ResPath.FromRelativeSystemPath(file.Substring(rootLength));
                }
            }

            public IEnumerable<string> GetEntries(ResPath path)
            {
                var fullPath = GetPath(path);
                if (!Directory.Exists(fullPath))
                    return Enumerable.Empty<string>();

                return Directory.EnumerateFileSystemEntries(fullPath)
                    .Select(c =>
                    {
                        var rel = Path.GetRelativePath(fullPath, c);
                        if (Directory.Exists(c))
                            return rel + "/";

                        return rel;
                    });
            }

            [Conditional("DEBUG")]
            private void CheckPathCasing(ResPath path)
            {
                if (!_checkCasing)
                    return;

                // Run this inside the thread pool due to overhead.
                Task.Run(() =>
                {
                    var prevPath = GetPath(ResPath.Root);
                    var diskPath = ResPath.Root;
                    var mismatch = false;
                    foreach (var segment in path.CanonPath.Split('/'))
                    {
                        var prevDir = new DirectoryInfo(prevPath);
                        var found = false;
                        foreach (var info in prevDir.EnumerateFileSystemInfos())
                        {
                            if (!string.Equals(info.Name, segment, StringComparison.InvariantCultureIgnoreCase))
                            {
                                // Not the dir info for this path segment, ignore it.
                                continue;
                            }

                            if (!string.Equals(info.Name, segment, StringComparison.InvariantCulture))
                            {
                                // Segments match insensitively but not the other way around. Bad.
                                mismatch = true;
                            }

                            diskPath /= info.Name;
                            prevPath = Path.Combine(prevPath, info.Name);
                            found = true;
                            break;
                        }

                        if (!found)
                        {
                            // File doesn't exist. Let somebody else throw the exception.
                            return;
                        }
                    }

                    if (mismatch)
                    {
                        _sawmill.Warning("Path '{0}' has mismatching case from file on disk ('{1}'). " +
                                        "This can cause loading failures on certain file system configurations " +
                                        "and should be corrected.", path, diskPath);
                    }
                });
            }
            public IEnumerable<string> GetRelativeFilePaths()
            {
                return GetRelativeFilePaths(_directory);
            }

            private IEnumerable<string> GetRelativeFilePaths(DirectoryInfo dir)
            {
                foreach (var file in dir.EnumerateFiles())
                {
                    if ((file.Attributes & FileAttributes.Hidden) != 0 || file.Name[0] == '.')
                    {
                        continue;
                    }

                    var filePath = file.FullName;
                    var relPath = filePath.Substring(_directory.FullName.Length);
                    yield return ResPath.FromRelativeSystemPath(relPath).ToRootedPath().ToString();
                }

                foreach (var subDir in dir.EnumerateDirectories())
                {
                    if (((subDir.Attributes & FileAttributes.Hidden) != 0) || subDir.Name[0] == '.')
                    {
                        continue;
                    }

                    foreach (var relPath in GetRelativeFilePaths(subDir))
                    {
                        yield return relPath;
                    }
                }
            }
        }
    }
}
