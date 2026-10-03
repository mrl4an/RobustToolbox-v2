using System.Collections;
using System.Collections.Generic;
using Robust.Shared.Analyzers;

namespace Robust.Shared.Input
{
    /// <summary>
    ///     A named set of <see cref="BoundKeyFunction"/>s that are currently allowed to fire.
    ///     A context may have a parent, in which case a function can be looked up either in the
    ///     context alone or through the context and all of its ancestors.
    /// </summary>
    /// <remarks>
    ///     Enumerating a context yields only the functions registered directly on it, never those
    ///     of its parent.
    /// </remarks>
    [NotContentImplementable]
    public interface IInputCmdContext : IEnumerable<BoundKeyFunction>
    {
        /// <summary>
        ///     Adds a function to this context. The parent context is not affected.
        /// </summary>
        /// <param name="function">The function to make available in this context.</param>
        void AddFunction(BoundKeyFunction function);

        /// <summary>
        ///     Checks whether this context itself contains the function. Parent contexts are not consulted;
        ///     use <see cref="FunctionExistsHierarchy"/> for that.
        /// </summary>
        /// <param name="function">The function to look for.</param>
        /// <returns>True if the function was added to this context and not removed since.</returns>
        bool FunctionExists(BoundKeyFunction function);

        /// <summary>
        ///     Checks whether the function is available in this context or in any of its ancestors.
        ///     The search starts at this context and moves up the parent chain, stopping at the first match.
        /// </summary>
        /// <param name="function">The function to look for.</param>
        /// <returns>True if this context or one of its ancestors contains the function.</returns>
        bool FunctionExistsHierarchy(BoundKeyFunction function);

        /// <summary>
        ///     Removes a function from this context. The parent context is not affected,
        ///     so the function may still be reported by <see cref="FunctionExistsHierarchy"/> afterwards.
        ///     Does nothing if the function is not present.
        /// </summary>
        /// <param name="function">The function to remove.</param>
        void RemoveFunction(BoundKeyFunction function);

        /// <summary>
        ///     The name of this context, exactly as it was given when the context was created.
        /// </summary>
        string Name { get; }
    }

    /// <summary>
    ///     The engine implementation of <see cref="IInputCmdContext"/>.
    /// </summary>
    /// <remarks>
    ///     Functions are stored in a plain list rather than a set: insertion order is preserved and the same
    ///     function can be added more than once. Each call to <see cref="RemoveFunction"/> removes only one
    ///     occurrence, so a function added twice still exists after a single removal.
    ///     Instances are not thread-safe and are meant to be used from the main/input thread only.
    ///     Instances are created through <see cref="IInputContextContainer"/>, which is also responsible
    ///     for validating context names.
    /// </remarks>
    internal sealed class InputCmdContext : IInputCmdContext
    {
        private readonly IInputCmdContext? _parent;
        private readonly List<BoundKeyFunction> _contextFunctions = new();

        /// <inheritdoc />
        public string Name { get; }

        /// <summary>
        ///     Creates a context that inherits from another one.
        /// </summary>
        /// <param name="parent">The parent context, or null for a context without a parent.</param>
        /// <param name="name">The name of the new context. It is stored as given and is not validated here.</param>
        internal InputCmdContext(IInputCmdContext? parent, string name)
        {
            _parent = parent;
            Name = name;
        }

        /// <summary>
        ///     Creates a context that has no parent.
        /// </summary>
        /// <param name="name">The name of the new context. It is stored as given and is not validated here.</param>
        internal InputCmdContext(string name)
        {
            _parent = null;
            Name = name;
        }

        /// <summary>
        ///     Appends the function to this context. Adding a function that is already present
        ///     creates a second entry; it is not ignored.
        /// </summary>
        /// <param name="function">The function to make available in this context.</param>
        public void AddFunction(BoundKeyFunction function)
        {
            _contextFunctions.Add(function);
        }

        /// <summary>
        ///     Checks only this context's own functions, ignoring the parent chain.
        ///     Function names are compared using the ordinal, case-sensitive equality of <see cref="BoundKeyFunction"/>.
        /// </summary>
        /// <param name="function">The function to look for.</param>
        /// <returns>True if this context holds at least one entry equal to the function.</returns>
        public bool FunctionExists(BoundKeyFunction function)
        {
            return _contextFunctions.Contains(function);
        }

        /// <summary>
        ///     Checks this context first and then walks up the parent chain, returning as soon as a match is found.
        ///     Children are never consulted. The parent is queried through <see cref="IInputCmdContext"/>,
        ///     so any parent implementation is supported.
        /// </summary>
        /// <param name="function">The function to look for.</param>
        /// <returns>True if this context or any ancestor holds the function.</returns>
        public bool FunctionExistsHierarchy(BoundKeyFunction function)
        {
            if (_contextFunctions.Contains(function))
            {
                return true;
            }

            return _parent?.FunctionExistsHierarchy(function) ?? false;
        }

        /// <summary>
        ///     Removes the first entry equal to the function from this context, leaving the order of the
        ///     other entries unchanged. If the function was added several times, the remaining copies stay.
        ///     Does nothing if the function is not present. The parent is never modified.
        /// </summary>
        /// <param name="function">The function to remove.</param>
        public void RemoveFunction(BoundKeyFunction function)
        {
            _contextFunctions.Remove(function);
        }

        /// <summary>
        ///     Enumerates the functions registered directly on this context, in insertion order and including
        ///     duplicates. Functions of the parent are not included.
        /// </summary>
        /// <remarks>
        ///     The enumeration is live, not a snapshot: calling <see cref="AddFunction"/> or
        ///     <see cref="RemoveFunction"/> while enumerating makes the next step of the enumeration throw
        ///     <see cref="System.InvalidOperationException"/>.
        /// </remarks>
        public IEnumerator<BoundKeyFunction> GetEnumerator()
        {
            return _contextFunctions.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
