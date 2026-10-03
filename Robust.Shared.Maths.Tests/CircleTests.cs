using System.Globalization;
using System.Numerics;
using NUnit.Framework;

namespace Robust.Shared.Maths.Tests
{
    /// <summary>
    ///     Tests for <see cref="Circle"/>: containment, circle/circle and circle/box overlap, equality and formatting.
    /// </summary>
    /// <remarks>
    ///     Box2 is built as <c>new Box2(left, bottom, right, top)</c>; boxes in the comments are written as
    ///     "x [left, right] by y [bottom, top]".
    ///
    ///     Two different tolerance regimes are covered on purpose (see the individual tests):
    ///     <see cref="Circle.Contains"/> and <see cref="Circle.Intersects(Box2)"/> are inclusive and fuzzy
    ///     (<c>MathHelper.CloseToPercent</c> on squared distances), while <see cref="Circle.Intersects(Circle)"/>
    ///     is a strict comparison without tolerance.
    /// </remarks>
    [TestFixture, Parallelizable, TestOf(typeof(Circle))]
    internal sealed class CircleTests
    {
        #region Construction

        [Test]
        public void Constructor_StoresPositionAndRadius()
        {
            var circle = new Circle(new Vector2(1f, 2f), 3f);

            Assert.That(circle.Position, Is.EqualTo(new Vector2(1f, 2f)));
            Assert.That(circle.Radius, Is.EqualTo(3f));
        }

        [Test]
        public void DefaultCircle_IsCenteredOnOriginWithZeroRadius()
        {
            var circle = default(Circle);

            Assert.That(circle.Position, Is.EqualTo(Vector2.Zero));
            Assert.That(circle.Radius, Is.EqualTo(0f));
        }

        [Test]
        public void Constructor_AcceptsNegativeRadiusWithoutChangingIt()
        {
            var circle = new Circle(Vector2.Zero, -2f);

            Assert.That(circle.Radius, Is.EqualTo(-2f));
        }

        #endregion

        #region Contains

        // All Contains tests use a circle centred on (1, 2) with radius 3, so r^2 = 9.

        private static readonly Circle ContainsCircle = new(new Vector2(1f, 2f), 3f);

        [Test]
        public void Contains_CentrePoint_ReturnsTrue()
        {
            Assert.That(ContainsCircle.Contains(new Vector2(1f, 2f)), Is.True);
        }

        [Test]
        public void Contains_PointInside_ReturnsTrue()
        {
            // Point (2, 3): d^2 = 1^2 + 1^2 = 2, which is less than r^2 = 9.
            Assert.That(ContainsCircle.Contains(new Vector2(2f, 3f)), Is.True);
        }

        [Test]
        public void Contains_PointClearlyOutside_ReturnsFalse()
        {
            // Point (5, 2): d^2 = 4^2 + 0 = 16. 16 is not below r^2 = 9, and the tolerance
            // (about 9 * 1e-5) is far too small to bridge the gap.
            Assert.That(ContainsCircle.Contains(new Vector2(5f, 2f)), Is.False);
        }

        [Test]
        public void Contains_PointExactlyOnBoundary_ReturnsTrue()
        {
            // Point (4, 2): d^2 = 3^2 = 9 = r^2. Not strictly inside, but the boundary is inclusive.
            Assert.That(ContainsCircle.Contains(new Vector2(4f, 2f)), Is.True);
        }

        [Test]
        public void Contains_PointJustOutsideBoundaryWithinTolerance_ReturnsTrue()
        {
            // Point (4.00001, 2): d ~ 3.00001, so d^2 ~ 9.00006.
            // The tolerance on squared distances is max(9 * 1e-5, 1e-5) = 9e-5, and
            // 9.00006 - 9 = 6e-5 is smaller than that, so the point still counts as contained.
            Assert.That(ContainsCircle.Contains(new Vector2(4.00001f, 2f)), Is.True);
        }

        [Test]
        public void Contains_PointOutsideBoundaryBeyondTolerance_ReturnsFalse()
        {
            // Point (4.001, 2): d^2 ~ 9.006, which exceeds r^2 = 9 by 6e-3, far above the 9e-5 tolerance.
            Assert.That(ContainsCircle.Contains(new Vector2(4.001f, 2f)), Is.False);
        }

        [Test]
        public void Contains_ZeroRadius_UsesAbsoluteToleranceOfSquaredDistance()
        {
            // With r = 0 the tolerance is the absolute floor of 1e-5 on the SQUARED distance.
            // A point 0.001 away has d^2 = 1e-6 <= 1e-5 and is therefore contained;
            // a point 0.01 away has d^2 = 1e-4 > 1e-5 and is not.
            var circle = new Circle(Vector2.Zero, 0f);

            Assert.That(circle.Contains(Vector2.Zero), Is.True);
            Assert.That(circle.Contains(new Vector2(0.001f, 0f)), Is.True);
            Assert.That(circle.Contains(new Vector2(0.01f, 0f)), Is.False);
        }

