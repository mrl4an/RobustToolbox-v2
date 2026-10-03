using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Robust.Shared.IoC;
using Robust.Shared.Utility;

namespace Robust.Shared.ContentPack
{
    /// <summary>
    ///     Loads the game's assemblies into the engine and keeps track of them.
    /// </summary>
    internal sealed class ModLoader : BaseModLoader, IModLoaderInternal, IDisposable
    {
        [Dependency] private readonly IResourceManagerInternal _res = default!;

        // List of extra assemblies side-loaded from the /Assemblies/ mounted path.
        private readonly List<Assembly> _sideModules = new();

        private readonly AssemblyLoadContext _loadContext;

        private readonly object _lock = new();

        private static int _modLoaderId;

        private bool _useLoadContext = true;
        private bool _sandboxingEnabled;

        private readonly List<string> _engineModuleDirectories = new();
        private readonly List<ExtraModuleLoad> _extraModuleLoads = new();

        public event ExtraModuleLoad ExtraModuleLoaders
        {
            add => _extraModuleLoads.Add(value);
            remove => _extraModuleLoads.Remove(value);
        }

        public ModLoader()
        {
            var id = Interlocked.Increment(ref _modLoaderId);
            _loadContext = new AssemblyLoadContext($"ModLoader-{id}");

            _loadContext.Resolving += ResolvingAssembly;

            AssemblyLoadContext.Default.Resolving += DefaultOnResolving;
        }

        public void SetUseLoadContext(bool useLoadContext)
        {
            _useLoadContext = useLoadContext;
            Sawmill.Debug("{0} assembly load context", useLoadContext ? "ENABLING" : "DISABLING");
        }

        public void SetEnableSandboxing(bool sandboxing)
        {
            _sandboxingEnabled = sandboxing;
            Sawmill.Debug("{0} sandboxing", sandboxing ? "ENABLING" : "DISABLING");
        }

        public Func<string, Stream?>? VerifierExtraLoadHandler { get; set; }

        public void AddEngineModuleDirectory(string dir)
        {
            _engineModuleDirectories.Add(dir);
        }

        public bool TryLoadModulesFrom(ResPath mountPath, string filterPrefix)
        {
            var paths = new List<ResPath>();

            foreach (var filePath in _res.ContentFindRelativeFiles(mountPath)
                         .Where(p => p.Filename.StartsWith(filterPrefix) &&
                                     p.Extension == "dll"))
            {
                var fullPath = mountPath / filePath;
                Sawmill.Debug($"Found module '{fullPath}'");

                paths.Add(fullPath);
            }

            return TryLoadModules(paths);
        }

