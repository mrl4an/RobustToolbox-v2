using System;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Timing;

namespace Robust.Client.State
{
    internal sealed class StateManager : IStateManager
    {
        [Dependency] private readonly IDynamicTypeFactory _typeFactory = default!;
        [Dependency] private readonly IUserInterfaceManager _interfaceManager = default!;
        public event Action<StateChangedEventArgs>? OnStateChanged;
        public State CurrentState { get; private set; }

        public StateManager()
        {
            CurrentState = new DefaultState();
        }

        /// <summary>
        ///     Passes the per-frame update on to whichever state is currently active.
        /// </summary>
        /// <param name="e">Timing information for the frame.</param>
        public void FrameUpdate(FrameEventArgs e)
        {
            CurrentState?.FrameUpdate(e);
        }

        /// <summary>
        ///     Generic convenience wrapper: switches to state type <typeparamref name="T"/> and returns the
        ///     resulting instance, already typed.
        /// </summary>
        /// <typeparam name="T">The state type to switch to.</typeparam>
        /// <returns>The active state of type <typeparamref name="T"/>.</returns>
        public T RequestStateChange<T>() where T : State, new()
        {
            return (T) RequestStateChange(typeof(T));
        }

        /// <summary>
        ///     Makes the state of the given type the active one. If a state of exactly that type
        ///     is already active, nothing happens and the existing instance is returned.
        /// </summary>
        /// <param name="type">A type deriving from <see cref="State"/>.</param>
        /// <returns>The state instance that is active after the call.</returns>
        /// <exception cref="ArgumentException"><paramref name="type"/> does not derive from <see cref="State"/>.</exception>
        public State RequestStateChange(Type type)
        {
            if (!typeof(State).IsAssignableFrom(type))
            {
                throw new ArgumentException("Needs to be derived from " + typeof(State).FullName, nameof(type));
            }

            // Exact type match only; derived types do not count as "already active".
            if (CurrentState?.GetType() == type)
            {
                return CurrentState;
            }

            return SwitchToState(type);
        }

        /// <summary>
        ///     Replaces the active state with a new instance of <paramref name="type"/>.
        ///     The new instance is built first so that a failing constructor leaves the old state untouched.
        /// </summary>
        /// <param name="type">The concrete state type to instantiate.</param>
        /// <returns>The newly activated state.</returns>
        private State SwitchToState(Type type)
        {
            Logger.Debug($"Switching to state {type}");

            var newState = _typeFactory.CreateInstance<State>(type);

            var previous = CurrentState;
            CurrentState?.ShutdownInternal(_interfaceManager);

            CurrentState = newState;
            newState.StartupInternal(_interfaceManager);

            OnStateChanged?.Invoke(new StateChangedEventArgs(previous, CurrentState));

            return CurrentState;
        }
    }
}
