using System.Numerics;
using NUnit.Framework;
using Robust.Shared.Maths;
using Robust.Shared.Physics;

namespace Robust.Shared.Tests.Physics;

/// <summary>
///     Tests for <see cref="Ray"/>: construction, the slab-method box intersection and value equality.
/// </summary>
/// <remarks>
///     Box2 is built as <c>new Box2(left, bottom, right, top)</c>. All boxes below are written as
///     "x [left, right] by y [bottom, top]" in the comments.
///
///     The <see cref="Ray"/> constructor asserts (in debug builds only) that the direction has unit length.
///     Where a test needs a direction that is not of unit length (to get exact numbers, or to check that the
///     distance is expressed in multiples of the direction length) the fields are assigned directly, which
///     bypasses that assertion.
/// </remarks>
[TestFixture, Parallelizable, TestOf(typeof(Ray))]
internal sealed class RayTests
{
    private const float Tolerance = 1e-4f;

    /// <summary>
    ///     A direction of exactly unit length that is representable without rounding trouble:
    ///     0.6^2 + 0.8^2 = 1 (a 3-4-5 triangle scaled down).
    /// </summary>
    private static readonly Vector2 UnitDiagonal = new(0.6f, 0.8f);

    /// <summary>
    ///     Builds a ray by assigning the public fields, skipping the constructor's unit-length assertion.
    /// </summary>
    private static Ray MakeRayUnchecked(Vector2 position, Vector2 direction)
    {
        var ray = default(Ray);
        ray.Position = position;
        ray.Direction = direction;
        return ray;
    }

    private static void AssertVector(Vector2 actual, float expectedX, float expectedY)
    {
        Assert.That(actual.X, Is.EqualTo(expectedX).Within(Tolerance));
        Assert.That(actual.Y, Is.EqualTo(expectedY).Within(Tolerance));
    }

    #region Construction

    [Test]
    public void Constructor_StoresPositionAndDirectionUnchanged()
    {
        var position = new Vector2(3.5f, -2f);
        var ray = new Ray(position, UnitDiagonal);

        Assert.That(ray.Position, Is.EqualTo(position));
        Assert.That(ray.Direction, Is.EqualTo(UnitDiagonal));
    }

    [Test]
    public void DefaultRay_HasZeroPositionAndDirection()
    {
        var ray = default(Ray);

        Assert.That(ray.Position, Is.EqualTo(Vector2.Zero));
        Assert.That(ray.Direction, Is.EqualTo(Vector2.Zero));
    }

    #endregion

    #region Intersects: hits

    [Test]
    public void Intersects_AxisAlignedRayFromOutside_ReturnsNearFace()
    {
        // Box: x [1, 3] by y [-1, 1]. The ray starts at (-2, 0) and travels along +X.
        // X slab: t1 = (1 - -2) / 1 = 3 (near face), t2 = (3 - -2) / 1 = 5 (far face).
        // The Y slab is parallel to the ray and the origin (y = 0) lies strictly inside it, so it does not
        // restrict the interval. Entry parameter is 3, hit position is (-2 + 3, 0) = (1, 0).
        var ray = new Ray(new Vector2(-2f, 0f), new Vector2(1f, 0f));
        var box = new Box2(1f, -1f, 3f, 1f);

        var hit = ray.Intersects(box, out var distance, out var hitPos);

        Assert.That(hit, Is.True);
        Assert.That(distance, Is.EqualTo(3f).Within(Tolerance));
        AssertVector(hitPos, 1f, 0f);
    }

    [Test]
    public void Intersects_AxisAlignedRayTravellingTowardsNegativeAxis_ReturnsNearFace()
    {
        // Box: x [1, 3] by y [-1, 1]. The ray starts at (5, 0) and travels along -X.
        // X slab: with inv = -1, t1 = (1 - 5) * -1 = 4 and t2 = (3 - 5) * -1 = 2. They are swapped so that the
        // near plane (the face at x = 3) gives 2. Hit position is (5 - 2, 0) = (3, 0).
        var ray = new Ray(new Vector2(5f, 0f), new Vector2(-1f, 0f));
        var box = new Box2(1f, -1f, 3f, 1f);

        var hit = ray.Intersects(box, out var distance, out var hitPos);

        Assert.That(hit, Is.True);
        Assert.That(distance, Is.EqualTo(2f).Within(Tolerance));
        AssertVector(hitPos, 3f, 0f);
    }

