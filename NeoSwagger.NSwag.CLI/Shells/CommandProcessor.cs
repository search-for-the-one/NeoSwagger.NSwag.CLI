using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using NeoSwagger.NSwag.CLI.Extensions;
using NeoSwagger.NSwag.CLI.Parsers;
using Newtonsoft.Json;

namespace NeoSwagger.NSwag.CLI.Shells
{
    using Parameters = List<(ParamValueType ParamValueType, string Name, string Value)>;

    internal class CommandProcessor : ICommandProcessor
    {
        private const string FileParameter = "FileParameter";
        private readonly ISwaggerClasses classes;

        private readonly HttpClient client;
        private readonly ICommandParser parser;
        private readonly IReadOnlyDictionary<string, Type> services;
        private readonly IVariables variables;

        public CommandProcessor(ICommandParser parser, IVariables variables, ISwaggerClasses classes, HttpClient client)
        {
            this.parser = parser;
            this.variables = variables;
            this.classes = classes;
            this.client = client;
            services = classes.ToDictionary(kvp => GetServiceName(kvp.Key), kvp => kvp.Key);
        }

        public string GetHelp(string service = "")
        {
            return string.IsNullOrWhiteSpace(service) ? GetTopLevelHelp() : GetServiceHelp(service);
        }

        public async Task<Response> Execute(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                throw new InvalidOperationException("Null command");

            parser.Parse(line, out var service, out var verb, out var parameters);
            if (!services.TryGetValue(service, out var type) || !classes.TryGetValue(type, out var methods))
                throw new InvalidOperationException($"Unknown service '{service}'");

            var method = methods.SingleOrDefault(m => string.Equals($"{verb}Async", m.Name, StringComparison.Ordinal));
            if (method == null)
                throw new InvalidOperationException($"Service '{service}' does not understand '{verb}'");

            var instance = Activator.CreateInstance(type, client);
            var result = method.Invoke(instance, await CreateParameterObjects(method.GetParameters(), parameters));
            if (!(result is Task))
                throw new NotSupportedException($"Expecting method to be async but got return type of '{result.GetType()}'");

            try
            {
                var response = await (dynamic) result;
                using var disposable = response as IDisposable;
                return disposable != null
                    ? (Response) await GetResponse(response) 
                    : throw new NotSupportedException($"Expecting method to have return type of 'IActionResult' but got '{response.GetType()}'");
            }
            catch (Exception ex)
            {
                if (ex.GetType().Name != "SwaggerException")
                    throw;

                return GetResponse(ex);
            }
        }

        private string GetServiceHelp(string service)
        {
            if (!services.TryGetValue(service, out var type))
                return $"Invalid service '{service}'";

            var sb = new StringBuilder();
            sb.AppendLine($"{service} service:");
            foreach (var method in classes[type].Select(info => (info.Name, Parameters: info.GetParameters())))
            {
                var parameters = GetParameters(method);
                sb.AppendLine($"  {GetVerbName(method.Name)}{parameters}");
            }

            return sb.ToString();
        }

        private static string GetParameters((string Name, ParameterInfo[] Parameters) method)
        {
            if (!method.Parameters.Any())
                return string.Empty;

            var parameters = FlattenParameters(method, method.Parameters.Select(p => (p.Name, p.ParameterType)));
            return $" <{string.Join("> <", parameters.Select(p => $"{p.Name} ({p.ParameterType.GetAlias()})"))}>";
        }

        private static IEnumerable<(string Name, Type ParameterType)> FlattenParameters((string Name, ParameterInfo[] Parameters) method, IEnumerable<(string Name, Type ParameterType)> parameters)
        {
            var result = parameters;
            var single = Flatten(method.Parameters);
            if (single != null)
                result = GetPocoProperties(single).Select(p => (p.Name, ParameterType: p.PropertyType)).ToArray();
            return result;
        }

        private static IEnumerable<PropertyInfo> GetPocoProperties(ParameterInfo parameter)
        {
            return parameter.ParameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(pi => pi.CanRead && pi.CanWrite);
        }

        private string GetTopLevelHelp()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Services:");
            foreach (var c in classes)
                sb.AppendLine($"  {GetServiceName(c.Key)}");
            sb.AppendLine("'help <service-name>' for more info");
            return sb.ToString();
        }

        private static async Task<Response> GetResponse(dynamic response)
        {
            var s = new MemoryStream();
            await ((Stream) response.Stream).CopyToAsync(s);
            s.Position = 0;
            return new Response(response.StatusCode, response.Headers, s);
        }

