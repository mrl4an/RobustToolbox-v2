using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Collections;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Network.Messages;
using Robust.Shared.Placement;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Robust.Server.Placement
{
    public sealed class PlacementManager : IPlacementManager
    {
        [Dependency] private readonly IComponentFactory _factory = default!;
        [Dependency] private readonly ITileDefinitionManager _tileDefinitionManager = default!;
        [Dependency] private readonly IServerNetManager _networkManager = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly IPrototypeManager _prototype = default!;
        [Dependency] private readonly IServerEntityManager _entityManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly ILogManager _logManager = default!;

        private EntityLookupSystem _lookup => _entityManager.System<EntityLookupSystem>();
        private SharedMapSystem _maps => _entityManager.System<SharedMapSystem>();
        private SharedTransformSystem _xformSystem => _entityManager.System<SharedTransformSystem>();

        // TODO: A mob can hold only one permission at the moment; allow several per mob.
        // TODO: Support placeables that can be used more than once (tiles and similar).
        public List<PlacementInformation> BuildPermissions { get; set; } = new();

        // Build permissions of every mob: what each one may ask to place and in which way. At most one entry per mob.

        public Func<MsgPlacement, bool>? AllowPlacementFunc { get; set; }

        private ISawmill _sawmill = default!;

        #region IPlacementManager Members

        public void Initialize()
        {
            // Someday PlacementManagerSystem my beloved.
            _sawmill = _logManager.GetSawmill("placement");

            _networkManager.RegisterNetMessage<MsgPlacement>(HandleNetMessage);
        }

        /// <summary>
        /// Entry point for every <see cref="MsgPlacement"/> received from a client.
        /// </summary>
        /// <remarks>
        /// If <see cref="AllowPlacementFunc"/> is set it is consulted exactly once, for every message kind
        /// (including kinds that are subsequently ignored); a <c>false</c> result drops the message silently.
        /// This is the only place the veto is applied: <see cref="HandlePlacementRequest"/> does not check it.
        /// Start/cancel/failed messages and unknown message types are ignored. Exceptions thrown by the delegate
        /// or by the handlers are not caught here and propagate to the network layer.
        /// </remarks>
        /// <param name="msg">The received message. Must not be null.</param>
        public void HandleNetMessage(MsgPlacement msg)
        {
            if (AllowPlacementFunc != null && !AllowPlacementFunc(msg))
                return;

            switch (msg.PlaceType)
            {
                case PlacementManagerMessage.RequestPlacement:
                    HandlePlacementRequest(msg);
                    break;

                case PlacementManagerMessage.RequestEntRemove:
                    HandleEntRemoveReq(msg);
                    break;

                case PlacementManagerMessage.RequestRectRemove:
                    HandleRectRemoveReq(msg);
                    break;

                // StartPlacement, CancelPlacement, PlacementFailed and undefined values are not
                // meaningful when received by the server and are ignored.
                default:
                    break;
            }
        }

        /// <summary>
        /// Performs a client's request to place an entity or a tile.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Order of operations: the sender's session is resolved (throws if the channel has no session), the
        /// sender must have an attached entity with a transform (otherwise the request is silently ignored),
        /// and the net coordinates must convert to valid local coordinates (otherwise a warning is logged and
        /// the request is dropped).
        /// </para>
        /// <para>
        /// No range, ownership or permission checks are made: <see cref="BuildPermissions"/> is neither read nor
        /// consumed, and <see cref="AllowPlacementFunc"/> is not consulted (that happens only in
        /// <see cref="HandleNetMessage"/>). The player's position relative to the target is not examined.
        /// </para>
        /// <para>
        /// Entity placement: when <see cref="MsgPlacement.Replacement"/> is set and the prototype carries a
        /// <see cref="PlacementReplacementComponent"/>, anchored entities on the target tile with the same
        /// replacement key are first collected, then each gets an Erase event (carrying the placement
        /// coordinates) followed by deletion. The new entity is then spawned attached to the coordinates with
        /// the requested rotation and a Create event is raised. Unknown prototypes throw.
        /// </para>
        /// <para>
        /// Tile placement: only tile definitions that allow rotation/mirroring honour the message's direction
        /// and mirrored flag; others are always placed with direction 0 and not mirrored. Unknown tile ids
        /// throw.
        /// </para>
        /// </remarks>
        /// <param name="msg">The request message.</param>
        public void HandlePlacementRequest(MsgPlacement msg)
        {
            var session = _playerManager.GetSessionByChannel(msg.MsgChannel);

            // Only the existence of a body matters; its position is deliberately not checked.
            if (!_entityManager.TryGetComponent<TransformComponent>(session.AttachedEntity, out _))
                return;

            var coordinates = _entityManager.GetCoordinates(msg.NetCoordinates);

            if (!coordinates.IsValid(_entityManager))
            {
                _sawmill.Warning($"{session} tried to place '{msg.ObjType}' at invalid coordinates {coordinates}.");
                return;
            }

            var userId = msg.MsgChannel.UserId;

            if (!msg.IsTile)
            {
                var prototypeId = msg.EntityTemplateName;

                if (msg.Replacement)
                {
                    var prototype = _prototype.Index<EntityPrototype>(prototypeId);
                    var replacementName = _factory.GetComponentName<PlacementReplacementComponent>();

                    if (prototype.Components.TryGetValue(replacementName, out var registration))
                    {
                        var replacementKey = ((PlacementReplacementComponent) registration.Component).Key;
                        var gridUid = _xformSystem.GetGrid(coordinates);

                        if (_entityManager.TryGetComponent<MapGridComponent>(gridUid, out var grid))
                        {
                            var tile = _maps.TileIndicesFor(gridUid!.Value, grid, coordinates);
                            var toReplace = new List<EntityUid>();

                            // Collect first so that deleting does not disturb the enumeration.
                            foreach (var anchored in _maps.GetAnchoredEntities(gridUid.Value, grid, tile))
                            {
                                if (_entityManager.TryGetComponent<PlacementReplacementComponent>(anchored, out var existing)
                                    && existing.Key == replacementKey)
                                {
                                    toReplace.Add(anchored);
                                }
                            }

                            foreach (var victim in toReplace)
                            {
                                var eraseEvent = new PlacementEntityEvent(victim, coordinates, PlacementEventAction.Erase, userId);
                                _entityManager.EventBus.RaiseEvent(EventSource.Local, eraseEvent);
                                _entityManager.DeleteEntity(victim);
                            }
                        }
                    }
                }

                var spawned = _entityManager.SpawnAttachedTo(prototypeId, coordinates, rotation: msg.DirRcv.ToAngle());

                var createEvent = new PlacementEntityEvent(spawned, coordinates, PlacementEventAction.Create, userId);
                _entityManager.EventBus.RaiseEvent(EventSource.Local, createEvent);
            }
            else
            {
                var tileId = msg.TileType;
                var definition = _tileDefinitionManager[tileId];

                if (definition.AllowRotationMirror)
                {
                    PlaceNewTile(tileId, coordinates, userId, Tile.DirectionToByte(msg.DirRcv), msg.Mirrored);
                }
                else
                {
                    PlaceNewTile(tileId, coordinates, userId, Tile.DirectionToByte(Direction.South), false);
                }
            }
        }

        private void PlaceNewTile(int tileType, EntityCoordinates coordinates, NetUserId placingUserId, byte direction, bool mirrored)
        {
            if (!coordinates.IsValid(_entityManager)) return;

            MapGridComponent? grid;

            EntityUid gridId = coordinates.EntityId;
            if (_entityManager.TryGetComponent(coordinates.EntityId, out grid)
                || _mapManager.TryFindGridAt(_xformSystem.ToMapCoordinates(coordinates), out gridId, out grid))
            {
                _maps.SetTile(gridId, grid, coordinates, new Tile(tileType, rotationMirroring: (byte)(direction + (mirrored ? 4 : 0))));

                var placementEraseEvent = new PlacementTileEvent(tileType, coordinates, placingUserId);
                _entityManager.EventBus.RaiseEvent(EventSource.Local, placementEraseEvent);
            }
            else if (tileType != 0) // create a new grid
            {
                var newGrid = _mapManager.CreateGridEntity(_xformSystem.GetMapId(coordinates));
                var newGridXform = new Entity<TransformComponent>(
                    newGrid.Owner,
                    _entityManager.GetComponent<TransformComponent>(newGrid));

                _xformSystem.SetWorldPosition(newGridXform, coordinates.Position - newGrid.Comp.TileSizeHalfVector); // assume bottom left tile origin
                var tilePos = _maps.WorldToTile(newGrid.Owner, newGrid.Comp, coordinates.Position);
                _maps.SetTile(newGrid.Owner, newGrid.Comp, tilePos, new Tile(tileType, rotationMirroring: (byte)(direction + (mirrored ? 4 : 0))));

                var placementEraseEvent = new PlacementTileEvent(tileType, coordinates, placingUserId);
                _entityManager.EventBus.RaiseEvent(EventSource.Local, placementEraseEvent);
            }
        }

        /// <summary>
        /// Deletes any existing entity.
        /// </summary>
        /// <param name="msg"></param>
        private void HandleEntRemoveReq(MsgPlacement msg)
        {
            // TODO: Require some kind of admin permission here.
            var entity = _entityManager.GetEntity(msg.EntityUid);

            if (!_entityManager.EntityExists(entity))
                return;

            var placementEraseEvent = new PlacementEntityEvent(entity,
                _entityManager.GetComponent<TransformComponent>(entity).Coordinates,
                PlacementEventAction.Erase,
                msg.MsgChannel.UserId);

            _entityManager.EventBus.RaiseEvent(EventSource.Local, placementEraseEvent);
            _entityManager.DeleteEntity(entity);
        }

        /// <summary>
        /// Deletes almost any existing entity within a selection box.
        /// </summary>
        /// <param name="msg"></param>
        private void HandleRectRemoveReq(MsgPlacement msg)
        {
            var start = _entityManager.GetCoordinates(msg.NetCoordinates);
            var rectSize = msg.RectSize;

            foreach (var entity in _lookup.GetEntitiesIntersecting(_xformSystem.GetMapId(start), new Box2(start.Position, start.Position + rectSize)))
            {
                if (_entityManager.Deleted(entity)
                    || _entityManager.HasComponent<MapGridComponent>(entity)
                    || _entityManager.HasComponent<ActorComponent>(entity))
                    continue;

                var xform = _entityManager.GetComponent<TransformComponent>(entity);
                var parent = xform.ParentUid;
                var isChildOfActor = false;

                while (parent.IsValid())
                {
                    if (_entityManager.HasComponent<ActorComponent>(parent))
                    {
                        isChildOfActor = true;
                        break;
                    }

                    if (_entityManager.TryGetComponent<TransformComponent>(parent, out var parentXform))
                    {
                        parent = parentXform.ParentUid;
                    }
                    else
                    {
                        break;
                    }
                }

                if (isChildOfActor)
                    continue;

                var placementEraseEvent = new PlacementEntityEvent(entity,
                    _entityManager.GetComponent<TransformComponent>(entity).Coordinates,
                    PlacementEventAction.Erase,
                    msg.MsgChannel.UserId);
                
                _entityManager.EventBus.RaiseEvent(EventSource.Local, placementEraseEvent);
                _entityManager.DeleteEntity(entity);
            }
        }

        /// <summary>
        /// Tells the player controlling <paramref name="mob"/> to enter entity placement mode.
        /// </summary>
        /// <remarks>
        /// Does nothing if the mob has no <see cref="ActorComponent"/> (no player, or invalid/deleted mob).
        /// Arguments are forwarded without validation. Sends a single message to that player only.
        /// </remarks>
        /// <param name="mob">The entity whose player should receive the message.</param>
        /// <param name="range">Placement range in tiles.</param>
        /// <param name="objectType">Entity prototype id the client may place.</param>
        /// <param name="alignOption">Name of the alignment mode.</param>
        public void SendPlacementBegin(EntityUid mob, int range, string objectType, string alignOption)
        {
            if (!_entityManager.TryGetComponent(mob, out ActorComponent? actor))
                return;

            var channel = actor.PlayerSession.Channel;

            var message = new MsgPlacement
            {
                PlaceType = PlacementManagerMessage.StartPlacement,
                Range = range,
                IsTile = false,
                ObjType = objectType,
                AlignOption = alignOption
            };

            _networkManager.ServerSendMessage(message, channel);
        }

        /// <summary>
        /// Tells the player controlling <paramref name="mob"/> to enter tile placement mode.
        /// </summary>
        /// <remarks>
        /// Same as <see cref="SendPlacementBegin"/> except the message is flagged as a tile placement. The tile
        /// definition NAME is passed through unchanged in the message's object type; it is not looked up or
        /// validated here. Does nothing if the mob has no <see cref="ActorComponent"/>.
        /// </remarks>
        /// <param name="mob">The entity whose player should receive the message.</param>
        /// <param name="range">Placement range in tiles.</param>
        /// <param name="tileType">Name of the tile definition the client may place.</param>
        /// <param name="alignOption">Name of the alignment mode.</param>
        public void SendPlacementBeginTile(EntityUid mob, int range, string tileType, string alignOption)
        {
            if (!_entityManager.TryGetComponent(mob, out ActorComponent? actor))
                return;

            var channel = actor.PlayerSession.Channel;

            var message = new MsgPlacement
            {
                PlaceType = PlacementManagerMessage.StartPlacement,
                Range = range,
                IsTile = true,
                ObjType = tileType,
                AlignOption = alignOption
            };

            _networkManager.ServerSendMessage(message, channel);
        }

        /// <summary>
        /// Asks the player controlling <paramref name="mob"/> to leave placement mode.
        /// </summary>
        /// <remarks>
        /// Does nothing if the mob has no <see cref="ActorComponent"/>. Caveat: <see cref="MsgPlacement"/> does
        /// not implement serialization of the CancelPlacement type, so with a real network manager this call
        /// throws <see cref="NotImplementedException"/> for a mob that has a player. The exception is deliberately
        /// not caught and propagates to the caller.
        /// </remarks>
        /// <param name="mob">The entity whose player should receive the message.</param>
        public void SendPlacementCancel(EntityUid mob)
        {
            if (!_entityManager.TryGetComponent(mob, out ActorComponent? actor))
                return;

            var channel = actor.PlayerSession.Channel;

            var message = new MsgPlacement { PlaceType = PlacementManagerMessage.CancelPlacement };

            _networkManager.ServerSendMessage(message, channel);
        }

        /// <summary>
        /// Records an entity build permission for <paramref name="mob"/> and tells its player to begin placement.
        /// </summary>
        /// <remarks>
        /// The permission is recorded even if the mob has no player; in that case no message is sent.
        /// Permissions are informational only and are never enforced by this class.
        /// </remarks>
        /// <param name="mob">The entity being granted the permission.</param>
        /// <param name="range">Placement range in tiles.</param>
        /// <param name="objectType">Entity prototype id.</param>
        /// <param name="alignOption">Name of the alignment mode.</param>
        public void StartBuilding(EntityUid mob, int range, string objectType, string alignOption)
        {
            AssignBuildPermission(mob, range, objectType, alignOption);
            SendPlacementBegin(mob, range, objectType, alignOption);
        }

        /// <summary>
        /// Records a build permission for <paramref name="mob"/> and tells its player to begin tile placement.
        /// </summary>
        /// <remarks>
        /// Quirk kept for compatibility: the permission is stored through the ENTITY overload
        /// (<see cref="AssignBuildPermission"/>), so the record has <c>IsTile = false</c>, holds the tile name in
        /// <c>EntityType</c> and leaves <c>TileType</c> at 0. No tile lookup happens, so an unknown tile name
        /// does not throw here. The message sent to the client is still flagged as a tile placement.
        /// </remarks>
        /// <param name="mob">The entity being granted the permission.</param>
        /// <param name="range">Placement range in tiles.</param>
        /// <param name="tileType">Name of the tile definition.</param>
        /// <param name="alignOption">Name of the alignment mode.</param>
        public void StartBuildingTile(EntityUid mob, int range, string tileType, string alignOption)
        {
            AssignBuildPermission(mob, range, tileType, alignOption);
            SendPlacementBeginTile(mob, range, tileType, alignOption);
        }

        /// <summary>
        /// Revokes all build permissions of <paramref name="mob"/> and tells its player to cancel placement.
        /// </summary>
        /// <remarks>
        /// Permissions are revoked first, so they are gone even if sending the cancel message throws (see
        /// <see cref="SendPlacementCancel"/> for the unimplemented-serialization caveat).
        /// </remarks>
        /// <param name="mob">The entity whose permissions are revoked.</param>
        public void CancelBuilding(EntityUid mob)
        {
            RevokeAllBuildPermissions(mob);
            SendPlacementCancel(mob);
        }

        /// <summary>
        /// Stores an entity build permission for <paramref name="mob"/>, replacing any existing one.
        /// </summary>
        /// <remarks>
        /// At most one record per mob is kept; the new record is always appended at the end of
        /// <see cref="BuildPermissions"/>, and records of other mobs are untouched. No validation, events or
        /// messages. The permission is not enforced anywhere.
        /// </remarks>
        /// <param name="mob">The entity being granted the permission.</param>
        /// <param name="range">Placement range in tiles.</param>
        /// <param name="objectType">Entity prototype id.</param>
        /// <param name="alignOption">Name of the alignment mode.</param>
        public void AssignBuildPermission(EntityUid mob, int range, string objectType, string alignOption)
        {
            var permission = new PlacementInformation
            {
                MobUid = mob,
                Range = range,
                IsTile = false,
                EntityType = objectType,
                PlacementOption = alignOption
            };

            if (BuildPermissions.Exists(p => p.MobUid == mob))
                RevokeAllBuildPermissions(mob);

            BuildPermissions.Add(permission);
        }

        /// <summary>
        /// Stores a tile build permission for <paramref name="mob"/>, replacing any existing one.
        /// </summary>
        /// <remarks>
        /// Behaves like <see cref="AssignBuildPermission"/> but the record is flagged as a tile and holds the
        /// numeric id of the named tile definition (its <c>EntityType</c> stays null). The tile name is resolved
        /// before anything is revoked or added, so an unknown name throws from the tile definition manager and
        /// leaves <see cref="BuildPermissions"/> unchanged.
        /// </remarks>
        /// <param name="mob">The entity being granted the permission.</param>
        /// <param name="range">Placement range in tiles.</param>
        /// <param name="tileType">Name of the tile definition.</param>
        /// <param name="alignOption">Name of the alignment mode.</param>
        public void AssignBuildPermissionTile(EntityUid mob, int range, string tileType, string alignOption)
        {
            var permission = new PlacementInformation
            {
                MobUid = mob,
                Range = range,
                IsTile = true,
                TileType = _tileDefinitionManager[tileType].TileId,
                PlacementOption = alignOption
            };

            if (BuildPermissions.Exists(p => p.MobUid == mob))
                RevokeAllBuildPermissions(mob);

            BuildPermissions.Add(permission);
        }

        /// <summary>
        /// Removes every build permission recorded for <paramref name="mob"/>.
        /// </summary>
        /// <remarks>
        /// Remaining records keep their relative order. Does nothing if there are none. This does not notify the
        /// client or end its placement mode; use <see cref="SendPlacementCancel"/> for that.
        /// </remarks>
        /// <param name="mob">The entity whose permissions are removed.</param>
        public void RevokeAllBuildPermissions(EntityUid mob)
        {
            BuildPermissions.RemoveAll(p => p.MobUid == mob);
        }

        #endregion IPlacementManager Members

        private PlacementInformation? GetPermission(EntityUid uid, string alignOpt)
        {
            foreach (var buildPermission in BuildPermissions)
            {
                if (buildPermission.MobUid == uid && buildPermission.PlacementOption == alignOpt)
                {
                    return buildPermission;
                }
            }

            return null;
        }
    }
}
