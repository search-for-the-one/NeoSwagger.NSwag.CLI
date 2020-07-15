using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using CommandLine;
using NeoSwagger.NSwag.CLI.Compilers;
using NeoSwagger.NSwag.CLI.Exceptions;
using NeoSwagger.NSwag.CLI.Parsers;
using NeoSwagger.NSwag.CLI.Shells;
using NeoSwagger.NSwag.CLI.Shells.ConsoleHosts;

namespace NeoSwagger.NSwag.CLI
{
    public class CommandLineInterface
    {
        private IConsoleHost ConsoleHost { get; set; } = new SystemConsoleHost();
        
        private void PrintException(Exception ex)
        {
            switch (ex)
            {
                case FileNotFoundException fileNotFound:
                    ConsoleHost.WriteLine($"Error: {fileNotFound.Message} - '{fileNotFound.FileName}'");
                    break;
                case ShellException shell:
                    ConsoleHost.WriteLine($"{shell.Message}");
                    var exception = shell.InnerException?.InnerException;
                    if (exception != null)
                        ConsoleHost.WriteLine(exception.Message);
                    break;
                case HttpRequestException request:
                    ConsoleHost.WriteLine(request.Message);
                    break;
                default:
                    ConsoleHost.WriteLine(ex.ToString());
                    break;
            }
        }

        public int Run(IEnumerable<string> args)
        {
            try
            {
                RunInternal(args);
                return 0;
            }
            catch
            {
                return -1;
            }
        }

        private void RunInternal(IEnumerable<string> args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(options =>
                {
                    try
                    {
                        try
                        {
                            Run(options).Wait();
                        }
                        catch (AggregateException ex)
                        {
                            ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw();
                            throw;
                        }
                    }
                    catch (Exception ex)
                    {
                        ConsoleHost.WriteLine();
                        PrintException(ex);
                        throw;
                    }
                })
                .WithNotParsed(HandleParseError);
        }

        private void HandleParseError(IEnumerable<Error> errs)
        {
            foreach (var err in errs)
                ConsoleHost.WriteLine(err.ToString());
        }

        private async Task Run(Options options)
        {
            var file = !string.IsNullOrEmpty(options.ScriptFile)
                ? LoadScriptFile(options.ScriptFile)
                : null;

            ConsoleHost = new SystemConsoleHost {PrintTextMaxChars = file == null ? options.PrintMaxTextLength : int.MaxValue};
            
            var verbose = file == null || options.VerboseMode;

            var baseUrl = new Uri(options.SwaggerEndpoint).GetLeftPart(UriPartial.Authority);

            if (verbose)
                ConsoleHost.Write($"Loading from swagger endpoint '{options.SwaggerEndpoint}'...");

            var commandParser = new CommandParser();
            var variables = new InMemoryVariables();

            var watch = Stopwatch.StartNew();
            using var client = new HttpClient {BaseAddress = new Uri(baseUrl)};
            var (_, classes) = await new CreateAssemblyFromSwagger(new CSharpCompiler(), new NSwagCodeGenerator(options.SwaggerEndpoint), ConsoleHost)
                .CreateAssembly();

            if (verbose)
                ConsoleHost.WriteLine($" Done ({watch.Elapsed.TotalMilliseconds:N0}ms).");
            
            ConsoleHost.WriteLine();

            var commandProcessor = new CommandProcessor(commandParser, variables, classes, client);
            if (file != null)
            {
                if (verbose)
                    ConsoleHost.Write("Running script... ");
                
                var consoleHost = options.PrintScriptResponses
                    ? ConsoleHost
                    : new NullConsoleHost();
                await new ScriptedShell(consoleHost, commandParser, variables, commandProcessor, file).Run();
                
                if (verbose)
                    ConsoleHost.WriteLine(" Done.");
                
                ConsoleHost.WriteLine();
            }

            if (!options.RunInteractiveShell)
                await new InteractiveShell(ConsoleHost, commandParser, variables, commandProcessor).Run();
        }

        private static StreamReader LoadScriptFile(string scriptFile)
        {
            return File.Exists(scriptFile)
                ? File.OpenText(scriptFile)
                : throw new FileNotFoundException("Script file not found", scriptFile);
        }
    }
}