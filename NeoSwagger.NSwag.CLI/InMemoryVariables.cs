using System;
using System.Collections.Generic;

namespace NeoSwagger.NSwag.CLI
{
    public class InMemoryVariables : Dictionary<string, string>, IVariables
    {
        public InMemoryVariables() : base(StringComparer.Ordinal)
        {
        }
    }
}