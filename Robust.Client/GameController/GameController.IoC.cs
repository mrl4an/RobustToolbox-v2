using System;
using System.Collections.Generic;
using System.Reflection;
using Robust.Shared.ContentPack;
using Robust.Shared.IoC;
using Robust.Shared.Reflection;

namespace Robust.Client
{
    // Partial of GameController to initialize IoC and some other low-level systems like it.
    internal sealed partial class GameController
    {
        /// <summary>
        /// Sets up the client's dependency collection: registers the client services for the display mode,
        /// builds the dependency graph, then registers the assemblies with the reflection manager.
        /// </summary>
        /// <param name="mode">The display mode that decides which implementations get registered.</param>
        /// <param name="deps">The collection to populate.</param>
        private static void InitIoC(DisplayMode mode, IDependencyCollection deps)
        {
            ClientIoC.RegisterIoC(mode, deps);
            deps.BuildGraph();
            RegisterReflection(deps);
        }

        /// <summary>
        /// Tells the reflection manager about the engine assemblies: first Robust.Shared, then the client assembly.
        /// </summary>
        /// <param name="deps">The collection the reflection manager is resolved from.</param>
        internal static void RegisterReflection(IDependencyCollection deps)
        {
            var reflection = deps.Resolve<IReflectionManager>();

            var assemblies = new List<Assembly>(2)
            {
                AppDomain.CurrentDomain.GetAssemblyByName("Robust.Shared"),
                Assembly.GetExecutingAssembly()
            };

            reflection.LoadAssemblies(assemblies);
        }
    }
}
