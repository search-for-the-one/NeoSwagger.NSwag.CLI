using System.Reflection;
using System.Threading.Tasks;

namespace NeoSwagger.NSwag.CLI
{
    internal interface ICreateAssemblyFromSwagger
    {
        Task<(Assembly Assembly, ISwaggerClasses Classes)> CreateAssembly();
    }
}