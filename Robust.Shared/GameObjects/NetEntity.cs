using System;
using JetBrains.Annotations;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Shared.ViewVariables;

namespace Robust.Shared.GameObjects;

/// <summary>
/// Network identifier for entities; used by client and server to refer to the same entity where their local <see cref="EntityUid"/> may differ.
/// </summary>
[Serializable, NetSerializable, CopyByRef]
public readonly struct NetEntity : IEquatable<NetEntity>, IComparable<NetEntity>, ISpanFormattable
{
    public readonly int Id;

    public const int ClientEntity = 2 << 29;

    /*
     * Differed to EntityUid to be more consistent with Arch.
     */

    /// <summary>
    ///     An Invalid entity UID you can compare against.
    /// </summary>
    public static readonly NetEntity Invalid = new(0);

    /// <summary>
    ///     The first network id an entity manager should hand out when it starts up.
    /// </summary>
    public static readonly NetEntity First = new(1);

    /// <summary>
    ///     Creates an instance that carries the given network id. The value is stored as-is, without validation.
    /// </summary>
    /// <param name="id">The raw network id.</param>
    public NetEntity(int id)
    {
        Id = id;
    }

    public bool Valid => IsValid();

    /// <summary>
    ///     Creates a network entity UID by parsing a string number.
    /// </summary>
    public static NetEntity Parse(ReadOnlySpan<char> uid)
    {
        if (uid.Length == 0)
            throw new FormatException($"An empty string is not a valid NetEntity");

        // 'c' prefix for client-side entities
        if (uid[0] != 'c')
            return new NetEntity(int.Parse(uid));

        if (uid.Length == 1)
            throw new FormatException($"'c' is not a valid NetEntity");

        var id = int.Parse(uid[1..]);
        return new NetEntity(id | ClientEntity);
    }

    public static bool TryParse(ReadOnlySpan<char> uid, out NetEntity entity)
    {
        entity = Invalid;
        int id;
        if (uid.Length == 0)
            return false;

        // 'c' prefix for client-side entities
        if (uid[0] != 'c')
        {
            if (!int.TryParse(uid, out id))
                return false;

            entity = new NetEntity(id);
            return true;
        }

        if (uid.Length == 1)
            return false;

        if (!int.TryParse(uid[1..], out id))
            return false;

        entity = new NetEntity(id | ClientEntity);
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
    public bool Equals(NetEntity other)
    {
        return Id == other.Id;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        // Only a boxed NetEntity can match; null and other types never do.
        return obj is NetEntity other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return Id;
    }

    /// <summary>
    ///     Check for equality by value between two objects.
    /// </summary>
    public static bool operator ==(NetEntity a, NetEntity b)
    {
        return a.Id == b.Id;
    }

    /// <summary>
    ///     Checks whether two network ids differ by value.
    /// </summary>
    public static bool operator !=(NetEntity a, NetEntity b)
    {
        return !(a == b);
    }

    /// <summary>
    ///     Explicit conversion from a network entity id to its raw int value. Intended only for special cases
    ///     such as serialization; content code must not use it.
    /// </summary>
    public static explicit operator int(NetEntity self)
    {
        return self.Id;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (IsClientSide())
            return $"c{Id & ~ClientEntity}";

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
        if (IsClientSide())
        {
            return FormatHelpers.TryFormatInto(
                destination,
                out charsWritten,
                $"c{Id & ~ClientEntity}");
        }

        return Id.TryFormat(destination, out charsWritten);
    }

    /// <inheritdoc />
    public int CompareTo(NetEntity other)
    {
        return Id.CompareTo(other.Id);
    }

    public bool IsClientSide() => (Id & ClientEntity) == ClientEntity;

    #region ViewVariables


    [ViewVariables]
    private string Representation
    {
        get
        {
            var entManager = IoCManager.Resolve<IEntityManager>();
            return entManager.ToPrettyString(entManager.GetEntity(this));
        }
    }

    [ViewVariables(VVAccess.ReadWrite)]
    private string Name
    {
        get => MetaData?.EntityName ?? string.Empty;
        set
        {
            if (MetaData is { } metaData)
            {
                var entManager = IoCManager.Resolve<IEntityManager>();
                entManager.System<MetaDataSystem>().SetEntityName(entManager.GetEntity(this), value, metaData);
            }
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
                entManager.System<MetaDataSystem>().SetEntityDescription(entManager.GetEntity(this), value, metaData);
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
    private MetaDataComponent? MetaData
    {
        get
        {
            var entManager = IoCManager.Resolve<IEntityManager>();
            return entManager.GetComponentOrNull<MetaDataComponent>(entManager.GetEntity(this));
        }
    }

    [ViewVariables]
    private TransformComponent? Transform
    {
        get
        {
            var entManager = IoCManager.Resolve<IEntityManager>();
            return entManager.GetComponentOrNull<TransformComponent>(entManager.GetEntity(this));
        }
    }

    [ViewVariables]
    private EntityUid _uid
    {
        get
        {
            return IoCManager.Resolve<IEntityManager>().GetEntity(this);
        }
    }

    [ViewVariables] private NetEntity _netId => this;

    #endregion
}
