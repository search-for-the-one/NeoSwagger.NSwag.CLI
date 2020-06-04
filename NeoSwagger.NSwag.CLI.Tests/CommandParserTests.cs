using System.Linq;
using NeoSwagger.NSwag.CLI.Exceptions;
using NeoSwagger.NSwag.CLI.Parsers;
using NUnit.Framework;

namespace NeoSwagger.NSwag.CLI.Tests
{
    public class CommandParserTests
    {
        [Test]
        public void ParseNoParams()
        {
            var parser = new CommandParser();
            parser.Parse("user verify", out var service, out var verb, out var parameters);
            Assert.AreEqual("user", service);
            Assert.AreEqual("verify", verb);
            Assert.AreEqual(0, parameters.Count);
        }

        [Test]
        public void ParseSingleParam()
        {
            var parser = new CommandParser();
            parser.Parse("user verify blah", out var service, out var verb, out var parameters);
            Assert.AreEqual("user", service);
            Assert.AreEqual("verify", verb);
            Assert.AreEqual(1, parameters.Count);
            Assert.AreEqual((ParamValueType.String, string.Empty, "blah"), parameters.Single());
        }

        [Test]
        public void ParseVarIdentifier()
        {
            var parser = new CommandParser();
            parser.Parse("user verify $LastResponse", out var service, out var verb, out var parameters);
            Assert.AreEqual("user", service);
            Assert.AreEqual("verify", verb);
            Assert.AreEqual(1, parameters.Count);
            Assert.AreEqual((ParamValueType.Var, string.Empty, "LastResponse"), parameters.Single());
        }

        [Test]
        public void ParseMultiParams()
        {
            const string param1 = "{\"string1\"}";
            const string param2 = @"Double Quoted """"String""""";
            const string param3 = "file://C:/file.txt";
            const string param4 = "plan-string";
            const string param5 = "\"file://C:/file with spaces.txt\"";

            var parser = new CommandParser();
            parser.Parse($"user signup '{param1}' \"{param2}\" {param3} {param4} {param5}", out var service, out var verb, out var parameters);
            Assert.AreEqual("user", service);
            Assert.AreEqual("signup", verb);
            Assert.AreEqual(5, parameters.Count);
            Assert.AreEqual((ParamValueType.Json, string.Empty, param1), parameters[0]);
            Assert.AreEqual((ParamValueType.String, string.Empty, param2.Replace("\"\"", "\"")), parameters[1]);
            Assert.AreEqual((ParamValueType.String, string.Empty, param3), parameters[2]);
            Assert.AreEqual((ParamValueType.String, string.Empty, param4), parameters[3]);
            Assert.AreEqual((ParamValueType.String, string.Empty, param5.Replace("\"", "")), parameters[4]);
        }

        [Test]
        public void ParseMultiParamsWithNames()
        {
            const string name1 = "JSON";
            const string name3 = "uri";

            const string param1 = "{\"string1\"}";
            const string param2 = @"Double Quoted """"String""""";
            const string param3 = "file://C:/file.txt";
            const string param4 = "plan-string";

            var parser = new CommandParser();
            parser.Parse($"serviceType blah {name1} =  '{param1}' \"{param2}\" {name3}={param3} {param4}   ", out var service, out var verb, out var parameters);
            Assert.AreEqual("serviceType", service);
            Assert.AreEqual("blah", verb);
            Assert.AreEqual(4, parameters.Count);
            Assert.AreEqual((ParamValueType.Json, name1, param1), parameters[0]);
            Assert.AreEqual((ParamValueType.String, string.Empty, param2.Replace("\"\"", "\"")), parameters[1]);
            Assert.AreEqual((ParamValueType.String, name3, param3), parameters[2]);
            Assert.AreEqual((ParamValueType.String, string.Empty, param4), parameters[3]);
        }

        [Test]
        public void ParseEmptyString()
        {
            var parser = new CommandParser();
            Assert.Throws<ParserException>(() => parser.Parse(string.Empty, out _, out _, out _));
        }

        [Test]
        public void ParseMissingVerb()
        {
            var parser = new CommandParser();
            Assert.Throws<ParserException>(() => parser.Parse("service", out _, out _, out _));
        }
    }
}