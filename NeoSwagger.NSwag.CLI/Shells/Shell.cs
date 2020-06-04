﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NeoSwagger.NSwag.CLI.Exceptions;
using NeoSwagger.NSwag.CLI.Parsers;
using NeoSwagger.NSwag.CLI.Shells.ErrorHandlers;

namespace NeoSwagger.NSwag.CLI.Shells
{
    using Parameters = IReadOnlyList<(ParamValueType ParamValueType, string Name, string Value)>;

    internal abstract class Shell : IShell
    {
        private const int PrintTextMaxChars = 1200;

        private const string LastResponseVar = "LastResponse";
        private const string LastResponseUriVar = "LastResponseUri";
        private const string LastResponseStatusCodeVar = "LastResponseStatusCode";

        private const string GetVerb = "get";
        private const string SetVerb = "set";
        private const string DebugVerb = "debug";
        private const string DownloadDirVerb = "downloaddir";
        private const string PrintVarsVerb = "vars";
        private const string VarVerb = "var";

        private static readonly Regex TokenSplitter = new Regex("\\s");
        private static readonly string[] ExitVerbs = {"exit", "quit", "bye", "q"};

        private readonly ICommandParser commandParser;
        private readonly ICommandProcessor commandProcessor;
        private readonly IConsoleHost consoleHost;
        private readonly IErrorHandler errorHandler;
        private readonly IVariables variables;

        private bool debugEnabled;
        private string downloadDir = Environment.GetFolderPath(Environment.SpecialFolder.Personal);

        protected Shell(IConsoleHost consoleHost, ICommandParser commandParser, ICommandProcessor commandProcessor, IVariables variables, IErrorHandler errorHandler)
        {
            this.consoleHost = consoleHost;
            this.commandParser = commandParser;
            this.variables = variables;
            this.errorHandler = errorHandler;
            this.commandProcessor = commandProcessor;
            ClearVars();
        }

        public abstract Task Run();

        protected async Task Handle(string line)
        {
            if (PrintVars(line) ||
                Help(line, commandProcessor) ||
                GetOrSet(line) ||
                string.IsNullOrWhiteSpace(line))
                return;

            await Execute(commandProcessor, line);
        }

        protected static bool Exit(string line)
        {
            return ExitVerbs.Any(v => string.Equals(line, v, StringComparison.Ordinal));
        }

        private bool GetOrSet(string line)
        {
            if (!GetOrSet(line, out var set)) return false;

            try
            {
                commandParser.Parse(line, out _, out var verb, out var parameters);
                if (!Debug(set, verb, parameters) &&
                    !DownloadDir(set, verb, parameters) &&
                    !Var(set, verb, parameters))
                    errorHandler.HandleError("Unknown command");
                consoleHost.WriteLine();
                return true;
            }
            catch (ParserException)
            {
                return false;
            }
        }

        private bool PrintVars(string line)
        {
            if (!string.Equals(PrintVarsVerb, line)) return false;
            consoleHost.WriteLine("Vars:");
            foreach (var v in variables)
                consoleHost.WriteLine($"  ${v.Key}");
            return true;
        }

        private bool Help(string line, ICommandProcessor proc)
        {
            if (!Help(line, out var service)) return false;

            if (string.Equals(service, "shell", StringComparison.Ordinal))
            {
                PrintShellHelp();
                return true;
            }

            consoleHost.WriteLine(proc.GetHelp(service));

            if (!string.IsNullOrEmpty(service)) return true;

            consoleHost.WriteLine("'help shell' to print help for this shell");
            consoleHost.WriteLine();

            return true;
        }

        private async Task Execute(ICommandProcessor proc, string line)
        {
            try
            {
                await HandleResponseAsync(await proc.Execute(line));
            }
            catch (ParserException e)
            {
                errorHandler.HandleError($"Parse error: {e.Message}", e);
            }
            catch (InvalidOperationException e)
            {
                errorHandler.HandleError($"Invalid operation: {e.Message}", e);
            }
            catch (Exception e)
            {
                errorHandler.HandleError($"Error: {e.Message}", e);
            }

            consoleHost.WriteLine();
        }

        private async Task HandleResponseAsync(Response response)
        {
            ClearVars();

            var statusCode = response.StatusCode.ToString(CultureInfo.InvariantCulture);
            variables[LastResponseStatusCodeVar] = statusCode;

            var responseText = GetString(response.Stream);

            var isError = IsError(response);
            if (debugEnabled || isError)
            {
                if (isError)
                    errorHandler.HandleError($"Error: {statusCode}", new WebException(responseText));
                else
                    consoleHost.WriteLine($"Status code: {statusCode}");
            }

            if (debugEnabled && response.Headers.Any())
            {
                consoleHost.WriteLine("Headers:");
                foreach (var header in response.Headers)
                    consoleHost.WriteLine($"  {header.Key} = {string.Join(", ", header.Value)}");
            }

            if (response.Headers.TryGetValue("Content-Type", out var values))
            {
                var contentType = values.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(contentType))
                {
                    var c = contentType.Split(';', StringSplitOptions.RemoveEmptyEntries).First().Trim();
                    if (!c.StartsWith("application/json"))
                    {
                        await SaveResponseToFile(c, response.Stream);
                        return;
                    }
                }
            }

            variables[LastResponseVar] = responseText;
            if (!string.IsNullOrWhiteSpace(responseText))
                consoleHost.WriteLine(Shorten(responseText));
        }

        private static string Shorten(string text)
        {
            var s = text.Substring(0, Math.Min(PrintTextMaxChars, text.Length));
            if (s.Length != text.Length)
                s = s + " ...";
            return s;
        }

