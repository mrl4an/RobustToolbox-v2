using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using Robust.Shared.Asynchronous;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Con = System.Console;

namespace Robust.Server.Console
{
    internal sealed class SystemConsoleManager : ISystemConsoleManager, IPostInjectInit, IDisposable
    {
        [Dependency] private readonly IServerConsoleHost _conShell = default!;
        [Dependency] private readonly ITaskManager _taskManager = default!;
        [Dependency] private readonly IBaseServer _baseServer = default!;
        [Dependency] private readonly IServerNetManager _netManager = default!;
        [Dependency] private readonly IGameTiming _time = default!;

        //
        // Command entry stuff.
        //

        private readonly Dictionary<int, string> commandHistory = new();
        private string currentBuffer = "";
        private int historyIndex;
        private int internalCursor;
        private List<string> tabCompleteList = new();
        private int tabCompleteIndex;
        private ConsoleKey lastKeyPressed = ConsoleKey.NoName;

        //
        // Title update stuff.
        //

        // This is ridiculously expensive to fetch for some reason.
        // I'm gonna just assume that this can't change during the lifetime of the process. I hope.
        // I want this ridiculous 0.1% CPU usage off my profiler.
        private readonly bool _userInteractive = Environment.UserInteractive && !System.Console.IsInputRedirected;

        private TimeSpan _lastTitleUpdate;
        private long _lastReceivedBytes;
        private long _lastSentBytes;

        private bool _hasCancelled;

        public void Dispose()
        {
            if (_rdLine != null)
            {
                _rdLine.Value.cts.Cancel();
                _rdLine.Value.cts.Dispose();

                try
                {
                    _rdLine.Value.task.Dispose();
                }
                catch
                {
                    // Don't care LOL
                }
            }

            if (Environment.UserInteractive)
            {
                Con.CancelKeyPress -= CancelKeyHandler;
            }
        }

        public void PostInject()
        {
            if (Environment.UserInteractive)
            {
                Con.CancelKeyPress += CancelKeyHandler;
            }
        }

        public void UpdateTick()
        {
            UpdateTitle();
        }

        /// <summary>
        ///     Refreshes the console window title with performance statistics: average FPS, frame time
        ///     standard deviation, network traffic and process private memory.
        /// </summary>
        /// <remarks>
        ///     Does nothing when the process is not interactive. The title is refreshed at most once per second
        ///     of real time; calls in between return early without touching the network baselines used by
        ///     <see cref="UpdateBps"/>. Exceptions thrown by the platform while setting the title are not caught.
        /// </remarks>
        private void UpdateTitle()
        {
            if (!_userInteractive)
                return;

            if ((_time.RealTime - _lastTitleUpdate).TotalSeconds < 1.0)
                return;

            var netText = UpdateBps();
            var privateBytes = Process.GetCurrentProcess().GetPrivateMemorySize64NotSlowHolyFuckingShitMicrosoft();

            System.Console.Title = string.Format(
                "FPS: {0:N2} SD: {1:N2}ms | Net: ({2}) | Memory: {3:N0} KiB",
                Math.Round(_time.FramesPerSecondAvg, 2),
                _time.RealFrameTimeStdDev.TotalMilliseconds,
                netText,
                privateBytes >> 10);

            // Deliberately a fresh read of the clock, taken after the title was updated.
            _lastTitleUpdate = _time.RealTime;
        }

        /// <summary>
        ///     Builds the network traffic text shown in the window title and advances the byte baselines.
        /// </summary>
        /// <remarks>
        ///     The values are the amounts sent and received since the previous call, truncated to whole KiB.
        ///     They are not divided by the elapsed time, so the "/s" label is only accurate when calls are
        ///     about one second apart. The baselines are replaced on every call, including the first one
        ///     (whose baseline is zero, so it reports the totals since startup).
        /// </remarks>
        /// <returns>Text such as <c>Send: 12 KiB/s, Recv: 3 KiB/s</c>.</returns>
        private string UpdateBps()
        {
            var stats = _netManager.Statistics;
            var sentNow = stats.SentBytes;
            var receivedNow = stats.ReceivedBytes;

            var sentKiB = (sentNow - _lastSentBytes) >> 10;
            var receivedKiB = (receivedNow - _lastReceivedBytes) >> 10;

            var text = string.Format("Send: {0:N0} KiB/s, Recv: {1:N0} KiB/s", sentKiB, receivedKiB);

            _lastSentBytes = sentNow;
            _lastReceivedBytes = receivedNow;

            return text;
        }

