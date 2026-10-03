using System.Collections.Generic;
using Robust.Shared.Analyzers;

namespace Robust.Shared.Input
{
    /// <summary>
    ///     Tracks the current <see cref="BoundKeyState"/> of each <see cref="BoundKeyFunction"/> for one player.
    /// </summary>
    /// <remarks>
    ///     A function that has never been set is reported as <see cref="BoundKeyState.Up"/>.
    /// </remarks>
    [NotContentImplementable]
    public interface IPlayerCommandStates
    {
        /// <summary>
        ///     Gets or sets the state of a function. Reading behaves like <see cref="GetState"/> and
        ///     writing behaves like <see cref="SetState"/>.
        /// </summary>
        /// <param name="function">The function whose state is accessed.</param>
        BoundKeyState this[BoundKeyFunction function] { get; set; }

        /// <summary>
        ///     Returns the state currently recorded for the given key function.
        /// </summary>
        /// <param name="function">The function to query.</param>
        /// <returns>
        ///     The state last stored for the function, or <see cref="BoundKeyState.Up"/> if none was ever stored.
        /// </returns>
        BoundKeyState GetState(BoundKeyFunction function);

        /// <summary>
        ///     Stores the state of a function, replacing any previous state.
        /// </summary>
        /// <param name="function">The function to update.</param>
        /// <param name="state">The new state.</param>
        void SetState(BoundKeyFunction function, BoundKeyState state);
    }

    /// <summary>
    ///     The engine implementation of <see cref="IPlayerCommandStates"/>, backed by a dictionary.
    /// </summary>
    /// <remarks>
    ///     Only functions that have been set explicitly take up storage. Reading never adds entries,
    ///     and no change notifications are raised.
    ///     Instances are not thread-safe and are meant to be used from a single thread, typically the one
    ///     that processes input.
    /// </remarks>
    public sealed class PlayerCommandStates : IPlayerCommandStates
    {
        private readonly Dictionary<BoundKeyFunction, BoundKeyState> _states = new();

        /// <inheritdoc />
        public BoundKeyState this[BoundKeyFunction function]
        {
            get => GetState(function);
            set => SetState(function, value);
        }

        /// <summary>
        ///     Gets the current state of a function without modifying this object.
        /// </summary>
        /// <param name="function">The function to query.</param>
        /// <returns>
        ///     The state last stored for the function, or <see cref="BoundKeyState.Up"/> if none was ever stored.
        /// </returns>
        public BoundKeyState GetState(BoundKeyFunction function)
        {
            return _states.TryGetValue(function, out var state) ? state : BoundKeyState.Up;
        }

        /// <summary>
        ///     Stores the state of a function, adding an entry if needed and overwriting an existing one otherwise.
        ///     The value is stored as given; storing <see cref="BoundKeyState.Up"/> does not remove the entry.
        /// </summary>
        /// <param name="function">The function to update.</param>
        /// <param name="state">The new state.</param>
        public void SetState(BoundKeyFunction function, BoundKeyState state)
        {
            _states[function] = state;
        }
    }
}