    [Test]
    public void Intersects_DiagonalRayFromOutside_ReturnsEntryPointOnNearFace()
    {
        // Box: x [3, 5] by y [2, 10]. Ray from the origin along (0.6, 0.8).
        // X slab: t in [3 / 0.6, 5 / 0.6] = [5, 8.33].
        // Y slab: t in [2 / 0.8, 10 / 0.8] = [2.5, 12.5].
        // The intervals overlap on [5, 8.33], so the entry parameter is 5 and the hit position is
        // (0.6 * 5, 0.8 * 5) = (3, 4), on the left face (x = 3) and inside the Y range [2, 10].
        var ray = new Ray(Vector2.Zero, UnitDiagonal);
        var box = new Box2(3f, 2f, 5f, 10f);

        var hit = ray.Intersects(box, out var distance, out var hitPos);

        Assert.That(hit, Is.True);
        Assert.That(distance, Is.EqualTo(5f).Within(Tolerance));
        AssertVector(hitPos, 3f, 4f);
    }

    [Test]
    public void Intersects_BoxWithInvertedEdges_IsTreatedLikeSortedBox()
    {
        // Same geometry as Intersects_AxisAlignedRayFromOutside_ReturnsNearFace, but the box is given with
        // Left > Right and Bottom > Top. The edges of each axis are sorted, so the result must be identical.
        var ray = new Ray(new Vector2(-2f, 0f), new Vector2(1f, 0f));
        var invertedBox = new Box2(3f, 1f, 1f, -1f);

        var hit = ray.Intersects(invertedBox, out var distance, out var hitPos);

        Assert.That(hit, Is.True);
        Assert.That(distance, Is.EqualTo(3f).Within(Tolerance));
        AssertVector(hitPos, 1f, 0f);
    }

    [Test]
    public void Intersects_DirectionThatIsNotUnitLength_ReportsDistanceInDirectionLengths()
    {
        // The distance is the ray parameter t, i.e. measured in multiples of |Direction|.
        // Box: x [1, 3] by y [-1, 1]. Ray from (-2, 0) with direction (2, 0) (length 2).
        // X slab: t1 = (1 - -2) / 2 = 1.5. The Euclidean distance would be 3, but the reported value is 1.5.
        // Hit position is (-2, 0) + (2, 0) * 1.5 = (1, 0).
        var ray = MakeRayUnchecked(new Vector2(-2f, 0f), new Vector2(2f, 0f));
        var box = new Box2(1f, -1f, 3f, 1f);

        var hit = ray.Intersects(box, out var distance, out var hitPos);

        Assert.That(hit, Is.True);
        Assert.That(distance, Is.EqualTo(1.5f).Within(Tolerance));
        AssertVector(hitPos, 1f, 0f);
    }

    #endregion

    #region Intersects: origin inside the box

    [Test]
    public void Intersects_OriginInsideBox_HitsWithZeroDistanceAtOrigin()
    {
        // Box: x [1, 3] by y [-1, 1]. The ray starts at (2, 0), strictly inside, and travels along +Y.
        // The entry parameter is clamped at 0 (the interval starts at tmin = 0), even though the "near" plane
        // lies behind the origin (t = -1). The hit position is therefore the origin itself.
        var ray = new Ray(new Vector2(2f, 0f), new Vector2(0f, 1f));
        var box = new Box2(1f, -1f, 3f, 1f);

        var hit = ray.Intersects(box, out var distance, out var hitPos);

        Assert.That(hit, Is.True);
        Assert.That(distance, Is.EqualTo(0f));
        Assert.That(hitPos, Is.EqualTo(new Vector2(2f, 0f)));
    }

