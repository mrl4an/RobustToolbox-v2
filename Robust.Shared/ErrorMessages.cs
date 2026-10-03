namespace Robust.Shared
{
    /// <summary>
    ///     Reusable message texts for reporting invalid method arguments.
    /// </summary>
    /// <remarks>
    ///     The values are compile-time constants, so consumers embed the text when they are compiled.
    /// </remarks>
    public static class ErrorMessages
    {
        /// <summary>
        ///     Message for a string argument that is not in the expected format. This is a composite
        ///     format string: argument 0 should describe the expected format.
        /// </summary>
        public const string InvalidStringFormat = "Invalid string format. Expected a string in the format \"{0}\".";

        /// <summary>
        ///     Message for a parameter that does not accept <see langword="null"/>.
        /// </summary>
        public const string NullNotAllowed = "This method does not accept null for this parameter.";
    }
}