        [Test]
        public void Contains_NegativeRadius_BehavesLikeItsAbsoluteValue()
        {
            // Only r^2 is used, so a radius of -2 behaves like a radius of 2.
            var circle = new Circle(Vector2.Zero, -2f);

            Assert.That(circle.Contains(new Vector2(1f, 0f)), Is.True);
            Assert.That(circle.Contains(new Vector2(3f, 0f)), Is.False);
        }

        #endregion

        #region Intersects(Circle)

        [Test]
        public void IntersectsCircle_OverlappingCircles_ReturnsTrue()
        {
            // Centres 1.5 apart, radii 1 + 1 = 2. d^2 = 2.25 < s^2 = 4.
            var a = new Circle(new Vector2(0f, 0f), 1f);
            var b = new Circle(new Vector2(1.5f, 0f), 1f);

            Assert.That(a.Intersects(b), Is.True);
            Assert.That(b.Intersects(a), Is.True);
        }

        [Test]
        public void IntersectsCircle_SeparateCircles_ReturnsFalse()
        {
            // Centres 3 apart, radii sum to 2. d^2 = 9 is not below s^2 = 4.
            var a = new Circle(new Vector2(0f, 0f), 1f);
            var b = new Circle(new Vector2(3f, 0f), 1f);

            Assert.That(a.Intersects(b), Is.False);
            Assert.That(b.Intersects(a), Is.False);
        }

        [Test]
        public void IntersectsCircle_ExactlyTouchingCircles_ReturnsFalse()
        {
            // Centres (0, 0) and (3, 4) are 5 apart (3-4-5 triangle) and the radii are 2 and 3, so the sum is 5.
            // d^2 = 25 = s^2. The test is a strict "less than" without tolerance, so touching is NOT an overlap.
            // (Contrast with Intersects(Box2), where touching counts.)
            var a = new Circle(new Vector2(0f, 0f), 2f);
            var b = new Circle(new Vector2(3f, 4f), 3f);

            Assert.That(a.Intersects(b), Is.False);
            Assert.That(b.Intersects(a), Is.False);
        }

        [Test]
        public void IntersectsCircle_CoincidentCircles_ReturnsTrue()
        {
            // Same centre and radius 1: d^2 = 0 < s^2 = 4.
            var a = new Circle(new Vector2(2f, 2f), 1f);
            var b = new Circle(new Vector2(2f, 2f), 1f);

            Assert.That(a.Intersects(b), Is.True);
        }

        [Test]
        public void IntersectsCircle_CoincidentZeroRadiusCircles_ReturnsFalse()
        {
            // d^2 = 0 and s^2 = 0. 0 < 0 is false, so two coincident points do not "intersect".
            var a = new Circle(new Vector2(2f, 2f), 0f);
            var b = new Circle(new Vector2(2f, 2f), 0f);

            Assert.That(a.Intersects(b), Is.False);
        }

        [Test]
        public void IntersectsCircle_OppositeSignRadii_PinsCurrentBehaviour()
        {
            // Pins current behaviour, not necessarily a desirable one: the SUM of the radii is squared, so
            // radii +1 and -1 give s = 0 and coincident circles do not intersect (0 < 0 is false),
            // whereas radii -1 and -1 give s = -2 and s^2 = 4, which behaves like a sum of 2.
            var positive = new Circle(Vector2.Zero, 1f);
            var negative = new Circle(Vector2.Zero, -1f);

            Assert.That(positive.Intersects(negative), Is.False);
            Assert.That(negative.Intersects(negative), Is.True);
        }

        #endregion

        #region Intersects(Box2)

        // Most tests use the box x [0, 4] by y [0, 2].
        private static readonly Box2 TestBox = new(0f, 0f, 4f, 2f);

        [Test]
        public void IntersectsBox_CentreInsideBox_ReturnsTrue()
        {
            // The closest point of the box to the centre is the centre itself, so d^2 = 0.
            var circle = new Circle(new Vector2(2f, 1f), 0.1f);

            Assert.That(circle.Intersects(TestBox), Is.True);
        }

        [Test]
        public void IntersectsBox_CircleOverlappingEdge_ReturnsTrue()
        {
            // Centre (-0.5, 1), radius 1. The closest box point is (0, 1) on the left edge:
            // d^2 = 0.5^2 = 0.25 < r^2 = 1.
            var circle = new Circle(new Vector2(-0.5f, 1f), 1f);

            Assert.That(circle.Intersects(TestBox), Is.True);
        }

