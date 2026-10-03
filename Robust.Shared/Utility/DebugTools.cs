using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;

namespace Robust.Shared.Utility
{
    public static class DebugTools
    {
        /// <summary>
        ///     Marks a code path that must never be reached. Always throws.
        /// </summary>
        /// <param name="message">Text of the exception that is thrown.</param>
        /// <exception cref="DebugAssertException">Always thrown.</exception>
        [Conditional("DEBUG")]
        [ContractAnnotation("=> halt")]
        [DoesNotReturn]
        public static void Assert(string message)
        {
            throw new DebugAssertException(message);
        }

        /// <summary>
        ///     Throws a <see cref="DebugAssertException"/> without a message when <paramref name="condition"/> is false.
        /// </summary>
        /// <param name="condition">The condition that is expected to hold.</param>
        /// <exception cref="DebugAssertException">The condition is false.</exception>
        [Conditional("DEBUG")]
        [AssertionMethod]
        public static void Assert([AssertionCondition(AssertionConditionType.IS_TRUE)] [DoesNotReturnIf(false)] bool condition)
        {
            if (!condition)
                throw new DebugAssertException();
        }

        /// <summary>
        ///     An assertion that will <see langword="throw"/> an exception if
        ///     <see cref="objA"/> and <see cref="objB"/> are not equal.
        /// </summary>
        [Conditional("DEBUG")]
        public static void AssertEqual(object? objA, object? objB)
        {
            if (ReferenceEquals(objA, objB))
                return;

            if (objA == null || !objA.Equals(objB))
                throw new DebugAssertException($"Expected: {objB ?? "null"} but was {objA ?? "null"}");
        }

        /// <summary>
        ///     An assertion that will <see langword="throw"/> an exception with <see cref="message"/> embedded if
        ///     <see cref="objA"/> and <see cref="objB"/> are not equal.
        /// </summary>
        [Conditional("DEBUG")]
        public static void AssertEqual(object? objA, object? objB, string message)
        {
            if (ReferenceEquals(objA, objB))
                return;

            if (objA == null || !objA.Equals(objB))
                throw new DebugAssertException($"{message}\nExpected: {objB ?? "null"} but was {objA ?? "null"}");
        }

        /// <summary>
        ///     A non-boxing assertion that will <see langword="throw"/> an exception if
        ///     <see cref="objA"/> and <see cref="objB"/> are not equal.
        /// </summary>
        [Conditional("DEBUG")]
        public static void AssertEqual<T>(T? objA, T? objB)
            where T: IEquatable<T>
        {
            if (objA == null && objB == null)
                return;

            if (objA == null || !objA.Equals(objB))
                throw new DebugAssertException($"Expected: {objB?.ToString() ?? "null"} but was {objA?.ToString() ?? "null"}");
        }

        /// <summary>
        ///     A non-boxing assertion that will <see langword="throw"/> an exception with <see cref="message"/> embedded if
        ///     <see cref="objA"/> and <see cref="objB"/> are not equal.
        /// </summary>
        [Conditional("DEBUG")]
        public static void AssertEqual<T>(T? objA, T? objB, string message)
            where T: IEquatable<T>
        {
            if (objA == null && objB == null)
                return;

            if (objA == null || !objA.Equals(objB))
                throw new DebugAssertException($"{message}\nExpected: {objB?.ToString() ?? "null"} but was {objA?.ToString() ?? "null"}");
        }

        /// <summary>
        ///     An assertion that will <see langword="throw"/> an exception if
        ///     <see cref="objA"/> and <see cref="objB"/> are equal.
        /// </summary>
        [Conditional("DEBUG")]
        public static void AssertNotEqual(object? objA, object? objB)
        {
            if (ReferenceEquals(objA, objB))
                throw new DebugAssertException($"Expected: not {objB ?? "null"}");

            if (objA == null || !objA.Equals(objB))
                return;

            throw new DebugAssertException($"Expected: not {objB}");
        }

        /// <summary>
        ///     An assertion that will <see langword="throw"/> an exception with <see cref="message"/> embedded if
        ///     <see cref="objA"/> and <see cref="objB"/> are not equal.
        /// </summary>
        [Conditional("DEBUG")]
        public static void AssertNotEqual(object? objA, object? objB, string message)
        {
            if (ReferenceEquals(objA, objB))
                throw new DebugAssertException($"{message}\nExpected: not {objB ?? "null"}");

            if (objA == null || !objA.Equals(objB))
                return;

            throw new DebugAssertException($"{message}\nExpected: not {objB}");
        }

        /// <summary>
        ///     A non-boxing assertion that will <see langword="throw"/> an exception if
        ///     <see cref="objA"/> and <see cref="objB"/> are equal.
        /// </summary>
        [Conditional("DEBUG")]
        public static void AssertNotEqual<T>(T? objA, T? objB)
            where T: IEquatable<T>
        {
            if (objA == null && objB == null)
                throw new DebugAssertException("Expected: not null");

            if (objA == null || !objA.Equals(objB))
                return;

            throw new DebugAssertException($"Expected: not {objB?.ToString() ?? "null"}");
        }

