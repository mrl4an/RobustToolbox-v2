using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Robust.Client.ComponentTrees;
using Robust.Client.GameObjects;
using Robust.Server.Debugging;
using Robust.Server.GameObjects;
using Robust.Server.GameStates;
using Robust.Server.Physics;
using Robust.Shared.ComponentTrees;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.Containers;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Reflection;
using Robust.Shared.Testing;
using Robust.Shared.Threading;
using Robust.Shared.Utility;
using AppearanceSystem = Robust.Client.GameObjects.AppearanceSystem;
using InputSystem = Robust.Server.GameObjects.InputSystem;
using MapSystem = Robust.Server.GameObjects.MapSystem;
using PointLightComponent = Robust.Client.GameObjects.PointLightComponent;

namespace Robust.UnitTesting
{
    public enum UnitTestProject : byte
    {
        Server,
        Client,
    }

    [Parallelizable]
    public abstract partial class RobustUnitTest
    {
        /// <summary>
        ///     Extra component types a derived test fixture wants registered in addition to the default set.
        ///     Null (the default) means none.
        /// </summary>
        protected virtual Type[]? ExtraComponents => null;
        private static Type[] _components = new []
            {
                typeof(EyeComponent),
                typeof(MapComponent),
                typeof(MapGridComponent),
                typeof(ContainerManagerComponent),
                typeof(MetaDataComponent),
                typeof(TransformComponent),
                typeof(PhysicsComponent),
                typeof(BroadphaseComponent),
                typeof(FixturesComponent),
                typeof(JointComponent),
                typeof(GridTreeComponent),
                typeof(JointRelayTargetComponent),
                typeof(OccluderComponent),
                typeof(OccluderTreeComponent),
                typeof(SpriteTreeComponent),
                typeof(LightTreeComponent),
                typeof(CollisionWakeComponent),
                typeof(CollideOnAnchorComponent),
                typeof(ActorComponent)
            };

        public virtual UnitTestProject Project => UnitTestProject.Server;