        private static Response GetResponse(Exception ex)
        {
            var response = (dynamic) ex;
            var s = new MemoryStream(Encoding.UTF8.GetBytes(response.Response)) {Position = 0};
            return new Response(response.StatusCode, response.Headers, s);
        }

        private static ParameterInfo Flatten(IEnumerable<ParameterInfo> parameters)
        {
            var single = parameters.OneOrDefault();
            return single != null && single.ParameterType.IsClass && GetPocoProperties(single).Any() && single.ParameterType.Name != FileParameter ? single : null;
        }

        private async Task<object[]> CreateParameterObjects(ParameterInfo[] signature, IEnumerable<(ParamValueType ParamValueType, string Name, string Value)> parameters)
        {
            var list = new Parameters(parameters);

            var single = Flatten(signature);
            if (single != null)
                return (await CreatePocoObjectAsync(single.ParameterType, GetPocoProperties(single), list)).ToArray();

            return await signature.Select(async p => await CreateObjectAsync(list, GetParameters(p))).ToArrayAsync();
        }

        private static (string Name, Type ParameterType, bool IsOptional, object DefaultValue) GetParameters(ParameterInfo p)
        {
            return (p.Name, p.ParameterType, p.IsOptional, p.DefaultValue);
        }

        private static (string Name, Type ParameterType, bool IsOptional, object DefaultValue) GetParameters(PropertyInfo p)
        {
            return (p.Name, p.PropertyType, false, null);
        }

        private async Task<object> CreatePocoObjectAsync(Type type, IEnumerable<PropertyInfo> properties, Parameters parameters)
        {
            var instance = Activator.CreateInstance(type);
            foreach (var propertyInfo in properties)
                propertyInfo.SetValue(instance, await CreateObjectAsync(parameters, GetParameters(propertyInfo)));
            return instance;
        }

        private async Task<object> CreateObjectAsync(Parameters parameters, (string Name, Type ParameterType, bool IsOptional, object DefaultValue) paramInfo)
        {
            if (!parameters.Any())
                return paramInfo.IsOptional ? paramInfo.DefaultValue : null;

            var findIndex = parameters.FindIndex(p => p.Name == paramInfo.Name);
            if (findIndex < 0 && !string.IsNullOrWhiteSpace(parameters.First().Name))
                return null;

            var paramIndex = Math.Max(0, findIndex);
            var param = parameters[paramIndex];
            parameters.RemoveAt(paramIndex);

            var destType = paramInfo.ParameterType;
            var value = GetValue(param);
            if (destType.Name == FileParameter)
            {
                if (param.ParamValueType == ParamValueType.Json)
                    throw new ArgumentException("Not expecting JSON argument");
                var stream = new MemoryStream(await Load(value));
                return Activator.CreateInstance(destType, stream, GetFileName(value));
            }

            if (destType.IsStringType())
                return value;

            var nullableUnderlyingType = Nullable.GetUnderlyingType(destType);
            if (nullableUnderlyingType != null)
                destType = nullableUnderlyingType;

            if (destType.IsBooleanType() || destType.IsNumbericType())
                return Convert.ChangeType(value, destType);

            return JsonConvert.DeserializeObject(param.ParamValueType == ParamValueType.Json ? value : await LoadString(value), destType);
        }

        private string GetValue((ParamValueType ParamValueType, string Name, string Value) param)
        {
            if (param.ParamValueType != ParamValueType.Var) return param.Value;
            return variables.TryGetValue(param.Value, out var result) ? result : throw new ArgumentException($"'${param.Value}' is undefined");
        }

        private static string GetFileName(string link)
        {
            return HttpUtility.UrlDecode(Path.GetFileName(new Uri(link).AbsolutePath));
        }

        private static async Task<byte[]> Load(string endpoint)
        {
            var uri = new Uri(endpoint);
            using var client = new WebClient();
            return await client.DownloadDataTaskAsync(uri);
        }

        private static async Task<string> LoadString(string endpoint)
        {
            var uri = new Uri(endpoint);
            using var client = new WebClient();
            return await client.DownloadStringTaskAsync(uri);
        }

        private static string GetServiceName(MemberInfo type)
        {
            const string suffix = "Client";
            var className = type.Name;
            return className.EndsWith(suffix) ? className.Substring(0, className.Length - suffix.Length) : className;
        }

        private static string GetVerbName(string name)
        {
            const string suffix = "Async";
            return name.EndsWith(suffix) ? name.Substring(0, name.Length - suffix.Length) : name;
        }
    }
}