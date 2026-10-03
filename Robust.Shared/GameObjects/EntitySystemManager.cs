using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Prometheus;
using Robust.Shared.IoC;
using Robust.Shared.IoC.Exceptions;
using Robust.Shared.Log;
using Robust.Shared.Profiling;
using Robust.Shared.Reflection;
using Robust.Shared.Utility;
using Robust.Shared.ViewVariables;
using Dependency = Robust.Shared.IoC.DependencyAttribute;
using Stopwatch = System.Diagnostics.Stopwatch;
#if EXCEPTION_TOLERANCE
using Robust.Shared.Exceptions;
#endif


namespace Robust.Shared.GameObjects
{
    public sealed class EntitySystemManager : IEntitySystemManager, IPostInjectInit
    {
        [Dependency] private readonly IReflectionManager _reflectionManager = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly ProfManager _profManager = default!;
        [Dependency] private readonly IDependencyCollection _dependencyCollection = default!;
        [Dependency] private readonly ILogManager _logManager = default!;

#if EXCEPTION_TOLERANCE
        [Dependency] private readonly IRuntimeLog _runtimeLog = default!;
#endif

        private ISawmill _sawmill = default!;

        internal DependencyCollection SystemDependencyCollection = default!;

        public IDependencyCollection DependencyCollection
        {
            get
            {
                if (_initialized)
                    return SystemDependencyCollection;

                throw new InvalidOperationException($"{nameof(EntitySystemManager)} has not been initialized.");
            }
        }

        private readonly List<Type> _systemTypes = new();

        private static readonly Histogram _tickUsageHistogram = Metrics.CreateHistogram("robust_entity_systems_update_usage",
            "Amount of time spent processing each entity system", new HistogramConfiguration
            {
                LabelNames = new[] {"system"},
                Buckets = Histogram.ExponentialBuckets(0.000_001, 1.5, 25)
            });

        [ViewVariables]
        private readonly List<Type> _extraLoadedTypes = new();

        private readonly Stopwatch _stopwatch = new();

        private bool _initialized;

        [ViewVariables] private UpdateReg[] _updateOrder = Array.Empty<UpdateReg>();
        [ViewVariables] private IEntitySystem[] _frameUpdateOrder = Array.Empty<IEntitySystem>();

        public bool MetricsEnabled { get; set; }

        /// <inheritdoc />
        public event EventHandler<SystemChangedArgs>? SystemLoaded;

        /// <inheritdoc />
        public event EventHandler<SystemChangedArgs>? SystemUnloaded;

        /// <exception cref="UnregisteredTypeException">Thrown when <typeparamref name="T"/> has no registration.</exception>
        public T GetEntitySystem<T>() where T : IEntitySystem
        {
            // Goes straight to the field (not the public property) so that the failure mode before the first
            // Initialize is a NullReferenceException rather than an InvalidOperationException.
            return SystemDependencyCollection.Resolve<T>();
        }

