using System;
using System.Linq;
using NUnit.Framework;
using Robust.Shared.Configuration;

namespace Robust.Shared.Tests.Configuration;

/// <summary>
///     Tests that pin the numeric layout of the <see cref="CVar"/> flags enum.
/// </summary>
/// <remarks>
///     <para>
///         Config files store cvar names and values, and the replication messages carry name/value pairs, so the
///         flag numbers never appear on disk or on the wire. They still must not change: content assemblies are
///         compiled against the engine and bake the enum constants into their own binaries (for example every
///         <c>CVarDef.Create(..., CVar.ARCHIVE | CVar.SERVER)</c> call). If a number changed, an already compiled
///         content assembly would silently register its cvars with different behavior. Tooling that casts to and
///         from <see cref="short"/> depends on the layout as well.
///     </para>
///     <para>
///         These tests exist so that renumbering, reordering by implicit increment, or inserting a member is a
///         deliberate decision that has to update this file, rather than an accident.
///     </para>
/// </remarks>
[TestFixture]
[Parallelizable(ParallelScope.All)]
[TestOf(typeof(CVar))]
internal sealed class CVarFlagsTests
{
    // The complete expected layout. Keep in sync with the CVar enum; see Enum_ContainsExactlyTheExpectedMembers.
    private static readonly (CVar Flag, string Name, int Value)[] ExpectedMembers =
    {
        (CVar.NONE, "NONE", 0),
        (CVar.CHEAT, "CHEAT", 1),
        (CVar.SERVER, "SERVER", 2),
        (CVar.NOT_CONNECTED, "NOT_CONNECTED", 4),
        (CVar.REPLICATED, "REPLICATED", 8),
        (CVar.ARCHIVE, "ARCHIVE", 16),
        (CVar.NOTIFY, "NOTIFY", 32),
        (CVar.SERVERONLY, "SERVERONLY", 64),
        (CVar.CLIENTONLY, "CLIENTONLY", 128),
        (CVar.CONFIDENTIAL, "CONFIDENTIAL", 256),
        (CVar.CLIENT, "CLIENT", 512),
    };

    // Type shape.

    [Test]
    public void UnderlyingType_IsShort()
    {
        // Compiled content inlines the constants as 16-bit values; widening or narrowing the type changes how
        // they are interpreted. Bit 15 is the sign bit of this type and must stay unused.
        Assert.That(Enum.GetUnderlyingType(typeof(CVar)), Is.EqualTo(typeof(short)));
    }

    [Test]
    public void Enum_HasFlagsAttribute()
    {
        // [Flags] is what makes ToString print combinations such as "CHEAT, REPLICATED" and lets Enum.Parse
        // accept comma-separated names.
        Assert.That(typeof(CVar).IsDefined(typeof(FlagsAttribute), false), Is.True);
    }

    // Exact values.

    [Test]
    [TestCase(CVar.NONE, 0)]
    [TestCase(CVar.CHEAT, 1)]
    [TestCase(CVar.SERVER, 2)]
    [TestCase(CVar.NOT_CONNECTED, 4)]
    [TestCase(CVar.REPLICATED, 8)]
    [TestCase(CVar.ARCHIVE, 16)]
    [TestCase(CVar.NOTIFY, 32)]
    [TestCase(CVar.SERVERONLY, 64)]
    [TestCase(CVar.CLIENTONLY, 128)]
    [TestCase(CVar.CONFIDENTIAL, 256)]
    [TestCase(CVar.CLIENT, 512)]
    public void Member_HasExpectedNumericValue(CVar flag, int expected)
    {
        // Every member has an explicit value and these numbers must never change (see the fixture remarks).
        Assert.That((short) flag, Is.EqualTo(expected));
    }

    [Test]
    public void None_IsZero()
    {
        // NONE is the default of the optional flags parameters on CVarDef.Create and RegisterCVar. Those defaults
        // bake the constant 0 into compiled callers, so NONE has to be exactly the empty set.
        Assert.That((short) CVar.NONE, Is.EqualTo(0));
        Assert.That(default(CVar), Is.EqualTo(CVar.NONE));
    }

