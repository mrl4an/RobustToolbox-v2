using System;
using JetBrains.Annotations;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Timing;
using Robust.Shared.ViewVariables;

namespace Robust.Shared.GameObjects
{
    /// <summary>
    ///     This type contains the unique identifier for a given entity.
    ///     This can be used with and obtained from <see cref="IEntityManager"/> to manipulate, query, and otherwise
    ///     work with entities and their components.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     An entity is a unique identifier (this type) and a collection of assorted <see cref="Component"/>s that are
    ///     attached to it. Components provide data to describe the entity, and entities+components are operated on by
    ///     <see cref="EntitySystem"/>s.
    /// </para>
    /// <para>
    ///     EntityUids are not guaranteed to be unique across individual instances of the game, or individual instances
    ///     of <see cref="IEntityManager"/>. For network identification, see <see cref="NetEntity"/>, and for global
    ///     uniqueness across time you'll need to make something yourself.
    /// </para>
    /// <para>
    ///     Sharing EntityUids between <see cref="IEntityManager"/>s, or otherwise summoning IDs from thin air, is
    ///     effectively undefined behavior and most likely will refer to some random other entity that may or may not
    ///     still exist.
    /// </para>
    /// </remarks>
    [CopyByRef]
    public readonly struct EntityUid : IEquatable<EntityUid>, IComparable<EntityUid>, ISpanFormattable
    {
        public readonly int Id;

        /// <summary>
        ///     An entity UID that is never assigned to a live entity. Compare against it to detect "no entity".
        /// </summary>
        public static readonly EntityUid Invalid = new(0);

        /// <summary>
        ///     The first UID an entity manager hands out when it starts up.
        /// </summary>
        public static readonly EntityUid FirstUid = new(1);

        /// <summary>
        ///     Creates an instance that carries the given unique id. The value is stored as-is, without validation.
        /// </summary>
        /// <param name="id">The raw numeric id.</param>
        public EntityUid(int id)
        {
            Id = id;
        }

        public bool Valid => IsValid();

        /// <summary>
        ///     Creates an entity UID by parsing a string number.
        /// </summary>
        public static EntityUid Parse(ReadOnlySpan<char> uid)
        {
            return new EntityUid(int.Parse(uid));
        }

        public static bool TryParse(ReadOnlySpan<char> uid, out EntityUid entityUid)
        {
            if (!int.TryParse(uid, out var id))
            {
                entityUid = default;
                return false;
            }

            entityUid = new(id);
            return true;
        }

        /// <summary>
        ///     Checks that the id value is plausible (strictly positive). This does NOT check whether
        ///     a live entity currently owns this id.
        /// </summary>
        /// <returns>True if <see cref="Id"/> is greater than zero.</returns>
        [Pure]
        public bool IsValid()
        {
            return Id > 0;
        }

        /// <inheritdoc />
        public bool Equals(EntityUid other)
        {
            return Id == other.Id;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            // Only a boxed EntityUid can match; null and other types never do.
            return obj is EntityUid other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            // The odd multiplier keeps hashes distinct from any future scheme that adds a version number.
            // Overflow is allowed to wrap.
            return unchecked(Id.GetHashCode() * 397);
        }

        /// <summary>
        ///     Checks whether two UIDs are equal by value.
        /// </summary>
        public static bool operator ==(EntityUid a, EntityUid b)
        {
            return a.Id == b.Id;
        }

        /// <summary>
        ///     Checks whether two UIDs differ by value.
        /// </summary>
        public static bool operator !=(EntityUid a, EntityUid b)
        {
            return !(a == b);
        }

        /// <summary>
        ///     Explicit conversion from an entity UID to its raw int id. Intended only for special cases such as
        ///     serialization; content code must not use it.
        /// </summary>
        public static explicit operator int(EntityUid self)
        {
            return self.Id;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Id.ToString();
        }

        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            return ToString();
        }

        public bool TryFormat(
            Span<char> destination,
            out int charsWritten,
            ReadOnlySpan<char> format,
            IFormatProvider? provider)
        {
            return Id.TryFormat(destination, out charsWritten);
        }

        /// <inheritdoc />
        public int CompareTo(EntityUid other)
        {
            return Id.CompareTo(other.Id);
        }

        #region ViewVariables


        [ViewVariables]
        private string Representation => IoCManager.Resolve<IEntityManager>().ToPrettyString(this);

        [ViewVariables(VVAccess.ReadWrite)]
        private string Name
        {
            get => MetaData?.EntityName ?? string.Empty;
            set
            {
                if (MetaData is {} metaData)
                    IoCManager.Resolve<IEntityManager>().System<MetaDataSystem>().SetEntityName(this, value, metaData);
            }
        }

        [ViewVariables(VVAccess.ReadWrite)]
        private string Description
        {
            get => MetaData?.EntityDescription ?? string.Empty;
            set
            {
                if (MetaData is { } metaData)
                {
                    var entManager = IoCManager.Resolve<IEntityManager>();
                    entManager.System<MetaDataSystem>().SetEntityDescription(this, value, metaData);
                }
            }
        }

        [ViewVariables]
        private EntityPrototype? Prototype => MetaData?.EntityPrototype;

        [ViewVariables]
        private GameTick LastModifiedTick => MetaData?.EntityLastModifiedTick ?? GameTick.Zero;

        [ViewVariables]
        private bool Paused => MetaData?.EntityPaused ?? false;

        [ViewVariables]
        private EntityLifeStage LifeStage => MetaData?.EntityLifeStage ?? EntityLifeStage.Deleted;

        [ViewVariables]
        private MetaDataComponent? MetaData =>
            IoCManager.Resolve<IEntityManager>().GetComponentOrNull<MetaDataComponent>(this);

        [ViewVariables]
        private TransformComponent? Transform =>
            IoCManager.Resolve<IEntityManager>().GetComponentOrNull<TransformComponent>(this);

        // This might seem useless, but it allows you to retrieve remote entities that don't exist on the client.
        [ViewVariables]
        private EntityUid _uid => this;

        [ViewVariables]
        private NetEntity _netId => IoCManager.Resolve<IEntityManager>().GetNetEntity(this);
        #endregion
    }
}
