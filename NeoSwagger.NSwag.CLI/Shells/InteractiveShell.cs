using System.Threading.Tasks;
using NeoSwagger.NSwag.CLI.Exceptions;
using NeoSwagger.NSwag.CLI.Parsers;
using NeoSwagger.NSwag.CLI.Shells.ErrorHandlers;

namespace NeoSwagger.NSwag.CLI.Shells
{
    internal class InteractiveShell : Shell
    {
        private readonly IConsoleHost consoleHost;

        public InteractiveShell(IConsoleHost consoleHost, ICommandParser commandParser, IVariables variables, ICommandProcessor commandProcessor)
            : base(consoleHost, commandParser, commandProcessor, variables, new ConsolePrintErrorHandler(consoleHost))
        {
            this.consoleHost = consoleHost;
        }

        public override async Task Run()
        {
            while (true)
            {
                var line = consoleHost.ReadLine("$ ").Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    if (Exit(line))
                        break;

                    await Handle(line);
                }
                catch (ParserException ex)
                {
                    consoleHost.WriteLine(ex.Message);
                }
            }
        }
    }
}