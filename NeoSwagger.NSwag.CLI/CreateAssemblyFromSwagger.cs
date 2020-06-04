using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Scripting;
using NeoSwagger.NSwag.CLI.Compilers;
using NeoSwagger.NSwag.CLI.Constants;
using NeoSwagger.NSwag.CLI.Extensions;
using NeoSwagger.NSwag.CLI.Shells;

namespace NeoSwagger.NSwag.CLI
{
    internal class CreateAssemblyFromSwagger : ICreateAssemblyFromSwagger
    {
        private const string ClientInterface = CodeGenerationConstants.Interface;
        private static readonly string ClientNamespace = CodeGenerationConstants.Namespace;
        private readonly ISwaggerCodeGenerator codeGenerator;
        private readonly IConsoleHost consoleHost;

        private readonly ICompiler compiler;

        public CreateAssemblyFromSwagger(ICompiler compiler, ISwaggerCodeGenerator codeGenerator, IConsoleHost consoleHost)
        {
            this.compiler = compiler;
            this.codeGenerator = codeGenerator;
            this.consoleHost = consoleHost;
        }

        public Assembly CreateAssembly(out ISwaggerClasses classes)
        {
            var code = GenerateCodeFromSwagger().Result;
            var asmCacheFile = GetAsmCacheFile(code);

            Assembly asm;
            if (File.Exists(asmCacheFile) && (asm = TryLoadFromCache(asmCacheFile)) != null)
            {
                classes = GetSwaggerClasses(asm);
                return asm;
            }

            var asmRaw = Compile(code);
            SaveCacheInBackground(asmCacheFile, asmRaw);
            asm = Assembly.Load(asmRaw);
            classes = GetSwaggerClasses(asm);
            return asm;
        }

        private static void SaveCacheInBackground(string asmCacheFile, byte[] asmRaw)
        {
            new Thread(() =>
            {
                try
                {
                    File.WriteAllBytes(asmCacheFile, asmRaw);
                }
                catch
                {
                    // ignore errors if we can't write cache
                }
            }).Start();
        }

        private static string GetAsmCacheFile(string code)
        {
            using var hash = MD5.Create();
            var guid = new Guid(hash.ComputeHash(Encoding.Unicode.GetBytes(code)));
            return Path.Combine(Path.GetTempPath(), $"{guid:N}.nswag.cli.tmp");
        }

        private static Assembly TryLoadFromCache(string asmCacheFile)
        {
            try
            {
                return Assembly.Load(File.ReadAllBytes(asmCacheFile));
            }
            catch
            {
                // ignore errors if we can't load cache
                return null;
            }
        }

        private async Task<string> GenerateCodeFromSwagger()
        {
            return GenerateFullCode(await codeGenerator.Generate());
        }

        private static string GenerateFullCode(string code)
        {
            return $"namespace {ClientNamespace} {{ {GetWebClientInterface()} {GetWebClientBaseClass()} {GetFileParameterClass(code)} {GetAuthenticationClass()} {GetUtilityClass()} }} {GetSwaggerCode(code)}";
        }

        private static string GetFileParameterClass(string code)
        {
            return code.Contains("public partial class FileParameter") 
                ? string.Empty 
                : @"
public partial class FileParameter
{
    public FileParameter(System.IO.Stream data) 
        : this (data, null)
    {
    }

    public FileParameter(System.IO.Stream data, string fileName)
    {
        Data = data;
        FileName = fileName;
    }

    public System.IO.Stream Data { get; private set; }

    public string FileName { get; private set; }
}
";
        }

        private static ISwaggerClasses GetSwaggerClasses(Assembly asm)
        {
            var classes = new SwaggerClasses();
            foreach (var t in asm.GetTypesThatImplement(asm.GetType($"{ClientNamespace}.{ClientInterface}")))
                classes.GetOrAdd(t, () => GetClientMethods(t.GetMethods(BindingFlags.Public | BindingFlags.Instance)));
            return classes;
        }

        private static IEnumerable<MethodInfo> GetClientMethods(IEnumerable<MethodInfo> methods)
        {
            return methods.Where(m => m.Name.EndsWith("Async", StringComparison.Ordinal) && m.GetParameters().All(p => p.ParameterType != typeof(CancellationToken)));
        }

        private byte[] Compile(string code)
        {
            try
            {
                return compiler.Compile(code);
            }
            catch (CompilationErrorException ex)
            {
                foreach (var d in ex.Diagnostics)
                    consoleHost.WriteLine(d.ToString());

                throw;
            }
        }

        private static string GetWebClientInterface()
        {
            return @"public interface IWebClient { }";
        }

