using System;
using System.Collections.Generic;
using System.Reflection;

namespace NeoSwagger.NSwag.CLI
{
    public class SwaggerClasses : Dictionary<Type, IEnumerable<MethodInfo>>, ISwaggerClasses
    {
    }
}