using System;
using System.Collections.Generic;
using System.IO;

namespace NeoSwagger.NSwag.CLI.Shells
{
    public class Response : IDisposable
    {
        public Response(int statusCode, Dictionary<string, IEnumerable<string>> headers, Stream stream)
        {
            StatusCode = statusCode;
            Headers = headers;
            Stream = stream;
        }

        public int StatusCode { get; }
        public Dictionary<string, IEnumerable<string>> Headers { get; }
        public Stream Stream { get; }

        public void Dispose()
        {
            Stream?.Dispose();
        }
    }
}