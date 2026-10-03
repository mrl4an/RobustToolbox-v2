using System.Numerics;
using NUnit.Framework;
using Robust.Shared.Maths;
using Robust.Shared.Physics;

namespace Robust.Shared.Tests.Physics;

/// <summary>
///     Tests for <see cref="CollisionRay"/>: a thin wrapper that bundles a <see cref="Ray"/> with a collision mask.
/// </summary>
/// <remarks>
///     Box2 is built as <c>new Box2(left, bottom, right, top)</c>.
///
///     Every ray below uses a direction of exactly unit length, because the <see cref="Ray"/> constructor
///     (which <see cref="CollisionRay"/> calls) asserts this in debug builds. (1, 0) and (0.6, 0.8) are used
///     since 0.6^2 + 0.8^2 = 1 is exact enough that the assertion never trips.
/// </remarks>
[TestFixture, Parallelizable, TestOf(typeof(CollisionRay))]
internal sealed class CollisionRayTests
{
    private const float Tolerance = 1e-4f;

    private static readonly Vector2 UnitDiagonal = new(0.6f, 0.8f);
    private static readonly Vector2 UnitX = new(1f, 0f);

    private static void AssertVector(Vector2 actual, float expectedX, float expectedY)
    {
        Assert.That(actual.X, Is.EqualTo(expectedX).Within(Tolerance));
        Assert.That(actual.Y, Is.EqualTo(expectedY).Within(Tolerance));
    }

    #region Construction and properties

    [Test]
    public void Constructor_StoresPositionDirectionAndMask()
    {
        var position = new Vector2(3.5f, -2f);

        var ray = new CollisionRay(position, UnitDiagonal, 0x1234);

        Assert.Multiple(() =>
        {
            Assert.That(ray.Position, Is.EqualTo(position));
            Assert.That(ray.Direction, Is.EqualTo(UnitDiagonal));
            Assert.That(ray.CollisionMask, Is.EqualTo(0x1234));
        });
    }

    [Test]
    public void Constructor_NamedCollisionMaskArgument_IsAccepted()
    {
        // Callers pass the mask by name, so the parameter name "collisionMask" is part of the public surface.
        var ray = new CollisionRay(position: Vector2.One, direction: UnitX, collisionMask: 7);

        Assert.Multiple(() =>
        {
            Assert.That(ray.Position, Is.EqualTo(Vector2.One));
            Assert.That(ray.Direction, Is.EqualTo(UnitX));
            Assert.That(ray.CollisionMask, Is.EqualTo(7));
        });
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(int.MaxValue)]
    [TestCase(int.MinValue)]
    public void Constructor_AcceptsAnyIntegerMaskUnchanged(int mask)
    {
        // The mask is an opaque bit set: no clamping, sign handling or masking is applied.
        var ray = new CollisionRay(Vector2.Zero, UnitX, mask);

        Assert.That(ray.CollisionMask, Is.EqualTo(mask));
    }

    [Test]
    public void Constructor_DoesNotNormalizeOrAlterPositionAndDirection()
    {
        // Values must come back bit-for-bit, so an exact comparison is used (Is.EqualTo on Vector2 is exact).
        var position = new Vector2(1e6f, -1e-6f);

        var ray = new CollisionRay(position, UnitDiagonal, 1);

        Assert.Multiple(() =>
        {
            Assert.That(ray.Position.X, Is.EqualTo(1e6f));
            Assert.That(ray.Position.Y, Is.EqualTo(-1e-6f));
            Assert.That(ray.Direction.X, Is.EqualTo(0.6f));
            Assert.That(ray.Direction.Y, Is.EqualTo(0.8f));
        });
    }

    [Test]
    public void DefaultValue_HasZeroPositionDirectionAndMask()
    {
        // default(CollisionRay) never runs the constructor, so the unit-length assertion cannot fire and the
        // direction is simply (0, 0).
        var ray = default(CollisionRay);

        Assert.Multiple(() =>
        {
            Assert.That(ray.Position, Is.EqualTo(Vector2.Zero));
            Assert.That(ray.Direction, Is.EqualTo(Vector2.Zero));
            Assert.That(ray.CollisionMask, Is.EqualTo(0));
        });
    }

