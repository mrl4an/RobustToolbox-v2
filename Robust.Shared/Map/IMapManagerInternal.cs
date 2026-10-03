using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Robust.Shared.Map
{
    /// <inheritdoc />
    internal interface IMapManagerInternal : IMapManager
    {
        /// <summary>
        ///     Announces that a tile of a grid has been replaced.
        /// </summary>
        /// <param name="entity">The grid entity and its grid component.</param>
        /// <param name="tileRef">Reference to the tile as it is now (the new tile).</param>
        /// <param name="oldTile">The tile value that was replaced.</param>
        /// <param name="chunk">Integer index of the chunk containing the tile.</param>
        void RaiseOnTileChanged(Entity<MapGridComponent> entity, TileRef tileRef, Tile oldTile, Vector2i chunk);
    }
}
