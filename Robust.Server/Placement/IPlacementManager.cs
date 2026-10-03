using System;
using Robust.Shared.Analyzers;
using Robust.Shared.GameObjects;
using Robust.Shared.Network.Messages;

namespace Robust.Server.Placement;

/// <summary>
///     Server-side placement service. It lets clients (typically sandbox, admin or mapping users) ask the server
///     to spawn entities, set tiles or delete entities, and lets server code push a "placement mode" onto a
///     player's client.
/// </summary>
/// <remarks>
///     <para>
///         Every <c>mob</c> parameter is the entity currently controlled by a player. A mob can only be mapped to a
///         client if it has an attached player session; methods that need a client silently do nothing otherwise.
///     </para>
///     <para>
///         The <c>range</c>, <c>objectType</c>/<c>tileType</c> and <c>alignOption</c> arguments are forwarded to the
///         client as placement-mode settings: <c>range</c> is measured in tiles, <c>objectType</c> is an entity
///         prototype id (or a tile definition name for the tile variants) and <c>alignOption</c> is the name of a
///         placement alignment mode.
///     </para>
///     <para>
///         The engine provides the only implementation. Content must not implement this interface.
///     </para>
///     <para>
///         The client assembly has an unrelated interface with the same simple name in
///         <c>Robust.Client.Placement</c>.
///     </para>
/// </remarks>
[NotContentImplementable]
public interface IPlacementManager
{
    /// <summary>
    ///     Brings the manager into a usable state: creates its log channel (named "placement") and registers the
    ///     placement network message so incoming <see cref="MsgPlacement"/> messages are routed to
    ///     <see cref="HandleNetMessage"/>.
    /// </summary>
    /// <remarks>
    ///     Called once by server start-up, after the player manager has been initialised. Calling it a second time
    ///     would register the network message twice, so callers other than the engine must not call it.
    /// </remarks>
    void Initialize();

    /// <summary>
    ///     Entry point for every placement message received from a client.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The message is first passed to <see cref="AllowPlacementFunc"/>; if that delegate is set and returns
    ///         <see langword="false"/>, the message is dropped without any response or effect.
    ///     </para>
    ///     <para>
    ///         Otherwise the message is dispatched on its placement type:
    ///     </para>
    ///     <list type="bullet">
    ///         <item>
    ///             <description>
    ///                 Start and cancel messages travel from server to client only, so they are ignored here.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 A placement request is forwarded to <see cref="HandlePlacementRequest"/>.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 An entity removal request deletes the single entity named in the message if it still exists.
    ///                 Before deleting, a local (non-networked) placement entity event with the action "erase" is
    ///                 raised, carrying the entity, its current coordinates and the sender's user id.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 A rectangle removal request deletes almost every entity whose bounds intersect the
    ///                 axis-aligned rectangle that starts at the message's coordinates and extends by the message's
    ///                 size vector (on the map of the start coordinates). Entities that are already deleted, grids,
    ///                 player-controlled entities, and entities with a player-controlled entity anywhere among their
    ///                 transform ancestors are skipped. Each deleted entity raises the same "erase" event first.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>Unrecognised placement types are ignored.</description>
    ///         </item>
    ///     </list>
    ///     <para>
    ///         Caveat: removal requests have no admin check of their own. The only authorisation is the optional
    ///         <see cref="AllowPlacementFunc"/> gate.
    ///     </para>
    /// </remarks>
    /// <param name="msg">The message received from a client.</param>
    void HandleNetMessage(MsgPlacement msg);

