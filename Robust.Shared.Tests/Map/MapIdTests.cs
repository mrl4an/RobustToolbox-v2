using System.Collections.Generic;
using NUnit.Framework;
using Robust.Shared.Map;

namespace Robust.Shared.Tests.Map;

/// <summary>
///     Tests for <see cref="MapId"/>, the typed wrapper around a signed 32-bit integer that identifies a map.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="MapId.Value"/> is <c>internal</c>. These tests can read it because Robust.Shared grants
///         internals access to Robust.Shared.Tests (see <c>InternalsVisibleTo</c> in Robust.Shared/AssemblyInfo.cs).
///         Content assemblies do not have that access and read the number through the explicit <c>int</c>
///         conversion, which is why both paths are covered here.
///     </para>
///     <para>
///         There is deliberately no implicit conversion from or to <see cref="int"/>. That is a compile-time property
///         and is not tested here: an implicit conversion would make overload resolution ambiguous in the many
///         engine methods that take an <c>EntityUid</c>, an <c>int</c> or a <see cref="MapId"/>. Code that needs a
///         <see cref="MapId"/> must call the constructor, and code that needs the raw number must cast explicitly.
///     </para>
/// </remarks>
[TestFixture]
[Parallelizable(ParallelScope.All)]
[TestOf(typeof(MapId))]
internal sealed class MapIdTests
{
    // Constructor, Value and the explicit int conversion.

