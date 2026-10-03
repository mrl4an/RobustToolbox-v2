using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Robust.Shared.IoC.Exceptions;
using Robust.Shared.Reflection;
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
    /// This type is thread safe: registration and <see cref="BuildGraph"/> can run concurrently with resolves.
    /// </para>
    /// </remarks>
    /// <seealso cref="IReflectionManager"/>
    [NotContentImplementable]
    public interface IDependencyCollection
    {
        IDependencyCollection FromParent(IDependencyCollection parentCollection);

        /// <summary>
        /// Enumerates over all registered types.
        /// </summary>
        IEnumerable<Type> GetRegisteredTypes();

        /// <summary>
        ///     Registers an implementation type under an interface (key) type. The collection creates the
        ///     instance itself using default construction.
        /// </summary>
        /// <remarks>
        ///     The instance is not created at registration time. It can be resolved only after
        ///     <see cref="BuildGraph"/> has run; resolving earlier throws <see cref="InvalidOperationException"/>.
        /// </remarks>
        /// <typeparam name="TInterface">The type under which the implementation will be resolvable.</typeparam>
        /// <typeparam name="TImplementation">The concrete type that will be instantiated.</typeparam>
        /// <param name="overwrite">
        ///     If true, replace an existing registration for <typeparamref name="TInterface"/> instead of
        ///     throwing.
        /// </param>
        /// <exception cref="InvalidOperationException">
        ///     Thrown if <paramref name="overwrite"/> is false and <typeparamref name="TInterface"/> is already
        ///     registered (this error takes precedence even if the old entry was already built), or if
        ///     <paramref name="overwrite"/> is true but the previous registration was already instantiated by an
        ///     earlier <see cref="BuildGraph"/>.
        /// </exception>
        void Register<TInterface, [MeansImplicitUse] TImplementation>(bool overwrite = false)
            where TImplementation : class, TInterface
            where TInterface : class;

        /// <summary>
        ///     Registers an implementation type under an interface (key) type, using a factory callback to
        ///     create the instance instead of default construction.
        /// </summary>
        /// <remarks>
        ///     The factory is not invoked at registration time; it is called when <see cref="BuildGraph"/>
        ///     runs, and the new interface can be resolved only after that.
        /// </remarks>
        /// <typeparam name="TInterface">The type under which the implementation will be resolvable.</typeparam>
        /// <typeparam name="TImplementation">The concrete type produced by the factory.</typeparam>
        /// <param name="factory">Callback that supplies the instance when the graph is built.</param>
        /// <param name="overwrite">
        ///     If true, replace an existing registration for <typeparamref name="TInterface"/> instead of
        ///     throwing.
        /// </param>
        /// <exception cref="InvalidOperationException">
        ///     Thrown if <paramref name="overwrite"/> is false and <typeparamref name="TInterface"/> is already
        ///     registered, or if <paramref name="overwrite"/> is true but the previous registration was already
        ///     instantiated by an earlier <see cref="BuildGraph"/>.
        /// </exception>
        void Register<TInterface, TImplementation>(DependencyFactoryDelegate<TImplementation> factory, bool overwrite = false)
            where TImplementation : class, TInterface
            where TInterface : class;


        /// <summary>
        /// Registers a simple implementation without an interface.
        /// </summary>
        /// <param name="implementation">The type that will be resolvable.</param>
        /// <param name="factory">A factory method to construct the instance of the implementation.</param>
        /// <param name="overwrite">
        /// If true, do not throw an <see cref="InvalidOperationException"/> if an interface is already registered,
        /// replace the current implementation instead.
        /// </param>
        void Register(Type implementation, DependencyFactoryDelegate<object>? factory = null, bool overwrite = false);

        /// <summary>
        /// Registers a simple implementation without an interface.
        /// </summary>
        /// <param name="interfaceType">The type that will be resolvable.</param>
        /// <param name="implementation">The type that will be resolvable.</param>
        /// <param name="factory">A factory method to construct the instance of the implementation.</param>
        /// <param name="overwrite">
        /// If true, do not throw an <see cref="InvalidOperationException"/> if an interface is already registered,
        /// replace the current implementation instead.
        /// </param>
        void Register(Type interfaceType, Type implementation, DependencyFactoryDelegate<object>? factory = null,
            bool overwrite = false);

        /// <summary>
        ///     Registers an already existing object as the answer for requests for
        ///     <typeparamref name="TInterface"/>. Nothing is constructed.
        /// </summary>
        /// <remarks>
        ///     The instance is intended to be resolvable without a graph build. However, the current
        ///     implementation queues the instance and publishes it on the next <see cref="BuildGraph"/>.
        /// </remarks>
        /// <typeparam name="TInterface">The type under which the object will be resolvable.</typeparam>
        /// <param name="implementation">The existing object; it must be assignable to <typeparamref name="TInterface"/>.</param>
        /// <param name="overwrite">
        ///     If true, replace an existing registration for <typeparamref name="TInterface"/> instead of
        ///     throwing.
        /// </param>
        /// <exception cref="InvalidOperationException">
        ///     Thrown if <paramref name="overwrite"/> is false and the interface is already registered.
        /// </exception>
        void RegisterInstance<TInterface>(object implementation, bool overwrite = false) where TInterface : class;

        /// <summary>
        ///     Registers an already existing object as the answer for requests for the given type. This is the
        ///     non-generic counterpart of <see cref="RegisterInstance{TInterface}"/>.
        /// </summary>
        /// <remarks>
        ///     The instance is intended to be resolvable without a graph build; see the timing note on
        ///     <see cref="RegisterInstance{TInterface}"/>.
        /// </remarks>
        /// <param name="type">The type under which the object will be resolvable.</param>
        /// <param name="implementation">The existing object; its runtime type must be assignable to <paramref name="type"/>.</param>
        /// <param name="overwrite">
        ///     If true, replace an existing registration for <paramref name="type"/> instead of throwing.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="implementation"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        ///     Thrown if the instance is not assignable to <paramref name="type"/>, or if the type is already
        ///     registered and <paramref name="overwrite"/> is false.
        /// </exception>
        void RegisterInstance(Type type, object implementation, bool overwrite = false);

        /// <summary>
        ///     Adds a callback to be called when attempting to resolve an unresolved type that matches the specified
        ///     base generic type, making it accessible to <see cref="IDependencyCollection.Resolve{T}"/>.
        ///     This instance will only be created the first time that it is attempted to be resolved.
        /// </summary>
        /// <param name="genericType">The base generic type of the type that will be resolvable.</param>
        /// <param name="factory">The callback to call to get an instance of the implementation for that generic type.</param>
        void RegisterBaseGenericLazy(Type genericType, DependencyFactoryBaseGenericLazyDelegate<object> factory);

        /// <summary>
        /// Clear all services and types.
        /// Use this between unit tests and on program shutdown.
        /// If a service implements <see cref="IDisposable"/>, <see cref="IDisposable.Dispose"/> will be called on it.
        /// </summary>
        void Clear();

        /// <summary>
        /// Resolve a dependency manually.
        /// </summary>
        /// <exception cref="UnregisteredTypeException">Thrown if the interface is not registered.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the resolved type hasn't been created yet
        /// because the object graph still needs to be constructed for it.
        /// </exception>
        [System.Diagnostics.Contracts.Pure]
        T Resolve<T>();

        /// <inheritdoc cref="Resolve{T}()"/>
        void Resolve<T>([NotNull] ref T? instance);

        /// <inheritdoc cref="Resolve{T}(ref T?)"/>
        /// <summary>
        /// Resolve two dependencies manually.
        /// </summary>
        void Resolve<T1, T2>([NotNull] ref T1? instance1, [NotNull] ref T2? instance2);

        /// <inheritdoc cref="Resolve{T1, T2}(ref T1?, ref T2?)"/>
        /// <summary>
        /// Resolve three dependencies manually.
        /// </summary>
        void Resolve<T1, T2, T3>([NotNull] ref T1? instance1, [NotNull] ref T2? instance2, [NotNull] ref T3? instance3);

        /// <inheritdoc cref="Resolve{T1, T2, T3}(ref T1?, ref T2?, ref T3?)"/>
        /// <summary>
        /// Resolve four dependencies manually.
        /// </summary>
        void Resolve<T1, T2, T3, T4>([NotNull] ref T1? instance1, [NotNull] ref T2? instance2, [NotNull] ref T3? instance3, [NotNull] ref T4? instance4);

        /// <summary>
        /// Resolve a dependency manually.
        /// </summary>
        /// <exception cref="UnregisteredTypeException">Thrown if the interface is not registered.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the resolved type hasn't been created yet
        /// because the object graph still needs to be constructed for it.
        /// </exception>
        [System.Diagnostics.Contracts.Pure]
        object ResolveType(Type type);

        /// <summary>
        /// Resolve a dependency manually.
        /// </summary>
        bool TryResolveType<T>([NotNullWhen(true)] out T? instance);

        /// <summary>
        /// Resolve a dependency manually.
        /// </summary>
        bool TryResolveType(Type objectType, [MaybeNullWhen(false)] out object instance);

        /// <summary>
        ///     Finishes setup of everything registered so far: constructs each pending registered
        ///     implementation (by default construction or its supplied factory), injects fields marked with
        ///     <see cref="DependencyAttribute"/> into them, and processes instances queued by
        ///     <c>RegisterInstance</c>.
        /// </summary>
        /// <remarks>
        ///     Must be called after <c>Register</c> (all overloads) for the newly registered interfaces to become
        ///     resolvable. Instances created by earlier builds are not touched again. Calling it with nothing
        ///     pending is harmless.
        /// </remarks>
        /// <exception cref="UnregisteredDependencyException">
        ///     A dependency field refers to a type that is not registered.
        /// </exception>
        void BuildGraph();

        /// <summary>
        ///     Injects dependencies into all fields with <see cref="DependencyAttribute"/> on the provided object.
        ///     This is useful for objects that are not IoC created, and want to avoid tons of IoC.Resolve() calls.
        /// </summary>
        /// <remarks>
        ///     This does NOT initialize IPostInjectInit objects!
        /// </remarks>
        /// <param name="obj">The object to inject into.</param>
        /// <param name="oneOff">If true, this object type is not expected to be injected commonly.</param>
        /// <exception cref="UnregisteredDependencyException">
        ///     Thrown if a dependency field on the object is not registered.
        /// </exception>
        /// <seealso cref="DependencyCollection.BuildGraph"/>
        void InjectDependencies(object obj, bool oneOff=false);
    }
}