        /// <summary>
        ///     Builds the whole engine dependency graph once for the fixture: IoC registrations, CVars, entity
        ///     systems, component types, and finally starts the entity and map managers.
        ///     Any exception propagates so NUnit fails the fixture.
        /// </summary>
        [OneTimeSetUp]
        public void BaseSetup()
        {
            // Start from an empty container, then let the sibling partial file register everything.
            var deps = IoCManager.InitThread();
            deps.Clear();

            RegisterIoC();

            // Assemblies the reflection manager and CVar loader should know about.
            var assemblies = new List<Assembly>(4);

            switch (Project)
            {
                case UnitTestProject.Client:
                    assemblies.Add(AppDomain.CurrentDomain.GetAssemblyByName("Robust.Client"));
                    break;
                case UnitTestProject.Server:
                    assemblies.Add(AppDomain.CurrentDomain.GetAssemblyByName("Robust.Server"));
                    break;
                default:
                    throw new NotSupportedException($"Unknown testing project: {Project}");
            }

            assemblies.Add(AppDomain.CurrentDomain.GetAssemblyByName("Robust.Shared"));
            assemblies.Add(Assembly.GetExecutingAssembly());

            var configurationManager = deps.Resolve<IConfigurationManagerInternal>();
            configurationManager.Initialize(Project == UnitTestProject.Server);

            deps.Resolve<IReflectionManager>().Initialize();

            foreach (var assembly in assemblies)
            {
                configurationManager.LoadCVarsFromAssembly(assembly);
            }

            var contentAssemblies = GetContentAssemblies();
            foreach (var assembly in contentAssemblies)
            {
                configurationManager.LoadCVarsFromAssembly(assembly);
            }

            configurationManager.LoadCVarsFromAssembly(typeof(RTCVars).Assembly);

            // Entity systems shared by both sides.
            var systems = deps.Resolve<IEntitySystemManager>();
            systems.LoadExtraSystemType<EntityLookupSystem>();
            systems.LoadExtraSystemType<SharedGridTraversalSystem>();
            systems.LoadExtraSystemType<FixtureSystem>();
            systems.LoadExtraSystemType<CollisionWakeSystem>();

            // Side specific systems.
            if (Project == UnitTestProject.Client)
            {
                systems.LoadExtraSystemType<ClientMetaDataSystem>();
                systems.LoadExtraSystemType<ContainerSystem>();
                systems.LoadExtraSystemType<Robust.Client.GameObjects.TransformSystem>();
                systems.LoadExtraSystemType<Robust.Client.Physics.BroadPhaseSystem>();
                systems.LoadExtraSystemType<Robust.Client.Physics.JointSystem>();
                systems.LoadExtraSystemType<Robust.Client.Physics.PhysicsSystem>();
                systems.LoadExtraSystemType<Robust.Client.Debugging.DebugRayDrawingSystem>();
                systems.LoadExtraSystemType<PrototypeReloadSystem>();
                systems.LoadExtraSystemType<Robust.Client.Debugging.DebugPhysicsSystem>();
                systems.LoadExtraSystemType<Robust.Client.GameObjects.MapSystem>();
                systems.LoadExtraSystemType<Robust.Client.GameObjects.PointLightSystem>();
                systems.LoadExtraSystemType<LightTreeSystem>();
                systems.LoadExtraSystemType<RecursiveMoveSystem>();
                systems.LoadExtraSystemType<SpriteSystem>();
                systems.LoadExtraSystemType<SpriteTreeSystem>();
                systems.LoadExtraSystemType<AppearanceSystem>();
                systems.LoadExtraSystemType<GridChunkBoundsDebugSystem>();
            }
            else
            {
                systems.LoadExtraSystemType<ServerMetaDataSystem>();
                systems.LoadExtraSystemType<PvsSystem>();
                systems.LoadExtraSystemType<Robust.Server.Containers.ContainerSystem>();
                systems.LoadExtraSystemType<Robust.Server.GameObjects.TransformSystem>();
                systems.LoadExtraSystemType<BroadPhaseSystem>();
                systems.LoadExtraSystemType<JointSystem>();
                systems.LoadExtraSystemType<PhysicsSystem>();
                systems.LoadExtraSystemType<DebugRayDrawingSystem>();
                systems.LoadExtraSystemType<PrototypeReloadSystem>();
                systems.LoadExtraSystemType<DebugPhysicsSystem>();
                systems.LoadExtraSystemType<InputSystem>();
                systems.LoadExtraSystemType<PvsOverrideSystem>();
                systems.LoadExtraSystemType<MapSystem>();
            }

            var entMan = deps.Resolve<IEntityManager>();
            var mapMan = deps.Resolve<IMapManager>();

            // Entity commands depend on systems that tests do not load, so skip discovering them.
            var consoleHost = deps.Resolve<EntityConsoleHost>();
            consoleHost.DiscoverCommands = false;

            // Component registration.
            var compFactory = deps.Resolve<IComponentFactory>();
            compFactory.RegisterTypes(_components);

            if (ExtraComponents != null)
                compFactory.RegisterTypes(ExtraComponents);

            compFactory.RegisterClass<MapSaveTileMapComponent>();
            compFactory.RegisterClass<YamlUidComponent>();

            if (Project != UnitTestProject.Server)
            {
                compFactory.RegisterClass<PointLightComponent>();
                compFactory.RegisterClass<SpriteComponent>();
            }

            deps.Resolve<IParallelManagerInternal>().Initialize();

            // The entity manager is initialized by hand so that Startup does not also initialize the
            // system manager.
            entMan.Initialize();

            // Make the event bus tolerate the component misuse that unit tests perform.
            entMan.EventBus.OnlyCallOnRobustUnitTestISwearToGodPleaseSomebodyKillThisNightmare();

            mapMan.Initialize();
            systems.Initialize();

            deps.Resolve<IReflectionManager>().LoadAssemblies(assemblies);

            var modLoader = deps.Resolve<TestingModLoader>();
            modLoader.Assemblies = contentAssemblies;
            modLoader.TryLoadModulesFrom(ResPath.Root, "");

            entMan.Startup();
            mapMan.Startup();
        }

        [OneTimeTearDown]
        public void BaseTearDown()
        {
            IoCManager.Clear();
        }

        /// <summary>
        /// Called after all IoC registration has been done, but before the graph has been built.
        /// This allows one to add new IoC types or overwrite existing ones if needed.
        /// </summary>
        protected virtual void OverrideIoC()
        {
        }

        protected virtual Assembly[] GetContentAssemblies()
        {
            return Array.Empty<Assembly>();
        }
    }
}
