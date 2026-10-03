using Robust.Shared.GameObjects;

namespace Robust.Shared.Enums;

public sealed class PlacementInformation
{
    /// <summary>
    /// Entity prototype to be placed
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>
    ///     True if the thing being placed is a tile; in that case <see cref="TileType"/> is used instead of
    ///     <see cref="EntityType"/>.
    /// </summary>
    public bool IsTile { get; set; }

    /// <summary>
    ///     The mob entity that is permitted to carry out this placement.
    /// </summary>
    public EntityUid MobUid { get; set; }

    /// <summary>
    /// Specifies the placement alignment
    /// </summary>
    public string? PlacementOption { get; set; }

    /// <summary>
    ///     Maximum distance from the mob at which the placement is still allowed.
    /// </summary>
    public int Range { get; set; }

    /// <summary>
    /// 
    /// </summary>
    public int TileType { get; set; }

    /// <summary>
    /// Number of times the entity can be placed
    /// </summary>
    public int Uses { get; set; } = 1;

    /// <summary>
    /// Sets whether the input context should switch to 'editor' mode
    /// </summary>
    public bool UseEditorContext { get; set; } = true;
}
