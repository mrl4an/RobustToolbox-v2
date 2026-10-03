using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Reflection;
using Robust.Shared.Timing;

namespace Robust.Shared.ContentPack
{
    internal abstract class BaseModLoader : IPostInjectInit
    {
        [Dependency] protected readonly IReflectionManager ReflectionManager = default!;
        [Dependency] protected readonly ILogManager LogManager = default!;
        [Dependency] private readonly IDependencyCollection _dependencies = default!;

        private readonly List<ModuleTestingCallbacks> _testingCallbacks = new();

        /// <summary>
        ///     Registry of every loaded content assembly, in the order the assemblies were initialized.
        ///     Subclasses may read and append to it, but the list instance itself can never be replaced.
        /// </summary>
        protected readonly List<ModInfo> Mods = new();

        protected ISawmill Sawmill { get; private set; } = default!;

        public IEnumerable<Assembly> LoadedModules => Mods.Select(p => p.GameAssembly);

        public Assembly GetAssembly(string name)
        {
            return Mods.Select(p => p.GameAssembly).Single(p => p.GetName().Name == name);
        }

        protected void InitMod(Assembly assembly)
        {
            var mod = new ModInfo(assembly);

            ReflectionManager.LoadAssemblies(mod.GameAssembly);

            var entryPoints = mod.GameAssembly.GetTypes().Where(t => typeof(GameShared).IsAssignableFrom(t));

            foreach (var entryPoint in entryPoints)
            {
                var entryPointInstance = (GameShared) Activator.CreateInstance(entryPoint)!;
                entryPointInstance.Dependencies = _dependencies;
                if (_testingCallbacks != null)
                {
                    entryPointInstance.SetTestingCallbacks(_testingCallbacks);
                }

                mod.EntryPoints.Add(entryPointInstance);
            }

            Mods.Add(mod);
        }

        public bool IsContentAssembly(Assembly typeAssembly)
        {
            foreach (var mod in Mods)
            {
                if (mod.GameAssembly == typeAssembly)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Invokes the init callback matching <paramref name="level"/> on every content entry point.
        ///     Assemblies are visited in load order, and entry points within an assembly in creation order.
        /// </summary>
        /// <param name="level">The run level to broadcast.</param>
        /// <remarks>
        ///     Exceptions from entry points are not caught: the first failure aborts the broadcast and
        ///     the remaining entry points are skipped. An unrecognized level logs one error per entry point.
        /// </remarks>
        public void BroadcastRunLevel(ModRunLevel level)
        {
            foreach (var mod in Mods)
            {
                foreach (var entryPoint in mod.EntryPoints)
                {
                    switch (level)
                    {
                        case ModRunLevel.PreInit:
                            entryPoint.PreInit();
                            break;
                        case ModRunLevel.Init:
                            entryPoint.Init();
                            break;
                        case ModRunLevel.PostInit:
                            entryPoint.PostInit();
                            break;
                        default:
                            Sawmill.Error($"Unknown RunLevel: {level}");
                            break;
                    }
                }
            }
        }

        public void BroadcastUpdate(ModUpdateLevel level, FrameEventArgs frameEventArgs)
        {
            foreach (var module in Mods)
            {
                foreach (var entryPoint in module.EntryPoints)
                {
                    entryPoint.Update(level, frameEventArgs);
                }
            }
        }

        public void SetModuleBaseCallbacks(ModuleTestingCallbacks testingCallbacks)
        {
            _testingCallbacks.Add(testingCallbacks);
        }

        public void Shutdown()
        {
            foreach (var module in Mods)
            {
                foreach (var entryPoint in module.EntryPoints)
                {
                    entryPoint.Shutdown();
                }

                foreach (var entryPoint in module.EntryPoints)
                {
                    entryPoint.Dispose();
                }
            }
        }

        void IPostInjectInit.PostInject()
        {
            Sawmill = LogManager.GetSawmill("res.mod");
        }

        /// <summary>
        ///     Details about one loaded assembly.
        /// </summary>
        protected sealed class ModInfo
        {
            /// <summary>
            ///     Creates the record for a loaded assembly, with an initially empty list of entry points.
            /// </summary>
            /// <param name="gameAssembly">The content assembly this record describes.</param>
            public ModInfo(Assembly gameAssembly)
            {
                GameAssembly = gameAssembly;
                EntryPoints = new List<GameShared>();
            }

            public Assembly GameAssembly { get; }
            public List<GameShared> EntryPoints { get; }
        }
    }
}
