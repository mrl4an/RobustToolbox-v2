using Robust.Shared.IoC.Exceptions;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Robust.Shared.Reflection;
using Robust.Shared.Utility;
using NotNull = System.Diagnostics.CodeAnalysis.NotNullAttribute;

namespace Robust.Shared.IoC
{
    /// <summary>
    /// The IoCManager handles Dependency Injection in the project.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Dependency Injection is a concept where instead of saying "I need the <c>EntityManager</c>",
    /// you say "I need something that implements <c>IEntityManager</c>".
    /// This decouples the various systems into swappable components that have standardized interfaces.
    /// </para>
    /// <para>
    /// This is useful for a couple of things.
    /// Firstly, it allows the shared code to request the client or server code implicitly, without hacks.
    /// Secondly, it's very useful for unit tests as we can replace components to test things.
    /// </para>
    /// <para>
    /// To use the IoCManager, it first needs some types registered through <see cref="Register{TInterface, TImplementation}"/>.
    /// These implementations can then be fetched with <see cref="Resolve{T}"/>, or through field injection with <see cref="DependencyAttribute" />.
    /// </para>
    /// <para>
    /// <c>IoCManager</c> is actually a static wrapper class around a thread local <see cref="IDependencyCollection"/>.
    /// As such, <c>IoCManager</c> will not work in other threads,
    /// unless they have first been initialized with <see cref="InitThread()"/> or <see cref="InitThread(IDependencyCollection,bool)"/>.
    /// You should not initialize IoC in thread pools like that of <see cref="Task.Run(Action)"/>,
    /// since said thread pool might be used by different running instances
    /// (for example, server and client running in the same process, they have a different IoC instance).
    /// </para>
    /// </remarks>
    /// <seealso cref="IReflectionManager"/>
    public static class IoCManager
    {
        private const string NoContextAssert = "IoC has no context on this thread. Are you calling IoC from the wrong thread or did you forget to initialize it?";
        private static readonly ThreadLocal<IDependencyCollection> _container = new();

        /// <summary>
        /// Returns the singleton thread-local instance of the IoCManager's dependency collection.
        /// </summary>
        /// <remarks>
        /// This property will be null if <see cref="InitThread()"/> has not been called on this thread yet.
        /// </remarks>
        public static IDependencyCollection? Instance => _container.IsValueCreated ? _container.Value : null;

        /// <summary>
        /// Ensures that the <see cref="IDependencyCollection"/> instance exists for this thread.
        /// </summary>
        /// <remarks>
        /// This will create a new instance of a <see cref="IDependencyCollection"/> for this thread,
        /// otherwise it will do nothing if one already exists.
        /// </remarks>
        /// <returns>The dependency collection for this thread.</returns>
        public static IDependencyCollection InitThread()
        {
            if (_container.IsValueCreated)
            {
                return _container.Value!;
            }

            var deps = new DependencyCollection();
            _container.Value = deps;
            return deps;
        }

        /// <summary>
        /// Sets an existing <see cref="IDependencyCollection"/> as the instance for this thread.
        /// </summary>
        /// <exception cref="InvalidOperationException">Will be thrown if a <see cref="IDependencyCollection"/> instance is already set for this thread,
        /// and replaceExisting is set to false.</exception>
        /// <param name="collection">Collection to set as the instance for this thread.</param>
        /// <param name="replaceExisting">If this is true, replaces the existing collection, if one is set for this thread.</param>
        public static void InitThread(IDependencyCollection collection, bool replaceExisting=false)
        {
            if (_container.IsValueCreated && !replaceExisting)
            {
                throw new InvalidOperationException("This thread has already been initialized.");
            }

            _container.Value = collection;
        }

        /// <summary>
        /// Registers an interface to an implementation, so that it can be resolved later.
        /// </summary>
        /// <typeparam name="TInterface">The type that will be resolved.</typeparam>
        /// <typeparam name="TImplementation">The type that is constructed to satisfy <typeparamref name="TInterface"/>.</typeparam>
        /// <param name="overwrite">
        /// If false, registering an interface that already has an entry is an error.
        /// If true, the existing entry is replaced.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if <paramref name="overwrite"/> is false and the interface is already registered,
        /// or if the replacement would overwrite an interface that has already been instantiated.
        /// </exception>
        public static void Register<TInterface, [MeansImplicitUse] TImplementation>(bool overwrite = false)
            where TImplementation : class, TInterface
            where TInterface : class
        {
            DebugTools.Assert(_container.IsValueCreated, NoContextAssert);

            _container.Value!.Register<TInterface, TImplementation>(overwrite);
        }

        /// <summary>
        /// Registers a type as both its own interface and its own implementation.
        /// </summary>
        /// <typeparam name="T">The type to register.</typeparam>
        /// <param name="overwrite">Whether an existing registration may be replaced.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if <paramref name="overwrite"/> is false and the type is already registered,
        /// or if the replacement would overwrite a type that has already been instantiated.
        /// </exception>
        public static void Register<[MeansImplicitUse] T>(bool overwrite = false) where T : class
        {
            Register<T, T>(overwrite);
        }

        /// <summary>
        /// Registers an interface to an implementation that is built by a caller-supplied factory.
        /// </summary>
        /// <remarks>
        /// <see cref="BuildGraph"/> must be called afterwards before the new interface can be resolved.
        /// </remarks>
        /// <typeparam name="TInterface">The type that will be resolved.</typeparam>
        /// <typeparam name="TImplementation">The concrete type produced by the factory.</typeparam>
        /// <param name="factory">Delegate that creates the implementation instance.</param>
        /// <param name="overwrite">Whether an existing registration may be replaced.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if <paramref name="overwrite"/> is false and the interface is already registered,
        /// or if the replacement would overwrite an interface that has already been instantiated.
        /// </exception>
        public static void Register<TInterface, TImplementation>(
            DependencyFactoryDelegate<TImplementation> factory,
            bool overwrite = false)
            where TImplementation : class, TInterface
            where TInterface : class
        {
            DebugTools.Assert(_container.IsValueCreated, NoContextAssert);

            _container.Value!.Register<TInterface, TImplementation>(factory, overwrite);
        }

