using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NeoSwagger.NSwag.CLI.Compilers;
using NeoSwagger.NSwag.CLI.Constants;
using NeoSwagger.NSwag.CLI.Shells.ConsoleHosts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoSwagger.NSwag.CLI.Tests
{
    public class CreateAssemblyFromSwaggerTests
    {
        private Assembly assembly;

        [Test]
        public void Compilation()
        {
            var classes = Compile();
            Assert.AreEqual(2, classes.Count);
            CollectionAssert.AreEquivalent(new []{"AuthenticationClient", "UtilityClient"}, classes.Keys.Select(t => t.Name));
        }

        [Test]
        public async Task Authentication()
        {
            dynamic authentication = CreateInstance("AuthenticationClient");
            Assert.AreEqual(200, (await authentication.UseTokenAsync("policy", "token")).StatusCode);
            var token = GetObject((await authentication.GetTokenAsync()).Stream);
            Assert.AreEqual("policy", GetString(token.Scheme));
            Assert.AreEqual("token", GetString(token.Parameter));
            Assert.AreEqual(200, (await authentication.ResetAsync()).StatusCode);
            Assert.IsNull(GetObject((await authentication.GetTokenAsync()).Stream));
        }

        [Test]
        public async Task AddHttpHeader()
        {
            dynamic utility = CreateInstance("UtilityClient");
            var result = await utility.AddHttpHeaderAsync("SomeKey", "SomeValue");
            Assert.AreEqual(200, result.StatusCode);
        }

        [Test]
        public async Task RemoveHttpHeader()
        {
            dynamic utility = CreateInstance("UtilityClient");
            var result = await utility.RemoveHttpHeaderAsync("SomeKey", "SomeValue");
            Assert.AreEqual(200, result.StatusCode);
        }

        [Test]
        public async Task Base64EncodeDecode()
        {
            dynamic utility = CreateInstance("UtilityClient");
            var data = GenerateRandomBytes();
            var encoded = await utility.EncodeBase64Async(CreateFileParameter(new MemoryStream(data)));
            var decoded = await utility.DecodeBase64Async(GetString(encoded.Stream));
            Assert.AreEqual(data, GetBytes(decoded.Stream));
        }

        [Test]
        public async Task JsonSelect()
        {
            dynamic utility = CreateInstance("UtilityClient");
            var result = await utility.JsonSelectAsync(@"{'Stores':['Lambton Quay','Willis Street']}", "Stores[0]");
            Assert.AreEqual("Lambton Quay", GetString(result.Stream));
        }

        [Test]
        public async Task InvalidJsonSelect()
        {
            dynamic utility = CreateInstance("UtilityClient");
            var result = await utility.JsonSelectAsync(@"{}", "Invalid[0]");
            Assert.AreNotEqual(200, result.StatusCode);
        }

        private static byte[] GenerateRandomBytes()
        {
            var result = new byte[100];
            new Random(5).NextBytes(result);
            return result;
        }

        private static dynamic GetObject(Stream stream) => JsonConvert.DeserializeObject(GetString(stream));
        private static string GetString(JToken value) => value.ToObject<string>();
        private static string GetString(Stream stream) => Encoding.UTF8.GetString(GetBytes(stream));

        private static byte[] GetBytes(Stream stream)
        {
            using (var memStream = new MemoryStream())
            {
                stream.CopyTo(memStream);
                memStream.Position = 0;
                return memStream.ToArray();
            }
        }

        private dynamic CreateFileParameter(Stream stream)
        {
            var type = assembly.GetTypes().Single(t => t.FullName == $"{CodeGenerationConstants.Namespace}.FileParameter");
            return Activator.CreateInstance(type, args: stream);
        }

        private object CreateInstance(string client)
        {
            return Activator.CreateInstance(Compile().Keys.Single(t => t.Name == client), args: new HttpClient());
        }

        private IReadOnlyDictionary<Type, IEnumerable<MethodInfo>> Compile()
        {
            assembly = new CreateAssemblyFromSwagger(new CSharpCompiler(), new MockCodeGenerator(), new NullConsoleHost())
                .CreateAssembly(out var classes);
            return classes;
        }
    }
}