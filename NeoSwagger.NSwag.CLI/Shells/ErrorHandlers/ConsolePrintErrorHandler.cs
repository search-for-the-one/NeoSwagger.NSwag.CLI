using System;

namespace NeoSwagger.NSwag.CLI.Shells.ErrorHandlers
{
    internal class ConsolePrintErrorHandler : IErrorHandler
    {
        private readonly IConsoleHost consoleHost;

        public ConsolePrintErrorHandler(IConsoleHost consoleHost)
        {
            this.consoleHost = consoleHost;
        }

        public void HandleError(string message, Exception innerException)
        {
            consoleHost.WriteLine(message);
        }
    }
}