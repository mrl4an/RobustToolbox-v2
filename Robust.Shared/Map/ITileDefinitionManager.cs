using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Random;

namespace Robust.Shared.Map
{
    /// <summary>
    ///     Keeps the registry of tile definitions that grids can use.
    /// </summary>
    [NotContentImplementable]
    public interface ITileDefinitionManager : IEnumerable<ITileDefinition>
    {
        Tile GetVariantTile(string name, IRobustRandom random);

        [Obsolete("Always use RobustRandom/IRobustRandom, System.Random does not provide any extra functionality.")]
        Tile GetVariantTile(string name, System.Random random);

        Tile GetVariantTile(ITileDefinition tileDef, IRobustRandom random);

        [Obsolete("Always use RobustRandom/IRobustRandom, System.Random does not provide any extra functionality.")]
        Tile GetVariantTile(ITileDefinition tileDef, System.Random random);

        /// <summary>
        /// Gets a tile definition by its name.
        /// </summary>
        /// <remarks>
        /// Names can be aliased, so the definition returned for a given name does not necessarily
        /// carry that same name or ID itself.
        /// </remarks>
        /// <param name="name">The name of the tile definition, or an alias of it.</param>
        /// <returns>The matching tile definition. Never null.</returns>
        /// <seealso cref="TryGetDefinition(string, out ITileDefinition?)"/>
        ITileDefinition this[string name] { get; }

        /// <summary>
        /// Gets a tile definition by its numeric tile ID.
        /// </summary>
        /// <param name="id">The integer tile ID, as stored in tile data.</param>
        /// <returns>The tile definition registered under that ID. Never null.</returns>
        /// <seealso cref="TryGetDefinition(int, out ITileDefinition?)"/>
        ITileDefinition this[int id] { get; }

        /// <summary>
        /// Try to retrieve a tile definition by name.
        /// </summary>
        /// <remarks>
        /// Note: In the presence of tile aliases, this[A].ID does not necessarily equal A.
        /// </remarks>
        /// <param name="name">The name of the tile definition to look up.</param>
        /// <param name="definition">The found tile definition, if it exists.</param>
        /// <returns>True if a tile definition was resolved, false otherwise.</returns>
        /// <seealso cref="this[string]"/>
        bool TryGetDefinition(string name, [NotNullWhen(true)] out ITileDefinition? definition);

        /// <summary>
        /// Try to retrieve a tile definition by tile ID.
        /// </summary>
        /// <param name="id">The ID of the tile definition to look up.</param>
        /// <param name="definition">The found tile definition, if it exists.</param>
        /// <returns>True if a tile definition was resolved, false otherwise.</returns>
        /// <seealso cref="this[int]"/>
        bool TryGetDefinition(int id, [NotNullWhen(true)] out ITileDefinition? definition);

        /// <summary>
        /// The number of tile definitions currently held by this manager.
        /// </summary>
        /// <remarks>
        /// Reflects the registrations made so far; an empty manager reports zero.
        /// </remarks>
        int Count { get; }

        void Initialize();

        /// <summary>
        /// Adds a tile definition to this manager so it can be found through the indexers,
        /// <c>TryGetDefinition</c>, and enumeration.
        /// </summary>
        /// <param name="tileDef">The tile definition to add. Must not be null.</param>
        void Register(ITileDefinition tileDef);
    }
}