    [Test]
    public void Intersects_DiagonalRayStartingInsideBox_HitsWithZeroDistanceAtOrigin()
    {
        // Box: x [1, 3] by y [-1, 1]; origin (2, 0) is inside. Both near planes are behind the origin
        // (negative t), both far planes are ahead, so the clamped entry parameter stays 0.
        var ray = new Ray(new Vector2(2f, 0f), UnitDiagonal);
        var box = new Box2(1f, -1f, 3f, 1f);

        var hit = ray.Intersects(box, out var distance, out var hitPos);

        Assert.That(hit, Is.True);
        Assert.That(distance, Is.EqualTo(0f));
        Assert.That(hitPos, Is.EqualTo(new Vector2(2f, 0f)));
    }

    #endregion

    #region Intersects: misses

    [Test]
    public void Intersects_RayParallelToSlabButOutsideIt_Misses()
    {
        // Box: x [1, 3] by y [-1, 1]. The ray travels along +X at y = 5, which is above the Y slab.
        // The direction's Y component is 0 (parallel) and the origin is outside [-1, 1], so it is rejected.
        var ray = new Ray(new Vector2(-2f, 5f), new Vector2(1f, 0f));
        var box = new Box2(1f, -1f, 3f, 1f);

        var hit = ray.Intersects(box, out _, out _);

        Assert.That(hit, Is.False);
    }

    [Test]
    public void Intersects_VerticalRayParallelToSlabButOutsideIt_Misses()
    {
        // Box: x [1, 3] by y [-1, 1]. The ray travels along +Y at x = 5, to the right of the X slab [1, 3].
        var ray = new Ray(new Vector2(5f, -5f), new Vector2(0f, 1f));
        var box = new Box2(1f, -1f, 3f, 1f);

        var hit = ray.Intersects(box, out _, out _);

        Assert.That(hit, Is.False);
    }

    [Test]
    public void Intersects_DiagonalRayPassingBesideBox_MissesAndReportsZeroOutputs()
    {
        // Box: x [3, 5] by y [10, 12]. Ray from the origin along (0.6, 0.8).
        // X slab: t in [5, 8.33]. Y slab: t in [10 / 0.8, 12 / 0.8] = [12.5, 15].
        // The ray has left the X slab (t = 8.33) before it enters the Y slab (t = 12.5), so there is no hit.
        // On failure the outputs must be reset, not left at the partially computed entry value of 12.5.
        var ray = new Ray(Vector2.Zero, UnitDiagonal);
        var box = new Box2(3f, 10f, 5f, 12f);

        var hit = ray.Intersects(box, out var distance, out var hitPos);

        Assert.That(hit, Is.False);
        Assert.That(distance, Is.EqualTo(0f));
        Assert.That(hitPos, Is.EqualTo(Vector2.Zero));
    }

    [Test]
    public void Intersects_BoxBehindRay_Misses()
    {
        // Box: x [1, 3] by y [-1, 1]. The ray starts at (5, 0) and travels along +X, away from the box.
        // X slab: t1 = (1 - 5) = -4, t2 = (3 - 5) = -2, both negative. tmin stays clamped at 0 while tmax = -2,
        // so tmin > tmax and the ray misses.
        var ray = new Ray(new Vector2(5f, 0f), new Vector2(1f, 0f));
        var box = new Box2(1f, -1f, 3f, 1f);

        var hit = ray.Intersects(box, out var distance, out var hitPos);

        Assert.That(hit, Is.False);
        Assert.That(distance, Is.EqualTo(0f));
        Assert.That(hitPos, Is.EqualTo(Vector2.Zero));
    }

    [Test]
    public void Intersects_DiagonalRayPointingAwayFromBox_Misses()
    {
        // Box: x [3, 5] by y [2, 10]. Ray from the origin along (-0.6, -0.8), i.e. in the opposite direction
        // of the box. X slab: t in [-8.33, -5] (after swapping). tmax = -5 < tmin = 0, so no hit.
        var ray = new Ray(Vector2.Zero, -UnitDiagonal);
        var box = new Box2(3f, 2f, 5f, 10f);

        var hit = ray.Intersects(box, out _, out _);

        Assert.That(hit, Is.False);
    }