        /// <summary>
        ///     Loads a set of content assemblies given by VFS path. When sandboxing is enabled, every assembly
        ///     is verified up front (in parallel) before any of them is loaded. Assemblies are then loaded in
        ///     dependency order, so a module is always loaded after the content modules it references.
        /// </summary>
        /// <param name="paths">VFS paths of the assembly files to load.</param>
        /// <returns>True if everything loaded; false if a failure was logged.</returns>
        /// <exception cref="AggregateException">
        ///     Sandbox verification failed for at least one assembly (wraps a <see cref="TypeCheckFailedException"/>).
        ///     This is deliberately not caught here.
        /// </exception>
        public bool TryLoadModules(IEnumerable<ResPath> paths)
        {
            Sawmill.Debug("LOADING modules");
            var totalTimer = Stopwatch.StartNew();

            // Assembly name -> (VFS path, full DLL bytes, names of referenced assemblies).
            var collected = new Dictionary<string, (ResPath Path, MemoryStream Bytes, string[] References)>();

            // Phase 1: read every file into memory and collect its name and references.
            foreach (var path in paths)
            {
                var buffer = new MemoryStream();
                using (var source = _res.ContentFileRead(path))
                {
                    source.CopyTo(buffer);
                }

                buffer.Position = 0;

                var data = GetAssemblyReferenceData(buffer);
                if (data == null)
                {
                    // Marked as "skip if sandboxed"; ignore this module.
                    continue;
                }

                var (references, name) = data.Value;

                if (collected.TryGetValue(name, out var existing))
                {
                    Sawmill.Error(
                        "Found multiple modules with the same assembly name '{0}', A: {1}, B: {2}.",
                        name, existing.Path, path);
                    return false;
                }

                collected.Add(name, (path, buffer, references));
            }

            // Phase 2: sandbox verification, only when sandboxing is on.
            if (_sandboxingEnabled)
            {
                var verifyTimer = Stopwatch.StartNew();

                var checker = MakeTypeChecker();
                var resolver = checker.CreateResolver();

                // Failures surface as an AggregateException and are intentionally not caught.
                Parallel.ForEach(collected, entry =>
                {
                    entry.Value.Bytes.Position = 0;

                    if (!checker.CheckAssembly(entry.Value.Bytes, resolver))
                    {
                        throw new TypeCheckFailedException($"Assembly {entry.Key} failed type checks.");
                    }
                });

                Sawmill.Debug("Verified assemblies in {0}ms", verifyTimer.ElapsedMilliseconds);
            }

            // Phase 3: order by references. References to assemblies we did not collect are
            // non-content assemblies and are ignored (allowMissing).
            var ordered = TopologicalSort.FromBeforeAfter(
                collected,
                kv => kv.Key,
                kv => kv.Value,
                _ => Array.Empty<string>(),
                kv => kv.Value.References,
                allowMissing: true);

            // Phase 4: load in dependency order. Verification was already handled above.
            foreach (var (path, bytes, _) in TopologicalSort.Sort(ordered))
            {
                Sawmill.Debug("Loading module: '{0}'", path);

                try
                {
                    if (_res.TryGetDiskFilePath(path, out var diskPath))
                    {
                        LoadGameAssembly(diskPath, skipVerify: true);
                    }
                    else
                    {
                        bytes.Position = 0;
                        using var symbols = _res.ContentFileReadOrNull(path.WithExtension("pdb"));
                        LoadGameAssembly(bytes, symbols, skipVerify: true);
                    }
                }
                catch (Exception e)
                {
                    Sawmill.Error("Exception loading module '{0}':\n{1}", path, e.ToStringBetter());
                    return false;
                }
            }

            Sawmill.Debug("DONE loading modules: {0}", totalTimer.Elapsed);
            return true;
        }

        private (string[] refs, string name)? GetAssemblyReferenceData(Stream stream)
        {
            using var reader = ModLoader.MakePEReader(stream, leaveOpen: true);
            var metaReader = reader.GetMetadataReader();

            var name = metaReader.GetString(metaReader.GetAssemblyDefinition().Name);

            // Try to find SkipIfSandboxedAttribute.

            if (_sandboxingEnabled && TryFindSkipIfSandboxed(metaReader))
            {
                Sawmill.Debug("Module {ModuleName} has SkipIfSandboxedAttribute, ignoring.", name);
                return null;
            }

            return (metaReader.AssemblyReferences
                .Select(a => metaReader.GetAssemblyReference(a))
                .Select(a => metaReader.GetString(a.Name)).ToArray(), name);
        }

        private static bool TryFindSkipIfSandboxed(MetadataReader reader)
        {
            foreach (var attribHandle in reader.CustomAttributes)
            {
                var attrib = reader.GetCustomAttribute(attribHandle);
                if (attrib.Parent.Kind != HandleKind.AssemblyDefinition)
                    continue;

                var ctor = attrib.Constructor;
                if (ctor.Kind != HandleKind.MemberReference)
                    continue;

                var memberRef = reader.GetMemberReference((MemberReferenceHandle) ctor);
                var typeRef = AssemblyTypeChecker.ParseTypeReference(reader, (TypeReferenceHandle)memberRef.Parent);

                if (typeRef.Namespace == "Robust.Shared.ContentPack" && typeRef.Name == "SkipIfSandboxedAttribute")
                    return true;
            }

            return false;
        }

