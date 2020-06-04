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
        private readonly string swaggerEndpoint;

        public NSwagCodeGenerator(string swaggerEndpoint)
        {
            this.swaggerEndpoint = swaggerEndpoint;
        }

        public async Task<string> Generate()
        {
            var document = await OpenApiDocument.FromUrlAsync(swaggerEndpoint);
            var settings = new CSharpClientGeneratorSettings
            {
                ClientBaseClass = ClientBaseClass,
                InjectHttpClient = true,
                UseBaseUrl = false
            };
            settings.CSharpGeneratorSettings.Namespace = ClientNamespace;
            return new CSharpClientGenerator(document, settings).GenerateFile();
        }
    }
}