    #endregion

    #region Intersects: grazing

    [Test]
    public void Intersects_RayTouchingBoxCornerExactly_CountsAsHit()
    {
        // Box: x [1, 2] by y [-1, 1]. Ray from the origin along (1, 1) (not normalised, so that the numbers
        // are exact in floating point).
        // X slab: t in [1, 2]. Y slab: t in [-1, 1]. The intervals meet in the single value t = 1, i.e.
        // tmin == tmax. Rejection requires tmin > tmax, so this is a hit at the corner (1, 1).
        var ray = MakeRayUnchecked(Vector2.Zero, new Vector2(1f, 1f));
        var box = new Box2(1f, -1f, 2f, 1f);

        var hit = ray.Intersects(box, out var distance, out var hitPos);

        Assert.That(hit, Is.True);
        Assert.That(distance, Is.EqualTo(1f));
        Assert.That(hitPos, Is.EqualTo(new Vector2(1f, 1f)));
    }

    [Test]
    public void Intersects_RayPassingJustOutsideBoxCorner_Misses()
    {
        // Same ray as the corner-touching test, but the top of the box is lowered to y = 0.5.
        // X slab: t in [1, 2]. Y slab: t in [-1, 0.5]. The ray leaves the Y slab at t = 0.5, before it
        // reaches the X slab at t = 1, so it passes below-right of the corner and misses.
        var ray = MakeRayUnchecked(Vector2.Zero, new Vector2(1f, 1f));
        var box = new Box2(1f, -1f, 2f, 0.5f);

        var hit = ray.Intersects(box, out _, out _);

        Assert.That(hit, Is.False);
    }

    #endregion

    #region Equality

    [Test]
    public void Equals_RaysWithSameFields_AreEqual()
    {
        var a = new Ray(new Vector2(1f, 2f), UnitDiagonal);
        var b = new Ray(new Vector2(1f, 2f), UnitDiagonal);

        Assert.That(a.Equals(b), Is.True);
        Assert.That(a.Equals((object) b), Is.True);
        Assert.That(a == b, Is.True);
        Assert.That(a != b, Is.False);
    }

    [Test]
    public void Equals_RaysWithDifferentPosition_AreNotEqual()
    {
        var a = new Ray(new Vector2(1f, 2f), UnitDiagonal);
        var b = new Ray(new Vector2(1f, 2.5f), UnitDiagonal);

        Assert.That(a.Equals(b), Is.False);
        Assert.That(a == b, Is.False);
        Assert.That(a != b, Is.True);
    }

    [Test]
    public void Equals_RaysWithDifferentDirection_AreNotEqual()
    {
        var a = new Ray(new Vector2(1f, 2f), new Vector2(0.6f, 0.8f));
        var b = new Ray(new Vector2(1f, 2f), new Vector2(0.8f, 0.6f));

        Assert.That(a.Equals(b), Is.False);
        Assert.That(a == b, Is.False);
        Assert.That(a != b, Is.True);
    }

    [Test]
    public void Equals_UsesExactComparison()
    {
        // The comparison is exact, with no tolerance: a one-ulp difference makes the rays unequal.
        var a = new Ray(new Vector2(1f, 2f), new Vector2(1f, 0f));
        var b = new Ray(new Vector2(MathF.BitIncrement(1f), 2f), new Vector2(1f, 0f));

        Assert.That(a == b, Is.False);
    }

    [Test]
    public void Equals_Object_RejectsNullAndOtherTypes()
    {
        var ray = new Ray(new Vector2(1f, 2f), UnitDiagonal);

        Assert.That(ray.Equals(null), Is.False);
        Assert.That(ray.Equals("not a ray"), Is.False);
        Assert.That(ray.Equals(new Vector2(1f, 2f)), Is.False);
    }

    [Test]
    public void GetHashCode_EqualRays_HaveEqualHashCodes()
    {
        var a = new Ray(new Vector2(1f, 2f), UnitDiagonal);
        var b = new Ray(new Vector2(1f, 2f), UnitDiagonal);

        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    #endregion
}