        /// <summary>
        ///     Loads a game assembly from a stream, optionally verifying it first, and registers it as a mod.
        /// </summary>
        /// <param name="assembly">Seekable stream holding the assembly image.</param>
        /// <param name="symbols">Optional debug symbols (PDB) stream.</param>
        /// <param name="skipVerify">
        ///     True if the caller already verified the assembly; the type checker is then not even created.
        /// </param>
        /// <exception cref="TypeCheckFailedException">The assembly failed verification.</exception>
        public void LoadGameAssembly(Stream assembly, Stream? symbols = null, bool skipVerify = false)
        {
            if (!skipVerify && !MakeTypeChecker().CheckAssembly(assembly))
            {
                throw new TypeCheckFailedException();
            }

            // The checker (or an earlier reader) may have moved the position.
            assembly.Position = 0;

            Assembly loaded;
            if (_useLoadContext)
            {
                loaded = _loadContext.LoadFromStream(assembly, symbols);
            }
            else
            {
                loaded = Assembly.Load(assembly.CopyToArray(), symbols?.CopyToArray());
            }

            InitMod(loaded);
        }

        public void LoadGameAssembly(string diskPath, bool skipVerify = false)
        {
            if (!skipVerify && !MakeTypeChecker().CheckAssembly(diskPath))
            {
                throw new TypeCheckFailedException();
            }

            Assembly assembly;
            if (_useLoadContext)
            {
                assembly = _loadContext.LoadFromAssemblyPath(diskPath);
            }
            else
            {
                assembly = Assembly.LoadFrom(diskPath);
            }

            InitMod(assembly);
        }

        /// <summary>
        ///     Loads a single game assembly by simple name from the VFS directory <c>/Assemblies/</c>.
        ///     Verification is always requested (the type checker decides based on the sandbox setting).
        /// </summary>
        /// <param name="assemblyName">Simple assembly name, without extension.</param>
        /// <returns>True on success; false on any failure. Load problems are logged, never thrown.</returns>
        public bool TryLoadAssembly(string assemblyName)
        {
            var dllPath = new ResPath($"/Assemblies/{assemblyName}.dll");

            // Prefer a real file on disk so debuggers keep working.
            if (_res.TryGetDiskFilePath(dllPath, out var diskPath))
            {
                Sawmill.Debug("Loading {0} DLL", assemblyName);
                try
                {
                    LoadGameAssembly(diskPath, skipVerify: false);
                    return true;
                }
                catch (Exception e)
                {
                    Sawmill.Error("Exception loading DLL {0}.dll: {1}", assemblyName, e.ToStringBetter());
                    return false;
                }
            }

            if (_res.TryContentFileRead(dllPath, out var gameDll))
            {
                using (gameDll)
                {
                    Sawmill.Debug("Loading {0} DLL", assemblyName);
                    try
                    {
                        if (_res.TryContentFileRead(new ResPath($"/Assemblies/{assemblyName}.pdb"), out var gamePdb))
                        {
                            using (gamePdb)
                            {
                                LoadGameAssembly(gameDll, gamePdb);
                            }
                        }
                        else
                        {
                            LoadGameAssembly(gameDll);
                        }

                        return true;
                    }
                    catch (Exception e)
                    {
                        Sawmill.Error("Exception loading DLL {0}.dll: {1}", assemblyName, e.ToStringBetter());
                        return false;
                    }
                }
            }

            Sawmill.Warning("Could not load {0} DLL: {1} does not exist in the VFS.", assemblyName, dllPath);
            return false;
        }

