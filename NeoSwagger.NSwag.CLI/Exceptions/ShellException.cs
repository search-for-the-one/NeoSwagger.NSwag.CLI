using System;
using System.Runtime.Serialization;

namespace NeoSwagger.NSwag.CLI.Exceptions
{
    internal class ShellException : Exception
    {
        public ShellException()
        {
        }

        public ShellException(string message) : base(message)
        {
        }

        public ShellException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected ShellException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}