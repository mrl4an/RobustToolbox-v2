using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Analyzers;

namespace Robust.Shared.Input
{
    /// <summary>
    ///     Owns the set of named <see cref="IInputCmdContext"/>s and tracks which one is currently active.
    /// </summary>
    /// <remarks>
    ///     Names are case-sensitive and are never trimmed or normalised. A context named <c>"common"</c> always
    ///     exists. The container does not track parent/child relations; each context holds a reference to its own parent.
    ///     Implementations are not thread-safe and are meant for the main/input thread only.
    /// </remarks>
    [NotContentImplementable]
    public interface IInputContextContainer
    {
        /// <summary>
        ///     The context that is currently active. If a switch is being deferred
        ///     (see <see cref="DeferringEnabled"/>), this still returns the previously active context.
        ///     Never null.
        /// </summary>
        IInputCmdContext ActiveContext { get; }

        /// <summary>
        ///     When true, <see cref="SetActiveContext"/> does not switch right away; the request is remembered and
        ///     only the most recent one is applied. Setting this back to false applies the remembered request,
        ///     if there is one, which raises <see cref="ContextChanged"/> at that moment.
        /// </summary>
        bool DeferringEnabled { get; set; }

        /// <summary>
        ///     Raised synchronously after the active context has been replaced. Handlers that read
        ///     <see cref="ActiveContext"/> see the new context. It is raised on every applied switch, even
        ///     when the new context is the one that was already active, and is not raised for requests that
        ///     are deferred or superseded before being applied.
        /// </summary>
        event EventHandler<ContextChangedEventArgs> ContextChanged;

        /// <summary>
        ///     Creates and registers a new context that derives from a registered context.
        /// </summary>
        /// <param name="uniqueName">Name of the new context. Must not be null, empty or whitespace, and must not be in use.</param>
        /// <param name="parentName">Name of an existing context to use as the parent.</param>
        /// <returns>The newly created context. It has no functions yet.</returns>
        /// <exception cref="ArgumentException">
        ///     Thrown, checked in this order, when <paramref name="uniqueName"/> is null, empty or whitespace
        ///     (also for null); when <paramref name="parentName"/> is null, empty or whitespace; when no context named
        ///     <paramref name="parentName"/> exists; or when a context named <paramref name="uniqueName"/> already exists.
        /// </exception>
        IInputCmdContext New(string uniqueName, string parentName);

        /// <summary>
        ///     Creates and registers a new context that derives from the given parent object.
        /// </summary>
        /// <param name="uniqueName">Name of the new context. Must not be null, empty or whitespace, and must not be in use.</param>
        /// <param name="parent">
        ///     The parent context. It does not have to be registered in this container.
        /// </param>
        /// <returns>The newly created context. It has no functions yet.</returns>
        /// <exception cref="ArgumentException">
        ///     Thrown, checked in this order, when <paramref name="uniqueName"/> is null, empty or whitespace, or
        ///     when a context with that name already exists.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        ///     <paramref name="parent"/> is null. This is checked after the name checks, so an already used name
        ///     is reported first.
        /// </exception>
        IInputCmdContext New(string uniqueName, IInputCmdContext parent);

        /// <summary>
        ///     Checks whether a context with exactly this name is registered.
        /// </summary>
        /// <param name="uniqueName">The name to look for.</param>
        /// <exception cref="ArgumentNullException"><paramref name="uniqueName"/> is null.</exception>
        bool Exists(string uniqueName);

        /// <summary>
        ///     Gets the registered context with the given name.
        /// </summary>
        /// <param name="uniqueName">The name of the context.</param>
        /// <exception cref="KeyNotFoundException">No context with that name is registered.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="uniqueName"/> is null.</exception>
        IInputCmdContext GetContext(string uniqueName);

        /// <summary>
        ///     Tries to get the registered context with the given name.
        /// </summary>
        /// <param name="uniqueName">The name of the context.</param>
        /// <param name="context">The context if found, otherwise null.</param>
        /// <returns>True if a context with that name is registered.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="uniqueName"/> is null.</exception>
        bool TryGetContext(string uniqueName, [NotNullWhen(true)] out IInputCmdContext? context);

        /// <summary>
        ///     Unregisters a context. Does nothing if no context has that name. The default context
        ///     <c>"common"</c> cannot be removed.
        /// </summary>
        /// <remarks>
        ///     This changes nothing else: if the removed context is active it stays active, a pending deferred
        ///     switch to it is still applied later, and contexts that use it as a parent keep their reference.
        /// </remarks>
        /// <param name="uniqueName">The name of the context to remove.</param>
        /// <exception cref="ArgumentException">The name is <c>"common"</c>.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="uniqueName"/> is null.</exception>
        void Remove(string uniqueName);

        /// <summary>
        ///     Makes the named context the active one, or schedules that if <see cref="DeferringEnabled"/> is set.
        /// </summary>
        /// <remarks>
        ///     The name is resolved immediately, so an unknown name is rejected even while deferring. When
        ///     deferring, a new request replaces any earlier pending one.
        /// </remarks>
        /// <param name="uniqueName">The name of the context to activate.</param>
        /// <exception cref="KeyNotFoundException">No context with that name is registered.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="uniqueName"/> is null.</exception>
        void SetActiveContext(string uniqueName);
    }

