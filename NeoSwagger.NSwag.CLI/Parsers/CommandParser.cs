using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NeoSwagger.NSwag.CLI.Exceptions;

namespace NeoSwagger.NSwag.CLI.Parsers
{
    internal class CommandParser : ICommandParser
    {
        private const RegexOptions RegexFlags = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline;

        private static readonly Regex IdentifierRegex = new(@"^(?:((?!\d)\w+(?:(?!\d)\w+)*))?((?!\d)\w+)", RegexFlags);
        private static readonly Regex VarIdentifierRegex = new(@"^\$(?:((?!\d)\w+(?:(?!\d)\w+)*))?((?!\d)\w+)", RegexFlags);
        private static readonly Regex SingleQuotedStringRegex = new(@"^\'(?<=')(?:[^']|'')*(?=')\'", RegexFlags);
        private static readonly Regex DoubleQuotedStringRegex = new(@"^\""(?<="")(?:[^""]|"""")*(?="")\""", RegexFlags);
        private static readonly Regex ParamAssignOperatorRegex = new(@"^\s*\=\s*", RegexFlags);
        private static readonly Regex TrimStartRegex = new(@"^[\s=]+", RegexFlags);
        private static readonly Regex TrimEndRegex = new(@"[\s=]+$", RegexFlags);
        private static readonly Regex PlainStringRegex = new(@"^[^\s]+", RegexFlags);

        public void Parse(string input, out string service, out string verb, out IReadOnlyList<(ParamValueType ParamValueType, string Name, string Value)> parameters)
        {
            service = GetIdentifier(input);
            if (service == string.Empty)
                throw new ParserException("Unknown service");

            input = RemoveStart(input, service);
            verb = GetIdentifier(input);
            if (verb == string.Empty)
                throw new ParserException($"Unable to parse verb for '{service}'");

            input = RemoveStart(input, verb);

            var result = new List<(ParamValueType ParamValueType, string Name, string Value)>();
            while (!string.IsNullOrWhiteSpace(input))
            {
                input = ParseParam(input, out var paramName, out var paramValueType, out var paramValue);
                result.Add((paramValueType, paramName, paramValue));
            }

            parameters = result;
        }

        private static string ParseParam(string input, out string paramName, out ParamValueType paramValueType, out string paramValue)
        {
            paramName = GetParamIdentifier(input);
            if (paramName == string.Empty)
                return ParseParamValue(input, out paramValueType, out paramValue);

            input = RemoveStart(input, paramName);
            paramName = TrimEndRegex.Replace(paramName, string.Empty);

            return ParseParamValue(input, out paramValueType, out paramValue);
        }

        private static string ParseParamValue(string input, out ParamValueType paramValueType, out string paramValue)
        {
            if (TryParseJson(input, out var json))
            {
                paramValueType = ParamValueType.Json;
                paramValue = Unescape(json);
                input = RemoveStart(input, json);
            }
            else if (TryParseVarString(input, out var v))
            {
                paramValueType = ParamValueType.Var;
                paramValue = v.Substring(1);
                input = RemoveStart(input, v);
            }
            else if (TryParseDoubleQuotedString(input, out var s))
            {
                paramValueType = ParamValueType.String;
                paramValue = Unescape(s);
                input = RemoveStart(input, s);
            }
            else
            {
                paramValueType = ParamValueType.String;
                paramValue = GetPlainString(input);
                input = RemoveStart(input, paramValue);
            }

            return input;
        }

        private static string Unescape(string i)
        {
            return i.Substring(1, i.Length - 2).Replace(new string(i.First(), 2), new string(i.First(), 1));
        }

        private static bool TryParseVarString(string i, out string s)
        {
            return TryParse(VarIdentifierRegex, i, out s);
        }

        private static bool TryParseDoubleQuotedString(string i, out string s)
        {
            return TryParse(DoubleQuotedStringRegex, i, out s);
        }

        private static string GetPlainString(string i)
        {
            return !TryParse(PlainStringRegex, i, out var result) ? string.Empty : result;
        }

        private static bool TryParseJson(string i, out string s)
        {
            return TryParse(SingleQuotedStringRegex, i, out s);
        }

        private static bool TryParse(Regex regex, string input, out string s)
        {
            var match = regex.Match(input);
            s = match.Value;
            return match.Success;
        }

        private static string RemoveStart(string input, string start)
        {
            return TrimStartRegex.Replace(input.Substring(start.Length), string.Empty);
        }

        private static string GetParamIdentifier(string input)
        {
            var match = IdentifierRegex.Match(input);
            if (!match.Success)
                return string.Empty;

            var paramIdentifier = match.Value;
            match = ParamAssignOperatorRegex.Match(input.Substring(paramIdentifier.Length));
            if (!match.Success)
                return string.Empty;

            return paramIdentifier + match.Value;
        }

        private static string GetIdentifier(string input)
        {
            var match = IdentifierRegex.Match(input);
            return match.Value;
        }
    }
}