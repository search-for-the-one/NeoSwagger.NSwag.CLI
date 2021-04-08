using System.Net.Http;
using System.Threading.Tasks;
using NeoSwagger.NSwag.CLI.Constants;
using NSwag;
using NSwag.CodeGeneration.CSharp;

namespace NeoSwagger.NSwag.CLI
{
    internal class NSwagCodeGenerator : ISwaggerCodeGenerator
    {
        private const string ClientBaseClass = CodeGenerationConstants.BaseClass;
        private static readonly string ClientNamespace = CodeGenerationConstants.Namespace;
        private readonly HttpClient httpClient;
        private readonly string swaggerEndpoint;

        public NSwagCodeGenerator(HttpClient httpClient, string swaggerEndpoint)
        {
            this.httpClient = httpClient;
            this.swaggerEndpoint = swaggerEndpoint;
        }

        public async Task<string> Generate()
        {
            var response = await httpClient.GetAsync(swaggerEndpoint);
            var document = await OpenApiDocument.FromJsonAsync(await response.Content.ReadAsStringAsync());
            var settings = new CSharpClientGeneratorSettings
            {
                ClientBaseClass = ClientBaseClass,
                InjectHttpClient = true,
                UseBaseUrl = false,
                ExceptionClass = "SwaggerException"
            };
            settings.CSharpGeneratorSettings.Namespace = ClientNamespace;
            
            return new CSharpClientGenerator(document, settings).GenerateFile();
        }
    }
}