    /// <summary>
    ///     Performs the placement that a client asked for. It is public so that it can be invoked directly.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This method does <b>not</b> consult <see cref="AllowPlacementFunc"/>; only
    ///         <see cref="HandleNetMessage"/> applies that gate.
    ///     </para>
    ///     <para>
    ///         The sender's session is resolved from the message's channel. If the session has no attached entity
    ///         with a transform, nothing happens. The message's networked coordinates are converted to local entity
    ///         coordinates; if they are not valid, a warning naming the session, object type and coordinates is
    ///         logged and the request is abandoned.
    ///     </para>
    ///     <para>
    ///         No range check and no per-mob permission check is applied. Data stored through
    ///         <see cref="AssignBuildPermission"/> is not consulted.
    ///     </para>
    ///     <para>
    ///         For entity placement: if the message's replacement flag is set and the prototype carries the
    ///         placement-replacement component, all anchored entities on the same grid tile with the same
    ///         replacement key are erased first (each raises the "erase" event before deletion). The new entity is
    ///         then spawned at the requested coordinates, attached to the parent of those coordinates and rotated by
    ///         the message's direction, and a local "create" placement entity event is raised with the entity, its
    ///         coordinates and the sender's user id.
    ///     </para>
    ///     <para>
    ///         For tile placement: the tile id comes from the message. If the tile definition allows rotation and
    ///         mirroring, the message's direction and mirrored flag are applied; otherwise the direction is forced
    ///         to south and the mirrored flag to <see langword="false"/>. The tile is set on the grid at the
    ///         coordinates (the coordinates' own entity if it is a grid, otherwise a grid found at the map position).
    ///         If there is no grid and the tile id is non-zero, a new grid is created on that map, positioned so the
    ///         target is its bottom-left tile, and the tile is set. A local placement tile event is raised on
    ///         success. Placing the empty tile when no grid exists does nothing.
    ///     </para>
    /// </remarks>
    /// <param name="msg">The placement request message.</param>
    void HandlePlacementRequest(MsgPlacement msg);

    /// <summary>
    ///     Tells the client controlling <paramref name="mob"/> to enter entity placement mode.
    /// </summary>
    /// <remarks>
    ///     Does nothing if the mob has no player session. Otherwise exactly one <see cref="MsgPlacement"/> is sent
    ///     to that player's channel, carrying a start-placement type, the range, the tile flag set to
    ///     <see langword="false"/>, the object type and the align option.
    /// </remarks>
    /// <param name="mob">The entity controlled by the target player.</param>
    /// <param name="range">Placement range, in tiles.</param>
    /// <param name="objectType">Entity prototype id to place.</param>
    /// <param name="alignOption">Name of the placement alignment mode.</param>
    void SendPlacementBegin(EntityUid mob, int range, string objectType, string alignOption);

    /// <summary>
    ///     Tells the client controlling <paramref name="mob"/> to enter tile placement mode.
    /// </summary>
    /// <remarks>
    ///     Behaves like <see cref="SendPlacementBegin"/>, except that the tile flag is set to
    ///     <see langword="true"/> and the object type field carries <paramref name="tileType"/>.
    /// </remarks>
    /// <param name="mob">The entity controlled by the target player.</param>
    /// <param name="range">Placement range, in tiles.</param>
    /// <param name="tileType">Name of the tile definition to place.</param>
    /// <param name="alignOption">Name of the placement alignment mode.</param>
    void SendPlacementBeginTile(EntityUid mob, int range, string tileType, string alignOption);

    /// <summary>
    ///     Tells the client controlling <paramref name="mob"/> to leave any placement mode it is in.
    /// </summary>
    /// <remarks>
    ///     Sends a cancel-placement <see cref="MsgPlacement"/> to the player, or does nothing if the mob has no
    ///     player session.
    /// </remarks>
    /// <param name="mob">The entity controlled by the target player.</param>
    void SendPlacementCancel(EntityUid mob);

    /// <summary>
    ///     Convenience wrapper that starts entity placement for a mob.
    /// </summary>
    /// <remarks>
    ///     Calls <see cref="AssignBuildPermission"/> and then <see cref="SendPlacementBegin"/>, in that order, with
    ///     the same arguments.
    /// </remarks>
    /// <param name="mob">The entity controlled by the target player.</param>
    /// <param name="range">Placement range, in tiles.</param>
    /// <param name="objectType">Entity prototype id to place.</param>
    /// <param name="alignOption">Name of the placement alignment mode.</param>
    void StartBuilding(EntityUid mob, int range, string objectType, string alignOption);

