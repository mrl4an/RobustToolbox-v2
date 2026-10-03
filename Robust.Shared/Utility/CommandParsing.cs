using System;
using System.Collections.Generic;
using System.Text;
using Robust.Shared.Collections;

namespace Robust.Shared.Utility
{
    public static class CommandParsing
    {
        /// <summary>
        ///     Splits a full console command line into its individual arguments.
        /// </summary>
        /// <param name="text">The complete input string.</param>
        /// <param name="args">The list the parsed arguments are appended to. It is not cleared first.</param>
        public static void ParseArguments(ReadOnlySpan<char> text, List<string> args)
        {
            // The source ranges are not needed here, so they are collected and thrown away.
            var ranges = new ValueList<(int start, int end)>();
            ParseArguments(text, args, ref ranges);
        }

        /// <summary>
        ///     Splits a console command line into arguments and also reports where each argument came from.
        ///     Arguments are separated by spaces; double quotes group text (including spaces) into one argument,
        ///     and a backslash makes the following character literal.
        /// </summary>
        /// <param name="text">The complete input string.</param>
        /// <param name="args">The list the parsed arguments are appended to. It is not cleared first.</param>
        /// <param name="ranges">
        ///     Receives one (start, end) pair per appended argument, with an exclusive end. The pairs are
        ///     index-aligned with the arguments added by this call. They are only an approximation of the
        ///     source location: a backslash escape moves the start to the escaped character, and the dropped
        ///     backslash itself is not accounted for.
        /// </param>
        internal static void ParseArguments(
            ReadOnlySpan<char> text,
            List<string> args,
            ref ValueList<(int start, int end)> ranges)
        {
            var current = new StringBuilder();
            var inQuotes = false;
            // Index where the argument being built began, or -1 if none has begun.
            var start = -1;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (c == '\\')
                {
                    // Escape: take the next character literally.
                    i++;
                    start = i;

                    if (i == text.Length)
                    {
                        // Nothing follows the backslash, so keep it as a plain character and stop.
                        current.Append('\\');
                        break;
                    }

                    current.Append(text[i]);
                    continue;
                }

                if (c == '"')
                {
                    if (inQuotes)
                    {
                        // Closing quote: the argument is complete, even if it is empty.
                        args.Add(current.ToString());
                        current.Clear();
                        ranges.Add((start, i + 1));
                        start = -1;
                    }
                    else if (start == -1)
                    {
                        start = i;
                    }

                    inQuotes = !inQuotes;
                    continue;
                }

                if (c == ' ' && !inQuotes)
                {
                    // Unquoted space ends the current argument, if there is one.
                    if (current.Length != 0)
                    {
                        args.Add(current.ToString());
                        current.Clear();
                        ranges.Add((start, i));
                        start = -1;
                    }

                    continue;
                }

                // Ordinary character (this includes spaces inside quotes).
                if (start == -1)
                {
                    start = i;
                }

                current.Append(c);
            }

            // Flush whatever is left, including the content of an unterminated quote.
            if (current.Length != 0)
            {
                args.Add(current.ToString());
                ranges.Add((start, text.Length));
            }
        }

        public static string Escape(string text)
        {
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
