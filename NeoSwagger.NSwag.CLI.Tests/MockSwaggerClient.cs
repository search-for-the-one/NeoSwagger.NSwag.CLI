using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace NeoSwagger.NSwag.CLI.Tests
{
    public interface IWebClient
    {
    }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public class TestClient : IWebClient
    {
        public HttpClient Client { get; }

        public TestClient(HttpClient client)
        {
            Client = client;
        }

        public string BaseUrl { get; set; }

        public Task<FileResponse> SignUpAsync(string email, string password)
        {
            return SignUpAsync(email, password, CancellationToken.None);
        }

        public async Task<FileResponse> SignUpAsync(string email, string password, CancellationToken cancellationToken)
        {
            return FileResponse.Create(email, password);
        }

        public Task<FileResponse> ChangePasswordAsync(ChangePassword changePassword)
        {
            return ChangePasswordAsync(changePassword, CancellationToken.None);
        }

        public async Task<FileResponse> ChangePasswordAsync(ChangePassword changePassword, CancellationToken cancellationToken)
        {
            return FileResponse.Create(changePassword.AccessToken, changePassword.CurrentPassword, changePassword.ProposedPassword);
        }

        public Task<FileResponse> ChangePasswordNestedAsync(Nested nested)
        {
            return ChangePasswordNestedAsync(nested, CancellationToken.None);
        }

        public async Task<FileResponse> ChangePasswordNestedAsync(Nested nested, CancellationToken cancellationToken)
        {
            return FileResponse.Create(nested.ChangePassword.AccessToken, nested.ChangePassword.CurrentPassword, nested.ChangePassword.ProposedPassword);
        }

        public Task<FileResponse> CreateAsync(string name, FileParameter file)
        {
            return CreateAsync(name, file, CancellationToken.None);
        }

        public async Task<FileResponse> CreateAsync(string name, FileParameter file, CancellationToken cancellationToken)
        {
            return FileResponse.Create(name, Encoding.UTF8.GetString(((MemoryStream)file.Data).ToArray()));
        }

        public Task<FileResponse> UseSomeNumbersAsync(long l, int i, ushort us, double d, float f)
        {
            return UseSomeNumbersAsync(l, i, us, d, f, CancellationToken.None);
        }

        public async Task<FileResponse> UseSomeNumbersAsync(long l, int i, ushort us, double d, float f, CancellationToken cancellationToken)
        {
            return FileResponse.Create(l, i, us, d, f);
        }

        public class ChangePassword
        {
            public string AccessToken { get; set; }
            public string CurrentPassword { get; set; }
            public string ProposedPassword { get; set; }
        }

        public class Nested
        {
            public ChangePassword ChangePassword { get; set; }
        }
    }
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously

    [GeneratedCode("NSwag", "11.12.13.0 (NJsonSchema v9.10.14.0 (Newtonsoft.Json v11.0.0.0))")]
    public class FileParameter
    {
        public Stream Data { get; }

        public string FileName { get; }

        public FileParameter(Stream data)
            : this(data, null)
        {
        }

        public FileParameter(Stream data, string fileName)
        {
            Data = data;
            FileName = fileName;
        }
    }

    public class FileResponse : IDisposable
    {
        private readonly IDisposable client;
        private readonly IDisposable response;

        public int StatusCode { get; }

        public Dictionary<string, IEnumerable<string>> Headers { get; }

        public Stream Stream { get; }

        public bool IsPartial => StatusCode == 206;

        public FileResponse(int statusCode, Dictionary<string, IEnumerable<string>> headers, Stream stream, IDisposable client, IDisposable response)
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

        public static FileResponse Create(params object[] parameters)
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(parameters.Select(o => (Value: o, Type: o.GetType())).ToArray())));
            return new FileResponse(200, new Dictionary<string, IEnumerable<string>>(), stream, null, null);
        }
    }

    [GeneratedCode("NSwag", "11.12.13.0 (NJsonSchema v9.10.14.0 (Newtonsoft.Json v11.0.0.0))")]
    public class SwaggerException : Exception
    {
        public string StatusCode { get; }

        public string Response { get; }

        public Dictionary<string, IEnumerable<string>> Headers { get; }

        public SwaggerException(string message, string statusCode, string response, Dictionary<string, IEnumerable<string>> headers, Exception innerException)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            Response = response;
            Headers = headers;
        }

        public override string ToString()
        {
            return string.Format("HTTP Response: \n\n{0}\n\n{1}", Response, base.ToString());
        }
    }

    [GeneratedCode("NSwag", "11.12.13.0 (NJsonSchema v9.10.14.0 (Newtonsoft.Json v11.0.0.0))")]
    public class SwaggerException<TResult> : SwaggerException
    {
        public TResult Result { get; }

        public SwaggerException(string message, string statusCode, string response, Dictionary<string, IEnumerable<string>> headers, TResult result, Exception innerException)
            : base(message, statusCode, response, headers, innerException)
        {
            Result = result;
        }
    }
}