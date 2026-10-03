using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Robust.Shared.Console;
using Robust.Shared.Utility;

namespace Robust.Client.Console
{
    [NotContentImplementable]
    public interface IClientConsoleHost : IConsoleHost, IDisposable
    {
        /// <summary>
        ///     Brings the client console host into a usable state. It is invoked once during client startup.
        /// </summary>
        void Initialize();

        /// <summary>
        ///     Raised when a plain text line should be shown in the console output.
        ///     The rich text counterpart is <see cref="AddFormatted"/>.
        /// </summary>
        event EventHandler<AddStringArgs> AddString;
        event EventHandler<AddFormattedMessageArgs> AddFormatted;

        void AddFormattedLine(FormattedMessage message);

        Task<CompletionResult> GetCompletions(List<string> args, string argStr, CancellationToken cancel);
    }
}