    #endregion

    #region Conversion to Ray

    [Test]
    public void ImplicitConversion_ToRay_CarriesPositionAndDirection()
    {
        var collisionRay = new CollisionRay(new Vector2(3.5f, -2f), UnitDiagonal, 0xFF);

        Ray ray = collisionRay;

        Assert.Multiple(() =>
        {
            Assert.That(ray.Position, Is.EqualTo(new Vector2(3.5f, -2f)));
            Assert.That(ray.Direction, Is.EqualTo(UnitDiagonal));
        });
    }

    [Test]
    public void ImplicitConversion_DropsTheMask()
    {
        // Ray has no mask, so two CollisionRays that differ only by mask convert to identical Rays.
        var withMaskA = new CollisionRay(new Vector2(1f, 2f), UnitDiagonal, 1);
        var withMaskB = new CollisionRay(new Vector2(1f, 2f), UnitDiagonal, 2);

        Ray rayA = withMaskA;
        Ray rayB = withMaskB;

        Assert.That(rayA == rayB, Is.True);
    }

    [Test]
    public void ImplicitConversion_ResultEqualsEquivalentRay()
    {
        var position = new Vector2(1f, 2f);
        var collisionRay = new CollisionRay(position, UnitDiagonal, 5);
        var plainRay = new Ray(position, UnitDiagonal);

        Ray converted = collisionRay;

        Assert.That(converted == plainRay, Is.True);
    }

    [Test]
    public void ImplicitConversion_AllowsPassingCollisionRayWhereRayIsExpected()
    {
        // This is how the physics queries consume the type: a CollisionRay is handed to a parameter of type Ray.
        var collisionRay = new CollisionRay(new Vector2(-2f, 0f), UnitX, 1);

        var result = ReadPosition(collisionRay);

        Assert.That(result, Is.EqualTo(new Vector2(-2f, 0f)));
    }

    private static Vector2 ReadPosition(Ray ray) => ray.Position;

    #endregion

    #region Intersects

