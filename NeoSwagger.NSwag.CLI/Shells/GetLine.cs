//
// getline.cs: A command line editor
//
// Authors:
//   Miguel de Icaza (miguel@novell.com)
//
// Copyright 2008 Novell, Inc.
// Copyright 2016 Xamarin Inc
//
// Completion wanted:
//
//   * Enable bash-like completion window the window as an option for non-GUI people?
//
//   * Continue completing when Backspace is used?
//
//   * Should we keep the auto-complete on "."?
//
//   * Completion produces an error if the value is not resolvable, we should hide those errors
//
// Dual-licensed under the terms of the MIT X11 license or the
// Apache License 2.0
//
// USE -define:DEMO to build this as a standalone file and test it
//
// TODO:
//    Enter an error (a = 1);  Notice how the prompt is in the wrong line
//		This is caused by Stderr not being tracked by System.Console.
//    Completion support
//    Why is Thread.Interrupt not working?   Currently I resort to Abort which is too much.
//
// Limitations in System.Console:
//    Console needs SIGWINCH support of some sort
//    Console needs a way of updating its position after things have been written
//    behind its back (P/Invoke puts for example).
//    System.Console needs to get the DELETE character, and report accordingly.
//
// Bug:
//   About 8 lines missing, type "Con<TAB>" and not enough lines are inserted at the bottom.
// 
//

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace NeoSwagger.NSwag.CLI.Shells
{
    [ExcludeFromCodeCoverage]
    public class LineEditor
    {
        public delegate Completion AutoCompleteHandler(string text, int pos);

        // If this is set, it contains an escape sequence to reset the Unix colors to the ones that were used on startup
        private static byte[] unixResetColors;

        // This contains a raw stream pointing to stdout, used to bypass the TermInfoDriver
        private static Stream unixRawOutput;

        private static Handler[] handlers;

        // Our object that tracks history
        private readonly History history;

        // The prompt specified, and the prompt shown to the user.

        // The text as it is rendered (replaces (char)1 with ^A on display for example).
        private readonly StringBuilder renderedText;

        /// <summary>
        ///     Invoked when the user requests auto-completion using the tab character
        /// </summary>
        /// <remarks>
        ///     The result is null for no values found, an array with a single
        ///     string, in that case the string should be the text to be inserted
        ///     for example if the word at pos is "T", the result for a completion
        ///     of "ToString" should be "oString", not "ToString".
        ///     When there are multiple results, the result should be the full
        ///     text
        /// </remarks>
        public AutoCompleteHandler AutoCompleteEvent;

        // If we have a popup completion, this is not null and holds the state.
        private CompletionState currentCompletion;

        // The current cursor position, indexes into "text", for an index
        // into rendered_text, use TextToRenderPos
        private int cursor;

        // If we are done editing, this breaks the interactive loop
        private bool done;

        // The thread where the Editing started taking place
        private Thread editThread;

        // null does nothing, "csharp" uses some heuristics that make sense for C#
        public string HeuristicsMode;

        // The row where we started displaying data.
        private int homeRow;

        // The contents of the kill buffer (cut/paste in Emacs parlance)
        private string killBuffer = "";

        // Used to implement the Kill semantics (multiple Alt-Ds accumulate)
        private KeyHandler lastHandler;

        private string lastSearch;

        // The position where we found the match.
        private int matchAt;

        // The maximum length that has been displayed on the screen
        private int maxRendered;

        // The string being searched for
        private string search;

        // whether we are searching (-1= reverse; 0 = no; 1 = forward)
        private int searching;

        private string shownPrompt;

        //static StreamWriter log;

        // The text being edited.
        private StringBuilder text;

        public LineEditor(string name) : this(name, 10)
        {
        }

        public LineEditor(string name, int histsize)
        {
            handlers = new[]
            {
                new Handler(ConsoleKey.Home, CmdHome),
                new Handler(ConsoleKey.End, CmdEnd),
                new Handler(ConsoleKey.LeftArrow, CmdLeft),
                new Handler(ConsoleKey.RightArrow, CmdRight),
                new Handler(ConsoleKey.UpArrow, CmdUp, false),
                new Handler(ConsoleKey.DownArrow, CmdDown, false),
                new Handler(ConsoleKey.Enter, CmdDone, false),
                new Handler(ConsoleKey.Backspace, CmdBackspace, false),
                new Handler(ConsoleKey.Delete, CmdDeleteChar),
                new Handler(ConsoleKey.Tab, CmdTabOrComplete, false),

                // Emacs keys
                Handler.Control('A', CmdHome),
                Handler.Control('E', CmdEnd),
                Handler.Control('B', CmdLeft),
                Handler.Control('F', CmdRight),
                Handler.Control('P', CmdUp, false),
                Handler.Control('N', CmdDown, false),
                Handler.Control('K', CmdKillToEof),
                Handler.Control('Y', CmdYank),
                Handler.Control('D', CmdDeleteChar),
                Handler.Control('L', CmdRefresh),
                Handler.Control('R', CmdReverseSearch),
                Handler.Control('G', delegate { }),
                Handler.Alt('B', ConsoleKey.B, CmdBackwardWord),
                Handler.Alt('F', ConsoleKey.F, CmdForwardWord),

                Handler.Alt('D', ConsoleKey.D, CmdDeleteWord),
                Handler.Alt((char) 8, ConsoleKey.Backspace, CmdDeleteBackword),

                // DEBUG
                //Handler.Control ('T', CmdDebug),

                // quote
                Handler.Control('Q', delegate { HandleChar(Console.ReadKey(true).KeyChar); })
            };

            renderedText = new StringBuilder();
            text = new StringBuilder();

            history = new History(name, histsize);

            GetUnixConsoleReset();
            //if (File.Exists ("log"))File.Delete ("log");
            //log = File.CreateText ("log"); 
        }

        private string Prompt { get; set; }

        private int LineCount => (shownPrompt.Length + renderedText.Length) / Console.WindowWidth;

        public bool TabAtStartCompletes { get; set; }

        // On Unix, there is a "default" color which is not represented by any colors in
        // ConsoleColor and it is not possible to set is by setting the ForegroundColor or
        // BackgroundColor properties, so we have to use the terminfo driver in Mono to
        // fetch these values

        private static void GetUnixConsoleReset()
        {
            //
            // On Unix, we want to be able to reset the color for the pop-up completion
            //
            var p = (int) Environment.OSVersion.Platform;
            var isUnix = p == 4 || p == 128;
            if (!isUnix)
                return;

            // Sole purpose of this call is to initialize the Terminfo driver

            try
            {
                var terminfoDriver = Type.GetType("System.ConsoleDriver")?.GetField("driver", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
                if (terminfoDriver == null)
                    return;

                if (terminfoDriver.GetType()?.GetField("origPair", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(terminfoDriver) is string unixResetColorsStr)
                    unixResetColors = Encoding.UTF8.GetBytes(unixResetColorsStr);
                unixRawOutput = Console.OpenStandardOutput();
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: " + e);
            }
        }

        private void Render()
        {
            Console.Write(shownPrompt);
            Console.Write(renderedText);

            var max = Math.Max(renderedText.Length + shownPrompt.Length, maxRendered);

            for (var i = renderedText.Length + shownPrompt.Length; i < maxRendered; i++)
                Console.Write(' ');
            maxRendered = shownPrompt.Length + renderedText.Length;

            // Write one more to ensure that we always wrap around properly if we are at the
            // end of a line.
            Console.Write(' ');

            UpdateHomeRow(max);
        }

        private void UpdateHomeRow(int screenpos)
        {
            var lines = 1 + screenpos / Console.WindowWidth;

            homeRow = Console.CursorTop - (lines - 1);
            if (homeRow < 0)
                homeRow = 0;
        }


        private void RenderFrom(int pos)
        {
            var rpos = TextToRenderPos(pos);
            int i;

            for (i = rpos; i < renderedText.Length; i++)
                Console.Write(renderedText[i]);

            if (shownPrompt.Length + renderedText.Length > maxRendered)
            {
                maxRendered = shownPrompt.Length + renderedText.Length;
            }
            else
            {
                var maxExtra = maxRendered - shownPrompt.Length;
                for (; i < maxExtra; i++)
                    Console.Write(' ');
            }
        }

        private void ComputeRendered()
        {
            renderedText.Length = 0;

            for (var i = 0; i < text.Length; i++)
            {
                int c = text[i];
                if (c < 26)
                    if (c == '\t')
                    {
                        renderedText.Append("    ");
                    }
                    else
                    {
                        renderedText.Append('^');
                        renderedText.Append((char) (c + 'A' - 1));
                    }
                else
                    renderedText.Append((char) c);
            }
        }

        private int TextToRenderPos(int pos)
        {
            var p = 0;

            for (var i = 0; i < pos; i++)
            {
                int c;

                c = text[i];

                if (c < 26)
                    if (c == 9)
                        p += 4;
                    else
                        p += 2;
                else
                    p++;
            }

            return p;
        }

        private int TextToScreenPos(int pos)
        {
            return shownPrompt.Length + TextToRenderPos(pos);
        }

        private void ForceCursor(int newpos)
        {
            cursor = newpos;

            var actualPos = shownPrompt.Length + TextToRenderPos(cursor);
            var row = homeRow + actualPos / Console.WindowWidth;
            var col = actualPos % Console.WindowWidth;

            if (row >= Console.BufferHeight)
                row = Console.BufferHeight - 1;
            Console.SetCursorPosition(col, row);

            //log.WriteLine ("Going to cursor={0} row={1} col={2} actual={3} prompt={4} ttr={5} old={6}", newpos, row, col, actual_pos, prompt.Length, TextToRenderPos (cursor), cursor);
            //log.Flush ();
        }

        private void UpdateCursor(int newpos)
        {
            if (cursor == newpos)
                return;

            ForceCursor(newpos);
        }

        private void InsertChar(char c)
        {
            var prevLines = LineCount;
            text = text.Insert(cursor, c);
            ComputeRendered();
            if (prevLines != LineCount)
            {
                Console.SetCursorPosition(0, homeRow);
                Render();
                ForceCursor(++cursor);
            }
            else
            {
                RenderFrom(cursor);
                ForceCursor(++cursor);
                UpdateHomeRow(TextToScreenPos(cursor));
            }
        }

        private static void SaveExcursion(Action code)
        {
            var savedCol = Console.CursorLeft;
            var savedRow = Console.CursorTop;
            var savedFore = Console.ForegroundColor;
            var savedBack = Console.BackgroundColor;

            code();

            Console.CursorLeft = savedCol;
            Console.CursorTop = savedRow;
            if (unixResetColors != null)
            {
                unixRawOutput.Write(unixResetColors, 0, unixResetColors.Length);
            }
            else
            {
                Console.ForegroundColor = savedFore;
                Console.BackgroundColor = savedBack;
            }
        }

        private void ShowCompletions(string prefix, string[] completions)
        {
            // Ensure we have space, determine window size
            var windowHeight = Math.Min(completions.Length, Console.WindowHeight / 5);
            var targetLine = Console.WindowHeight - windowHeight - 1;
            if (Console.CursorTop > targetLine)
            {
                var delta = Console.CursorTop - targetLine;
                Console.CursorLeft = 0;
                Console.CursorTop = Console.WindowHeight - 1;
                for (var i = 0; i < delta + 1; i++)
                for (var c = Console.WindowWidth; c > 0; c--)
                    Console.Write(" "); // To debug use ("{0}", i%10);
                Console.CursorTop = targetLine;
                Console.CursorLeft = 0;
                Render();
            }

            const int maxWidth = 50;
            var windowWidth = 12;
            var plen = prefix.Length;
            foreach (var s in completions)
                windowWidth = Math.Max(plen + s.Length, windowWidth);
            windowWidth = Math.Min(windowWidth, maxWidth);

            if (currentCompletion == null)
            {
                var left = Console.CursorLeft - prefix.Length;

                if (left + windowWidth + 1 >= Console.WindowWidth)
                    left = Console.WindowWidth - windowWidth - 1;

                currentCompletion = new CompletionState(left, Console.CursorTop + 1, windowWidth, windowHeight)
                {
                    Prefix = prefix,
                    Completions = completions
                };
            }
            else
            {
                currentCompletion.Prefix = prefix;
                currentCompletion.Completions = completions;
            }

            currentCompletion.Show();
            Console.CursorLeft = 0;
        }

        private void HideCompletions()
        {
            if (currentCompletion == null)
                return;
            currentCompletion.Remove();
            currentCompletion = null;
        }

        //
        // Triggers the completion engine, if insertBestMatch is true, then this will
        // insert the best match found, this behaves like the shell "tab" which will
        // complete as much as possible given the options.
        //
        private void Complete()
        {
            var completion = AutoCompleteEvent(text.ToString(), cursor);
            var completions = completion.Result;
            if (completions == null)
            {
                HideCompletions();
                return;
            }

            var ncompletions = completions.Length;
            if (ncompletions == 0)
            {
                HideCompletions();
                return;
            }

            if (completions.Length == 1)
            {
                InsertTextAtCursor(completions[0]);
                HideCompletions();
            }
            else
            {
                var last = -1;

                for (var p = 0; p < completions[0].Length; p++)
                {
                    var c = completions[0][p];


                    for (var i = 1; i < ncompletions; i++)
                    {
                        if (completions[i].Length < p)
                            goto mismatch;

                        if (completions[i][p] != c)
                            goto mismatch;
                    }

                    last = p;
                }

                mismatch:
                var prefix = completion.Prefix;
                if (last != -1)
                {
                    InsertTextAtCursor(completions[0].Substring(0, last + 1));

                    // Adjust the completions to skip the common prefix
                    prefix += completions[0].Substring(0, last + 1);
                    for (var i = 0; i < completions.Length; i++)
                        completions[i] = completions[i].Substring(last + 1);
                }

                ShowCompletions(prefix, completions);
                Render();
                ForceCursor(cursor);
            }
        }

        //
        // When the user has triggered a completion window, this will try to update
        // the contents of it.   The completion window is assumed to be hidden at this
        // point
        // 
        private void UpdateCompletionWindow()
        {
            if (currentCompletion != null)
                throw new Exception("This method should only be called if the window has been hidden");

            var completion = AutoCompleteEvent(text.ToString(), cursor);
            var completions = completion.Result;
            if (completions == null)
                return;

            var ncompletions = completions.Length;
            if (ncompletions == 0)
                return;

            ShowCompletions(completion.Prefix, completion.Result);
            Render();
            ForceCursor(cursor);
        }


        //
        // Commands
        //
        private void CmdDone()
        {
            if (currentCompletion != null)
            {
                InsertTextAtCursor(currentCompletion.Current);
                HideCompletions();
                return;
            }

            done = true;
        }

        private void CmdTabOrComplete()
        {
            var complete = false;

            if (AutoCompleteEvent != null)
            {
                if (TabAtStartCompletes)
                    complete = true;
                else
                    for (var i = 0; i < cursor; i++)
                        if (!char.IsWhiteSpace(text[i]))
                        {
                            complete = true;
                            break;
                        }

                if (complete)
                    Complete();
                else
                    HandleChar('\t');
            }
            else
            {
                HandleChar('t');
            }
        }

        private void CmdHome()
        {
            UpdateCursor(0);
        }

        private void CmdEnd()
        {
            UpdateCursor(text.Length);
        }

        private void CmdLeft()
        {
            if (cursor == 0)
                return;

            UpdateCursor(cursor - 1);
        }

        private void CmdBackwardWord()
        {
            var p = WordBackward(cursor);
            if (p == -1)
                return;
            UpdateCursor(p);
        }

        private void CmdForwardWord()
        {
            var p = WordForward(cursor);
            if (p == -1)
                return;
            UpdateCursor(p);
        }

        private void CmdRight()
        {
            if (cursor == text.Length)
                return;

            UpdateCursor(cursor + 1);
        }

        private void RenderAfter(int p)
        {
            ForceCursor(p);
            RenderFrom(p);
            ForceCursor(cursor);
        }

        private void CmdBackspace()
        {
            if (cursor == 0)
                return;

            var completing = currentCompletion != null;
            HideCompletions();

            text.Remove(--cursor, 1);
            ComputeRendered();
            RenderAfter(cursor);
            if (completing)
                UpdateCompletionWindow();
        }

        private void CmdDeleteChar()
        {
            // If there is no input, this behaves like EOF
            if (text.Length == 0)
            {
                done = true;
                text = null;
                Console.WriteLine();
                return;
            }

            if (cursor == text.Length)
                return;
            text.Remove(cursor, 1);
            ComputeRendered();
            RenderAfter(cursor);
        }

        private int WordForward(int p)
        {
            if (p >= text.Length)
                return -1;

            var i = p;
            if (char.IsPunctuation(text[p]) || char.IsSymbol(text[p]) || char.IsWhiteSpace(text[p]))
            {
                for (; i < text.Length; i++)
                    if (char.IsLetterOrDigit(text[i]))
                        break;
                for (; i < text.Length; i++)
                    if (!char.IsLetterOrDigit(text[i]))
                        break;
            }
            else
            {
                for (; i < text.Length; i++)
                    if (!char.IsLetterOrDigit(text[i]))
                        break;
            }

            if (i != p)
                return i;
            return -1;
        }

        private int WordBackward(int p)
        {
            if (p == 0)
                return -1;

            var i = p - 1;
            if (i == 0)
                return 0;

            if (char.IsPunctuation(text[i]) || char.IsSymbol(text[i]) || char.IsWhiteSpace(text[i]))
            {
                for (; i >= 0; i--)
                    if (char.IsLetterOrDigit(text[i]))
                        break;
                for (; i >= 0; i--)
                    if (!char.IsLetterOrDigit(text[i]))
                        break;
            }
            else
            {
                for (; i >= 0; i--)
                    if (!char.IsLetterOrDigit(text[i]))
                        break;
            }

            i++;

            if (i != p)
                return i;

            return -1;
        }

        private void CmdDeleteWord()
        {
            var pos = WordForward(cursor);

            if (pos == -1)
                return;

            var k = text.ToString(cursor, pos - cursor);

            if (lastHandler == CmdDeleteWord)
                killBuffer = killBuffer + k;
            else
                killBuffer = k;

            text.Remove(cursor, pos - cursor);
            ComputeRendered();
            RenderAfter(cursor);
        }

        private void CmdDeleteBackword()
        {
            var pos = WordBackward(cursor);
            if (pos == -1)
                return;

            var k = text.ToString(pos, cursor - pos);

            if (lastHandler == CmdDeleteBackword)
                killBuffer = k + killBuffer;
            else
                killBuffer = k;

            text.Remove(pos, cursor - pos);
            ComputeRendered();
            RenderAfter(pos);
        }

        //
        // Adds the current line to the history if needed
        //
        private void HistoryUpdateLine()
        {
            history.Update(text.ToString());
        }

        private void CmdHistoryPrev()
        {
            if (!history.PreviousAvailable())
                return;

            HistoryUpdateLine();

            SetText(history.Previous());
        }

        private void CmdHistoryNext()
        {
            if (!history.NextAvailable())
                return;

            history.Update(text.ToString());
            SetText(history.Next());
        }

        private void CmdUp()
        {
            if (currentCompletion == null)
                CmdHistoryPrev();
            else
                currentCompletion.SelectPrevious();
        }

        private void CmdDown()
        {
            if (currentCompletion == null)
                CmdHistoryNext();
            else
                currentCompletion.SelectNext();
        }

        private void CmdKillToEof()
        {
            killBuffer = text.ToString(cursor, text.Length - cursor);
            text.Length = cursor;
            ComputeRendered();
            RenderAfter(cursor);
        }

        private void CmdYank()
        {
            InsertTextAtCursor(killBuffer);
        }

        private void InsertTextAtCursor(string str)
        {
            var prevLines = LineCount;
            text.Insert(cursor, str);
            ComputeRendered();
            if (prevLines != LineCount)
            {
                Console.SetCursorPosition(0, homeRow);
                Render();
                cursor += str.Length;
                ForceCursor(cursor);
            }
            else
            {
                RenderFrom(cursor);
                cursor += str.Length;
                ForceCursor(cursor);
                UpdateHomeRow(TextToScreenPos(cursor));
            }
        }

        private void SetSearchPrompt(string s)
        {
            SetPrompt("(reverse-i-search)`" + s + "': ");
        }

        private void ReverseSearch()
        {
            int p;

            if (cursor == text.Length)
            {
                // The cursor is at the end of the string

                p = text.ToString().LastIndexOf(search, StringComparison.Ordinal);
                if (p != -1)
                {
                    matchAt = p;
                    cursor = p;
                    ForceCursor(cursor);
                    return;
                }
            }
            else
            {
                // The cursor is somewhere in the middle of the string
                var start = cursor == matchAt ? cursor - 1 : cursor;
                if (start != -1)
                {
                    p = text.ToString().LastIndexOf(search, start, StringComparison.Ordinal);
                    if (p != -1)
                    {
                        matchAt = p;
                        cursor = p;
                        ForceCursor(cursor);
                        return;
                    }
                }
            }

            // Need to search backwards in history
            HistoryUpdateLine();
            var s = history.SearchBackward(search);
            if (s != null)
            {
                matchAt = -1;
                SetText(s);
                ReverseSearch();
            }
        }

        private void CmdReverseSearch()
        {
            if (searching == 0)
            {
                matchAt = -1;
                lastSearch = search;
                searching = -1;
                search = "";
                SetSearchPrompt("");
            }
            else
            {
                if (search == "")
                {
                    if (!string.IsNullOrEmpty(lastSearch))
                    {
                        search = lastSearch;
                        SetSearchPrompt(search);

                        ReverseSearch();
                    }

                    return;
                }

                ReverseSearch();
            }
        }

        private void SearchAppend(char c)
        {
            search = search + c;
            SetSearchPrompt(search);

            //
            // If the new typed data still matches the current text, stay here
            //
            if (cursor < text.Length)
            {
                var r = text.ToString(cursor, text.Length - cursor);
                if (r.StartsWith(search))
                    return;
            }

            ReverseSearch();
        }

        private void CmdRefresh()
        {
            Console.Clear();
            maxRendered = 0;
            Render();
            ForceCursor(cursor);
        }

        private void InterruptEdit(object sender, ConsoleCancelEventArgs a)
        {
            // Do not abort our program:
            a.Cancel = true;

            // Interrupt the editor
            editThread.Abort();
        }

        //
        // Implements heuristics to show the completion window based on the mode
        //
        private bool HeuristicAutoComplete(bool wasCompleting, char insertedChar)
        {
            if (HeuristicsMode == "csharp")
            {
                // csharp heuristics
                if (wasCompleting)
                {
                    if (insertedChar == ' ')
                        return false;
                    return true;
                }

                // If we were not completing, determine if we want to now
                if (insertedChar == '.')
                {
                    // Avoid completing for numbers "1.2" for example
                    if (cursor > 1 && char.IsDigit(text[cursor - 2]))
                    {
                        for (var p = cursor - 3; p >= 0; p--)
                        {
                            var c = text[p];
                            if (char.IsDigit(c))
                                continue;
                            if (c == '_')
                                return true;
                            if (char.IsLetter(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsControl(c))
                                return true;
                        }

                        return false;
                    }

                    return true;
                }
            }

            return false;
        }

        private void HandleChar(char c)
        {
            if (searching != 0)
            {
                SearchAppend(c);
            }
            else
            {
                var completing = currentCompletion != null;
                HideCompletions();

                InsertChar(c);
                if (HeuristicAutoComplete(completing, c))
                    UpdateCompletionWindow();
            }
        }

        private void EditLoop()
        {
            while (!done)
            {
                ConsoleModifiers mod;

                var cki = Console.ReadKey(true);
                if (cki.Key == ConsoleKey.Escape)
                    if (currentCompletion != null)
                    {
                        HideCompletions();
                        continue;
                    }
                    else
                    {
                        cki = Console.ReadKey(true);

                        mod = ConsoleModifiers.Alt;
                    }
                else
                    mod = cki.Modifiers;

                var handled = false;

                foreach (var handler in handlers)
                {
                    var t = handler.Cki;

                    if (t.Key == cki.Key && t.Modifiers == mod)
                    {
                        handled = true;
                        if (handler.ResetCompletion)
                            HideCompletions();
                        handler.KeyHandler();
                        lastHandler = handler.KeyHandler;
                        break;
                    }

                    if (t.KeyChar == cki.KeyChar && t.Key == ConsoleKey.Zoom)
                    {
                        handled = true;
                        if (handler.ResetCompletion)
                            HideCompletions();

                        handler.KeyHandler();
                        lastHandler = handler.KeyHandler;
                        break;
                    }
                }

                if (handled)
                {
                    if (searching != 0)
                        if (lastHandler != CmdReverseSearch)
                        {
                            searching = 0;
                            SetPrompt(Prompt);
                        }

                    continue;
                }

                if (cki.KeyChar != (char) 0)
                    HandleChar(cki.KeyChar);
            }
        }

        private void InitText(string initial)
        {
            text = new StringBuilder(initial);
            ComputeRendered();
            cursor = text.Length;
            Render();
            ForceCursor(cursor);
        }

        private void SetText(string newtext)
        {
            Console.SetCursorPosition(0, homeRow);
            InitText(newtext);
        }

        private void SetPrompt(string newprompt)
        {
            shownPrompt = newprompt;
            Console.SetCursorPosition(0, homeRow);
            Render();
            ForceCursor(cursor);
        }

        public string Edit(string prompt, string initial)
        {
            editThread = Thread.CurrentThread;
            searching = 0;
            Console.CancelKeyPress += InterruptEdit;

            done = false;
            history.CursorToEnd();
            maxRendered = 0;

            Prompt = prompt;
            shownPrompt = prompt;
            InitText(initial);
            history.Append(initial);

            do
            {
                try
                {
                    EditLoop();
                }
                catch (ThreadAbortException)
                {
                    searching = 0;
                    Thread.ResetAbort();
                    Console.WriteLine();
                    SetPrompt(prompt);
                    SetText("");
                }
            } while (!done);

            Console.WriteLine();

            Console.CancelKeyPress -= InterruptEdit;

            if (text == null)
            {
                history.Close();
                return null;
            }

            var result = text.ToString();
            if (result != "")
                history.Accept(result);
            else
                history.RemoveLast();

            return result;
        }

        public void SaveHistory()
        {
            history?.Close();
        }

        public class Completion
        {
            public string Prefix;
            public string[] Result;

            public Completion(string prefix, string[] result)
            {
                Prefix = prefix;
                Result = result;
            }
        }

        private delegate void KeyHandler();

        private struct Handler
        {
            public readonly ConsoleKeyInfo Cki;
            public readonly KeyHandler KeyHandler;
            public readonly bool ResetCompletion;

            public Handler(ConsoleKey key, KeyHandler h, bool resetCompletion = true)
            {
                Cki = new ConsoleKeyInfo((char) 0, key, false, false, false);
                KeyHandler = h;
                ResetCompletion = resetCompletion;
            }

            private Handler(char c, KeyHandler h, bool resetCompletion = true)
            {
                KeyHandler = h;
                // Use the "Zoom" as a flag that we only have a character.
                Cki = new ConsoleKeyInfo(c, ConsoleKey.Zoom, false, false, false);
                ResetCompletion = resetCompletion;
            }

            private Handler(ConsoleKeyInfo cki, KeyHandler h, bool resetCompletion = true)
            {
                Cki = cki;
                KeyHandler = h;
                ResetCompletion = resetCompletion;
            }

            public static Handler Control(char c, KeyHandler h, bool resetCompletion = true)
            {
                return new Handler((char) (c - 'A' + 1), h, resetCompletion);
            }

            public static Handler Alt(char c, ConsoleKey k, KeyHandler h)
            {
                var cki = new ConsoleKeyInfo(c, k, false, true, false);
                return new Handler(cki, h);
            }
        }

        private class CompletionState
        {
            private readonly int col;
            private readonly int height;
            private readonly int row;
            private readonly int width;
            public string[] Completions;
            public string Prefix;
            private int selectedItem, topItem;

            public CompletionState(int col, int row, int width, int height)
            {
                this.col = col;
                this.row = row;
                this.width = width;
                this.height = height;

                if (this.col < 0)
                    throw new ArgumentException("Cannot be less than zero" + this.col, nameof(col));
                if (this.row < 0)
                    throw new ArgumentException("Cannot be less than zero", nameof(row));
                if (this.width < 1)
                    throw new ArgumentException("Cannot be less than one", nameof(width));
                if (this.height < 1)
                    throw new ArgumentException("Cannot be less than one", nameof(height));
            }

            public string Current => Completions[selectedItem];

            private void DrawSelection()
            {
                for (var r = 0; r < height; r++)
                {
                    var itemIdx = topItem + r;
                    var selected = itemIdx == selectedItem;

                    Console.ForegroundColor = selected ? ConsoleColor.Black : ConsoleColor.Gray;
                    Console.BackgroundColor = selected ? ConsoleColor.Cyan : ConsoleColor.Blue;

                    var item = Prefix + Completions[itemIdx];
                    if (item.Length > width)
                        item = item.Substring(0, width);

                    Console.CursorLeft = col;
                    Console.CursorTop = row + r;
                    Console.Write(item);
                    for (var space = item.Length; space <= width; space++)
                        Console.Write(" ");
                }
            }

            public void Show()
            {
                SaveExcursion(DrawSelection);
            }

            public void SelectNext()
            {
                if (selectedItem + 1 < Completions.Length)
                {
                    selectedItem++;
                    if (selectedItem - topItem >= height)
                        topItem++;
                    SaveExcursion(DrawSelection);
                }
            }

            public void SelectPrevious()
            {
                if (selectedItem > 0)
                {
                    selectedItem--;
                    if (selectedItem < topItem)
                        topItem = selectedItem;
                    SaveExcursion(DrawSelection);
                }
            }

            private void Clear()
            {
                for (var r = 0; r < height; r++)
                {
                    Console.CursorLeft = col;
                    Console.CursorTop = row + r;
                    for (var space = 0; space <= width; space++)
                        Console.Write(" ");
                }
            }

            public void Remove()
            {
                SaveExcursion(Clear);
            }
        }

        //
        // Emulates the bash-like behavior, where edits done to the
        // history are recorded
        //
        private class History
        {
            private readonly string histfile;
            private readonly string[] history;
            private int cursor, count;
            private int head, tail;

            public History(string app, int size)
            {
                if (size < 1)
                    throw new ArgumentException("size");

                if (app != null)
                {
                    var dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    //Console.WriteLine (dir);
                    if (!Directory.Exists(dir))
                        try
                        {
                            Directory.CreateDirectory(dir);
                        }
                        catch
                        {
                            app = null;
                        }

                    if (app != null)
                        histfile = Path.Combine(dir, app) + ".history";
                }

                history = new string[size];
                head = tail = cursor = 0;

                if (File.Exists(histfile))
                {
                    using var sr = File.OpenText(histfile);
                    string line;

                    while ((line = sr.ReadLine()) != null)
                        if (line != "")
                            Append(line);
                }
            }

            public void Close()
            {
                if (histfile == null)
                    return;

                try
                {
                    using var sw = File.CreateText(histfile);
                    var start = count == history.Length ? head : tail;
                    for (var i = start; i < start + count; i++)
                    {
                        var p = i % history.Length;
                        sw.WriteLine(history[p]);
                    }
                }
                catch
                {
                    // ignore
                }
            }

            //
            // Appends a value to the history
            //
            public void Append(string s)
            {
                //Console.WriteLine ("APPENDING {0} head={1} tail={2}", s, head, tail);
                history[head] = s;
                head = (head + 1) % history.Length;
                if (head == tail)
                    tail = tail + 1 % history.Length;
                if (count != history.Length)
                    count++;
                //Console.WriteLine ("DONE: head={1} tail={2}", s, head, tail);
            }

            //
            // Updates the current cursor location with the string,
            // to support editing of history items.   For the current
            // line to participate, an Append must be done before.
            //
            public void Update(string s)
            {
                history[cursor] = s;
            }

            public void RemoveLast()
            {
                head = head - 1;
                if (head < 0)
                    head = history.Length - 1;
            }

            public void Accept(string s)
            {
                var t = head - 1;
                if (t < 0)
                    t = history.Length - 1;

                history[t] = s;
            }

            public bool PreviousAvailable()
            {
                //Console.WriteLine ("h={0} t={1} cursor={2}", head, tail, cursor);
                if (count == 0)
                    return false;
                var next = cursor - 1;
                if (next < 0)
                    next = count - 1;

                if (next == head)
                    return false;

                return true;
            }

            public bool NextAvailable()
            {
                if (count == 0)
                    return false;
                var next = (cursor + 1) % history.Length;
                if (next == head)
                    return false;
                return true;
            }


            //
            // Returns: a string with the previous line contents, or
            // nul if there is no data in the history to move to.
            //
            public string Previous()
            {
                if (!PreviousAvailable())
                    return null;

                cursor--;
                if (cursor < 0)
                    cursor = history.Length - 1;

                return history[cursor];
            }

            public string Next()
            {
                if (!NextAvailable())
                    return null;

                cursor = (cursor + 1) % history.Length;
                return history[cursor];
            }

            public void CursorToEnd()
            {
                if (head == tail)
                    return;

                cursor = head;
            }

            public string SearchBackward(string term)
            {
                for (var i = 0; i < count; i++)
                {
                    var slot = cursor - i - 1;
                    if (slot < 0)
                        slot = history.Length + slot;
                    if (slot >= history.Length)
                        slot = 0;
                    if (history[slot] != null && history[slot].IndexOf(term, StringComparison.Ordinal) != -1)
                    {
                        cursor = slot;
                        return history[slot];
                    }
                }

                return null;
            }
        }
    }

#if DEMO
	class Demo {
		static void Main ()
		{
			LineEditor le = new LineEditor ("foo") {
				HeuristicsMode = "csharp"
			};
			le.AutoCompleteEvent += delegate (string a, int pos){
				string prefix = "";
				var completions = new string [] { "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten" };
				return new Mono.Terminal.LineEditor.Completion (prefix, completions);
			};
			
			string s;
			
			while ((s = le.Edit ("shell> ", "")) != null){
				Console.WriteLine ("----> [{0}]", s);
			}
		}
	}
#endif
}