        public T? GetEntitySystemOrNull<T>() where T : IEntitySystem
        {
            SystemDependencyCollection.TryResolveType<T>(out var system);
            return system;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Resolve<T>([NotNull] ref T? instance)
            where T : IEntitySystem
        {
            SystemDependencyCollection.Resolve(ref instance);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Resolve<T1, T2>([NotNull] ref T1? instance1, [NotNull] ref T2? instance2)
            where T1 : IEntitySystem
            where T2 : IEntitySystem
        {
            SystemDependencyCollection.Resolve(ref instance1, ref instance2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Resolve<T1, T2, T3>([NotNull] ref T1? instance1, [NotNull] ref T2? instance2, [NotNull] ref T3? instance3)
            where T1 : IEntitySystem
            where T2 : IEntitySystem
            where T3 : IEntitySystem
        {
            SystemDependencyCollection.Resolve(ref instance1, ref instance2, ref instance3);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Resolve<T1, T2, T3, T4>([NotNull] ref T1? instance1, [NotNull] ref T2? instance2, [NotNull] ref T3? instance3, [NotNull] ref T4? instance4)
            where T1 : IEntitySystem
            where T2 : IEntitySystem
            where T3 : IEntitySystem
            where T4 : IEntitySystem
        {
            SystemDependencyCollection.Resolve(ref instance1, ref instance2, ref instance3, ref instance4);
        }

        /// <inheritdoc />
        public bool TryGetEntitySystem<T>([NotNullWhen(true)] out T? entitySystem) where T : IEntitySystem
        {
            return SystemDependencyCollection.TryResolveType(out entitySystem);
        }

        /// <inheritdoc />
        public void Initialize(bool discover = true)
        {
            // Repeated calls are harmless: EntityManager calls this directly, so no assertion here.
            if (_initialized)
                return;

            // Start from a clean per-manager container that falls back to the global one.
            SystemDependencyCollection = new DependencyCollection(_dependencyCollection);
            _systemTypes.Clear();

            // Gather candidate system types, optionally including everything found by reflection.
            IEnumerable<Type> candidates = discover
                ? _reflectionManager.GetAllChildren<IEntitySystem>().Concat(_extraLoadedTypes)
                : _extraLoadedTypes;

            // Supertypes shared by several systems cannot be aliases; supertypes with exactly one implementer can.
            var ambiguous = new HashSet<Type>();
            var aliasToSystem = new Dictionary<Type, Type>();

            foreach (var systemType in candidates)
            {
                _sawmill.Debug("Initializing entity system {0}", systemType);

                SystemDependencyCollection.Register(systemType);
                _systemTypes.Add(systemType);

                // A concrete system is never an alias for anything else.
                ambiguous.Add(systemType);
                aliasToSystem.Remove(systemType);

                foreach (var superType in GetBaseTypes(systemType))
                {
                    if (ambiguous.Contains(superType))
                        continue;

                    if (aliasToSystem.Remove(superType))
                    {
                        // A second implementer showed up, so this supertype is ambiguous from now on.
                        ambiguous.Add(superType);
                    }
                    else
                    {
                        aliasToSystem[superType] = systemType;
                    }
                }
            }

            // Expose each uniquely implemented supertype as an alias of its system, and make sure
            // the alias is not initialized or shut down as a separate system.
            foreach (var (superType, systemType) in aliasToSystem)
            {
                SystemDependencyCollection.Register(superType, systemType, overwrite: true);
                _systemTypes.Remove(superType);
            }

            // Allow systems to depend on EntityQuery<T>: any closed EntityQuery<X> is created on demand
            // through EntityManager.GetEntityQuery<X>().
            SystemDependencyCollection.RegisterBaseGenericLazy(typeof(EntityQuery<>), (closedType, services) =>
            {
                var method = typeof(EntityManager)
                    .GetMethod(nameof(EntityManager.GetEntityQuery), 1, Type.EmptyTypes)!
                    .MakeGenericMethod(closedType.GenericTypeArguments[0]);

                return method.Invoke(services.Resolve<IEntityManager>(), null)!;
            });

            // Instantiate every registered system and inject its dependencies.
            SystemDependencyCollection.BuildGraph();

            // Initialize in registration order and announce each system right after it is ready.
            foreach (var systemType in _systemTypes)
            {
                var system = (IEntitySystem) SystemDependencyCollection.ResolveType(systemType);
                system.Initialize();
                SystemLoaded?.Invoke(this, new SystemChangedArgs(system));
            }

            // Only now compute update ordering; ordering errors therefore appear after all Initialize calls.
            var (frameUpdates, tickUpdates) = CalculateUpdateOrder(_systemTypes, aliasToSystem, SystemDependencyCollection);

            _frameUpdateOrder = frameUpdates.ToArray();
            _updateOrder = tickUpdates
                .Select(system => new UpdateReg
                {
                    System = system,
                    Monitor = _tickUsageHistogram.WithLabels(system.GetType().Name)
                })
                .ToArray();

            _initialized = true;
        }

        private static (IEnumerable<IEntitySystem> frameUpd, IEnumerable<IEntitySystem> upd)
            CalculateUpdateOrder(
                List<Type> systemTypes,
                Dictionary<Type, Type> subTypes,
                DependencyCollection dependencyCollection)
        {
            var allNodes = new List<TopologicalSort.GraphNode<IEntitySystem>>();
            var typeToNode = new Dictionary<Type, TopologicalSort.GraphNode<IEntitySystem>>();

            foreach (var systemType in systemTypes)
            {
                var node = new TopologicalSort.GraphNode<IEntitySystem>((IEntitySystem) dependencyCollection.ResolveType(systemType));

                typeToNode.Add(systemType, node);
                allNodes.Add(node);
            }

            foreach (var (type, system) in subTypes)
            {
                var node = typeToNode[system];
                typeToNode[type] = node;
            }

            foreach (var node in typeToNode.Values)
            {
                foreach (var after in node.Value.UpdatesAfter)
                {
                    var system = typeToNode[after];

                    system.Dependant.Add(node);
                }

                foreach (var before in node.Value.UpdatesBefore)
                {
                    var system = typeToNode[before];

                    node.Dependant.Add(system);
                }
            }

            var order = TopologicalSort.Sort(allNodes).ToArray();
            var frameUpdate = order.Where(p => NeedsFrameUpdate(p.GetType()));
            var update = order.Where(p => NeedsUpdate(p.GetType()));

            return (frameUpdate, update);
        }

        private static IEnumerable<Type> GetBaseTypes(Type type) {
            if(type.BaseType == null) return type.GetInterfaces();

            return Enumerable.Repeat(type.BaseType, 1)
                .Concat(type.GetInterfaces())
                .Concat(type.GetInterfaces().SelectMany<Type, Type>(GetBaseTypes))
                .Concat(GetBaseTypes(type.BaseType));
        }

        /// <inheritdoc />
        public void Shutdown()
        {
            // Registration order, not reversed. Exceptions are intentionally not caught here.
            foreach (var systemType in _systemTypes)
            {
                if (SystemDependencyCollection == null)
                    continue;

                var system = (IEntitySystem) SystemDependencyCollection.ResolveType(systemType);

                // Notify listeners before the system actually goes away.
                SystemUnloaded?.Invoke(this, new SystemChangedArgs(system));
                system.Shutdown();
                _entityManager.EventBus.UnsubscribeEvents(system);
            }

            Clear();
        }

        public void Clear()
        {
            _extraLoadedTypes.Clear();
            _systemTypes.Clear();
            _updateOrder = Array.Empty<UpdateReg>();
            _frameUpdateOrder = Array.Empty<IEntitySystem>();
            _initialized = false;
            SystemDependencyCollection?.Clear();
        }

        /// <inheritdoc />
        public void TickUpdate(float frameTime, bool noPredictions)
        {
            foreach (var reg in _updateOrder)
            {
                if (noPredictions && !reg.System.UpdatesOutsidePrediction)
                    continue;

                if (MetricsEnabled)
                    _stopwatch.Restart();

#if EXCEPTION_TOLERANCE
                try
                {
#endif
                    using (_profManager.Value(reg.System.GetType().Name))
                    {
                        reg.System.Update(frameTime);
                    }
#if EXCEPTION_TOLERANCE
                }
                catch (Exception e)
                {
                    _runtimeLog.LogException(e, "entsys");
                }
#endif

                // The flag is deliberately re-read, so it may differ from the check before the update.
                if (MetricsEnabled)
                    reg.Monitor.Observe(_stopwatch.Elapsed.TotalSeconds);
            }
        }

        /// <inheritdoc />
        public void FrameUpdate(float frameTime)
        {
            foreach (var system in _frameUpdateOrder)
            {
#if EXCEPTION_TOLERANCE
                try
                {
#endif
                    using (_profManager.Value(system.GetType().Name))
                    {
                        system.FrameUpdate(frameTime);
                    }
#if EXCEPTION_TOLERANCE
                }
                catch (Exception e)
                {
                    _runtimeLog.LogException(e, "entsys");
                }
#endif
            }
        }

        public void LoadExtraSystemType<T>() where T : IEntitySystem, new()
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "Cannot use LoadExtraSystemType when the entity system manager is initialized.");
            }

            _extraLoadedTypes.Add(typeof(T));
        }

        public IEnumerable<Type> GetEntitySystemTypes()
        {
            return _systemTypes;
        }

        public bool TryGetEntitySystem(Type sysType, [NotNullWhen(true)] out object? system)
        {
            return SystemDependencyCollection.TryResolveType(sysType, out system);
        }

        public object GetEntitySystem(Type sysType)
        {
            return SystemDependencyCollection.ResolveType(sysType);
        }

        private static bool NeedsUpdate(Type type)
        {
            if (!typeof(EntitySystem).IsAssignableFrom(type))
            {
                return true;
            }

            var mUpdate = type.GetMethod(nameof(EntitySystem.Update), new[] {typeof(float)});

            DebugTools.AssertNotNull(mUpdate);

            return mUpdate!.DeclaringType != typeof(EntitySystem);
        }

        private static bool NeedsFrameUpdate(Type type)
        {
            if (!typeof(EntitySystem).IsAssignableFrom(type))
            {
                return true;
            }

            var mFrameUpdate = type.GetMethod(nameof(EntitySystem.FrameUpdate), new[] {typeof(float)});

            DebugTools.AssertNotNull(mFrameUpdate);

            return mFrameUpdate!.DeclaringType != typeof(EntitySystem);
        }

        internal IEnumerable<Type> FrameUpdateOrder => _frameUpdateOrder.Select(c => c.GetType());
        internal IEnumerable<Type> TickUpdateOrder => _updateOrder.Select(c => c.System.GetType());

        private struct UpdateReg
        {
            [ViewVariables] public IEntitySystem System;
            [ViewVariables] public Histogram.Child Monitor;

            public override string? ToString()
            {
                return System.ToString();
            }
        }

        void IPostInjectInit.PostInject()
        {
            _sawmill = _logManager.GetSawmill("go.sys");
        }
    }

    public sealed class SystemChangedArgs : EventArgs
    {
        public IEntitySystem System { get; }

        public SystemChangedArgs(IEntitySystem system)
        {
            System = system;
        }
    }
}
