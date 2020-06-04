using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NeoSwagger.NSwag.CLI.Extensions;
using NeoSwagger.NSwag.CLI.Parsers;
using NeoSwagger.NSwag.CLI.Shells;
using Newtonsoft.Json;
using NUnit.Framework;

namespace NeoSwagger.NSwag.CLI.Tests
{
    public class CommandProcessorTests
    {
        [Test]
        public void Help()
        {
            var proc = CreateCommandProcessor();
            var help = proc.GetHelp();
            Assert.IsNotEmpty(help);
            Assert.IsTrue(help.IndexOf("Test", StringComparison.Ordinal) >= 0);
            Console.WriteLine(help);
            help = proc.GetHelp("Test");
            Assert.IsTrue(help.IndexOf("SignUp", StringComparison.Ordinal) >= 0);
            Console.WriteLine(help);
        }

        [Test]
        public async Task ExecuteString()
        {
            var proc = CreateCommandProcessor();
            using (var response = await proc.Execute("Test SignUp test@test.com password"))
            {
                var parameters = GetResult(response);
                Assert.AreEqual("test@test.com", parameters[0]);
                Assert.AreEqual("password", parameters[1]);
            }
        }

        [Test]
        public async Task ExecuteJson()
        {
            var proc = CreateCommandProcessor();
            using (var response = await proc.Execute("Test ChangePasswordNested '{AccessToken:\"123456\", CurrentPassword:\"password\", ProposedPassword:\"new\"}'"))
            {
                var parameters = GetResult(response);
                Assert.AreEqual("123456", parameters[0].ToString());
                Assert.AreEqual("password", parameters[1].ToString());
                Assert.AreEqual("new", parameters[2].ToString());
            }
        }

        [Test]
        public async Task ExecuteJsonViaUri()
        {
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, "{AccessToken:\"123456\", CurrentPassword:\"password\", ProposedPassword:\"new\"}", Encoding.UTF8);
            tempFile = tempFile.Replace(Path.DirectorySeparatorChar, '/');
            try
            {
                var proc = CreateCommandProcessor();
                using (var response = await proc.Execute($"Test ChangePasswordNested file://{tempFile}"))
                {
                    var parameters = GetResult(response);
                    Assert.AreEqual("123456", parameters[0].ToString());
                    Assert.AreEqual("password", parameters[1].ToString());
                    Assert.AreEqual("new", parameters[2].ToString());
                }
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Test]
        public async Task ExecuteUri()
        {
            var tempFile = Path.GetTempFileName();
            var bytes = Encoding.UTF8.GetBytes("foobar");
            File.WriteAllBytes(tempFile, bytes);
            tempFile = tempFile.Replace(Path.DirectorySeparatorChar, '/');
            try
            {
                var proc = CreateCommandProcessor();
                using (var response = await proc.Execute($"Test Create file://{tempFile} name=filename"))
                {
                    var parameters = GetResult(response);
                    Assert.AreEqual("filename", parameters[0]);
                    Assert.AreEqual("foobar", parameters[1]);
                }
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Test]
        public async Task ExecuteNumbers()
        {
            var proc = CreateCommandProcessor();
            using (var response = await proc.Execute("Test UseSomeNumbers 1 2 3 4 5"))
            {
                var parameters = GetResult(response);
                Assert.AreEqual(typeof(long), parameters[0].GetType());
                Assert.AreEqual(1, parameters[0]);
                Assert.AreEqual(typeof(int), parameters[1].GetType());
                Assert.AreEqual(2, parameters[1]);
                Assert.AreEqual(typeof(ushort), parameters[2].GetType());
                Assert.AreEqual(3, parameters[2]);
                Assert.AreEqual(typeof(double), parameters[3].GetType());
                Assert.AreEqual(4, parameters[3]);
                Assert.AreEqual(typeof(float), parameters[4].GetType());
                Assert.AreEqual(5, parameters[4]);
            }
            using (var response = await proc.Execute("Test UseSomeNumbers 1 2 3 4.4 5.5"))
            {
                var parameters = GetResult(response);
                Assert.AreEqual(typeof(long), parameters[0].GetType());
                Assert.AreEqual(1, parameters[0]);
                Assert.AreEqual(typeof(int), parameters[1].GetType());
                Assert.AreEqual(2, parameters[1]);
                Assert.AreEqual(typeof(ushort), parameters[2].GetType());
                Assert.AreEqual(3, parameters[2]);
                Assert.AreEqual(typeof(double), parameters[3].GetType());
                Assert.AreEqual(4.4, parameters[3]);
                Assert.AreEqual(typeof(float), parameters[4].GetType());
                Assert.AreEqual(5.5, parameters[4]);
            }
        }

        [Test]
        public void ExecuteInvalidService()
        {
            var proc = CreateCommandProcessor();
            Assert.ThrowsAsync<InvalidOperationException>(async () => await proc.Execute("IDontUnderstandThis foo"));
        }

        [Test]
        public void ExecuteInvalidServiceMethod()
        {
            var proc = CreateCommandProcessor();
            Assert.ThrowsAsync<InvalidOperationException>(async () => await proc.Execute("Test IDontUnderstandThis"));
        }

        private static object[] GetResult(Response response)
        {
            var result = JsonConvert.DeserializeObject<(object Value, Type Type)[]>(Encoding.UTF8.GetString(((MemoryStream) response.Stream).ToArray()));
            return result.Select(p => Convert.ChangeType(p.Value, p.Type)).ToArray();
        }

        private static CommandProcessor CreateCommandProcessor()
        {
            new MockCreateAssemblyFromSwagger().CreateAssembly(out var classes);
            return new CommandProcessor(new CommandParser(), new InMemoryVariables(), classes, new HttpClient());
        }
    }

    public class MockCreateAssemblyFromSwagger : ICreateAssemblyFromSwagger
    {
        public Assembly CreateAssembly(out ISwaggerClasses classes)
        {
            var dic = new SwaggerClasses();
            var asm = Assembly.GetExecutingAssembly();
            foreach (var t in asm.GetTypesThatImplement<IWebClient>())
                if (t.Namespace == typeof(IWebClient).Namespace)
                    dic.GetOrAdd(t, () => GetClientMethods(t.GetMethods(BindingFlags.Public | BindingFlags.Instance)));
            classes = dic;
            return asm;
        }

        private static IEnumerable<MethodInfo> GetClientMethods(IEnumerable<MethodInfo> methods)
        {
            return methods.Where(m => m.Name.EndsWith("Async", StringComparison.Ordinal) && m.GetParameters().All(p => p.ParameterType != typeof(CancellationToken)));
        }
    }
}