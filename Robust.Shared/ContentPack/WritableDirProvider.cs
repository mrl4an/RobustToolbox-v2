using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Robust.Shared.Utility;

namespace Robust.Shared.ContentPack
{
    /// <inheritdoc />
    internal sealed class WritableDirProvider : IWritableDirProvider
    {
        private readonly bool _hideRootDir;

        public string RootDir { get; }

        string? IWritableDirProvider.RootDir => _hideRootDir ? null : RootDir;

        /// <summary>
        ///     Creates a provider that serves files below a single real directory.
        /// </summary>
        /// <param name="rootDir">The directory on disk that all virtual paths are resolved against.</param>
        /// <param name="hideRootDir">
        ///     When true, the interface-level <c>RootDir</c> reports null so callers cannot learn the real location.
        /// </param>
        public WritableDirProvider(DirectoryInfo rootDir, bool hideRootDir)
        {
            // FullName carries no trailing separator. We append exactly one, because the path-safety check and
            // the prefix stripping in Find both rely on the root ending with a separator.
            RootDir = rootDir.FullName + Path.DirectorySeparatorChar;
            _hideRootDir = hideRootDir;
        }

        #region File Access

        /// <inheritdoc />
        public void CreateDir(ResPath path)
        {
            var osPath = GetFullPath(path);
            // Creates missing parents too, and is a no-op when the directory already exists.
            Directory.CreateDirectory(osPath);
        }

        /// <inheritdoc />
        public void Delete(ResPath path)
        {
            var osPath = GetFullPath(path);

            // Directories are checked first; a missing entry is silently ignored.
            if (Directory.Exists(osPath))
            {
                Directory.Delete(osPath, true);
            }
            else if (File.Exists(osPath))
            {
                File.Delete(osPath);
            }
        }

        /// <inheritdoc />
        public bool Exists(ResPath path)
        {
            var osPath = GetFullPath(path);
            return Directory.Exists(osPath) || File.Exists(osPath);
        }

        /// <inheritdoc />
        public (IEnumerable<ResPath> files, IEnumerable<ResPath> directories) Find(string pattern, bool recursive = true)
        {
            if (pattern.Contains(".."))
            {
                throw new InvalidOperationException($"Pattern may not contain '..'. Pattern: {pattern}.");
            }

            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            // Both scans are done eagerly so the caller receives fully materialised lists.
            var foundFiles = Directory.GetFiles(RootDir, pattern, option);
            var foundDirs = Directory.GetDirectories(RootDir, pattern, option);

            // Cutting at Length - 1 keeps the final separator, so the remainder starts with one.
            var prefixLength = RootDir.Length - 1;

            var files = new List<ResPath>();
            var directories = new List<ResPath>();

            foreach (var osPath in foundFiles)
            {
                if (IsSuspiciousPath(osPath))
                    continue;

                files.Add(ResPath.FromRelativeSystemPath(osPath.Substring(prefixLength)).ToRootedPath());
            }

            foreach (var osPath in foundDirs)
            {
                if (IsSuspiciousPath(osPath))
                    continue;

                directories.Add(ResPath.FromRelativeSystemPath(osPath.Substring(prefixLength)).ToRootedPath());
            }

            return (files, directories);

            // Defensive filter: a plain substring test, so any name starting with two dots is dropped as well.
            static bool IsSuspiciousPath(string osPath)
            {
                return osPath.Contains("\\..") || osPath.Contains("/..");
            }
        }

        public IEnumerable<string> DirectoryEntries(ResPath path)
        {
            var fullPath = GetFullPath(path);

            if (!Directory.Exists(fullPath))
                yield break;

            foreach (var entry in Directory.EnumerateFileSystemEntries(fullPath))
            {
                yield return Path.GetRelativePath(fullPath, entry);
            }
        }

        /// <inheritdoc />
        public bool IsDir(ResPath path)
        {
            var osPath = GetFullPath(path);
            return Directory.Exists(osPath);
        }

        /// <inheritdoc />
        public Stream Open(ResPath path, FileMode fileMode, FileAccess access, FileShare share)
        {
            var osPath = GetFullPath(path);
            // The caller owns the returned stream and must dispose it.
            return File.Open(osPath, fileMode, access, share);
        }

        public IWritableDirProvider OpenSubdirectory(ResPath path)
        {
            if (!IsDir(path))
                throw new FileNotFoundException();

            var dirInfo = new DirectoryInfo(GetFullPath(path));
            return new WritableDirProvider(dirInfo, _hideRootDir);
        }

        /// <inheritdoc />
        public void Rename(ResPath oldPath, ResPath newPath)
        {
            // The old path is resolved first, so an invalid source is reported before an invalid target.
            var oldOsPath = GetFullPath(oldPath);
            var newOsPath = GetFullPath(newPath);

            // Files only; this overload fails if the destination already exists.
            File.Move(oldOsPath, newOsPath);
        }

        public void OpenOsWindow(ResPath path)
        {
            if (!IsDir(path))
                path = path.Directory;

            var fullPath = GetFullPath(path);
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"{Environment.GetEnvironmentVariable("SystemRoot")}\\explorer.exe",
                    Arguments = ".",
                    WorkingDirectory = fullPath,
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = ".",
                    WorkingDirectory = fullPath,
                });
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = ".",
                    WorkingDirectory = fullPath,
                });
            }
            else
            {
                throw new NotSupportedException("Opening OS windows not supported on this OS");
            }
        }

        #endregion

        public string GetFullPath(ResPath path)
        {
            if (!path.IsRooted)
            {
                throw new ArgumentException($"Path must be rooted. Path: {path}");
            }

            path = path.Clean();

            return PathHelpers.SafeGetResourcePath(RootDir, path);
        }
    }
}
