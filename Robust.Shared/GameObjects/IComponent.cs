using System;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Timing;

namespace Robust.Shared.GameObjects
{
    /// <summary>
    ///     The base interface for an ECS component. You probably want <see cref="Component"/>.<br/>
    /// </summary>
    /// <include file='../Serialization/Manager/Attributes/Docs.xml' path='entries/entry[@name="ImpliesDataDefinition"]/*'/>
    [ImplicitDataDefinitionForInheritors]
    [NotContentImplementable]
    public partial interface IComponent
    {
        /// <summary>
        ///     The current lifetime stage of this component. You can use this to check
        ///     if the component is initialized or being deleted.
        /// </summary>
        ComponentLifeStage LifeStage { get; internal set; }

        internal bool Networked { get; set; }

        /// <summary>
        ///     Per-instance switch that controls whether the server's state overwrites this component on clients.
        ///     When true (the default), the server sends its data to the client copies of this component.
        ///     When false, a client may change its local copy without the change being overwritten.
        /// </summary>
        /// <remarks>
        ///     Only has an effect if the component type is marked with <see cref="NetworkedComponentAttribute"/>;
        ///     otherwise it is ignored.
        /// </remarks>
        bool NetSyncEnabled { get; set; }

        /// <summary>
        ///     If true, and if this is a networked component, then component data will only be sent to players if their
        ///     controlled entity is the owner of this component. This is less performance intensive than <see cref="SessionSpecific"/>.
        /// </summary>
        bool SendOnlyToOwner { get; }

        /// <summary>
        ///     If true, and if this is a networked component, then this component will cause <see
        ///     cref="ComponentGetStateAttemptEvent"/> events to be raised to check whether a given player should
        ///     receive this component's state.
        /// </summary>
        bool SessionSpecific { get; }

        /// <summary>
        ///     The entity this component is attached to. Assigned by the entity system when the component is added.
        /// </summary>
        /// <remarks>
        ///     Deprecated: new code should pass the entity id around explicitly instead of reading it from the component.
        /// </remarks>
        [Obsolete("Update your API to allow accessing Owner through other means")]
        EntityUid Owner { get; set; }

        /// <summary>
        ///     Whether the component has been or is being initialized.
        /// </summary>
        /// <seealso cref="ComponentInit"/>
        bool Initialized { get; }

        /// <summary>
        ///     True while the component is active: after it has been started and before it is shut down.
        /// </summary>
        bool Running { get; }

        /// <summary>
        ///     True if the component has been removed from its owner, AKA deleted.
        /// </summary>
        /// <seealso cref="ComponentRemove"/>
        bool Deleted { get; }

        /// <summary>
        ///     Marks the component as changed so the networking layer sends it to clients again.
        ///     Also sets <see cref="LastModifiedTick"/> to the current tick.
        /// </summary>
        /// <param name="entManager">Optionally, the entity manager to use for the operation.</param>
        /// <remarks>
        ///     Deprecated: prefer the dirty API of the entity manager.
        /// </remarks>
        [Obsolete]
        void Dirty(IEntityManager? entManager = null);

        /// <summary>
        ///     This is the tick the component was created.
        /// </summary>
        GameTick CreationTick { get; internal set; }

        /// <summary>
        ///     The game tick at which the component was last marked as dirty.
        /// </summary>
        /// <remarks>
        ///     The getter is public; the setter is only accessible within the assembly.
        /// </remarks>
        GameTick LastModifiedTick { get; internal set; }

        internal void ClearTicks();

        internal void ClearCreationTick();
    }
}
