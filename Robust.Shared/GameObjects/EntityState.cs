using Robust.Shared.Serialization;
using System;
using NetSerializer;
using Robust.Shared.Timing;
using System.Collections.Generic;

namespace Robust.Shared.GameObjects
{
    [Serializable, NetSerializable]
    public sealed class EntityState
    {
        /// <summary>
        /// Network identifier for the entity.
        /// </summary>
        public NetEntity NetEntity;

        public NetListAsArray<ComponentChange> ComponentChanges { get; }

        public bool Empty => (ComponentChanges.Value is null or { Count: 0 }) && NetComponents == null;

        public readonly GameTick EntityLastModified;

        /// <summary>
        ///     Set of all networked component ids. Only sent to clients if a component has been removed sometime since the
        ///     entity was last sent to a player.
        /// </summary>
        public HashSet<ushort>? NetComponents;

        /// <summary>
        ///     Creates the state of one entity. The arguments are stored as given, without validation or copying.
        /// </summary>
        /// <param name="netEntity">Network id of the entity.</param>
        /// <param name="changedComponents">Component changes carried by this state.</param>
        /// <param name="lastModified">Tick at which the entity was last modified.</param>
        /// <param name="netComps">Optional set of all networked component ids; null when not sent.</param>
        public EntityState(NetEntity netEntity, NetListAsArray<ComponentChange> changedComponents, GameTick lastModified, HashSet<ushort>? netComps = null)
        {
            NetEntity = netEntity;
            ComponentChanges = changedComponents;
            EntityLastModified = lastModified;
            NetComponents = netComps;
        }
    }

    [Serializable, NetSerializable]
    public readonly struct ComponentChange
    {
        /// <summary>
        /// State data for the created/modified component, if any.
        /// </summary>
        public readonly IComponentState? State;

        /// <summary>
        ///     The Network ID of the component to remove.
        /// </summary>
        public readonly ushort NetID;

        public readonly GameTick LastModifiedTick;

        /// <summary>
        ///     Creates a description of a single component change.
        /// </summary>
        /// <param name="netId">Network id of the component.</param>
        /// <param name="state">State data of the component, or null if there is none.</param>
        /// <param name="lastModifiedTick">Tick at which the component was last modified.</param>
        public ComponentChange(ushort netId, IComponentState? state, GameTick lastModifiedTick)
        {
            State = state;
            NetID = netId;
            LastModifiedTick = lastModifiedTick;
        }

        /// <summary>
        ///     Text form: the network id, a space, then the short type name of the state (empty if the state is null).
        /// </summary>
        public override string ToString()
        {
            return $"{NetID} {State?.GetType().Name}";
        }
    }
}