        /// <summary>
        /// Registers an already constructed object as the implementation of an interface.
        /// </summary>
        /// <remarks>
        /// Unlike <c>Register</c>, no <see cref="BuildGraph"/> call is needed afterwards,
        /// except when deferred injection is being used.
        /// </remarks>
        /// <typeparam name="TInterface">The type that will be resolved.</typeparam>
        /// <param name="implementation">The object to hand out for <typeparamref name="TInterface"/>.</param>
        /// <param name="overwrite">If false, an already registered interface causes an exception.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if <paramref name="overwrite"/> is false and the interface is already registered.
        /// </exception>
        public static void RegisterInstance<TInterface>(object implementation, bool overwrite = false) where TInterface : class
        {
            DebugTools.Assert(_container.IsValueCreated, NoContextAssert);

            _container.Value!.RegisterInstance<TInterface>(implementation, overwrite);
        }

        /// <summary>
        /// Drops all registrations of the collection on this thread. Intended for unit tests and shutdown.
        /// Services that implement <see cref="IDisposable"/> are disposed by the collection.
        /// Does nothing if this thread has no collection.
        /// </summary>
        public static void Clear()
        {
            if (!_container.IsValueCreated)
            {
                return;
            }

            _container.Value!.Clear();
        }

        /// <summary>
        /// Resolves a registered service.
        /// </summary>
        /// <typeparam name="T">The interface (or type) to look up.</typeparam>
        /// <returns>The instance registered for <typeparamref name="T"/>.</returns>
        /// <exception cref="UnregisteredTypeException">Thrown if the type is not registered.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the type is registered but has not been constructed yet
        /// because <see cref="BuildGraph"/> is still pending.
        /// </exception>
        [System.Diagnostics.Contracts.Pure]
        public static T Resolve<T>()
        {
            DebugTools.Assert(_container.IsValueCreated, NoContextAssert);

            return _container.Value!.Resolve<T>();
        }

        /// <inheritdoc cref="Resolve{T}()"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Resolve<T>([NotNull] ref T? instance)
        {
            // Do not call into IDependencyCollection immediately for this,
            // avoids thread local lookup if instance is already given.
            instance ??= Resolve<T>()!;
        }

        /// <inheritdoc cref="Resolve{T}(ref T?)"/>
        /// <summary>
        /// Resolve two dependencies manually.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Resolve<T1, T2>([NotNull] ref T1? instance1, [NotNull] ref T2? instance2)
        {
            DebugTools.Assert(_container.IsValueCreated, NoContextAssert);

            _container.Value!.Resolve(ref instance1, ref instance2);
        }

        /// <inheritdoc cref="Resolve{T1, T2}(ref T1?, ref T2?)"/>
        /// <summary>
        /// Resolve three dependencies manually.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Resolve<T1, T2, T3>([NotNull] ref T1? instance1, [NotNull] ref T2? instance2, [NotNull] ref T3? instance3)
        {
            DebugTools.Assert(_container.IsValueCreated, NoContextAssert);

            _container.Value!.Resolve(ref instance1, ref instance2, ref instance3);
        }

        /// <inheritdoc cref="Resolve{T1, T2, T3}(ref T1?, ref T2?, ref T3?)"/>
        /// <summary>
        /// Resolve four dependencies manually.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Resolve<T1, T2, T3, T4>([NotNull] ref T1? instance1, [NotNull] ref T2? instance2, [NotNull] ref T3? instance3, [NotNull] ref T4? instance4)
        {
            DebugTools.Assert(_container.IsValueCreated, NoContextAssert);

            _container.Value!.Resolve(ref instance1, ref instance2, ref instance3, ref instance4);
        }

        /// <summary>
        /// Resolve a dependency manually.
        /// </summary>
        /// <exception cref="UnregisteredTypeException">Thrown if the interface is not registered.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the resolved type hasn't been created yet
        /// because the object graph still needs to be constructed for it.
        /// </exception>
        [System.Diagnostics.Contracts.Pure]
        public static object ResolveType(Type type)
        {
            DebugTools.Assert(_container.IsValueCreated, NoContextAssert);

            return _container.Value!.ResolveType(type);
        }

        /// <summary>
        /// Initializes the object graph by building every object and resolving all dependencies.
        /// </summary>
        /// <seealso cref="InjectDependencies{T}"/>
        public static void BuildGraph()
        {
            DebugTools.Assert(_container.IsValueCreated, NoContextAssert);

            _container.Value!.BuildGraph();
        }

        /// <summary>
        /// Fills every field of <paramref name="obj"/> that is marked with <see cref="DependencyAttribute"/>.
        /// Meant for objects that were not created by IoC.
        /// </summary>
        /// <remarks>
        /// <see cref="IPostInjectInit"/> hooks are not run by this method.
        /// </remarks>
        /// <typeparam name="T">The type of the object receiving dependencies.</typeparam>
        /// <param name="obj">The object to inject into.</param>
        /// <returns>The same <paramref name="obj"/> reference.</returns>
        /// <exception cref="UnregisteredDependencyException">
        /// Thrown if a dependency field has a type that is not registered.
        /// </exception>
        public static T InjectDependencies<T>(T obj) where T : notnull
        {
            DebugTools.Assert(_container.IsValueCreated, NoContextAssert);

            _container.Value!.InjectDependencies(obj);

            return obj;
        }
    }
}
