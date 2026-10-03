using System;
using System.Collections.Generic;
using System.IO;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;
using Robust.Shared.Utility;

namespace Robust.Shared.EntitySerialization;

[TypeSerializer]
internal sealed class MapChunkSerializer : ITypeSerializer<MapChunk, MappingDataNode>, ITypeCopyCreator<MapChunk>
{
    public ValidationNode Validate(
        ISerializationManager serializationManager,
        MappingDataNode node,
        IDependencyCollection dependencies,
        ISerializationContext? context = null)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    ///     Reads a grid chunk from its mapping node: the chunk index ("ind"), the base64 tile blob ("tiles"),
    ///     an optional "size" and an optional "version" (defaults to 1) that selects the tile record layout.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     The context is not an <see cref="EntityDeserializer"/> that already has a tile map.
    /// </exception>
    public MapChunk Read(ISerializationManager serializationManager, MappingDataNode node,
        IDependencyCollection dependencies, SerializationHookContext hookCtx, ISerializationContext? context = null,
        ISerializationManager.InstantiationDelegate<MapChunk>? instantiationDelegate = null)
    {
        var ind = (Vector2i) serializationManager.Read(typeof(Vector2i), node["ind"], hookCtx, context)!;

        var tileBytes = Convert.FromBase64String(((ValueDataNode) node["tiles"]).Value);
        using var stream = new MemoryStream(tileBytes);
        using var reader = new BinaryReader(stream);

        var mapManager = dependencies.Resolve<IMapManager>();
        mapManager.SuppressOnTileChanged = true;

        ushort size = 16;
        if (node.TryGet("size", out ValueDataNode? sizeNode))
            size = (ushort) serializationManager.Read(typeof(ushort), sizeNode, context)!;

        var chunk = instantiationDelegate != null
            ? instantiationDelegate()
            : new MapChunk(ind.X, ind.Y, size);

        IReadOnlyDictionary<int, string>? tileMap = null;
        if (context is EntityDeserializer deserializer)
            tileMap = deserializer.TileMap;

        if (tileMap == null)
        {
            throw new InvalidOperationException(
                "Someone tried deserializing a gridchunk before deserializing the tileMap.");
        }

        chunk.SuppressCollisionRegeneration = true;

        var tileDefinitionManager = dependencies.Resolve<ITileDefinitionManager>();

        var version = node.TryGetValue("version", out var versionNode)
            ? ((ValueDataNode) versionNode).AsInt()
            : 1;

        for (ushort y = 0; y < chunk.ChunkSize; y++)
        {
            for (ushort x = 0; x < chunk.ChunkSize; x++)
            {
                int fileId;
                byte flags;
                byte variant;
                byte rotationMirroring = 0;

                if (version >= 7)
                {
                    fileId = reader.ReadInt32();
                    flags = reader.ReadByte();
                    variant = reader.ReadByte();
                    rotationMirroring = reader.ReadByte();
                }
                else
                {
                    fileId = version < 6 ? reader.ReadUInt16() : reader.ReadInt32();
                    flags = reader.ReadByte();
                    variant = reader.ReadByte();
                }

                // Translate file id -> definition name -> runtime tile id.
                var defName = tileMap[fileId];
                var runtimeId = tileDefinitionManager[defName].TileId;

                var tile = version >= 7
                    ? new Tile(runtimeId, flags, variant, rotationMirroring)
                    : new Tile(runtimeId, flags, variant);

                chunk.TrySetTile(x, y, tile, out _, out _);
            }
        }

        chunk.SuppressCollisionRegeneration = false;
        mapManager.SuppressOnTileChanged = false;

        return chunk;
    }

    public DataNode Write(ISerializationManager serializationManager, MapChunk value,
        IDependencyCollection dependencies, bool alwaysWrite = false,
        ISerializationContext? context = null)
    {
        DebugTools.Assert(value.FilledTiles > 0, "Attempting to write an empty chunk");
        var root = new MappingDataNode();
        var ind = new ValueDataNode($"{value.X},{value.Y}");
        root.Add("ind", ind);

        var gridNode = new ValueDataNode();
        root.Add("tiles", gridNode);

        root.Add("version", new ValueDataNode("7"));

        gridNode.Value = SerializeTiles(value, context as EntitySerializer);

        return root;
    }

    /// <summary>
    ///     Encodes all tiles of a chunk (row-major, 7 bytes each: Int32 id, flags, variant, rotation/mirroring)
    ///     as base64. When a serializer is given, runtime tile ids are mapped to the ids of the file's tile map.
    /// </summary>
    private static string SerializeTiles(MapChunk chunk, EntitySerializer? serializer)
    {
        const int bytesPerTile = 7;
        var buffer = new byte[chunk.ChunkSize * chunk.ChunkSize * bytesPerTile];

        using (var stream = new MemoryStream(buffer))
        using (var writer = new BinaryWriter(stream))
        {
            if (serializer == null)
            {
                // No map context: store runtime ids as they are.
                for (ushort y = 0; y < chunk.ChunkSize; y++)
                {
                    for (ushort x = 0; x < chunk.ChunkSize; x++)
                    {
                        var tile = chunk.GetTile(x, y);
                        writer.Write(tile.TypeId);
                        writer.Write((byte) tile.Flags);
                        writer.Write(tile.Variant);
                        writer.Write(tile.RotationMirroring);
                    }
                }

                return Convert.ToBase64String(buffer);
            }

            // Cache the id translation across runs of identical tiles.
            var lastTile = -1;
            var yamlId = -1;

            for (ushort y = 0; y < chunk.ChunkSize; y++)
            {
                for (ushort x = 0; x < chunk.ChunkSize; x++)
                {
                    var tile = chunk.GetTile(x, y);
                    if (tile.TypeId != lastTile)
                        yamlId = serializer.GetYamlTileId(tile.TypeId);

                    lastTile = tile.TypeId;

                    writer.Write(yamlId);
                    writer.Write((byte) tile.Flags);
                    writer.Write(tile.Variant);
                    writer.Write(tile.RotationMirroring);
                }
            }
        }

        return Convert.ToBase64String(buffer);
    }

    /// <summary>
    ///     Makes an independent chunk with the same position, size and tiles as <paramref name="source"/>.
    /// </summary>
    public MapChunk CreateCopy(
        ISerializationManager serializationManager,
        MapChunk source,
        IDependencyCollection dependencies,
        SerializationHookContext hookCtx,
        ISerializationContext? context = null)
    {
        var mapManager = dependencies.Resolve<IMapManager>();
        mapManager.SuppressOnTileChanged = true;

        var chunk = new MapChunk(source.X, source.Y, source.ChunkSize)
        {
            SuppressCollisionRegeneration = true
        };

        for (ushort y = 0; y < source.ChunkSize; y++)
        {
            for (ushort x = 0; x < source.ChunkSize; x++)
            {
                chunk.TrySetTile(x, y, source.GetTile(x, y), out _, out _);
            }
        }

        mapManager.SuppressOnTileChanged = false;
        chunk.SuppressCollisionRegeneration = false;

        return chunk;
    }
}