        [Test]
        public void IntersectsBox_CircleSeparatedFromEdge_ReturnsFalse()
        {
            // Centre (-2, 1), radius 1. The closest box point is (0, 1): d^2 = 4 > r^2 = 1.
            var circle = new Circle(new Vector2(-2f, 1f), 1f);

            Assert.That(circle.Intersects(TestBox), Is.False);
        }

        [Test]
        public void IntersectsBox_CircleOverlappingCorner_ReturnsTrue()
        {
            // Centre (5, 3), radius 1.5. The closest box point is the corner (4, 2):
            // d^2 = 1^2 + 1^2 = 2 < r^2 = 2.25.
            var circle = new Circle(new Vector2(5f, 3f), 1.5f);

            Assert.That(circle.Intersects(TestBox), Is.True);
        }

        [Test]
        public void IntersectsBox_CircleCloseToCornerButTooSmall_ReturnsFalse()
        {
            // Centre (5, 3), radius 1.2. The circle reaches 1.2 in x and in y, which is more than the 1 unit
            // gap along each axis separately, but the actual distance to the corner (4, 2) is sqrt(2) ~ 1.414.
            // d^2 = 2 > r^2 = 1.44, so there is no overlap. This catches implementations that test the axes
            // independently instead of using the closest point.
            var circle = new Circle(new Vector2(5f, 3f), 1.2f);

            Assert.That(circle.Intersects(TestBox), Is.False);
        }

        [Test]
        public void IntersectsBox_CircleTouchingEdge_ReturnsTrue()
        {
            // Centre (5, 1), radius 1. The closest box point is (4, 1): d^2 = 1 = r^2.
            // Unlike circle/circle, touching a box counts as an intersection.
            var circle = new Circle(new Vector2(5f, 1f), 1f);

            Assert.That(circle.Intersects(TestBox), Is.True);
        }

        [Test]
        public void IntersectsBox_BoxFullyInsideCircle_ReturnsTrue()
        {
            // The centre (2, 1) is inside the box, and the large radius covers the whole box.
            var circle = new Circle(new Vector2(2f, 1f), 10f);

            Assert.That(circle.Intersects(TestBox), Is.True);
        }

        [Test]
        public void IntersectsBox_BoxWithInvertedEdges_IsTreatedLikeSortedBox()
        {
            // The same box as TestBox but with Left > Right and Bottom > Top. Closest-point clamping
            // sorts the edges, so results must match the sorted box.
            var invertedBox = new Box2(4f, 2f, 0f, 0f);

            var overlapping = new Circle(new Vector2(-0.5f, 1f), 1f);
            var separate = new Circle(new Vector2(-2f, 1f), 1f);

            Assert.That(overlapping.Intersects(invertedBox), Is.True);
            Assert.That(separate.Intersects(invertedBox), Is.False);
        }

        [Test]
        public void IntersectsBox_ZeroSizeBox_ActsAsPoint()
        {
            // The box is the single point (1, 1). A circle of radius 1 centred at (2, 1) touches it
            // (d^2 = 1 = r^2, inclusive); centred at (3, 1) it does not (d^2 = 4 > 1).
            var point = new Box2(1f, 1f, 1f, 1f);

            Assert.That(new Circle(new Vector2(2f, 1f), 1f).Intersects(point), Is.True);
            Assert.That(new Circle(new Vector2(3f, 1f), 1f).Intersects(point), Is.False);
        }

        #endregion

        #region Equality

        [Test]
        public void Equals_CirclesWithSameFields_AreEqual()
        {
            var a = new Circle(new Vector2(1f, 2f), 3f);
            var b = new Circle(new Vector2(1f, 2f), 3f);

            Assert.That(a.Equals(b), Is.True);
            Assert.That(a.Equals((object) b), Is.True);
            Assert.That(a == b, Is.True);
            Assert.That(a != b, Is.False);
        }

        [Test]
        public void Equals_CirclesWithDifferentPosition_AreNotEqual()
        {
            var a = new Circle(new Vector2(1f, 2f), 3f);
            var b = new Circle(new Vector2(1f, 2.5f), 3f);

            Assert.That(a.Equals(b), Is.False);
            Assert.That(a == b, Is.False);
            Assert.That(a != b, Is.True);
        }

        [Test]
        public void Equals_CirclesWithDifferentRadius_AreNotEqual()
        {
            var a = new Circle(new Vector2(1f, 2f), 3f);
            var b = new Circle(new Vector2(1f, 2f), 3.5f);

            Assert.That(a.Equals(b), Is.False);
            Assert.That(a == b, Is.False);
            Assert.That(a != b, Is.True);
        }

        [Test]
        public void Equals_Object_RejectsNullAndOtherTypes()
        {
            var circle = new Circle(new Vector2(1f, 2f), 3f);

            Assert.That(circle.Equals(null), Is.False);
            Assert.That(circle.Equals("not a circle"), Is.False);
            Assert.That(circle.Equals(new Vector2(1f, 2f)), Is.False);
        }

