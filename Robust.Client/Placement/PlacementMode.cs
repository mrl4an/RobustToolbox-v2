using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;

namespace Robust.Client.Placement
{
    public abstract class PlacementMode
    {
        public readonly PlacementManager pManager;

        /// <summary>
        ///     The tile currently under the cursor. Written by the concrete placement modes.
        /// </summary>
        public TileRef CurrentTile { get; set; }

        /// <summary>
        ///     Cursor position expressed in coordinates local to the grid (or map) beneath it.
        ///     Written by the modes derived from <c>AlignPlacementMode</c>.
        /// </summary>
        public EntityCoordinates MouseCoords { get; set; }

        /// <summary>
        /// Texture resources to draw to represent the entity we are trying to spawn
        /// </summary>
        public List<Texture>? TexturesToDraw { get; set; }

        /// <summary>
        ///     Tint applied to the placement ghost when the current position is a legal place to build.
        /// </summary>
        public Color ValidPlaceColor { get; set; } = new Color(20, 180, 20);

        /// <summary>
        ///     Tint applied to the placement ghost when the current position is not a legal place to build.
        /// </summary>
        public Color InvalidPlaceColor { get; set; } = new Color(180, 20, 20);

        /// <summary>
        ///     Distance in world units between consecutive entities while line or grid placement is active.
        /// </summary>
        protected float GridDistancing = 1f;

        /// <summary>
        ///     Whether placement with this mode is limited by the player's reach (see <see cref="RangeCheck"/>).
        ///     Modes that want the distance test override this to return true.
        /// </summary>
        public virtual bool RangeRequired => false;

        /// <summary>
        ///     Whether this mode supports dragging out a straight line of entities.
        /// </summary>
        public virtual bool HasLineMode => false;

        /// <summary>
        ///     Whether this mode supports dragging out a rectangle of entities.
        /// </summary>
        public virtual bool HasGridMode => false;

        protected PlacementMode(PlacementManager pMan)
        {
            pManager = pMan;
        }

        public virtual string ModeName => GetType().Name;

        /// <summary>
        ///     Converts a screen position into placement coordinates and updates <see cref="MouseCoords"/>
        ///     and <see cref="CurrentTile"/> accordingly. Does not return a value.
        /// </summary>
        /// <param name="mouseScreen">The cursor position on screen.</param>
        public abstract void AlignPlacementMode(ScreenCoordinates mouseScreen);

        /// <summary>
        ///     Decides whether an entity may be placed at the given position. The result selects the
        ///     ghost tint used by <see cref="Render"/>.
        /// </summary>
        /// <param name="position">The candidate placement position.</param>
        /// <returns>True if placement at <paramref name="position"/> is allowed.</returns>
        public abstract bool IsValidPosition(EntityCoordinates position);

        public virtual void Render(in OverlayDrawArgs args)
        {
            var uid = pManager.CurrentPlacementOverlayEntity;

            // Nothing to draw without a visible ghost sprite.
            if (!pManager.EntityManager.TryGetComponent(uid, out SpriteComponent? sprite) || !sprite.Visible)
                return;

            IEnumerable<EntityCoordinates> locations;
            switch (pManager.PlacementType)
            {
                case PlacementManager.PlacementTypes.Line:
                    locations = LineCoordinates();
                    break;
                case PlacementManager.PlacementTypes.Grid:
                    locations = GridCoordinates();
                    break;
                default:
                    locations = SingleCoordinate();
                    break;
            }

            var directionAngle = pManager.Direction.ToAngle();
            var spriteSys = pManager.EntityManager.System<SpriteSystem>();
            var transformSys = pManager.EntityManager.System<SharedTransformSystem>();

            foreach (var coordinate in locations)
            {
                // An invalid coordinate aborts the whole draw, it is not merely skipped.
                if (!coordinate.IsValid(pManager.EntityManager))
                    return;

                var worldPos = transformSys.ToMapCoordinates(coordinate).Position;
                var worldRot = transformSys.GetWorldRotation(coordinate.EntityId) + directionAngle;

                // The tint is written onto the shared ghost sprite before each draw.
                sprite.Color = IsValidPosition(coordinate) ? ValidPlaceColor : InvalidPlaceColor;

                var eyeRot = args.Viewport.Eye?.Rotation ?? default(Angle);

                spriteSys.RenderSprite((uid.Value, sprite), args.WorldHandle, eyeRot, worldRot, worldPos);
            }
        }

        public IEnumerable<EntityCoordinates> SingleCoordinate()
        {
            yield return MouseCoords;
        }

        public IEnumerable<EntityCoordinates> LineCoordinates()
        {
            var cursorScreen = pManager.InputManager.MouseScreenPosition;
            var cursorMap = pManager.EyeManager.PixelToMap(cursorScreen);

            if (cursorMap.MapId == MapId.Nullspace)
                yield break;

            var start = pManager.StartPoint;
            var transformSys = pManager.EntityManager.System<SharedTransformSystem>();

            // Offset from the start point to the cursor, in the start point's frame.
            var offset = (transformSys.ToCoordinates(start.EntityId, cursorMap) - start).Position;
            var dx = offset.X;
            var dy = offset.Y;

            float count;
            Vector2 step;

            // The longer axis wins; ties and a zero offset fall through to Y.
            if (Math.Abs(dx) > Math.Abs(dy))
            {
                count = MathF.Ceiling(Math.Abs((dx + GridDistancing / 2 * Math.Sign(dx)) / GridDistancing));
                step = dx > 0 ? new Vector2(1, 0) : new Vector2(-1, 0);
            }
            else
            {
                count = MathF.Ceiling(Math.Abs((dy + GridDistancing / 2 * Math.Sign(dy)) / GridDistancing));
                step = dy > 0 ? new Vector2(0, 1) : new Vector2(0, -1);
            }

            // Note: the step has unit length; the spacing only influences how many cells are produced.
            for (var i = 0; i < count; i++)
            {
                yield return new EntityCoordinates(start.EntityId, start.Position + step * i);
            }
        }