        private Assembly? ResolvingAssembly(AssemblyLoadContext context, AssemblyName name)
        {
            try
            {
                lock (_lock)
                {
                    Sawmill.Verbose("ResolvingAssembly {0}", name);

                    // Try main modules.
                    foreach (var mod in Mods)
                    {
                        if (mod.GameAssembly.FullName == name.FullName)
                        {
                            Sawmill.Verbose($"Found assembly in modloader ALC: {mod.GameAssembly}");
                            return mod.GameAssembly;
                        }
                    }

                    if (TryLoadExtra(name) is { } asm)
                    {
                        Sawmill.Verbose($"Found assembly through extra loader: {asm}");
                        return asm;
                    }

                    // Do not allow sideloading when sandboxing is enabled.
                    // Side loaded assemblies would not be checked for sandboxing currently, so we can't have that.
                    if (!_sandboxingEnabled)
                    {
                        foreach (var assembly in _sideModules)
                        {
                            if (assembly.FullName == name.FullName)
                            {
                                Sawmill.Verbose($"Found assembly in existing side modules: {assembly}");
                                return assembly;
                            }
                        }

                        // Try to resolve assemblies in the default AssemblyLoadContext.
                        // If we don't do this manually, the sideloading code below could load assemblies from content,
                        // even if Robust provides its own versions.
                        // This can lead to:
                        // * Multiple copies of the same assembly being loaded.
                        // * Mismatching versions of dependencies being loaded.
                        //   * e.g. Microsoft.Extensions.Primitives 6.0 with Microsoft.Extensions.DependencyInjection 7.0
                        //
                        // Now, to be clear, this is 100% an error in packaging. But it's also one that's really easy to make.
                        //
                        try
                        {
                            var defaultAssembly = AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(name.Name!));
                            Sawmill.Verbose($"Found assembly through default ALC (early): {defaultAssembly}");
                            return defaultAssembly;
                        }
                        catch
                        {
                             // Assume assembly not loadable from Robust's directory, proceed with loading from content.
                        }

                        if (_res.TryContentFileRead($"/Assemblies/{name.Name}.dll", out var dll))
                        {
                            var assembly = _loadContext.LoadFromStream(dll);
                            _sideModules.Add(assembly);
                            Sawmill.Verbose($"Found assembly in NEW side module: {assembly}");
                            return assembly;
                        }
                    }

                    Sawmill.Verbose("Did not find assembly directly. Should fall back to default ALC.");
                    return null;
                }
            }
            catch (Exception e)
            {
                Sawmill.Error("Exception in ResolvingAssembly: {0}", e);
                ExceptionDispatchInfo.Capture(e).Throw();
                throw null; // Unreachable.
            }
        }

        public void Dispose()
        {
            AssemblyLoadContext.Default.Resolving -= DefaultOnResolving;
        }

        private Assembly? DefaultOnResolving(AssemblyLoadContext ctx, AssemblyName name)
        {
            // We have to hook AssemblyLoadContext.Default.Resolving so that C# interactive loads assemblies correctly.
            // Otherwise it would load the assemblies a second time which is an amazing way to have everything break.
            if (_useLoadContext)
            {
                Sawmill.Verbose($"RESOLVING DEFAULT: {name}");
                foreach (var module in LoadedModules)
                {
                    if (module.GetName().Name == name.Name)
                    {
                        return module;
                    }
                }

                foreach (var module in _sideModules)
                {
                    if (module.GetName().Name == name.Name)
                    {
                        return module;
                    }
                }

                if (TryLoadExtra(name) is { } asm)
                    return asm;
            }

            return null;
        }

        private Assembly? TryLoadExtra(AssemblyName name)
        {
            foreach (var extra in _extraModuleLoads)
            {
                if (extra(name) is { } asm)
                    return asm;
            }

            return null;
        }

        private AssemblyTypeChecker MakeTypeChecker()
        {
            return new(_res, LogManager.GetSawmill("res.typecheck"))
            {
                VerifyIL = _sandboxingEnabled,
                DisableTypeCheck = !_sandboxingEnabled,
                ExtraRobustLoader = VerifierExtraLoadHandler,
                EngineModuleDirectories = _engineModuleDirectories.ToArray()
            };
        }

        internal static PEReader MakePEReader(Stream stream, bool leaveOpen=false, PEStreamOptions options=PEStreamOptions.Default)
        {
            if (!stream.CanSeek)
                stream = leaveOpen ? stream.CopyToMemoryStream() : stream.ConsumeToMemoryStream();

            if (leaveOpen)
                options |= PEStreamOptions.LeaveOpen;

            return new PEReader(stream, options);
        }
    }
}
