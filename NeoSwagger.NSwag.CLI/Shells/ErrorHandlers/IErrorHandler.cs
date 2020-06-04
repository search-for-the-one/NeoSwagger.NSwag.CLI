using System;

namespace NeoSwagger.NSwag.CLI.Shells.ErrorHandlers
{
    internal interface IErrorHandler
    {
        void HandleError(string message, Exception innerException = null);
    }
}