        [Test]
        public void GetHashCode_EqualCircles_HaveEqualHashCodes()
        {
            var a = new Circle(new Vector2(1f, 2f), 3f);
            var b = new Circle(new Vector2(1f, 2f), 3f);

            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        #endregion

        #region Formatting

        // Numbers are formatted with the current culture, so the tests that print fractions pin the culture.

        [Test]
        public void ToString_WholeNumbers_UsesDocumentedFormat()
        {
            var circle = new Circle(new Vector2(1f, 2f), 3f);

            Assert.That(circle.ToString(), Is.EqualTo("Circle (1, 2), 3 r"));
        }

        [Test, SetCulture("en-US")]
        public void ToString_FractionalNumbers_UsesCurrentCultureForEachValue()
        {
            var circle = new Circle(new Vector2(1.5f, -2.25f), 0.5f);

            Assert.That(circle.ToString(), Is.EqualTo("Circle (1.5, -2.25), 0.5 r"));
        }

        [Test, SetCulture("en-US")]
        public void ToStringWithFormatAndProvider_IgnoresBothArguments()
        {
            // A German provider would print decimal commas, and "F3" would add digits; neither may have an effect.
            var circle = new Circle(new Vector2(1.5f, 2.5f), 3.5f);
            var expected = circle.ToString();

            Assert.That(circle.ToString("F3", CultureInfo.GetCultureInfo("de-DE")), Is.EqualTo(expected));
            Assert.That(circle.ToString(null, null), Is.EqualTo(expected));
            Assert.That(expected, Is.EqualTo("Circle (1.5, 2.5), 3.5 r"));
        }

        [Test]
        public void TryFormat_LargeEnoughBuffer_WritesSameTextAsToString()
        {
            var circle = new Circle(new Vector2(1f, 2f), 3f);
            var expected = circle.ToString();
            var buffer = new char[64];

            var success = circle.TryFormat(buffer, out var charsWritten, ReadOnlySpan<char>.Empty, null);

            Assert.That(success, Is.True);
            Assert.That(charsWritten, Is.EqualTo(expected.Length));
            Assert.That(new string(buffer, 0, charsWritten), Is.EqualTo(expected));
        }

        [Test]
        public void TryFormat_BufferExactlyAsLongAsText_Succeeds()
        {
            var circle = new Circle(new Vector2(1f, 2f), 3f);
            var expected = circle.ToString();
            var buffer = new char[expected.Length];

            var success = circle.TryFormat(buffer, out var charsWritten, ReadOnlySpan<char>.Empty, null);

            Assert.That(success, Is.True);
            Assert.That(charsWritten, Is.EqualTo(expected.Length));
            Assert.That(new string(buffer), Is.EqualTo(expected));
        }

        [Test]
        public void TryFormat_BufferTooSmallForLiteralText_ReturnsFalse()
        {
            // The buffer cannot even hold the leading literal "Circle (" (8 characters), so the output is
            // truncated: the call reports failure, and whatever was written is a prefix of the full text
            // that never exceeds the buffer.
            var circle = new Circle(new Vector2(1f, 2f), 3f);
            var expected = circle.ToString();
            var buffer = new char[4];

            var success = circle.TryFormat(buffer, out var charsWritten, ReadOnlySpan<char>.Empty, null);

            Assert.That(success, Is.False);
            Assert.That(charsWritten, Is.InRange(0, buffer.Length));
            Assert.That(expected, Does.StartWith(new string(buffer, 0, charsWritten)));
        }

        [Test]
        public void TryFormat_EmptyBuffer_ReturnsFalseAndWritesNothing()
        {
            var circle = new Circle(new Vector2(1f, 2f), 3f);

            var success = circle.TryFormat(Span<char>.Empty, out var charsWritten, ReadOnlySpan<char>.Empty, null);

            Assert.That(success, Is.False);
            Assert.That(charsWritten, Is.EqualTo(0));
        }

        [Test]
        public void TryFormat_BufferOneCharShort_ReturnsFalse()
        {
            // The text ends with the literal " r" (2 characters). With one character missing, that final
            // literal does not fit, which is reported as a failure.
            var circle = new Circle(new Vector2(1f, 2f), 3f);
            var expected = circle.ToString();
            var buffer = new char[expected.Length - 1];

            var success = circle.TryFormat(buffer, out var charsWritten, ReadOnlySpan<char>.Empty, null);

            Assert.That(success, Is.False);
            Assert.That(charsWritten, Is.LessThanOrEqualTo(buffer.Length));
            Assert.That(expected, Does.StartWith(new string(buffer, 0, charsWritten)));
        }

        #endregion
    }
}
