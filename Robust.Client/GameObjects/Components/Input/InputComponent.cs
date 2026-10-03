using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;

namespace Robust.Client.GameObjects;

/// <summary>
///     Names the input command context that the local client activates while the local player is
///     attached to the entity that has this component. The component contains no logic; the client
///     input system reads it when the attached entity changes.
/// </summary>
/// <remarks>
///     This component exists on the client only and is not networked. In prototypes it is written
///     as <c>- type: Input</c> with the <c>context</c> field.
/// </remarks>
[RegisterComponent]
public sealed partial class InputComponent : Component
{
    /// <summary>
    ///     Name of the input context to activate. Defaults to the shared default context (<c>"common"</c>).
    ///     No validation happens here; whether a context with this name exists is only checked
    ///     when the player attaches, and an unknown name falls back to the default context.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField("context")]
    public string ContextName { get; set; } = InputContextContainer.DefaultContextName;
}
