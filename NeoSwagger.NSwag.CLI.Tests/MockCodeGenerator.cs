using System.Threading.Tasks;
using NeoSwagger.NSwag.CLI.Constants;

namespace NeoSwagger.NSwag.CLI.Tests
{
    public class MockCodeGenerator : ISwaggerCodeGenerator
    {
        public Task<string> Generate()
        {
            return Task.FromResult($"namespace {CodeGenerationConstants.Namespace} {{ {GetMockSwaggerCode()} }}");
        }

        private static string GetMockSwaggerCode()
        {
            return @"
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously

    public partial class FileParameter
    {
        public System.IO.Stream Data { get; }

        public string FileName { get; }

        public FileParameter(System.IO.Stream data)
            : this(data, null)
        {
        }

        public FileParameter(System.IO.Stream data, string fileName)
        {
            Data = data;
            FileName = fileName;
        }
    }

    public class FileResponse : System.IDisposable
    {
        private readonly System.IDisposable client;
        private readonly System.IDisposable response;

        public int StatusCode { get; }

        public System.Collections.Generic.Dictionary<string, System.Collections.Generic.IEnumerable<string>> Headers { get; }

        public System.IO.Stream Stream { get; }

        public bool IsPartial => StatusCode == 206;

        public FileResponse(int statusCode, System.Collections.Generic.Dictionary<string, System.Collections.Generic.IEnumerable<string>> headers, System.IO.Stream stream, System.IDisposable client, System.IDisposable response)
        {
            StatusCode = statusCode;
            Headers = headers;
            Stream = stream;
            this.client = client;
            this.response = response;
        }

        public void Dispose()
        {
            Stream?.Dispose();
            response?.Dispose();
            client?.Dispose();
        }
    }
    
    public class SwaggerException : System.Exception
    {
        public int StatusCode { get; }

        public string Response { get; }

        public System.Collections.Generic.Dictionary<string, System.Collections.Generic.IEnumerable<string>> Headers { get; }

        public SwaggerException(string message, int statusCode, string response, System.Collections.Generic.Dictionary<string, System.Collections.Generic.IEnumerable<string>> headers, System.Exception innerException)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            Response = response;
            Headers = headers;
        }

        public override string ToString()
        {
            return string.Format(""HTTP Response: \n\n{0}\n\n{1}"", Response, base.ToString());
        }
    }

    public class SwaggerException<TResult> : SwaggerException
    {
        public TResult Result { get; }

        public SwaggerException(string message, int statusCode, string response, System.Collections.Generic.Dictionary<string, System.Collections.Generic.IEnumerable<string>> headers, TResult result, System.Exception innerException)
            : base(message, statusCode, response, headers, innerException)
        {
            Result = result;
        }
    }
";
        }
    }
}