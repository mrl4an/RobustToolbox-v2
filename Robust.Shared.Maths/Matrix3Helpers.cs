using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Robust.Shared.Maths;

public static class Matrix3Helpers
{
    public static bool EqualsApprox(this Matrix3x2 a, Matrix3x2 b, float tolerance = 1e-6f)
    {
        return
            Math.Abs(a.M11 - b.M11) <= tolerance &&
            Math.Abs(a.M12 - b.M12) <= tolerance &&
            Math.Abs(a.M21 - b.M21) <= tolerance &&
            Math.Abs(a.M22 - b.M22) <= tolerance &&
            Math.Abs(a.M31 - b.M31) <= tolerance &&
            Math.Abs(a.M32 - b.M32) <= tolerance;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool EqualsApprox(this Matrix3x2 a, Matrix3x2 b, double tolerance)
    {
        return a.EqualsApprox(b, (float) tolerance);
    }

    [Obsolete("Use TransformBox")]
    // This method was previously broken, and now just returns an bounding box pretending to be a Box2Rotated
    public static Box2Rotated TransformBounds(this Matrix3x2 refFromBox, Box2Rotated box)
    {
        return new Box2Rotated(TransformBox(refFromBox, box));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Box2 TransformBox(this Matrix3x2 refFromBox, in Box2Rotated box)
    {
        return (box.Transform * refFromBox).TransformBox(box.Box);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void TransformBox(
        this Matrix3x2 refFromBox,
        in Box2Rotated box,
        out Vector128<float> x,
        out Vector128<float> y)
    {
        (box.Transform * refFromBox).TransformBox(box.Box, out x, out y);
    }

    public static Box2 TransformBox(this Matrix3x2 refFromBox, in Box2 box)
    {
        TransformBox(refFromBox, box, out var x, out var y);
        var aabb = SimdHelpers.GetAABB(x, y);
        return Unsafe.As<Vector128<float>, Box2>(ref aabb);
    }

    /// <summary>
    /// Applies a transformation matrix to all of a box's corners and returns their coordinates in two simd vectors.
    /// </summary>
    /// <remarks>The corners are ordered clockwise, starting from what was the bottom left corner prior to the transformation.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void TransformBox(
        this Matrix3x2 refFromBox,
        in Box2 box,
        out Vector128<float> x,
        out Vector128<float> y)
    {
        var boxVec = Unsafe.As<Box2, Vector128<float>>(ref Unsafe.AsRef(in box));

        // Convert box into list of X and Y values for each of the 4 corners
        var boxX = Vector128.Shuffle(boxVec, Vector128.Create(0, 2, 2, 0));
        var boxY = Vector128.Shuffle(boxVec, Vector128.Create(1, 1, 3, 3));

        // Transform coordinates
        x = Vector128.Create(refFromBox.M31)
            + boxX * Vector128.Create(refFromBox.M11)
            + boxY * Vector128.Create(refFromBox.M21);
        y = Vector128.Create(refFromBox.M32)
            + boxX * Vector128.Create(refFromBox.M12)
            + boxY * Vector128.Create(refFromBox.M22);
    }

    /// <summary>
    /// Gets the rotation of the Matrix. Will have some precision loss.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Angle Rotation(this Matrix3x2 t)
    {
        return new Angle(Math.Atan2(t.M12, t.M11));
    }

    /// <summary>
    /// Builds a matrix that rotates by <paramref name="angle"/> and then moves by the given position.
    /// </summary>
    /// <param name="posX">X component of the translation.</param>
    /// <param name="posY">Y component of the translation.</param>
    /// <param name="angle">Rotation in radians.</param>
    /// <returns>The combined rotation and translation matrix.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix3x2 CreateTransform(float posX, float posY, double angle)
    {
        var sin = (float) Math.Sin(angle);
        var cos = (float) Math.Cos(angle);

        var result = new Matrix3x2();
        result.M11 = cos;
        result.M12 = sin;
        result.M21 = -sin;
        result.M22 = cos;
        result.M31 = posX;
        result.M32 = posY;
        return result;
    }

    /// <summary>
    /// Builds a matrix that scales, then rotates, then translates.
    /// </summary>
    /// <param name="posX">X component of the translation.</param>
    /// <param name="posY">Y component of the translation.</param>
    /// <param name="angle">Rotation in radians.</param>
    /// <param name="scaleX">Factor applied to the first matrix row.</param>
    /// <param name="scaleY">Factor applied to the second matrix row.</param>
    /// <returns>The combined scale, rotation and translation matrix. The translation is not scaled.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix3x2 CreateTransform(float posX, float posY, double angle, float scaleX = 1, float scaleY = 1)
    {
        var sin = (float) Math.Sin(angle);
        var cos = (float) Math.Cos(angle);

        var result = new Matrix3x2();
        result.M11 = cos * scaleX;
        result.M12 = sin * scaleX;
        result.M21 = -sin * scaleY;
        result.M22 = cos * scaleY;
        result.M31 = posX;
        result.M32 = posY;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix3x2 CreateTransform(in Vector2 position, in Angle angle)
    {
        // Rounding moment
        return angle.Theta switch
        {
            -Math.PI / 2 => new Matrix3x2(0f, -1f, 1, 0, position.X, position.Y),
            Math.PI / 2 => new Matrix3x2(0f, 1f, -1f, 0f, position.X, position.Y),
            Math.PI => new Matrix3x2(-1f, 0f, 0f, -1f, position.X, position.Y),
            _ => CreateTransform(position.X, position.Y, (float) angle.Theta)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix3x2 CreateTransform(in Vector2 position, in Angle angle, in Vector2 scale)
    {
        return CreateTransform(position.X, position.Y, (float)angle.Theta, scale.X, scale.Y);
    }


    /// <summary>
    /// Builds the matrix that reverses the result of the matching <c>CreateTransform</c> call.
    /// </summary>
    /// <param name="posX">X component of the translation that is being undone.</param>
    /// <param name="posY">Y component of the translation that is being undone.</param>
    /// <param name="angle">Rotation in radians that is being undone.</param>
    /// <param name="scaleX">Scale of the first row that is being undone.</param>
    /// <param name="scaleY">Scale of the second row that is being undone.</param>
    /// <returns>The inverse matrix. A zero scale produces infinity or NaN components rather than an exception.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix3x2 CreateInverseTransform(float posX, float posY, double angle, float scaleX = 1, float scaleY = 1)
    {
        var sin = (float) Math.Sin(angle);
        var cos = (float) Math.Cos(angle);

        var result = new Matrix3x2();
        result.M11 = cos / scaleX;
        result.M12 = -sin / scaleY;
        result.M21 = sin / scaleX;
        result.M22 = cos / scaleY;
        result.M31 = -(posX * cos + posY * sin) / scaleX;
        result.M32 = (posX * sin - posY * cos) / scaleY;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix3x2 CreateInverseTransform(in Vector2 position, in Angle angle)
    {
        return CreateInverseTransform(position.X, position.Y, (float)angle.Theta);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix3x2 CreateInverseTransform(in Vector2 position, in Angle angle, in Vector2 scale)
    {
        return CreateInverseTransform(position.X, position.Y, (float)angle.Theta, scale.X, scale.Y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix3x2 CreateTranslation(float x, float y)
    {
        return new Matrix3x2 {
            M11 = 1,
            M12 = 0,
            M21 = 0,
            M22 = 1,
            M31 = x,
            M32 = y
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix3x2 CreateTranslation(Vector2 vector)
    {
        return CreateTranslation(vector.X, vector.Y);
    }

    /// <summary>
    /// Builds a pure rotation matrix.
    /// </summary>
    /// <param name="angle">Rotation in radians.</param>
    /// <returns>A matrix with no translation.</returns>
    public static Matrix3x2 CreateRotation(double angle)
    {
        var cos = (float) Math.Cos(angle);
        var sin = (float) Math.Sin(angle);

        var result = new Matrix3x2();
        result.M11 = cos;
        result.M12 = sin;
        result.M21 = -sin;
        result.M22 = cos;
        result.M31 = 0;
        result.M32 = 0;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix3x2 CreateScale(float x, float y)
    {
        return new Matrix3x2 {
            M11 = x,
            M12 = 0,
            M21 = 0,
            M22 = y,
            M31 = 0,
            M32 = 0
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix3x2 CreateScale(in Vector2 scale)
    {
        return CreateScale(scale.X, scale.Y);
    }
}
