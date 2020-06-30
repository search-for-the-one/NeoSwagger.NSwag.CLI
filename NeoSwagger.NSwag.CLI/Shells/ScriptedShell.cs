using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NeoSwagger.NSwag.CLI.Exceptions;
using NeoSwagger.NSwag.CLI.Parsers;
using NeoSwagger.NSwag.CLI.Shells.ErrorHandlers;

namespace NeoSwagger.NSwag.CLI.Shells
{
    internal class ScriptedShell : Shell
    {
        private readonly string[] newLine = {Environment.NewLine};
        private readonly TextReader reader;

        public ScriptedShell(IConsoleHost consoleHost, ICommandParser commandParser, IVariables variables, ICommandProcessor commandProcessor, TextReader reader)
            : base(consoleHost, commandParser, commandProcessor, variables, new ThrowShellExceptionErrorHandler())
        {
            this.reader = reader;
        }

        public override async Task Run()
        {
            try
            {
                var lines = (await reader.ReadToEndAsync()).Split(newLine, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines.Select(l => l.Trim()))
                {
                    await Handle(line);
                }
            }
            catch (Exception ex)
            {
                throw new ShellException(ex.Message, ex);
            }
        }
    }
}