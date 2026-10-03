using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Robust.Shared.ContentPack
{
    /// <summary>
    ///     The mod loader is in charge of loading content assemblies and managing them.
    /// </summary>
    [NotContentImplementable]
    public interface IModLoader
    {
        /// <summary>
        ///     All directly loaded content assemblies.
        /// </summary>
        IEnumerable<Assembly> LoadedModules { get; }

        Assembly GetAssembly(string name);

        /// <summary>
        ///     Adds a testing callbacks that will be passed to <see cref="GameShared.SetTestingCallbacks"/>.
        /// </summary>
        void SetModuleBaseCallbacks(ModuleTestingCallbacks testingCallbacks);

        bool IsContentAssembly(Assembly typeAssembly);
    }

    internal delegate Assembly? ExtraModuleLoad(AssemblyName name);

    internal interface IModLoaderInternal : IModLoader
    {
        /// <summary>
        ///     Loads all content assemblies from the specified resource directory and filename prefix.
        /// </summary>
        /// <param name="mountPath">The directory in which to look for assemblies.</param>
        /// <param name="filterPrefix">The prefix files need to have to be considered. e.g. <c>Content.</c></param>
        /// <returns>True if all modules loaded successfully. False if there were load errors.</returns>
        bool TryLoadModulesFrom(ResPath mountPath, string filterPrefix);

        /// <summary>
        ///     Loads all content assemblies given
        /// </summary>
        /// <param name="paths">The list of paths to content assemblies that must be loaded.</param>
        /// <returns>True if all modules loaded successfully. False if there were load errors.</returns>
        bool TryLoadModules(IEnumerable<ResPath> paths);

        /// <summary>
        ///     Loads a single content assembly from a raw image supplied as a stream and registers it as a mod.
        /// </summary>
        /// <param name="assembly">
        ///     Stream containing the assembly image. It should be seekable, because the loader rewinds it before reading.
        /// </param>
        /// <param name="symbols">Optional stream containing debug symbols (PDB) for the assembly, or null for none.</param>
        /// <param name="skipVerify">
        ///     When true, the sandbox type check is skipped, which is appropriate if the caller has already verified the
        ///     assembly. When false, verification runs and a failure is reported by throwing from the implementation.
        /// </param>
        /// <remarks>Failures are reported by throwing; nothing is returned.</remarks>
        void LoadGameAssembly(Stream assembly, Stream? symbols = null, bool skipVerify = false);

        /// <summary>
        ///     Loads a single content assembly from a file on the host's real disk and registers it as a mod.
        ///     Prefer this overload over the stream-based one whenever a real file is available, since it keeps
        ///     debugger symbol lookup working.
        /// </summary>
        /// <param name="diskPath">Path of the assembly file in the host file system.</param>
        /// <param name="skipVerify">
        ///     When true, the sandbox type check is skipped, which is appropriate if the caller has already verified the
        ///     assembly. When false, verification runs and a failure is reported by throwing from the implementation.
        /// </param>
        /// <remarks>Failures are reported by throwing; nothing is returned.</remarks>
        void LoadGameAssembly(string diskPath, bool skipVerify = false);

        /// <summary>
        ///     Announces to every entry point of every loaded content assembly that the game has reached a
        ///     lifecycle stage.
        /// </summary>
        /// <param name="level">
        ///     The stage being announced. The valid values are <see cref="ModRunLevel.PreInit"/>,
        ///     <see cref="ModRunLevel.Init"/> and <see cref="ModRunLevel.PostInit"/>.
        /// </param>
        /// <remarks>
        ///     The implementation visits assemblies in load order and, within each assembly, entry points in the
        ///     order they were created, invoking the callback that matches the level. An unrecognized value is
        ///     logged as an error instead of being dispatched. Exceptions thrown by entry points propagate to
        ///     the caller.
        /// </remarks>
        void BroadcastRunLevel(ModRunLevel level);

        void BroadcastUpdate(ModUpdateLevel level, FrameEventArgs frameEventArgs);

        /// <summary>
        ///     Tries to find and load one named game assembly from the assemblies directory
        ///     (<c>/Assemblies/</c>) of the virtual file system.
        /// </summary>
        /// <param name="assemblyName">
        ///     Simple file name of the assembly without its extension. The loader appends <c>.dll</c> itself and
        ///     looks for a matching <c>.pdb</c> file for symbols.
        /// </param>
        /// <returns>
        ///     True if the assembly was found and loaded; false if it was missing or loading failed. Load problems
        ///     are logged by the implementation rather than thrown.
        /// </returns>
        /// <remarks>Sandbox verification is not skipped on this path.</remarks>
        bool TryLoadAssembly(string assemblyName);

        void SetUseLoadContext(bool useLoadContext);
        void SetEnableSandboxing(bool sandboxing);

        Func<string, Stream?>? VerifierExtraLoadHandler { get; set; }

        void AddEngineModuleDirectory(string dir);

        void Shutdown();
        event ExtraModuleLoad ExtraModuleLoaders;
    }
}
