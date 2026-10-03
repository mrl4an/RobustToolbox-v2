using System;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Robust.Shared.GameObjects;

public abstract partial class SharedMapSystem
{
    /// <summary>
    /// Writes a tile into a chunk at chunk-relative indices and raises the tile-modified handling when
    /// the stored value actually changed.
    /// </summary>
    /// <param name="uid">The grid entity.</param>
    /// <param name="grid">The grid component owning the chunk.</param>
    /// <param name="chunk">The chunk to modify.</param>
    /// <param name="xIndex">X index of the tile inside the chunk.</param>
    /// <param name="yIndex">Y index of the tile inside the chunk.</param>
    /// <param name="tile">The tile to store.</param>
    /// <param name="oldTile">The tile that was previously stored at that position.</param>
    /// <returns>True if the chunk was changed, false if the tile was already identical.</returns>
    internal bool SetChunkTile(EntityUid uid, MapGridComponent grid, MapChunk chunk, ushort xIndex, ushort yIndex, Tile tile, out Tile oldTile)
    {
        if (!chunk.TrySetTile(xIndex, yIndex, tile, out oldTile, out var shapeChanged))
            return false;

        var tileIndices = new Vector2i(xIndex, yIndex);
        OnTileModified(uid, grid, chunk, tileIndices, tile, oldTile, shapeChanged);
        return true;
    }
}
