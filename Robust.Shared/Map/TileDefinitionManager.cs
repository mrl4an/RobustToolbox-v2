using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Random;

namespace Robust.Shared.Map
{
    [Virtual]
    internal class TileDefinitionManager : ITileDefinitionManager
    {
        protected readonly List<ITileDefinition> TileDefs;
        private readonly Dictionary<string, ITileDefinition> _tileNames;
        private readonly Dictionary<string, List<string>> _awaitingAliases;

        /// <summary>
        ///     Creates an empty manager. All internal collections start out empty and no tiles are registered.
        /// </summary>
        public TileDefinitionManager()
        {
            TileDefs = new List<ITileDefinition>();
            _tileNames = new Dictionary<string, ITileDefinition>();
            _awaitingAliases = new Dictionary<string, List<string>>();
        }

        /// <summary>
        ///     Override point for derived managers, which load tile prototypes and call <see cref="Register"/>.
        ///     The base implementation intentionally does nothing.
        /// </summary>
        public virtual void Initialize()
        {
        }

        /// <summary>
        ///     Adds a tile definition. The definition receives the next free numeric id, which equals its index in
        ///     the internal list. Because ids are persisted in map files, ids are handed out strictly in call order.
        /// </summary>
        /// <param name="tileDef">The definition to register.</param>
        /// <exception cref="ArgumentException">The name is already used by another definition or alias.</exception>
        /// <exception cref="OverflowException">More than 65536 definitions would be registered (ids are 16-bit).</exception>
        public virtual void Register(ITileDefinition tileDef)
        {
            var name = tileDef.ID;

            if (_tileNames.ContainsKey(name))
            {
                throw new ArgumentException(
                    "Another tile definition or alias with the same name has already been registered.",
                    nameof(tileDef));
            }

            // Checked conversion: throws before any state is changed if the id does not fit in 16 bits.
            var id = checked((ushort) TileDefs.Count);

            tileDef.AssignTileId(id);
            TileDefs.Add(tileDef);
            _tileNames[name] = tileDef;
        }


        public Tile GetVariantTile(string name, IRobustRandom random)
        {
            var tileDef = this[name];
            return GetVariantTile(tileDef, random);
        }

        public Tile GetVariantTile(string name, System.Random random)
        {
            var tileDef = this[name];
            return GetVariantTile(tileDef, random);
        }

        public Tile GetVariantTile(ITileDefinition tileDef, IRobustRandom random)
        {
            return new Tile(tileDef.TileId, variant: random.NextByte(tileDef.Variants));
        }

        public Tile GetVariantTile(ITileDefinition tileDef, System.Random random)
        {
            return new Tile(tileDef.TileId, variant: random.NextByte(tileDef.Variants));
        }

        public ITileDefinition this[string name] => _tileNames[name];

        public ITileDefinition this[int id] => TileDefs[id];

        public bool TryGetDefinition(string name, [NotNullWhen(true)] out ITileDefinition? definition)
        {
            return _tileNames.TryGetValue(name, out definition);
        }

        public bool TryGetDefinition(int id, [NotNullWhen(true)] out ITileDefinition? definition)
        {
            if (id >= TileDefs.Count)
            {
                definition = null;
                return false;
            }

            definition = TileDefs[id];
            return true;
        }

        public int Count => TileDefs.Count;

        /// <summary>
        ///     Enumerates all registered definitions in id order. This is a live view of the underlying list.
        /// </summary>
        public IEnumerator<ITileDefinition> GetEnumerator()
        {
            return TileDefs.GetEnumerator();
        }

        /// <summary>
        ///     Non-generic enumeration; forwards to <see cref="GetEnumerator()"/>.
        /// </summary>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