    [Test]
    public void Names_AreSpelledAsExpected()
    {
        // Names are used by tooling and documentation, and they are what ToString and Enum.Parse use. Note the
        // underscore in NOT_CONNECTED and the lack of one in SERVERONLY and CLIENTONLY.
        foreach (var (flag, name, _) in ExpectedMembers)
        {
            Assert.That(flag.ToString(), Is.EqualTo(name));
            Assert.That(Enum.Parse<CVar>(name), Is.EqualTo(flag));
        }
    }

    // Bit layout.

    [Test]
    public void NonZeroFlags_AreEachExactlyOneBit()
    {
        // A flag that spans several bits would make combinations ambiguous: testing for it would also match any
        // value that merely contains some of its bits.
        foreach (var (flag, name, _) in ExpectedMembers.Where(m => m.Flag != CVar.NONE))
        {
            var value = (int) flag;

            Assert.That(value, Is.GreaterThan(0), $"{name} must be positive (bit 15 is the sign bit and unused)");
            Assert.That(value & (value - 1), Is.EqualTo(0), $"{name} must be a power of two");
        }
    }

    [Test]
    public void Flags_DoNotShareBits()
    {
        // Each flag must be independently settable and testable. Accumulating the members one by one, every
        // non-zero flag has to bring a bit that no earlier flag already used.
        var seen = 0;

        foreach (var (flag, name, _) in ExpectedMembers)
        {
            var bits = (int) flag;

            Assert.That(seen & bits, Is.EqualTo(0), $"{name} overlaps with an earlier flag");
            seen |= bits;
        }
    }

    [Test]
    public void AllFlagsTogether_StayInsideTheDocumentedBits()
    {
        // Bits 0 to 9 are used. Bits 10 to 14 are free for future flags and bit 15 (the sign bit of short) must not
        // be used, so the union of every flag fits within the lower ten bits.
        var all = 0;
        foreach (var (flag, _, _) in ExpectedMembers)
        {
            all |= (int) flag;
        }

        Assert.That(all, Is.EqualTo(0x03FF));
    }

    // Combining and testing.

    [Test]
    public void Combination_ServerAndClient_ContainsBothAndNothingElse()
    {
        // Documented in CVar.CLIENT: a cvar may carry both authority flags, which blocks non-forced changes on
        // both sides in a networked game.
        var combined = CVar.SERVER | CVar.CLIENT;

        Assert.That((short) combined, Is.EqualTo(2 + 512));
        Assert.That(combined.HasFlag(CVar.SERVER), Is.True);
        Assert.That(combined.HasFlag(CVar.CLIENT), Is.True);
        Assert.That((combined & CVar.SERVER) != 0, Is.True);
        Assert.That((combined & CVar.CLIENT) != 0, Is.True);

        // Neighbouring concepts must not leak in: SERVERONLY and CLIENTONLY control registration, not authority.
        Assert.That(combined.HasFlag(CVar.SERVERONLY), Is.False);
        Assert.That(combined.HasFlag(CVar.CLIENTONLY), Is.False);
        Assert.That((combined & CVar.REPLICATED) != 0, Is.False);
    }

    [Test]
    public void Combination_ArchiveAndReplicated_AreIndependent()
    {
        // Persistence (ARCHIVE) and synchronization (REPLICATED) are independent concepts, and the very common
        // ARCHIVE | REPLICATED | SERVER combination relies on them being separate bits.
        var combined = CVar.ARCHIVE | CVar.REPLICATED;

        Assert.That((short) combined, Is.EqualTo(16 + 8));
        Assert.That(combined.HasFlag(CVar.ARCHIVE), Is.True);
        Assert.That(combined.HasFlag(CVar.REPLICATED), Is.True);
        Assert.That((combined & CVar.ARCHIVE) != 0, Is.True);
        Assert.That((combined & CVar.REPLICATED) != 0, Is.True);
        Assert.That((combined & CVar.SERVER) != 0, Is.False);
        Assert.That(combined.HasFlag(CVar.NOTIFY), Is.False);
    }