    /// <inheritdoc cref="IInputContextContainer"/>
    internal sealed class InputContextContainer : IInputContextContainer
    {
        /// <summary>
        ///     Name of the context that always exists and is active at start-up.
        /// </summary>
        public const string DefaultContextName = "common";

        private readonly Dictionary<string, IInputCmdContext> _contexts = new();

        // Assigned by the constructor through SetActiveContext before any caller can observe the instance.
        private IInputCmdContext _activeContext = null!;

        // The single remembered switch while deferring is enabled.
        private IInputCmdContext? _pendingContext;

        private bool _deferringEnabled;

        /// <inheritdoc />
        public event EventHandler<ContextChangedEventArgs>? ContextChanged;

        /// <inheritdoc />
        public IInputCmdContext ActiveContext => _activeContext;

        /// <inheritdoc />
        /// <remarks>
        ///     The new value is stored before a pending switch is applied, and the pending slot is cleared before
        ///     events are raised, so handlers may safely re-enter the container. If a handler throws, the exception
        ///     propagates from the setter, but the flag and the switch have already taken effect.
        /// </remarks>
        public bool DeferringEnabled
        {
            get => _deferringEnabled;
            set
            {
                _deferringEnabled = value;

                if (!value && _pendingContext != null)
                {
                    var pending = _pendingContext;
                    _pendingContext = null;
                    SwitchNow(pending);
                }
            }
        }

        /// <summary>
        ///     Creates the container with the parentless <see cref="DefaultContextName"/> context registered and
        ///     already active. The activation is never deferred.
        /// </summary>
        public InputContextContainer()
        {
            _contexts.Add(DefaultContextName, new InputCmdContext(DefaultContextName));
            SetActiveContext(DefaultContextName);
        }

        /// <inheritdoc />
        public IInputCmdContext New(string uniqueName, string parentName)
        {
            if (string.IsNullOrWhiteSpace(uniqueName))
                throw new ArgumentException("Context name must not be null, empty or whitespace.", nameof(uniqueName));

            if (string.IsNullOrWhiteSpace(parentName))
                throw new ArgumentException("Parent context name must not be null, empty or whitespace.", nameof(parentName));

            if (!_contexts.TryGetValue(parentName, out var parent))
                throw new ArgumentException($"The parent context '{parentName}' does not exist.", nameof(parentName));

            if (_contexts.ContainsKey(uniqueName))
                throw new ArgumentException($"A context named '{uniqueName}' already exists.", nameof(uniqueName));

            var context = new InputCmdContext(parent, uniqueName);
            _contexts.Add(uniqueName, context);
            return context;
        }

        /// <inheritdoc />
        public IInputCmdContext New(string uniqueName, IInputCmdContext parent)
        {
            if (string.IsNullOrWhiteSpace(uniqueName))
                throw new ArgumentException("Context name must not be null, empty or whitespace.", nameof(uniqueName));

            if (_contexts.ContainsKey(uniqueName))
                throw new ArgumentException($"A context named '{uniqueName}' already exists.", nameof(uniqueName));

            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            var context = new InputCmdContext(parent, uniqueName);
            _contexts.Add(uniqueName, context);
            return context;
        }

        /// <inheritdoc />
        public bool Exists(string uniqueName)
        {
            return _contexts.ContainsKey(uniqueName);
        }

        /// <inheritdoc />
        public IInputCmdContext GetContext(string uniqueName)
        {
            return _contexts[uniqueName];
        }

        /// <inheritdoc />
        public bool TryGetContext(string uniqueName, [NotNullWhen(true)] out IInputCmdContext? context)
        {
            return _contexts.TryGetValue(uniqueName, out context);
        }

        /// <inheritdoc />
        public void Remove(string uniqueName)
        {
            if (uniqueName == DefaultContextName)
                throw new ArgumentException("The default context cannot be removed.", nameof(uniqueName));

            _contexts.Remove(uniqueName);
        }

        /// <inheritdoc />
        public void SetActiveContext(string uniqueName)
        {
            // Resolved eagerly so that an unknown name fails at call time even while deferring.
            var target = _contexts[uniqueName];

            if (_deferringEnabled)
            {
                _pendingContext = target;
                return;
            }

            SwitchNow(target);
        }

        /// <summary>
        ///     Replaces the active context and then raises <see cref="ContextChanged"/>.
        /// </summary>
        private void SwitchNow(IInputCmdContext target)
        {
            var args = new ContextChangedEventArgs(_activeContext, target);
            _activeContext = target;
            ContextChanged?.Invoke(this, args);
        }
    }

    /// <summary>
    ///     Data for <see cref="IInputContextContainer.ContextChanged"/>.
    /// </summary>
    public sealed class ContextChangedEventArgs : EventArgs
    {
        /// <summary>
        ///     The context that became active.
        /// </summary>
        public IInputCmdContext? NewContext { get; }

        /// <summary>
        ///     The context that was active before the switch.
        /// </summary>
        public IInputCmdContext? OldContext { get; }

        /// <summary>
        ///     Creates the event data. No validation is done; null is accepted for either context.
        /// </summary>
        /// <param name="oldContext">The context that was active before the switch.</param>
        /// <param name="newContext">The context that became active.</param>
        public ContextChangedEventArgs(IInputCmdContext? oldContext, IInputCmdContext? newContext)
        {
            OldContext = oldContext;
            NewContext = newContext;
        }
    }
}
