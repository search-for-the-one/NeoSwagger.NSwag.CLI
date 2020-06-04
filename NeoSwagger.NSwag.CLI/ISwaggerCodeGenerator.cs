using System.Threading.Tasks;

namespace NeoSwagger.NSwag.CLI
{
    internal interface ISwaggerCodeGenerator
    {
        Task<string> Generate();
    }
}