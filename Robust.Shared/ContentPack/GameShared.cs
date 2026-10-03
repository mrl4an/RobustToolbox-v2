using System;
using System.Collections.Generic;
using Robust.Shared.IoC;
using Robust.Shared.Timing;

namespace Robust.Shared.ContentPack
{
    /// <summary>
    ///     Base class of the entry point of a content assembly, shared by client and server.
    /// </summary>
    public abstract class GameShared : IDisposable
    {
        protected internal IDependencyCollection Dependencies { get; internal set; } = default!;
        protected List<ModuleTestingCallbacks> TestingCallbacks { get; private set; } = new();

        public void SetTestingCallbacks(List<ModuleTestingCallbacks> testingCallbacks)
        {
            TestingCallbacks = testingCallbacks;
        }

        public virtual void PreInit()
        {
        }

        /// <summary>
        ///     Overridable startup hook. The base implementation intentionally does nothing;
        ///     derived entry points override it to perform their main initialization.
        /// </summary>
        public virtual void Init()
        {
        }

        /// <summary>
        ///     Overridable hook that the engine invokes after <see cref="Init"/> has completed.
        ///     The base implementation intentionally does nothing; derived entry points override it
        ///     for setup that depends on everything else being initialized.
        /// </summary>
        public virtual void PostInit()
        {
        }

        public virtual void Update(ModUpdateLevel level, FrameEventArgs frameEventArgs)
        {
        }

        public virtual void Shutdown()
        {
        }

        /// <summary>
        ///     Releases resources held by this entry point. Delegates the actual cleanup to
        ///     <see cref="Dispose(bool)"/> (with <c>true</c>, meaning an explicit disposal) and then
        ///     tells the garbage collector that no finalizer needs to run for this instance.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
        }
    }
}
