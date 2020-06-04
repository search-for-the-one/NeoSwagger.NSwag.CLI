using System;
using System.Collections.Generic;
using System.Reflection;

namespace NeoSwagger.NSwag.CLI
{
    public interface ISwaggerClasses : IReadOnlyDictionary<Type, IEnumerable<MethodInfo>>
    {
    }
}