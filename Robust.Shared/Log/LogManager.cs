using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Robust.Shared.Utility;

namespace Robust.Shared.Log
{
    // Sealed. New functionality should be added with handlers.
    public partial class LogManager : ILogManager, IDisposable
    {
        public const string SawmillProperty = "Sawmill";
        public const string ROOT = "root";
        private readonly Sawmill rootSawmill;
        public ISawmill RootSawmill => rootSawmill;

        private readonly Dictionary<string, Sawmill> sawmills = new();
        private readonly ReaderWriterLockSlim _sawmillsLock = new();

        /// <summary>
        /// Returns the sawmill with the given full name, creating it (and any missing ancestors) on first use.
        /// </summary>
        /// <param name="name">Dot-separated sawmill name; matching is ordinal and case-sensitive.</param>
        public ISawmill GetSawmill(string name)
        {
            // Fast path under the shared lock.
            _sawmillsLock.EnterReadLock();
            try
            {
                if (sawmills.TryGetValue(name, out var existing))
                {
                    return existing;
                }
            }
            finally
            {
                _sawmillsLock.ExitReadLock();
            }

            // Slow path. The read lock is already gone, so there is no upgrade; the lookup is
            // repeated inside the helper in case another thread created the sawmill meanwhile.
            _sawmillsLock.EnterWriteLock();
            try
            {
                return _getSawmillUnlocked(name);
            }
            finally
            {
                _sawmillsLock.ExitWriteLock();
            }
        }

        public IEnumerable<ISawmill> AllSawmills
        {
            get
            {
                using var _ = _sawmillsLock.ReadGuard();

                return sawmills.Values.ToArray();
            }
        }

        /// <summary>
        /// Finds or creates a sawmill. The caller must hold the write lock.
        /// </summary>
        private Sawmill _getSawmillUnlocked(string name)
        {
            if (sawmills.TryGetValue(name, out var found))
            {
                return found;
            }

            // The parent is named by everything before the last dot; names without a dot hang off the root.
            var lastDot = name.LastIndexOf('.');
            var parentName = lastDot < 0 ? ROOT : name.Substring(0, lastDot);

            // Recursion creates any missing ancestors, working toward the root.
            var parent = _getSawmillUnlocked(parentName);

            var created = new Sawmill(parent, name);
            sawmills.Add(name, created);
            return created;
        }

        /// <summary>
        /// Creates the manager together with its root sawmill, which logs at Debug level by default.
        /// </summary>
        public LogManager()
        {
            rootSawmill = new Sawmill(null, ROOT) { Level = LogLevel.Debug };
            sawmills[ROOT] = rootSawmill;
        }

        public void Dispose()
        {
            foreach (Sawmill p in sawmills.Values)
            {
                p.Dispose();
            }
        }
    }
}