    [Test]
    public void Intersects_Hit_ReturnsNearFaceDistanceAndHitPosition()
    {
        // Box: x [1, 3] by y [-1, 1]. The ray starts at (-2, 0) and travels along +X.
        // X slab: t1 = (1 - -2) / 1 = 3 (near face), t2 = (3 - -2) / 1 = 5 (far face).
        // The Y slab is parallel to the ray and the origin (y = 0) lies strictly inside it, so the entry
        // parameter is 3 and the hit position is (-2 + 3, 0) = (1, 0).
        var ray = new CollisionRay(new Vector2(-2f, 0f), UnitX, 1);
        var box = new Box2(1f, -1f, 3f, 1f);

        var hit = ray.Intersects(box, out var distance, out var hitPos);

        Assert.Multiple(() =>
        {
            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(3f).Within(Tolerance));
            AssertVector(hitPos, 1f, 0f);
        });
    }

    [Test]
    public void Intersects_Miss_ReturnsFalseWithZeroedOutputs()
    {
        // Box: x [1, 3] by y [-1, 1]. The ray travels along +X at y = 5, above the Y slab [-1, 1].
        // Its Y direction component is 0 (parallel) and the origin is outside that slab, so it misses.
        // On a miss both out parameters are reset: distance = 0 and hitPos = (0, 0).
        var ray = new CollisionRay(new Vector2(-2f, 5f), UnitX, 1);
        var box = new Box2(1f, -1f, 3f, 1f);

        var hit = ray.Intersects(box, out var distance, out var hitPos);

        Assert.Multiple(() =>
        {
            Assert.That(hit, Is.False);
            Assert.That(distance, Is.EqualTo(0f));
            Assert.That(hitPos, Is.EqualTo(Vector2.Zero));
        });
    }

    [Test]
    public void Intersects_DiagonalHit_MatchesEquivalentRay()
    {
        // The wrapper must forward to Ray.Intersects unchanged, so the results are compared with those of a
        // plain Ray built from the same origin and direction.
        // Box: x [3, 5] by y [2, 10]; ray from the origin along (0.6, 0.8): X slab t in [5, 8.33],
        // Y slab t in [2.5, 12.5], so the entry parameter is 5 and the hit position is (3, 4).
        var box = new Box2(3f, 2f, 5f, 10f);
        var plainRay = new Ray(Vector2.Zero, UnitDiagonal);
        var collisionRay = new CollisionRay(Vector2.Zero, UnitDiagonal, 42);

        var expectedHit = plainRay.Intersects(box, out var expectedDistance, out var expectedHitPos);
        var actualHit = collisionRay.Intersects(box, out var actualDistance, out var actualHitPos);

        Assert.Multiple(() =>
        {
            Assert.That(expectedHit, Is.True, "Sanity check of the scenario: the plain ray must hit.");
            Assert.That(actualHit, Is.EqualTo(expectedHit));
            Assert.That(actualDistance, Is.EqualTo(expectedDistance));
            Assert.That(actualHitPos, Is.EqualTo(expectedHitPos));
            Assert.That(actualDistance, Is.EqualTo(5f).Within(Tolerance));
            AssertVector(actualHitPos, 3f, 4f);
        });
    }

    [Test]
    public void Intersects_DiagonalMiss_MatchesEquivalentRay()
    {
        // Box: x [3, 5] by y [10, 12]. X slab t in [5, 8.33], Y slab t in [12.5, 15]. The ray leaves the X slab
        // before it enters the Y slab, so it passes beside the box and misses.
        var box = new Box2(3f, 10f, 5f, 12f);
        var plainRay = new Ray(Vector2.Zero, UnitDiagonal);
        var collisionRay = new CollisionRay(Vector2.Zero, UnitDiagonal, 42);

        var expectedHit = plainRay.Intersects(box, out var expectedDistance, out var expectedHitPos);
        var actualHit = collisionRay.Intersects(box, out var actualDistance, out var actualHitPos);

        Assert.Multiple(() =>
        {
            Assert.That(expectedHit, Is.False, "Sanity check of the scenario: the plain ray must miss.");
            Assert.That(actualHit, Is.EqualTo(expectedHit));
            Assert.That(actualDistance, Is.EqualTo(expectedDistance));
            Assert.That(actualHitPos, Is.EqualTo(expectedHitPos));
        });
    }

    [Test]
    public void Intersects_OriginInsideBox_HitsAtZeroDistance()
    {
        // Box: x [1, 3] by y [-1, 1]; the origin (2, 0) lies inside, so the hit is at the origin itself.
        var ray = new CollisionRay(new Vector2(2f, 0f), new Vector2(0f, 1f), 1);
        var box = new Box2(1f, -1f, 3f, 1f);

        var hit = ray.Intersects(box, out var distance, out var hitPos);

        Assert.Multiple(() =>
        {
            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(0f));
            Assert.That(hitPos, Is.EqualTo(new Vector2(2f, 0f)));
        });
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(int.MaxValue)]
    public void Intersects_IgnoresCollisionMask(int mask)
    {
        // The mask is only used by physics queries; the geometric test must give the same answer for every mask,
        // including 0 (which would filter out everything in a physics query).
        var box = new Box2(1f, -1f, 3f, 1f);
        var ray = new CollisionRay(new Vector2(-2f, 0f), UnitX, mask);

        var hit = ray.Intersects(box, out var distance, out var hitPos);

        Assert.Multiple(() =>
        {
            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(3f).Within(Tolerance));
            AssertVector(hitPos, 1f, 0f);
        });
    }

    #endregion

    #region Equality

    [Test]
    public void Equals_SamePositionDirectionAndMask_AreEqual()
    {
        var a = new CollisionRay(new Vector2(1f, 2f), UnitDiagonal, 3);
        var b = new CollisionRay(new Vector2(1f, 2f), UnitDiagonal, 3);

        Assert.Multiple(() =>
        {
            Assert.That(a.Equals(b), Is.True);
            Assert.That(a.Equals((object) b), Is.True);
            Assert.That(a == b, Is.True);
            Assert.That(a != b, Is.False);
        });
    }

    [Test]
    public void Equals_DifferentMaskOnly_AreStillEqual()
    {
        // The collision mask is deliberately not part of the equality. This is locked in by test because
        // callers (for example hash sets keyed by ray) may depend on it.
        var a = new CollisionRay(new Vector2(1f, 2f), UnitDiagonal, 1);
        var b = new CollisionRay(new Vector2(1f, 2f), UnitDiagonal, 2);

        Assert.Multiple(() =>
        {
            Assert.That(a.Equals(b), Is.True);
            Assert.That(a.Equals((object) b), Is.True);
            Assert.That(a == b, Is.True);
            Assert.That(a != b, Is.False);
        });
    }

    [Test]
    public void Equals_DifferentPosition_AreNotEqual()
    {
        var a = new CollisionRay(new Vector2(1f, 2f), UnitDiagonal, 1);
        var b = new CollisionRay(new Vector2(1f, 2.5f), UnitDiagonal, 1);

        Assert.Multiple(() =>
        {
            Assert.That(a.Equals(b), Is.False);
            Assert.That(a == b, Is.False);
            Assert.That(a != b, Is.True);
        });
    }

    [Test]
    public void Equals_DifferentDirection_AreNotEqual()
    {
        var a = new CollisionRay(new Vector2(1f, 2f), new Vector2(0.6f, 0.8f), 1);
        var b = new CollisionRay(new Vector2(1f, 2f), new Vector2(0.8f, 0.6f), 1);

        Assert.Multiple(() =>
        {
            Assert.That(a.Equals(b), Is.False);
            Assert.That(a == b, Is.False);
            Assert.That(a != b, Is.True);
        });
    }

    [Test]
    public void Equals_DifferentPositionAndMask_AreNotEqual()
    {
        // The differing mask does not make the rays equal; the differing position still makes them unequal.
        var a = new CollisionRay(new Vector2(1f, 2f), UnitX, 1);
        var b = new CollisionRay(new Vector2(9f, 2f), UnitX, 2);

        Assert.That(a == b, Is.False);
    }

    [Test]
    public void Equals_Object_Null_ReturnsFalse()
    {
        var ray = new CollisionRay(new Vector2(1f, 2f), UnitX, 1);

        Assert.That(ray.Equals(null), Is.False);
    }

    [Test]
    public void Equals_Object_PlainRayWithSameValues_ReturnsFalse()
    {
        // A plain Ray is a different type, so it is never equal even if position and direction match.
        var collisionRay = new CollisionRay(new Vector2(1f, 2f), UnitX, 1);
        var plainRay = new Ray(new Vector2(1f, 2f), UnitX);

        Assert.That(collisionRay.Equals((object) plainRay), Is.False);
    }

    [Test]
    public void Equals_Object_OtherTypes_ReturnsFalse()
    {
        var ray = new CollisionRay(new Vector2(1f, 2f), UnitX, 1);

        Assert.Multiple(() =>
        {
            Assert.That(ray.Equals("not a ray"), Is.False);
            Assert.That(ray.Equals(new Vector2(1f, 2f)), Is.False);
            Assert.That(ray.Equals(1), Is.False);
        });
    }

    [Test]
    public void Equals_DefaultValues_AreEqual()
    {
        Assert.That(default(CollisionRay) == default(CollisionRay), Is.True);
    }

    [Test]
    public void GetHashCode_EqualRays_HaveEqualHashCodes()
    {
        var a = new CollisionRay(new Vector2(1f, 2f), UnitDiagonal, 3);
        var b = new CollisionRay(new Vector2(1f, 2f), UnitDiagonal, 3);

        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void GetHashCode_DifferentMaskOnly_HasEqualHashCodes()
    {
        // The hash must stay consistent with Equals, which ignores the mask.
        var a = new CollisionRay(new Vector2(1f, 2f), UnitDiagonal, 1);
        var b = new CollisionRay(new Vector2(1f, 2f), UnitDiagonal, int.MinValue);

        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void GetHashCode_ExtremeValues_DoesNotThrow()
    {
        // The hash is computed with unchecked arithmetic, so large components must not cause an overflow exception.
        var ray = new CollisionRay(new Vector2(float.MaxValue, float.MinValue), UnitX, 1);

        Assert.DoesNotThrow(() => ray.GetHashCode());
    }

    #endregion
}