    [Test]
    public void Combination_ArchiveReplicatedServer_IncludesEachMember()
    {
        // The most common definition in the engine and content: persistent, server-authoritative, synced to clients.
        var combined = CVar.ARCHIVE | CVar.REPLICATED | CVar.SERVER;

        Assert.That((short) combined, Is.EqualTo(16 + 8 + 2));
        Assert.That(combined.HasFlag(CVar.ARCHIVE | CVar.REPLICATED | CVar.SERVER), Is.True);
        Assert.That(combined.HasFlag(CVar.CLIENT), Is.False);
    }

    [Test]
    public void RemovingAFlag_LeavesTheOthersIntact()
    {
        var combined = CVar.ARCHIVE | CVar.REPLICATED | CVar.SERVER;

        var withoutReplicated = combined & ~CVar.REPLICATED;

        Assert.That(withoutReplicated, Is.EqualTo(CVar.ARCHIVE | CVar.SERVER));
        Assert.That(withoutReplicated.HasFlag(CVar.REPLICATED), Is.False);
    }

    [Test]
    public void None_BehavesAsTheEmptySet()
    {
        // HasFlag(NONE) is always true for any value, which is why the documented way to test for "no flags" is
        // an equality check.
        Assert.That(CVar.SERVER.HasFlag(CVar.NONE), Is.True);
        Assert.That(CVar.NONE.HasFlag(CVar.NONE), Is.True);
        Assert.That(CVar.NONE.HasFlag(CVar.SERVER), Is.False);

        // OR-ing with NONE changes nothing, AND-ing with NONE clears everything.
        Assert.That(CVar.ARCHIVE | CVar.NONE, Is.EqualTo(CVar.ARCHIVE));
        Assert.That(CVar.ARCHIVE & CVar.NONE, Is.EqualTo(CVar.NONE));
    }

    [Test]
    public void Combination_ToStringAndParse_RoundTrip()
    {
        // [Flags] makes combinations print as comma-separated names in ascending value order.
        var combined = CVar.REPLICATED | CVar.CHEAT;

        Assert.That(combined.ToString(), Is.EqualTo("CHEAT, REPLICATED"));
        Assert.That(Enum.Parse<CVar>("CHEAT, REPLICATED"), Is.EqualTo(combined));
    }

    // Member set.

    [Test]
    public void Enum_ContainsExactlyTheExpectedMembers()
    {
        // This pins the set of members. Adding a new flag must be a deliberate change that also updates
        // ExpectedMembers, the per-member test cases above and the bit-union check. There are also no composite
        // aliases such as an "All" member, and no second name for the zero value.
        var actualNames = Enum.GetNames<CVar>();
        var expectedNames = ExpectedMembers.Select(m => m.Name).ToArray();

        Assert.That(actualNames, Is.EquivalentTo(expectedNames));

        var actualValues = Enum.GetValues<CVar>().Select(v => (int) v).ToArray();
        var expectedValues = ExpectedMembers.Select(m => m.Value).ToArray();

        Assert.That(actualValues, Is.EquivalentTo(expectedValues));
        Assert.That(actualValues, Has.Length.EqualTo(11));
        Assert.That(actualValues.Distinct().Count(), Is.EqualTo(actualValues.Length), "Two members share a value");
    }

    [Test]
    public void Enum_MembersAreDeclaredInAscendingValueOrder()
    {
        // Members must not be reordered or inserted in the middle; declaration order mirrors the numeric order.
        var values = Enum.GetValues<CVar>().Select(v => (int) v).ToArray();

        Assert.That(values, Is.Ordered.Ascending);
    }
}