        private (Task task, CancellationTokenSource cts, Channel<string> chan)? _rdLine = null;

        public void UpdateInput()
        {
            if (_userInteractive)
            {
                HandleKeyboard();
                return;
            }

            if (_rdLine.HasValue) // Already running, check the channel.
            {
                if (_rdLine.Value.chan.Reader.TryRead(out var cmd))
                    _conShell.ExecuteCommand(cmd);

                return;
            }

            // Set up the new thread & thread accessories
            var rlc = new CancellationTokenSource();
            var chan = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity: 32)
                {
                    FullMode=BoundedChannelFullMode.Wait,
                    SingleReader=true,
                }
            );

            _rdLine = (
                task: Task.Run(
                    async () =>
                    {
                        while (!rlc.IsCancellationRequested)
                        {
                            var str = await Con.In
                                .ReadLineAsync()
                                .WaitAsync(TimeSpan.FromSeconds(2.0), rlc.Token);

                            await chan.Writer.WriteAsync(str!, rlc.Token);
                        }
                    },
                    rlc.Token
                ),
                cts: rlc,
                chan: chan
            );
        }

        /// <summary>
        ///     Drains all pending key presses from the terminal without blocking, updating the line editing
        ///     state and redrawing the prompt after every key.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Keys are read with echo suppressed; everything shown on screen is drawn by this class.
        ///         Modifier keys are never inspected, so for example Shift+Enter and Ctrl+Enter act as Enter.
        ///     </para>
        ///     <para>
        ///         Keys whose character is not a control character are inserted into the buffer at the cursor.
        ///         That path leaves the last-pressed-key tracking, the history position and the tab completion
        ///         state untouched. Control keys are dispatched on the key identity: Enter (run the line),
        ///         Backspace, Delete, Up/Down (history), Escape (reset the line), Left/Right and Tab
        ///         (command name completion). Home, End and any other control key change nothing except that
        ///         the line is redrawn and the key is remembered as the last pressed control key.
        ///     </para>
        ///     <para>
        ///         Since only control keys update the last-pressed-key tracking, typing ordinary characters
        ///         between two Tab presses does not restart tab completion.
        ///     </para>
        ///     <para>
        ///         This method does not check whether the process is interactive; the caller is expected to.
        ///     </para>
        /// </remarks>
        public void HandleKeyboard()
        {
            while (Con.KeyAvailable)
            {
                var key = Con.ReadKey(intercept: true);

                if (Con.WindowWidth > 0)
                    Con.SetCursorPosition(0, Con.CursorTop);

                if (!char.IsControl(key.KeyChar))
                {
                    currentBuffer = currentBuffer.Insert(internalCursor, key.KeyChar.ToString());
                    internalCursor += 1;
                    DrawCommandLine();
                    continue;
                }

                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                    {
                        if (currentBuffer.Length == 0)
                            break;

                        var submitted = currentBuffer;

                        Con.WriteLine("> " + submitted);

                        commandHistory[commandHistory.Count] = submitted;
                        historyIndex = commandHistory.Count;

                        _conShell.ExecuteCommand(submitted);

                        currentBuffer = "";
                        internalCursor = 0;
                        break;
                    }

                    case ConsoleKey.Backspace:
                    {
                        if (currentBuffer.Length > 0 && internalCursor > 0)
                        {
                            currentBuffer = currentBuffer.Remove(internalCursor - 1, 1);
                            internalCursor -= 1;
                        }

                        break;
                    }

                    case ConsoleKey.Delete:
                    {
                        if (currentBuffer.Length > 0 && internalCursor < currentBuffer.Length)
                            currentBuffer = currentBuffer.Remove(internalCursor, 1);

                        break;
                    }

                    case ConsoleKey.UpArrow:
                    {
                        if (historyIndex > 0)
                            historyIndex -= 1;

                        currentBuffer = commandHistory.TryGetValue(historyIndex, out var entry) ? entry : "";
                        internalCursor = currentBuffer.Length;
                        break;
                    }

                    case ConsoleKey.DownArrow:
                    {
                        if (historyIndex < commandHistory.Count)
                            historyIndex += 1;

                        currentBuffer = commandHistory.TryGetValue(historyIndex, out var entry) ? entry : "";
                        internalCursor = currentBuffer.Length;
                        break;
                    }

                    case ConsoleKey.Escape:
                    {
                        historyIndex = commandHistory.Count;
                        currentBuffer = "";
                        internalCursor = 0;
                        break;
                    }

                    case ConsoleKey.LeftArrow:
                    {
                        if (internalCursor > 0)
                            internalCursor -= 1;

                        break;
                    }

                    case ConsoleKey.RightArrow:
                    {
                        if (internalCursor < currentBuffer.Length)
                            internalCursor += 1;

                        break;
                    }

                    case ConsoleKey.Tab:
                    {
                        // A new completion session starts unless the previous control key was also Tab.
                        if (lastKeyPressed != ConsoleKey.Tab)
                            tabCompleteList.Clear();

                        var completed = TabComplete();
                        if (completed.Length > 0)
                        {
                            currentBuffer = completed;
                            internalCursor = currentBuffer.Length;
                        }

                        break;
                    }
                }

                lastKeyPressed = key.Key;
                DrawCommandLine();
            }
        }

        public void Print(string text)
        {
            Con.Write(text);
        }

        /// <summary>
        ///     Redraws the prompt line (<c>"&gt; "</c> followed by the current buffer) on the row the console
        ///     cursor is currently on and places the cursor at the editing position.
        /// </summary>
        /// <remarks>
        ///     Does nothing when no real console is attached (window width of zero or less). There is no
        ///     wrapping or scrolling logic: a buffer longer than the window wraps naturally and only the last
        ///     row is cleared on the next redraw. Placing the cursor outside the window raises the console's
        ///     usual exception, which is not caught here.
        /// </remarks>
        public void DrawCommandLine()
        {
            if (Con.WindowWidth <= 0)
                return;

            ClearCurrentLine();
            Con.SetCursorPosition(0, Con.CursorTop);
            Con.Write("> " + currentBuffer);
            // Offset of two for the "> " prefix.
            Con.SetCursorPosition(internalCursor + 2, Con.CursorTop);
        }

        /// <summary>
        ///     Blanks the current console row and leaves the cursor at its first column.
        /// </summary>
        /// <remarks>
        ///     Only <c>WindowWidth - 1</c> columns are overwritten so that the write never wraps onto the next
        ///     row; a character in the very last column therefore survives. Callers must make sure the window
        ///     width is positive, otherwise the negative repeat count throws.
        /// </remarks>
        private static void ClearCurrentLine()
        {
            var row = Con.CursorTop;
            Con.SetCursorPosition(0, row);
            Con.Write(new string(' ', Con.WindowWidth - 1));
            Con.SetCursorPosition(0, row);
        }

        /// <summary>
        ///     Computes the next command name completion for the current buffer.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Only command names are completed, and the whole buffer is used as the prefix, so a buffer
        ///         with arguments matches nothing. Candidates are gathered only when the candidate list is
        ///         empty and keep the enumeration order of the available commands. Each call hands out the
        ///         next candidate, wrapping to the first one after the last.
        ///     </para>
        ///     <para>
        ///         The candidate index is never reset when the list is rebuilt; it only wraps back to zero once
        ///         it reaches the list length. A new completion session can therefore start at a later
        ///         candidate than the first.
        ///     </para>
        ///     <para>
        ///         This method does not change the buffer or the cursor; the caller applies the result.
        ///     </para>
        /// </remarks>
        /// <returns>The text that should replace the whole buffer, or an empty string if nothing applies.</returns>
        private string TabComplete()
        {
            if (currentBuffer.Trim().Length == 0)
                return "";

            if (tabCompleteList.Count == 0)
            {
                foreach (var name in _conShell.AvailableCommands.Keys)
                {
                    if (name.StartsWith(currentBuffer))
                        tabCompleteList.Add(name);
                }

                if (tabCompleteList.Count == 0)
                    return "";
            }

            if (tabCompleteIndex >= tabCompleteList.Count)
                tabCompleteIndex = 0;

            var candidate = tabCompleteList[tabCompleteIndex];
            tabCompleteIndex += 1;
            return candidate;
        }

        private void CancelKeyHandler(object? sender, ConsoleCancelEventArgs args)
        {
            if (_hasCancelled)
            {
                Con.WriteLine("Double CancelKey, terminating process.");
                return;
            }

            // Handle process exiting ourselves.
            args.Cancel = true;
            _hasCancelled = true;

            _taskManager.RunOnMainThread(() => { _baseServer.Shutdown("CancelKey"); });
        }
    }
}
