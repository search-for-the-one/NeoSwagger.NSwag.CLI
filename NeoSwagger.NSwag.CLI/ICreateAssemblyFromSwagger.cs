using System.Reflection;

namespace NeoSwagger.NSwag.CLI
{
    internal interface ICreateAssemblyFromSwagger
    {
        Assembly CreateAssembly(out ISwaggerClasses classes);
    }
}