using System.Collections.Generic;

namespace NeoSwagger.NSwag.CLI.Parsers
{
    internal interface ICommandParser
    {
        void Parse(string input, out string service, out string verb, out IReadOnlyList<(ParamValueType ParamValueType, string Name, string Value)> parameters);
    }

    internal enum ParamValueType
    {
        Json,
        String,
        Var
    }
}