    [Test]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(42)]
    [TestCase(-1)]
    [TestCase(-7)]
    [TestCase(int.MaxValue)]
    [TestCase(int.MinValue)]
    public void Constructor_StoresValueUnmodified(int raw)
    {
        // The constructor performs no validation and no transformation: any int, including negative values
        // (client-side maps) and int.MinValue, must come back exactly as it went in.
        var id = new MapId(raw);

        Assert.That(id.Value, Is.EqualTo(raw));
    }

    [Test]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(42)]
    [TestCase(-1)]
    [TestCase(-7)]
    [TestCase(int.MaxValue)]
    [TestCase(int.MinValue)]
    public void ExplicitIntConversion_ReturnsConstructorValue(int raw)
    {
        // Content assemblies cannot see the internal Value field, so this conversion is their only way to get
        // the raw number back. It must round-trip every int.
        var id = new MapId(raw);

        Assert.That((int) id, Is.EqualTo(raw));
    }

    [Test]
    public void ExplicitIntConversion_AgreesWithInternalValue()
    {
        // The public conversion and the internal field must never diverge.
        var id = new MapId(1234);

        Assert.That((int) id, Is.EqualTo(id.Value));
    }

    // Nullspace and default.

    [Test]
    public void Nullspace_HasValueZero()
    {
        Assert.That(MapId.Nullspace.Value, Is.EqualTo(0));
        Assert.That((int) MapId.Nullspace, Is.EqualTo(0));
    }

    [Test]
    public void Nullspace_EqualsConstructedZero()
    {
        // Nullspace is just the id with value 0; there is nothing special about the instance itself.
        var zero = new MapId(0);

        Assert.That(MapId.Nullspace, Is.EqualTo(zero));
        Assert.That(MapId.Nullspace == zero, Is.True);
        Assert.That(MapId.Nullspace.GetHashCode(), Is.EqualTo(zero.GetHashCode()));
    }

    [Test]
    public void Default_IsNullspace()
    {
        // The engine relies on this: component properties and struct fields that were never assigned hold
        // default(MapId), and that must mean "no map", i.e. Nullspace.
        var defaultId = default(MapId);

        Assert.That(defaultId.Value, Is.EqualTo(0));
        Assert.That(defaultId, Is.EqualTo(MapId.Nullspace));
        Assert.That(defaultId == MapId.Nullspace, Is.True);
        Assert.That(defaultId.GetHashCode(), Is.EqualTo(MapId.Nullspace.GetHashCode()));
    }

    [Test]
    public void Nullspace_IsNotClientSide()
    {
        Assert.That(MapId.Nullspace.IsClientSide, Is.False);
    }

    // Equality.

    [Test]
    public void EqualsMapId_SameValue_IsTrue()
    {
        var a = new MapId(5);
        var b = new MapId(5);

        Assert.That(a.Equals(b), Is.True);
        Assert.That(b.Equals(a), Is.True);
    }

    [Test]
    public void EqualsMapId_DifferentValue_IsFalse()
    {
        var a = new MapId(5);
        var b = new MapId(6);

        Assert.That(a.Equals(b), Is.False);
        Assert.That(b.Equals(a), Is.False);
    }

    [Test]
    public void EqualsMapId_PositiveAndNegativeOfSameMagnitude_AreDifferent()
    {
        // A server map 3 and a client-side map -3 are unrelated maps, even though they print similarly ("3" and "c3").
        Assert.That(new MapId(3).Equals(new MapId(-3)), Is.False);
    }

    [Test]
    public void EqualsObject_Null_IsFalse()
    {
        Assert.That(new MapId(5).Equals((object?) null), Is.False);
    }

    [Test]
    public void EqualsObject_OtherType_IsFalse()
    {
        // A boxed int with the same number is not a MapId; there is no implicit relationship between the types.
        var id = new MapId(5);

        Assert.That(id.Equals((object) 5), Is.False);
        Assert.That(id.Equals("5"), Is.False);
    }

    [Test]
    public void EqualsObject_BoxedMapId_BehavesLikeTypedEquals()
    {
        var id = new MapId(5);
        object sameBoxed = new MapId(5);
        object otherBoxed = new MapId(6);

        Assert.That(id.Equals(sameBoxed), Is.True);
        Assert.That(id.Equals(otherBoxed), Is.False);
    }

    [Test]
    public void EqualityOperator_ComparesValues()
    {
        Assert.That(new MapId(5) == new MapId(5), Is.True);
        Assert.That(new MapId(5) == new MapId(6), Is.False);
        Assert.That(new MapId(-1) == new MapId(-1), Is.True);
        Assert.That(new MapId(int.MinValue) == new MapId(int.MinValue), Is.True);
    }

    [Test]
    public void InequalityOperator_IsExactNegationOfEquality()
    {
        // Checked over a spread of values, including pairs that are equal and pairs that are not.
        int[] samples = { 0, 1, -1, 5, -5, int.MaxValue, int.MinValue };

        foreach (var left in samples)
        {
            foreach (var right in samples)
            {
                var a = new MapId(left);
                var b = new MapId(right);

                Assert.That(a != b, Is.EqualTo(!(a == b)), $"Mismatch for {left} and {right}");
            }
        }
    }

    [Test]
    public void InequalityOperator_ComparesValues()
    {
        Assert.That(new MapId(5) != new MapId(6), Is.True);
        Assert.That(new MapId(5) != new MapId(5), Is.False);
    }

    // Hash code.

    [Test]
    public void GetHashCode_EqualIdsHaveEqualHashCodes()
    {
        // The basic contract every hash-based collection relies on.
        Assert.That(new MapId(17).GetHashCode(), Is.EqualTo(new MapId(17).GetHashCode()));
        Assert.That(new MapId(-17).GetHashCode(), Is.EqualTo(new MapId(-17).GetHashCode()));
    }

    [Test]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(42)]
    [TestCase(-1)]
    [TestCase(-7)]
    [TestCase(int.MaxValue)]
    [TestCase(int.MinValue)]
    public void GetHashCode_IsTheRawValue(int raw)
    {
        // Unlike many hashes, this one is part of the documented contract: the id is its own hash. Hash-based
        // collection iteration order and some content code that hashes map ids (for example together with chunk
        // coordinates) can observe it. It is a plain integer, not a process-randomised value, so it is safe to assert.
        Assert.That(new MapId(raw).GetHashCode(), Is.EqualTo(raw));
    }

    // IsClientSide.

    [Test]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(42)]
    [TestCase(int.MaxValue)]
    public void IsClientSide_ZeroAndPositive_IsFalse(int raw)
    {
        // Zero is Nullspace and positive ids are allocated by the server and shared code. None of them are client-side.
        Assert.That(new MapId(raw).IsClientSide, Is.False);
    }

    [Test]
    [TestCase(-1)]
    [TestCase(-7)]
    [TestCase(int.MinValue)]
    public void IsClientSide_Negative_IsTrue(int raw)
    {
        // Negative ids are reserved for maps that exist only on a client. The game uses this property to reject
        // creating such maps on a server.
        Assert.That(new MapId(raw).IsClientSide, Is.True);
    }

    // ToString.

    [Test]
    [TestCase(0, "0")]
    [TestCase(1, "1")]
    [TestCase(7, "7")]
    [TestCase(42, "42")]
    [TestCase(int.MaxValue, "2147483647")]
    public void ToString_NonNegative_IsPlainDecimal(int raw, string expected)
    {
        // The text shows up in logs, console output and debug UI, so the format is a contract. No group
        // separators and no culture-specific formatting.
        Assert.That(new MapId(raw).ToString(), Is.EqualTo(expected));
    }

    [Test]
    [TestCase(-1, "c1")]
    [TestCase(-3, "c3")]
    [TestCase(-7, "c7")]
    [TestCase(-42, "c42")]
    public void ToString_ClientSide_IsLowercaseCFollowedByMagnitude(int raw, string expected)
    {
        // Client-side ids are printed as "c" plus the positive magnitude, with no minus sign.
        // Consequence worth knowing: this text cannot be fed back into int.Parse-based console commands.
        Assert.That(new MapId(raw).ToString(), Is.EqualTo(expected));
    }

    [Test]
    public void ToString_IntMinValue_PinnedQuirk()
    {
        // PINNED QUIRK, not a desired property. -int.MinValue overflows in unchecked arithmetic and stays
        // int.MinValue, so the "magnitude" printed after the "c" still carries a minus sign. This test documents
        // current behaviour so that any change to it is deliberate; it is not an endorsement of the output.
        Assert.That(new MapId(int.MinValue).ToString(), Is.EqualTo("c-2147483648"));
    }

    [Test]
    public void ToString_UsedInStringInterpolation()
    {
        // Callers routinely interpolate ids into messages (MapCoordinates.ToString prints "Map=<MapId>, ...").
        var server = new MapId(2);
        var client = new MapId(-2);

        Assert.That($"Map={server}", Is.EqualTo("Map=2"));
        Assert.That($"Map={client}", Is.EqualTo("Map=c2"));
    }

    // Use in collections.

    [Test]
    public void Dictionary_UsesValueEqualityForKeys()
    {
        var dict = new Dictionary<MapId, string>
        {
            [new MapId(1)] = "first",
            [new MapId(-1)] = "client",
            [MapId.Nullspace] = "nullspace",
        };

        // Keys constructed independently must find the same entries.
        Assert.That(dict[new MapId(1)], Is.EqualTo("first"));
        Assert.That(dict[new MapId(-1)], Is.EqualTo("client"));
        Assert.That(dict[default], Is.EqualTo("nullspace"));
        Assert.That(dict.ContainsKey(new MapId(2)), Is.False);
        Assert.That(dict, Has.Count.EqualTo(3));

        // Assigning through an equal key replaces the entry instead of adding a new one.
        dict[new MapId(1)] = "replaced";

        Assert.That(dict[new MapId(1)], Is.EqualTo("replaced"));
        Assert.That(dict, Has.Count.EqualTo(3));
    }

    [Test]
    public void HashSet_DeduplicatesEqualIds()
    {
        var set = new HashSet<MapId>
        {
            new MapId(3),
            new MapId(3),
            new MapId(-3),
            MapId.Nullspace,
            default,
        };

        // 3, -3 and Nullspace; default(MapId) is the same id as Nullspace, and the repeated 3 collapses.
        Assert.That(set, Has.Count.EqualTo(3));
        Assert.That(set.Contains(new MapId(3)), Is.True);
        Assert.That(set.Contains(new MapId(-3)), Is.True);
        Assert.That(set.Contains(new MapId(0)), Is.True);
        Assert.That(set.Contains(new MapId(4)), Is.False);
    }
}
