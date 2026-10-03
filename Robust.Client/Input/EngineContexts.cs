using Robust.Shared.Input;

namespace Robust.Client.Input
{
    /// <summary>
    ///     Helper that registers the input contexts the engine provides by default.
    /// </summary>
    internal static class EngineContexts
    {
        /// <summary>
        ///     Populates a context container with the engine's standard input contexts: the root "common"
        ///     context holding every shared key function, plus the "editor" and "human" child contexts.
        /// </summary>
        /// <param name="contexts">The container that will receive the default contexts.</param>
        public static void SetupContexts(IInputContextContainer contexts)
        {
            // The root context already exists in the container; only fill it.
            var common = contexts.GetContext(InputContextContainer.DefaultContextName);

            var commonFunctions = new[]
            {
                // Interaction.
                EngineKeyFunctions.Use,
                EngineKeyFunctions.UseSecondary,
                EngineKeyFunctions.UIClick,
                EngineKeyFunctions.UIRightClick,

                // General, movement and camera.
                EngineKeyFunctions.CloseModals,
                EngineKeyFunctions.EscapeMenu,
                EngineKeyFunctions.HideUI,
                EngineKeyFunctions.ShowDebugConsole,
                EngineKeyFunctions.ShowDebugMonitors,
                EngineKeyFunctions.MoveUp,
                EngineKeyFunctions.MoveDown,
                EngineKeyFunctions.MoveLeft,
                EngineKeyFunctions.MoveRight,
                EngineKeyFunctions.Walk,
                EngineKeyFunctions.CameraRotateRight,
                EngineKeyFunctions.CameraRotateLeft,
                EngineKeyFunctions.CameraReset,

                // GUI tab navigation.
                EngineKeyFunctions.GuiTabNavigateNext,
                EngineKeyFunctions.GuiTabNavigatePrev,

                // Window closing.
                EngineKeyFunctions.WindowCloseRecent,
                EngineKeyFunctions.WindowCloseAll,

                // Text cursor movement.
                EngineKeyFunctions.TextCursorLeft,
                EngineKeyFunctions.TextCursorRight,
                EngineKeyFunctions.TextCursorUp,
                EngineKeyFunctions.TextCursorDown,
                EngineKeyFunctions.TextCursorWordLeft,
                EngineKeyFunctions.TextCursorWordRight,
                EngineKeyFunctions.TextCursorBegin,
                EngineKeyFunctions.TextCursorEnd,

                // Text selection.
                EngineKeyFunctions.TextCursorSelect,
                EngineKeyFunctions.TextCursorSelectLeft,
                EngineKeyFunctions.TextCursorSelectRight,
                EngineKeyFunctions.TextCursorSelectUp,
                EngineKeyFunctions.TextCursorSelectDown,
                EngineKeyFunctions.TextCursorSelectWordLeft,
                EngineKeyFunctions.TextCursorSelectWordRight,
                EngineKeyFunctions.TextCursorSelectBegin,
                EngineKeyFunctions.TextCursorSelectEnd,

                // Text editing.
                EngineKeyFunctions.TextBackspace,
                EngineKeyFunctions.TextDelete,
                EngineKeyFunctions.TextWordBackspace,
                EngineKeyFunctions.TextWordDelete,
                EngineKeyFunctions.TextNewline,
                EngineKeyFunctions.TextSubmit,
                EngineKeyFunctions.MultilineTextSubmit,
                EngineKeyFunctions.TextCopy,
                EngineKeyFunctions.TextCut,
                EngineKeyFunctions.TextPaste,
                EngineKeyFunctions.TextSelectAll,
                EngineKeyFunctions.TextHistoryPrev,
                EngineKeyFunctions.TextHistoryNext,
                EngineKeyFunctions.TextReleaseFocus,
                EngineKeyFunctions.TextScrollToBottom,
                EngineKeyFunctions.TextTabComplete,
                EngineKeyFunctions.TextCompleteNext,
                EngineKeyFunctions.TextCompletePrev,
            };

            foreach (var function in commonFunctions)
            {
                common.AddFunction(function);
            }

            // Editor context: inherits everything from common and adds the placement controls.
            var editor = contexts.New("editor", common);
            editor.AddFunction(EngineKeyFunctions.EditorLinePlace);
            editor.AddFunction(EngineKeyFunctions.EditorGridPlace);
            editor.AddFunction(EngineKeyFunctions.EditorPlaceObject);
            editor.AddFunction(EngineKeyFunctions.EditorCancelPlace);
            editor.AddFunction(EngineKeyFunctions.EditorRotateObject);

            // Human context: only inherits from common; it is created empty on purpose.
            contexts.New("human", common);
        }
    }
}