        /// <summary>
        ///     An assertion that will <see langword="throw"/> an exception with <see cref="message"/> embedded if
        ///     <see cref="objA"/> and <see cref="objB"/> are not equal.
        /// </summary>
        [Conditional("DEBUG")]
        public static void AssertNotEqual<T>(T? objA, T? objB, string message)
            where T: IEquatable<T>
        {
            if (objA == null && objB == null)
                throw new DebugAssertException($"{message}\nExpected: not null");

            if (objA == null || !objA.Equals(objB))
                return;

            throw new DebugAssertException($"{message}\nExpected: not {objB?.ToString() ?? "null"}");
        }

        [Conditional("DEBUG")]
        public static void AssertOwner(EntityUid? uid, IComponent? component)
        {
            if (component == null)
                return;

            if (uid == null)
                throw new DebugAssertException($"Null entity uid cannot own a component. Component: {component.GetType().Name}");

            // Whenever .owner is removed this will need to be replaced by something.
            // As long as components are just reference types, we could just get the component and check if the references are equal?
#pragma warning disable CS0618 // Type or member is obsolete
            if (component.Owner != uid)
#pragma warning restore CS0618 // Type or member is obsolete
                throw new DebugAssertException($"Entity {uid} is not the owner of the component. Component: {component.GetType().Name}");
        }

        /// <summary>
        ///     Throws a <see cref="DebugAssertException"/> carrying <paramref name="message"/> when
        ///     <paramref name="condition"/> is false.
        /// </summary>
        /// <param name="condition">The condition that is expected to hold.</param>
        /// <param name="message">Text of the exception, used verbatim.</param>
        /// <exception cref="DebugAssertException">The condition is false.</exception>
        [Conditional("DEBUG")]
        [AssertionMethod]
        public static void Assert([AssertionCondition(AssertionConditionType.IS_TRUE)] [DoesNotReturnIf(false)] bool condition, string message)
        {
            if (!condition)
                throw new DebugAssertException(message);
        }

        /// <summary>
        ///     Throws a <see cref="DebugAssertException"/> when <paramref name="condition"/> is false. The message is an
        ///     interpolated string that is only built if the assertion actually fails.
        /// </summary>
        /// <param name="condition">The condition that is expected to hold.</param>
        /// <param name="message">Lazily built message, rendered only on failure.</param>
        /// <exception cref="DebugAssertException">The condition is false.</exception>
        [Conditional("DEBUG")]
        [AssertionMethod]
        public static void Assert(
            [AssertionCondition(AssertionConditionType.IS_TRUE)] [DoesNotReturnIf(false)] bool condition,
            [InterpolatedStringHandlerArgument("condition")] ref AssertInterpolatedStringHandler message)
        {
            if (!condition)
                throw new DebugAssertException(message.ToStringAndClear());
        }

        /// <summary>
        ///     Throws a <see cref="DebugAssertException"/> when <paramref name="arg"/> is null.
        /// </summary>
        /// <param name="arg">The value that must not be null.</param>
        /// <param name="message">Optional exception text; a generic one is used when omitted.</param>
        /// <exception cref="DebugAssertException"><paramref name="arg"/> is null.</exception>
        [Conditional("DEBUG")]
        [AssertionMethod]
        public static void AssertNotNull(
            [AssertionCondition(AssertionConditionType.IS_NOT_NULL)] [System.Diagnostics.CodeAnalysis.NotNull] object? arg,
            string? message = null)
        {
            if (arg == null)
                throw new DebugAssertException(message ?? "value cannot be null");
        }

        /// <summary>
        ///     Throws a <see cref="DebugAssertException"/> when <paramref name="arg"/> is not null.
        /// </summary>
        /// <param name="arg">The value that must be null.</param>
        /// <param name="message">Optional exception text; a generic one is used when omitted.</param>
        /// <exception cref="DebugAssertException"><paramref name="arg"/> is not null.</exception>
        [Conditional("DEBUG")]
        [AssertionMethod]
        public static void AssertNull(
            [AssertionCondition(AssertionConditionType.IS_NULL)] object? arg,
            string? message = null)
        {
            if (arg != null)
                throw new DebugAssertException(message ?? "value should be null");
        }

        /// <summary>
        /// If a debugger is attached to the process, calling this function will cause the
        /// debugger to break. Equivalent to a software interrupt (INT 3).
        /// </summary>
        public static void Break()
        {
            Debugger.Break();
        }

        [InterpolatedStringHandler]
        public ref struct AssertInterpolatedStringHandler
        {
            private DefaultInterpolatedStringHandler _handler;

            public AssertInterpolatedStringHandler(int literalLength, int formattedCount, bool condition, out bool shouldAppend)
            {
                if (condition)
                {
                    shouldAppend = false;
                    _handler = default;
                }
                else
                {
                    shouldAppend = true;
                    _handler = new DefaultInterpolatedStringHandler(literalLength, formattedCount);
                }
            }

            public string ToStringAndClear() => _handler.ToStringAndClear();
            public override string ToString() => _handler.ToString();
            public void AppendLiteral(string value) => _handler.AppendLiteral(value);
            public void AppendFormatted<T>(T value) => _handler.AppendFormatted(value);
            public void AppendFormatted<T>(T value, string? format) => _handler.AppendFormatted(value, format);
        }
    }

    [Virtual]
    public class DebugAssertException : Exception
    {
        public DebugAssertException()
        {
        }

        public DebugAssertException(string? message) : base(message)
        {
        }
    }
}