        private static string GetWebClientBaseClass()
        {
            return @"
public abstract class WebClient : IWebClient
{
    protected void PrepareRequest(System.Net.Http.HttpClient client, System.Net.Http.HttpRequestMessage request, string url)
    {
        if (request.Content is System.Net.Http.MultipartFormDataContent c)
        {
            foreach (var formData in c)
            {
                if (formData is System.Net.Http.StreamContent s)
                {
                    var contentType = MimeMapping.MimeUtility.GetMimeMapping(formData.Headers.ContentDisposition?.FileName ?? string.Empty);
                    s.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                }
            }
        }
    }
}
";
        }

        private static string GetUtilityClass()
        {
            return @"
#pragma warning disable // Disable all warnings

public class UtilityClient : WebClient
{
    private readonly System.Net.Http.HttpClient client;

    public UtilityClient(System.Net.Http.HttpClient client)
    {
        this.client = client;
    }

    public async System.Threading.Tasks.Task<FileResponse> AddHttpHeaderAsync(string key, string value) 
    {
        client.DefaultRequestHeaders.Remove(key);
        client.DefaultRequestHeaders.Add(key, value);

        var stream = new System.IO.MemoryStream();
        return new FileResponse(200, new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IEnumerable<string>>(), stream, null, null);
    }

    public async System.Threading.Tasks.Task<FileResponse> RemoveHttpHeaderAsync(string key, string value) 
    {
        client.DefaultRequestHeaders.Remove(key);

        var stream = new System.IO.MemoryStream();
        return new FileResponse(200, new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IEnumerable<string>>(), stream, null, null);
    }

    public async System.Threading.Tasks.Task<FileResponse> EncodeBase64Async(FileParameter file)
    {
        using (var stream = file.Data)
        {
            stream.Position = 0;
            using(var memoryStream = new System.IO.MemoryStream())
            {
                await stream.CopyToAsync(memoryStream);
                var encoded = System.Convert.ToBase64String(memoryStream.ToArray());
                var result = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(encoded));
                result.Position = 0;
                return new FileResponse(200, new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IEnumerable<string>>(), result, null, null);
            }
        }
    }

    public async System.Threading.Tasks.Task<FileResponse> DecodeBase64Async(string encoded)
    {
        var headers = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IEnumerable<string>>();
        headers[""Content-Type""] = new []{""application/octet-stream""};
        var result = new System.IO.MemoryStream(System.Convert.FromBase64String(encoded));
        return new FileResponse(200, headers, result, null, null);
    }

    public async System.Threading.Tasks.Task<FileResponse> JsonSelectAsync(string json, string path)
    {
        try
        {
            var o = Newtonsoft.Json.Linq.JObject.Parse(json).SelectToken(path);
            if (o == null) throw new System.ArgumentException(""Invalid JsonSelect path"");
            var val = o.ToString();
            var result = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(val));
            result.Position = 0;
            return new FileResponse(200, new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IEnumerable<string>>(), result, null, null);
        }
        catch (System.Exception ex)
        {
            var result = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(ex.Message));
            return new FileResponse(400, new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IEnumerable<string>>(), result, null, null);
        }
    }
}
";
        }

        private static string GetAuthenticationClass()
        {
            return @"
#pragma warning disable // Disable all warnings

public class AuthenticationClient : WebClient
{
    private readonly System.Net.Http.HttpClient client;

    public AuthenticationClient(System.Net.Http.HttpClient client)
    {
        this.client = client;
    }

    public async System.Threading.Tasks.Task<FileResponse> UseTokenAsync(string policy, string token) 
    {
        client.DefaultRequestHeaders.Remove(""Authorization"");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(policy, token);

        var stream = new System.IO.MemoryStream();
        return new FileResponse(200, new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IEnumerable<string>>(), stream, null, null);
    }

    public async System.Threading.Tasks.Task<FileResponse> GetTokenAsync()
    {
        var auth = client.DefaultRequestHeaders.Authorization;
        var stream = auth == null ? new System.IO.MemoryStream() : new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(auth)));
        stream.Position = 0;
        return new FileResponse(200, new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IEnumerable<string>>(), stream, null, null);
    }

    public async System.Threading.Tasks.Task<FileResponse> ResetAsync() 
    {
        client.DefaultRequestHeaders.Remove(""Authorization"");

        var stream = new System.IO.MemoryStream();
        return new FileResponse(200, new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IEnumerable<string>>(), stream, null, null);
    }
}";
        }

        private static string GetSwaggerCode(string code)
        {
            return code.Replace("partial void PrepareRequest(System.Net.Http.HttpClient client, System.Net.Http.HttpRequestMessage request, string url);", string.Empty);
        }
    }
}