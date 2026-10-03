using System;
using System.Diagnostics;
using Robust.Shared.Map;

namespace Robust.Shared.Input
{
    /// <summary>
    ///     Information passed along when a bound key changes state.
    /// </summary>
    [Virtual]
    [DebuggerDisplay("{Function}, {State}, CF: {CanFocus}, H: {Handled}")]
    public class BoundKeyEventArgs : EventArgs
    {
        /// <summary>
        ///     The bound key function whose state this event reports on.
        /// </summary>
        public BoundKeyFunction Function { get; }

        /// <summary>
        ///     The new state of <see cref="Function"/>, for example pressed or released.
        /// </summary>
        public BoundKeyState State { get; }

        /// <summary>
        ///     Where the pointer was on screen at the moment the event was raised.
        /// </summary>
        public ScreenCoordinates PointerLocation { get; }

        /// <summary>
        ///     Whether the Bound key can change the focused control.
        /// </summary>
        public bool CanFocus { get; internal set; }

        public bool Handled { get; private set; }

        /// <summary>
        /// Is this a repeated keypress (i.e., are they holding down the key)?
        /// </summary>
        public readonly bool IsRepeat;

        /// <summary>
        ///     Creates event data for a bound key state change. The event starts out unhandled and is not a repeat.
        /// </summary>
        /// <param name="function">The bound key function that changed.</param>
        /// <param name="state">The new state of the function.</param>
        /// <param name="pointerLocation">Pointer position in screen coordinates when the event occurred.</param>
        /// <param name="canFocus">Whether this key may change the focused control.</param>
        public BoundKeyEventArgs(BoundKeyFunction function, BoundKeyState state, ScreenCoordinates pointerLocation,
            bool canFocus)
        {
            Function = function;
            State = state;
            PointerLocation = pointerLocation;
            CanFocus = canFocus;
        }

        /// <summary>
        ///     Creates event data for a bound key state change, optionally marking it as a repeated key press.
        /// </summary>
        /// <param name="function">The bound key function that changed.</param>
        /// <param name="state">The new state of the function.</param>
        /// <param name="pointerLocation">Pointer position in screen coordinates when the event occurred.</param>
        /// <param name="canFocus">Whether this key may change the focused control.</param>
        /// <param name="isRepeat">True if the key is being held down and this is an auto-repeat; false by default.</param>
        public BoundKeyEventArgs(BoundKeyFunction function, BoundKeyState state, ScreenCoordinates pointerLocation,
            bool canFocus, bool isRepeat = false)
            : this(function, state, pointerLocation, canFocus)
        {
            IsRepeat = isRepeat;
        }

        /// <summary>
        ///     Mark this event as handled.
        /// </summary>
        public void Handle()
        {
            Handled = true;
        }
    }
}