        private void ClearVars()
        {
            variables[LastResponseVar] = string.Empty;
            variables[LastResponseUriVar] = string.Empty;
            variables[LastResponseStatusCodeVar] = string.Empty;
        }

        private static bool IsError(Response response)
        {
            return response.StatusCode != 200 && response.StatusCode != 206;
        }

        private async Task SaveResponseToFile(string contentType, Stream stream)
        {
            var filename = Path.Combine(downloadDir, $"{Guid.NewGuid():N}.{GetExtension()}");
            await using (var fileStream = new FileStream(filename, FileMode.Create))
            {
                await stream.CopyToAsync(fileStream);
            }

            var uri = GetUri(filename);
            consoleHost.WriteLine($"Response saved as '{uri}'");
            variables[LastResponseUriVar] = uri;

            string GetExtension()
            {
                return Constants.MimeMapping.ReverseTypeMap.TryGetValue(contentType.Trim().ToLowerInvariant(), out var extensions)
                    ? extensions.First()
                    : contentType.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(1).Single();
            }
        }

        private static string GetUri(string filename)
        {
            return new Uri(filename.Replace('%', '?')).AbsoluteUri.Replace("%3F", "%25");
        }

        private static string GetString(Stream stream)
        {
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private void PrintShellHelp()
        {
            consoleHost.WriteLine("Shell help:");
            consoleHost.WriteLine("  get/set debug on/off       - Turn on/off debug mode");
            consoleHost.WriteLine("  get/set downloaddir <path> - Set download dir");
            consoleHost.WriteLine("  get/set var <name>=<value> - Set variable");
            consoleHost.WriteLine("  set var <name>             - Undefine variable");
            consoleHost.WriteLine("  vars                       - List all defined variables");
            consoleHost.WriteLine("  help                       - Help");
            consoleHost.WriteLine("  q/quit/exit/bye            - Quit");
            consoleHost.WriteLine();
        }

        private static bool Help(string line, out string service)
        {
            service = string.Empty;

            var tokens = SplitTokens(line);
            if (!string.Equals(tokens.First(), "help", StringComparison.Ordinal))
                return false;

            if (tokens.Count > 1)
                service = tokens.Skip(1).First();

            return true;
        }

        private static bool GetOrSet(string line, out bool set)
        {
            var tokens = SplitTokens(line);
            set = string.Equals(tokens.First(), SetVerb, StringComparison.Ordinal);
            return set || string.Equals(tokens.First(), GetVerb, StringComparison.Ordinal);
        }

        private static bool HandleGetOrSet(bool set, string expectedVerb, string verb, Func<bool> getFunc, Func<bool> setFunc, Action thenFunc = null)
        {
            if (!string.Equals(verb, expectedVerb, StringComparison.Ordinal))
                return false;

            var result = set ? setFunc() : getFunc();
            thenFunc?.Invoke();
            return result;
        }

        private bool Debug(bool set, string verb, Parameters parameters)
        {
            return HandleGetOrSet(set, DebugVerb, verb,
                () => !parameters.Any(),
                () =>
                {
                    if (parameters.Count != 1) return false;
                    var p1 = parameters.Single();
                    if (!string.IsNullOrEmpty(p1.Name)) return false;
                    if (string.Equals(p1.Value, "on", StringComparison.Ordinal))
                    {
                        debugEnabled = true;
                        return true;
                    }

                    if (string.Equals(p1.Value, "off", StringComparison.Ordinal))
                    {
                        debugEnabled = false;
                        return true;
                    }

                    return false;
                },
                () => consoleHost.WriteLine(debugEnabled ? "Debug: On" : "Debug: Off"));
        }

        private bool DownloadDir(bool set, string verb, Parameters parameters)
        {
            return HandleGetOrSet(set, DownloadDirVerb, verb,
                () => !parameters.Any(),
                () =>
                {
                    if (parameters.Count != 1) return false;
                    var p = parameters.Single();
                    if (!string.IsNullOrEmpty(p.Name)) return false;
                    downloadDir = p.Value;
                    return true;
                },
                () => consoleHost.WriteLine($"Download dir: '{downloadDir}'"));
        }

        private bool Var(bool set, string verb, Parameters parameters)
        {
            if (parameters.Count != 1) return false;
            var p = parameters.Single();

            return HandleGetOrSet(set, VarVerb, verb,
                () =>
                {
                    if (!string.IsNullOrEmpty(p.Name)) return false;
                    if (!variables.TryGetValue(p.Value, out var v))
                        PrintVarUndefined(p.Value);
                    else
                        consoleHost.WriteLine($"var: ${p.Value} = {Shorten(v)}");
                    return true;
                },
                () =>
                {
                    if (string.IsNullOrEmpty(p.Name))
                    {
                        if (!variables.Remove(p.Value))
                            PrintVarUndefined(p.Value);
                        return true;
                    }

                    var v = p.Value;
                    if (p.ParamValueType == ParamValueType.Var && !variables.TryGetValue(p.Value, out v))
                    {
                        PrintVarUndefined(p.Value);
                        return true;
                    }

                    variables[p.Name] = v;
                    consoleHost.WriteLine($"var ${p.Name} = {Shorten(v)}");
                    return true;
                });

            void PrintVarUndefined(string name)
            {
                errorHandler.HandleError($"${name} is undefined");
            }
        }

        private static List<string> SplitTokens(string line)
        {
            return TokenSplitter.Split(line).Where(s => !string.IsNullOrEmpty(s)).ToList();
        }
    }
}