        // This name is a nice reminder of our origins. Never forget.
        public IEnumerable<EntityCoordinates> GridCoordinates()
        {
            var cursorScreen = pManager.InputManager.MouseScreenPosition;
            var cursorMap = pManager.EyeManager.PixelToMap(cursorScreen);

            if (cursorMap.MapId == MapId.Nullspace)
                yield break;

            var start = pManager.StartPoint;
            var transformSys = pManager.EntityManager.System<SharedTransformSystem>();

            var diff = transformSys.ToCoordinates(start.EntityId, cursorMap) - start;

            var xSign = Math.Sign(diff.X);
            var ySign = Math.Sign(diff.Y);

            var countX = Math.Ceiling(Math.Abs((diff.X + GridDistancing / 2f * xSign) / GridDistancing));
            var countY = Math.Ceiling(Math.Abs((diff.Y + GridDistancing / 2f * ySign) / GridDistancing));

            // Columns are the outer loop, rows the inner one, both growing toward the cursor.
            for (var x = 0; x < countX; x++)
            {
                for (var y = 0; y < countY; y++)
                {
                    yield return new EntityCoordinates(start.EntityId,
                        start.Position + new Vector2(x * xSign, y * ySign) * GridDistancing);
                }
            }
        }

        /// <summary>
        ///     Returns the tile ref for a grid, or a map.
        /// </summary>
        public TileRef GetTileRef(EntityCoordinates coordinates)
        {
            var gridUidOpt = pManager.EntityManager.System<SharedTransformSystem>().GetGrid(coordinates);
            return gridUidOpt is { } gridUid && gridUid.IsValid()
                ? pManager.EntityManager.System<SharedMapSystem>().GetTileRef(gridUid, pManager.EntityManager.GetComponent<MapGridComponent>(gridUid), MouseCoords)
                : new TileRef(gridUidOpt ?? EntityUid.Invalid,
                    MouseCoords.ToVector2i(pManager.EntityManager, pManager.MapManager, pManager.EntityManager.System<SharedTransformSystem>()), Tile.Empty);
        }

        public TextureResource GetSprite(string key)
        {
            return pManager.ResourceCache.GetResource<TextureResource>(new ResPath("/Textures/") / key);
        }

        public bool TryGetSprite(string key, [NotNullWhen(true)] out TextureResource? sprite)
        {
            return pManager.ResourceCache.TryGetResource(new ResPath(@"/Textures/") / key, out sprite);
        }

        /// <summary>
        ///     Checks whether the local player is close enough to place at the given coordinates.
        ///     Always succeeds when <see cref="RangeRequired"/> is false; a permission range of zero
        ///     or less means unlimited reach.
        /// </summary>
        /// <param name="coordinates">The position the player wants to place at.</param>
        /// <returns>True if placement is within reach.</returns>
        public bool RangeCheck(EntityCoordinates coordinates)
        {
            if (!RangeRequired)
                return true;

            var controlled = pManager.PlayerManager.LocalEntity ?? EntityUid.Invalid;
            if (controlled == EntityUid.Invalid)
                return false;

            var range = pManager.CurrentPermission!.Range;
            if (range > 0
                && !pManager.EntityManager.System<SharedTransformSystem>().InRange(
                    pManager.EntityManager.GetComponent<TransformComponent>(controlled).Coordinates,
                    coordinates,
                    range))
            {
                return false;
            }

            return true;
        }

        public bool IsColliding(EntityCoordinates coordinates)
        {
            var bounds = pManager.ColliderAABB;
            var mapCoords = pManager.EntityManager.System<SharedTransformSystem>().ToMapCoordinates(coordinates);
            var (x, y) = mapCoords.Position;

            // Translate (without rotating) the ghost's local collider bounds to the world position.
            var box = Box2.FromDimensions(bounds.Left + x, bounds.Bottom + y, bounds.Width, bounds.Height);

            return pManager.EntityManager.System<SharedPhysicsSystem>().TryCollideRect(box, mapCoords.MapId);
        }

        protected Vector2 ScreenToWorld(Vector2 point)
        {
            return pManager.EyeManager.ScreenToMap(point).Position;
        }

        protected Vector2 WorldToScreen(Vector2 point)
        {
            return pManager.EyeManager.WorldToScreen(point);
        }

        protected EntityCoordinates ScreenToCursorGrid(ScreenCoordinates coords)
        {
            var mapCoords = pManager.EyeManager.PixelToMap(coords.Position);
            var transformSys = pManager.EntityManager.System<SharedTransformSystem>();
            if (!pManager.MapManager.TryFindGridAt(mapCoords, out var gridUid, out _))
            {
                return transformSys.ToCoordinates(mapCoords);
            }

            return transformSys.ToCoordinates(gridUid, mapCoords);
        }
    }
}
