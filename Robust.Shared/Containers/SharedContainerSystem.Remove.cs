using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Utility;

namespace Robust.Shared.Containers;

public abstract partial class SharedContainerSystem
{
    /// <summary>
    ///     Takes an entity out of a container.
    /// </summary>
    /// <param name="toRemove">The entity to remove, with optionally pre-resolved transform and metadata components.</param>
    /// <param name="container">The container to remove the entity from.</param>
    /// <param name="reparent">
    ///     Whether to move the entity to a new parent after removal. Ignored when <paramref name="destination"/> is given.
    /// </param>
    /// <param name="force">If true, the can-remove checks are skipped.</param>
    /// <param name="destination">
    ///     Where to place the entity. Supplying it avoids extra broadphase work.
    /// </param>
    /// <param name="localRotation">Optional final local rotation of the entity.</param>
    /// <returns>True if the entity was removed, false if the removal was refused. Refusal never throws.</returns>
    public bool Remove(
        Entity<TransformComponent?, MetaDataComponent?> toRemove,
        BaseContainer container,
        bool reparent = true,
        bool force = false,
        EntityCoordinates? destination = null,
        Angle? localRotation = null)
    {
        var (uid, xform, meta) = toRemove;

        // Physics is deliberately not resolved: it is optional.
        if (!Resolve(uid, ref xform, ref meta))
            return false;

        DebugTools.AssertNotNull(container.Manager);
        DebugTools.Assert(Exists(uid), "toRemove does not exist");

        if (!force && !CanRemove(toRemove, container))
            return false;

        if (force && !container.Contains(toRemove))
        {
            DebugTools.Assert("Attempted to force remove an entity that was never inside of the container.");
            return false;
        }

        // Terminating entities may only be removed with force and without reparenting
        // (detaching to null-space right before deletion).
        if (meta.EntityLifeStage >= EntityLifeStage.Terminating && !(force && !reparent))
        {
            Log.Error($"Attempting to remove an entity from a container while it is terminating. Entity: {ToPrettyString(toRemove, meta)}. Container: {ToPrettyString(container.Owner)}. Trace: {Environment.StackTrace}");
            return false;
        }

        DebugTools.Assert(meta.EntityLifeStage < EntityLifeStage.Terminating || (force && !reparent), "Entity is terminating");
        DebugTools.Assert(xform.Broadphase == null || !xform.Broadphase.Value.IsValid(), "broadphase is invalid");
        DebugTools.Assert(!xform.Anchored || _timing.ApplyingState, "anchor is invalid");
        DebugTools.Assert((meta.Flags & MetaDataFlags.InContainer) != 0, "metadata is invalid");
        DebugTools.Assert(!PhysicsQuery.TryGetComponent(uid, out var physics) || (!physics.Awake && !physics.CanCollide), "physics is invalid");

        // Clear the flag before any parent-change events can be raised.
        meta.Flags &= ~MetaDataFlags.InContainer;

        container.InternalRemove(toRemove, EntityManager);
        DebugTools.Assert((meta.Flags & MetaDataFlags.InContainer) == 0, "metadata is invalid");

        var oldParent = xform.ParentUid;

        if (destination != null)
        {
            _transform.SetCoordinates(toRemove, xform, destination.Value, localRotation);
        }
        else if (reparent)
        {
            // Goes into the next container up the hierarchy, else the grid, else the map.
            AttachParentToContainerOrGrid((uid, xform));

            if (localRotation != null)
                _transform.SetLocalRotation(uid, localRotation.Value, xform);
        }

        // If no move event handled the entity, make sure it ends up in the entity tree.
        if (xform.ParentUid == oldParent && xform.Broadphase == null)
            _lookup.FindAndAddToEntityTree(toRemove, xform: xform);

        RecursivelyUpdateJoints((uid, xform));

        RaiseLocalEvent(container.Owner, new EntRemovedFromContainerMessage(toRemove, container), true);
        RaiseLocalEvent(toRemove, new EntGotRemovedFromContainerMessage(toRemove, container), false);

        DebugTools.Assert(destination == null || xform.Coordinates.Equals(destination.Value),
            $"Failed to set coordinates of entity {ToPrettyString(toRemove)} removed from container {container.ID} of {ToPrettyString(container.Owner)}.");

        Dirty(container.Owner, container.Manager);
        return true;
    }

    /// <summary>
    /// Checks if the entity can be removed from this container.
    /// </summary>
    /// <returns>True if the entity can be removed, false otherwise.</returns>
    public bool CanRemove(EntityUid toRemove, BaseContainer container)
    {
        if (!container.Contains(toRemove))
            return false;

        //raise events
        var removeAttemptEvent = new ContainerIsRemovingAttemptEvent(container, toRemove);
        RaiseLocalEvent(container.Owner, removeAttemptEvent, true);
        if (removeAttemptEvent.Cancelled)
            return false;

        var gettingRemovedAttemptEvent = new ContainerGettingRemovedAttemptEvent(container, toRemove);
        RaiseLocalEvent(toRemove, gettingRemovedAttemptEvent, true);
        return !gettingRemovedAttemptEvent.Cancelled;
    }

    public void RemoveDeletedEntity(EntityUid staleUid, BaseContainer container)
    {
        container.InternalRemove(staleUid, EntityManager);
        Dirty(container.Owner, container.Manager);
    }
}
