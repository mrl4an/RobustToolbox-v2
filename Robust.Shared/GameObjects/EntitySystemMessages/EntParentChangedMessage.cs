namespace Robust.Shared.GameObjects;

/// <summary>
/// Raised on an entity after its parent has changed, and also once for every entity when its
/// <see cref="TransformComponent"/> starts up.
/// </summary>
/// <remarks>
/// <para>
/// The event is raised by the transform system whenever a move or reparent results in a parent that differs from
/// the previous one. It is directed at <see cref="Entity"/> and also broadcast, so both directed and non-directed
/// subscriptions receive it. It is raised after the physics system has handled the parent change and before the
/// move event is raised.
/// </para>
/// <para>
/// During transform component startup the event is raised with a <see langword="null"/> <see cref="OldParent"/> and
/// a <see langword="null"/> <see cref="OldMapId"/>, even though nothing actually changed. This lets systems that
/// need to react to an entity's initial parent (for example container or power handling) run their set-up logic.
/// Subscribers must therefore tolerate events where no real change took place.
/// </para>
/// <para>
/// Content code may also build a synthetic instance of this event and pass it directly to a handler method without
/// going through the event bus, for example to forward a parent change to entities following the changed entity.
/// </para>
/// <para>
/// This is a local event; it is never networked or serialized. It is marked with <see cref="ByRefEvent"/>, so it
/// must be raised by reference and handlers should declare the event parameter as <see langword="ref"/>.
/// Because the struct is read-only, handlers cannot modify it.
/// </para>
/// <para>
/// The new parent is not stored directly on the event. Read it from <see cref="Transform"/>, for example
/// <see cref="TransformComponent.ParentUid"/>, <see cref="TransformComponent.GridUid"/>,
/// <see cref="TransformComponent.MapUid"/> or <see cref="TransformComponent.Coordinates"/>.
/// </para>
/// </remarks>
[ByRefEvent]
public readonly struct EntParentChangedMessage
{
    /// <summary>
    /// The entity whose parent changed, that is, the child. It is a valid entity when the event is raised, and it is
    /// the entity the event is directed at.
    /// </summary>
    public EntityUid Entity { get; }

    /// <summary>
    /// The entity that was the parent immediately before the change, or <see langword="null"/> if there was none or
    /// it is not known.
    /// </summary>
    /// <remarks>
    /// "No previous parent" can be expressed in two ways. The component startup path and synthetic events created by
    /// content use <see langword="null"/>, while the reparent path forwards the previous parent as-is, which may be
    /// <see cref="EntityUid.Invalid"/> for an entity that previously had no parent. Subscribers should treat both
    /// values as meaning that there was no previous parent.
    /// </remarks>
    public EntityUid? OldParent { get; }

    /// <summary>
    /// The map entity that <see cref="Entity"/> was on before the change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Despite the name, this is the uid of a map entity and not a <see cref="Robust.Shared.Map.MapId"/> integer.
    /// </para>
    /// <para>
    /// It is stored explicitly because the old parent may already have been detached from the map hierarchy without
    /// the cached map data of its children being updated. In that case the old parent's own map cannot be relied on,
    /// and looking it up would require fetching another transform component.
    /// </para>
    /// <para>
    /// Subscribers typically compare this to <see cref="TransformComponent.MapUid"/> of <see cref="Transform"/>
    /// to find out whether the entity moved to a different map. It is <see langword="null"/> if the entity was not on
    /// a map or the map is unknown, and always <see langword="null"/> when the event is raised during transform
    /// component startup.
    /// </para>
    /// This is intentionally a public field rather than a property, to keep binary compatibility with compiled code.
    /// </remarks>
    public readonly EntityUid? OldMapId;

    /// <summary>
    /// The transform component of <see cref="Entity"/>, in its state after the change. It already holds the new
    /// parent and the new position data, so subscribers can use it instead of fetching the component again.
    /// </summary>
    public TransformComponent Transform { get; }

    /// <summary>
    /// Creates a new parent-changed event.
    /// </summary>
    /// <param name="entity">The entity whose parent changed.</param>
    /// <param name="oldParent">The previous parent of the entity, if any. See <see cref="OldParent"/>.</param>
    /// <param name="oldMapId">The map entity the entity was on before the change. See <see cref="OldMapId"/>.</param>
    /// <param name="xform">The transform component of <paramref name="entity"/>, after the change.</param>
    public EntParentChangedMessage(EntityUid entity, EntityUid? oldParent, EntityUid? oldMapId, TransformComponent xform)
    {
        Entity = entity;
        OldParent = oldParent;
        OldMapId = oldMapId;
        Transform = xform;
    }
}
