using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;

namespace Robust.Client.Input
{
    /// <summary>
    ///     Manages key bindings, input commands and other misc. input systems.
    /// </summary>
    [NotContentImplementable]
    public interface IInputManager
    {
        bool Enabled { get; set; }

        /// <summary>
        ///     Relay a key event that hit a viewport further down the input stack.
        /// </summary>
        /// <remarks>
        ///     This is ONLY intended to be used in key handler from viewport controls.
        /// </remarks>
        /// <param name="control">The viewport control that relayed the input.</param>
        /// <param name="eventArgs">The key event args triggering the input.</param>
        void ViewportKeyEvent(Control? control, BoundKeyEventArgs eventArgs);

        /// <summary>
        ///     The position and window of the mouse.
        /// </summary>
        ScreenCoordinates MouseScreenPosition { get; }

        /// <summary>
        ///     The mapping between key function names and the compact numeric ids used when input is sent
        ///     over the network. The same instance is returned on every access.
        /// </summary>
        BoundKeyMap NetworkBindMap { get; }

        /// <summary>
        ///     The container holding the named input contexts, each a set of active key functions, along with
        ///     the currently active context and the default one. Use it to switch which bindings are live.
        /// </summary>
        IInputContextContainer Contexts { get; }

        /// <summary>
        ///     One-time setup of the input manager. Called once at client startup, before input is used.
        ///     It is not expected to be called twice.
        /// </summary>
        void Initialize();
        void SaveToUserData();

        /// <summary>
        ///     Called by the windowing/input layer when a physical key or mouse button is pressed.
        ///     The manager resolves the press against the active bindings and raises the matching
        ///     bound-key state change events (down or repeat).
        /// </summary>
        /// <param name="e">Describes the key: key, repeat flag, modifiers and window.</param>
        void KeyDown(KeyEventArgs e);
        /// <summary>
        ///     Counterpart of <see cref="KeyDown"/>, called when a key or mouse button is released. The manager
        ///     updates its down state and raises the matching "up" notifications for bindings that were active.
        /// </summary>
        /// <param name="e">Describes the released key, as in <see cref="KeyDown"/>.</param>
        void KeyUp(KeyEventArgs e);

        IKeyBinding RegisterBinding(in KeyBindingRegistration reg, bool markModified=true, bool invalid=false);

        void RemoveBinding(IKeyBinding binding, bool markModified=true);

        /// <summary>
        ///     Gets a key binding according to the function it is bound to.
        /// </summary>
        /// <remarks>
        ///     Which keybind is returned if there are multiple for a key function is unspecified.
        /// </remarks>
        /// <param name="function">The function the key binding is bound to.</param>
        /// <returns>The key binding.</returns>
        /// <exception cref="KeyNotFoundException">
        ///     Thrown if no there is no keybind for the specified function.
        /// </exception>
        IKeyBinding GetKeyBinding(BoundKeyFunction function);

        /// <remarks>
        ///     Which keybind is returned if there are multiple for a key function is unspecified.
        /// </remarks>
        bool TryGetKeyBinding(BoundKeyFunction function, [NotNullWhen(true)] out IKeyBinding? binding);

        /// <summary>
        ///     Returns the input command bound to a key function.
        /// </summary>
        /// <param name="function">The key function to find the bound input command for.</param>
        /// <returns>An input command, if any. Null if no command is set.</returns>
        InputCmdHandler? GetInputCommand(BoundKeyFunction function);

        void SetInputCommand(BoundKeyFunction function, InputCmdHandler? cmdHandler);

        /// <summary>
        ///     UIKeyBindStateChanged is called when a keybind is found.
        /// </summary>
        event Func<BoundKeyEventArgs, bool> UIKeyBindStateChanged;

        /// <summary>
        /// Called to check if the UI is considered "focused".
        /// </summary>
        /// <remarks>
        /// This is effectively similar to the return value of <see cref="UIKeyBindStateChanged"/>,
        /// but may be called at different times.
        /// </remarks>
        event Func<bool> CheckUIIsFocused;

        /// <summary>
        ///     If UIKeyBindStateChanged did not handle the BoundKeyEvent, KeyBindStateChanged is called.
        /// </summary>
        event Action<ViewportBoundKeyEventArgs>? KeyBindStateChanged;

        IEnumerable<BoundKeyFunction> DownKeyFunctions { get; }

        /// <summary>
        ///     Gets the name of the key on the keyboard, based on the current input method and language.
        /// </summary>
        string GetKeyName(Keyboard.Key key);

        /// <summary>
        ///     Gets a user-presentable, localized & keyboard-adjusted string for which combination
        ///     of buttons is bound to the specified key function.
        /// </summary>
        string GetKeyFunctionButtonString(BoundKeyFunction function);

        /// <summary>
        ///     Gets all the key bindings currently registered into the manager.
        /// </summary>
        IEnumerable<IKeyBinding> AllBindings { get; }

        /// <summary>
        ///     An event that gets fired before everything else when a key event comes in.
        ///     For key down events, the event can be handled to block it.
        /// </summary>
        /// <remarks>
        ///     Do not use this for regular input handling.
        ///     This is a low-level API intended solely for stuff like the key rebinding menu.
        /// </remarks>
        event KeyEventAction FirstChanceOnKeyEvent;

        event Action<IKeyBinding> OnKeyBindingAdded;
        event Action<IKeyBinding> OnKeyBindingRemoved;
        event Action OnInputModeChanged;

        /// <summary>
        ///     Gets all the keybinds bound to a specific function.
        /// </summary>
        IReadOnlyList<IKeyBinding> GetKeyBindings(BoundKeyFunction function);

        /// <summary>
        ///     Resets the bindings for a specific BoundKeyFunction to the defaults from the resource files.
        /// </summary>
        /// <param name="function">The key function to reset the bindings for.</param>
        void ResetBindingsFor(BoundKeyFunction function);

        /// <summary>
        ///     Resets ALL the keybinds to the defaults from the resource files.
        /// </summary>
        void ResetAllBindings();

        bool IsKeyFunctionModified(BoundKeyFunction function);

        bool IsKeyDown(Keyboard.Key key);

        void InputModeChanged();
    }
}