    /// <summary>
    ///     Convenience wrapper that starts tile placement for a mob.
    /// </summary>
    /// <remarks>
    ///     The current engine implementation grants its permission record through the entity variant,
    ///     <see cref="AssignBuildPermission"/>, rather than <see cref="AssignBuildPermissionTile"/>, and then calls
    ///     <see cref="SendPlacementBeginTile"/>. Because permissions are never enforced, the only observable
    ///     difference is the stored record, which is flagged as non-tile and holds the tile name as its entity type.
    ///     Implementations may use the tile variant instead.
    /// </remarks>
    /// <param name="mob">The entity controlled by the target player.</param>
    /// <param name="range">Placement range, in tiles.</param>
    /// <param name="tileType">Name of the tile definition to place.</param>
    /// <param name="alignOption">Name of the placement alignment mode.</param>
    void StartBuildingTile(EntityUid mob, int range, string tileType, string alignOption);

    /// <summary>
    ///     Ends building for a mob.
    /// </summary>
    /// <remarks>
    ///     Calls <see cref="RevokeAllBuildPermissions"/> and then <see cref="SendPlacementCancel"/>, in that order.
    /// </remarks>
    /// <param name="mob">The entity controlled by the target player.</param>
    void CancelBuilding(EntityUid mob);

    /// <summary>
    ///     Records that <paramref name="mob"/> may place the given entity prototype with the given range and
    ///     alignment.
    /// </summary>
    /// <remarks>
    ///     Only one permission is kept per mob: any existing permissions for the mob are removed before the new
    ///     one (not a tile, with the given entity type and placement option) is stored. The recorded data is
    ///     currently never checked when placements are handled, so it grants nothing by itself. Do not start
    ///     enforcing it without auditing callers, since existing sandbox and admin flows rely on
    ///     <see cref="AllowPlacementFunc"/> alone.
    /// </remarks>
    /// <param name="mob">The entity controlled by the target player.</param>
    /// <param name="range">Placement range, in tiles.</param>
    /// <param name="objectType">Entity prototype id.</param>
    /// <param name="alignOption">Name of the placement alignment mode.</param>
    void AssignBuildPermission(EntityUid mob, int range, string objectType, string alignOption);

    /// <summary>
    ///     Records that <paramref name="mob"/> may place the given tile with the given range and alignment.
    /// </summary>
    /// <remarks>
    ///     Same as <see cref="AssignBuildPermission"/> except that the record is flagged as a tile and stores the
    ///     numeric id of the tile definition named <paramref name="tileType"/>. Looking up an unknown tile name
    ///     throws from the tile definition manager.
    /// </remarks>
    /// <param name="mob">The entity controlled by the target player.</param>
    /// <param name="range">Placement range, in tiles.</param>
    /// <param name="tileType">Name of the tile definition.</param>
    /// <param name="alignOption">Name of the placement alignment mode.</param>
    void AssignBuildPermissionTile(EntityUid mob, int range, string tileType, string alignOption);

    /// <summary>
    ///     Removes every stored build permission belonging to <paramref name="mob"/>. Does nothing if there are
    ///     none.
    /// </summary>
    /// <param name="mob">The entity whose permissions are removed.</param>
    void RevokeAllBuildPermissions(EntityUid mob);

    /// <summary>
    ///     Optional gatekeeper for all incoming client placement messages, including removal requests.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Returning <see langword="true"/> lets <see cref="HandleNetMessage"/> process the message; returning
    ///         <see langword="false"/> rejects it silently. <see langword="null"/>, the default, allows every
    ///         message.
    ///     </para>
    ///     <para>
    ///         This is a single slot rather than a multicast event: assigning a delegate replaces the previous one.
    ///         Content normally installs it when its system initialises (for example, allowing the message when
    ///         sandbox mode is on or the sender is an admin with placement rights) and resets it to
    ///         <see langword="null"/> on shutdown.
    ///     </para>
    ///     <para>
    ///         The delegate is invoked on the main thread while a network message is being handled, and must not
    ///         keep a reference to the message after returning.
    ///     </para>
    /// </remarks>
    Func<MsgPlacement, bool>? AllowPlacementFunc { get; set; }
}
