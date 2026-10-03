using System.Collections.Generic;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Robust.Shared.Map
{
    /// <summary>
    ///     Describes one kind of grid tile.
    /// </summary>
    public interface ITileDefinition : IPrototype
    {
        /// <summary>
        ///     The numeric identifier under which this tile type is stored in map grid data.
        ///     The engine never invents these numbers; they are assigned through <see cref="AssignTileId"/>.
        /// </summary>
        ushort TileId { get; }

        /// <summary>
        ///     Human-readable, user-visible name of this tile type. It is not an identifier for lookups.
        /// </summary>
        string Name { get; }

        /// <summary>
        ///     Resource path of the texture drawn for this tile, or <see langword="null"/> if the tile
        ///     has no sprite and draws nothing.
        /// </summary>
        ResPath? Sprite { get; }

        /// <summary>
        ///     Possible sprites to use if we're neighboring another tile.
        /// </summary>
        Dictionary<Direction, ResPath> EdgeSprites { get; }

        /// <summary>
        ///     When drawing adjacent tiles that both specify edge sprites, the one with the higher priority
        ///     is always solely drawn.
        /// </summary>
        int EdgeSpritePriority { get; }

        /// <summary>
        ///     Physics objects that are interacting on this tile are slowed down by this float.
        /// </summary>
        float Friction { get; }

        /// <summary>
        ///     Number of variants this tile has. ALSO DETERMINES THE EXPECTED INPUT TEXTURE SIZE.
        /// </summary>
        byte Variants { get; }

        /// <summary>
        ///     Allows the tile to be rotated/mirrored when placed on a grid.
        /// </summary>
        bool AllowRotationMirror => false;

        /// <summary>
        ///     Called when this definition is registered with the tile registry. Stores the id so that later
        ///     reads of <see cref="TileId"/> return it, overwriting any previous value.
        /// </summary>
        /// <param name="id">The new numeric tile id.</param>
        void AssignTileId(ushort id);

        /// <summary>
        ///     Allows you to hide tiles from the tile spawn menu.
        /// </summary>
        bool EditorHidden => false;
    }
}
