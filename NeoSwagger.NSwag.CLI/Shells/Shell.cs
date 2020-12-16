using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
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
        private const string LastResponseVar = "LastResponse";
        private const string LastResponseUriVar = "LastResponseUri";
        private const string LastResponseStatusCodeVar = "LastResponseStatusCode";

        private const string GetVerb = "get";
        private const string SetVerb = "set";
        private const string DebugVerb = "debug";
        private const string DownloadDirVerb = "downloaddir";
        private const string PrintVarsVerb = "vars";
        private const string VarVerb = "var";

        private static readonly char[] Semicolon = {';'};
        private static readonly char[] Slash = {'/'};
        private static readonly Regex TokenSplitter = new("\\s");
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
            if (!GetOrSet(line, out var set))
                return false;

            try
            {
                commandParser.Parse(line, out _, out var verb, out var parameters);
                if (!Debug(set, verb, parameters) &&
                    !DownloadDir(set, verb, parameters) &&
                    !Var(set, verb, parameters))
                    errorHandler.HandleError("Error: Unknown command");
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
            if (!string.Equals(PrintVarsVerb, line))
                return false;
            
            consoleHost.WriteLine("Vars:");
            foreach (var v in variables)
                consoleHost.WriteLine($"  ${v.Key}");
            
            return true;
        }

        private bool Help(string line, ICommandProcessor proc)
        {
            if (!Help(line, out var service))
                return false;

            if (string.Equals(service, "shell", StringComparison.Ordinal))
            {
                PrintShellHelp();
                return true;
            }

            consoleHost.WriteLine(proc.GetHelp(service));

            if (!string.IsNullOrEmpty(service))
                return true;

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
            catch (ShellException e)
            {
                errorHandler.HandleError(e.Message, e);
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

            if (response == null)
                return;

            var statusCode = response.StatusCode.ToString(CultureInfo.InvariantCulture);
            variables[LastResponseStatusCodeVar] = statusCode;

            var responseText = GetString(response);

            var isError = IsError(response);
            if (debugEnabled)
            {
                consoleHost.WriteLine(isError
                    ? $"Error: {GetErrorMessage(statusCode)}"
                    : $"Status code: {GetHttpStatusCodeString(statusCode)}");
            }

            if (debugEnabled && response.Headers.Any())
            {
                consoleHost.WriteLine("Headers:");
                foreach (var kvp in response.Headers)
                    consoleHost.WriteLine($"  {kvp.Key} = {string.Join(", ", kvp.Value)}");
            }

            if (debugEnabled || !isError)
            {
                if (await SaveResponseToFile(response))
                    return;
            }

            variables[LastResponseVar] = responseText;
            if (!string.IsNullOrWhiteSpace(responseText))
                consoleHost.WriteLine(Shorten(responseText));
            
            if (isError)
                errorHandler.HandleError($"Error: {GetErrorMessage(statusCode)}");
        }

        private static bool IsTextBasedMimeType(Response response)
        {
            return IsMimeType(response, "application/json", "application/ld+json", "text");
        }

        private static string GetErrorMessage(string statusCode)
        {
            return statusCode switch
            {
                "404" => $"{GetHttpStatusCodeString(statusCode)} (hint: incorrect number of parameters?)",
                _ => GetHttpStatusCodeString(statusCode)
            };
        }

        private static string GetHttpStatusCodeString(string statusCode)
        {
            return Enum.TryParse<HttpStatusCode>(statusCode, out var result) ? $"{statusCode} {result}" : statusCode;
        }

        private string Shorten(string text)
        {
            var s = text.Substring(0, Math.Min(consoleHost.PrintTextMaxChars, text.Length));
            if (s.Length != text.Length)
                s += " ...";
            return s;
        }

        private void ClearVars()
        {
            variables[LastResponseVar] = string.Empty;
            variables[LastResponseUriVar] = string.Empty;
            variables[LastResponseStatusCodeVar] = string.Empty;
        }

        private static bool IsError(Response response) => response.StatusCode >= 400;

        private async Task<bool> SaveResponseToFile(Response response)
        {
            if (!TryGetMimeType(response, out var mimeType))
                return false;

            var stream = response.Stream;
            if (stream.Position == stream.Length)
                return false;
            
            var filename = Path.Combine(downloadDir, $"{Guid.NewGuid():N}.{GetExtension()}");
            using (var fileStream = new FileStream(filename, FileMode.Create))
            {
                await stream.CopyToAsync(fileStream);
            }

            var uri = GetUri(filename);
            consoleHost.WriteLine($"Response saved as '{uri}'");
            variables[LastResponseUriVar] = uri;

            return true;

            string GetExtension()
            {
                return Constants.MimeMapping.ReverseTypeMap.TryGetValue(mimeType.Trim().ToLowerInvariant(), out var extensions)
                    ? extensions.First()
                    : mimeType.Split(Slash, StringSplitOptions.RemoveEmptyEntries).Skip(1).Single();
            }
        }

        private static string GetUri(string filename)
        {
            return new Uri(filename.Replace('%', '?')).AbsoluteUri.Replace("%3F", "%25");
        }

        private static bool TryGetMimeType(Response response, out string mimeType)
        {
            mimeType = null;
            if (!response.Headers.TryGetValue("Content-Type", out var values)) 
                return false;
            
            var contentType = values.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(contentType)) 
                return false;
            
            mimeType = contentType.Split(Semicolon, StringSplitOptions.RemoveEmptyEntries).First().Trim();
            return true;
        }

        private static bool IsMimeType(Response response, params string[] mimeTypes)
        {
            return TryGetMimeType(response, out var mimeType) && mimeTypes.Any(m => mimeType.StartsWith(m));
        }

        private static string GetString(Response response)
        {
            if (!IsTextBasedMimeType(response))
                return string.Empty;
            
            var stream = response.Stream;
            using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true, bufferSize: -1);
            return reader.ReadToEnd();

        }

        private void PrintShellHelp()
        {
            consoleHost.WriteLine("Shell help:");
            consoleHost.WriteLine("  get/set debug on/off       - Turn on/off debug mode");
            consoleHost.WriteLine("  get/set downloaddir <path> - Get or set download dir");
            consoleHost.WriteLine("  get/set var <name>=<value> - Get or set variable");
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
                    if (parameters.Count != 1)
                        return false;
                    
                    var p1 = parameters.Single();
                    if (!string.IsNullOrEmpty(p1.Name))
                        return false;
                    
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
                    if (parameters.Count != 1)
                        return false;
                    
                    var p = parameters.Single();
                    if (!string.IsNullOrEmpty(p.Name))
                        return false;
                    
                    downloadDir = p.Value;
                    return true;
                },
                () => consoleHost.WriteLine($"Download dir: '{downloadDir}'"));
        }

        private bool Var(bool set, string verb, Parameters parameters)
        {
            if (parameters.Count != 1)
                return false;
            
            var p = parameters.Single();

            return HandleGetOrSet(set, VarVerb, verb,
                () =>
                {
                    if (!string.IsNullOrEmpty(p.Name))
                        return false;
                    
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

            void PrintVarUndefined(string name) => errorHandler.HandleError($"Error: ${name} is undefined");
        }

        private static List<string> SplitTokens(string line)
        {
            return TokenSplitter.Split(line).Where(s => !string.IsNullOrEmpty(s)).ToList();
        }
    }
}