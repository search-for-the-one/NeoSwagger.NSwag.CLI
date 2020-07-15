using System;
using NeoSwagger.NSwag.CLI.Exceptions;

namespace NeoSwagger.NSwag.CLI.Shells.ErrorHandlers
{
    internal class ThrowShellExceptionErrorHandler : IErrorHandler
    {
        public void HandleError(string message, Exception innerException)
        {
            throw new ShellException(message);
        }
    }
}