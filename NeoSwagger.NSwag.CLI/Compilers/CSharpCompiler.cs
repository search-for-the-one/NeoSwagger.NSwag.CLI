using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Scripting;
using MimeMapping;
using NeoSwagger.NSwag.CLI.Extensions;
using Newtonsoft.Json;

namespace NeoSwagger.NSwag.CLI.Compilers
{
    internal class CSharpCompiler : ICompiler
    {
        public byte[] Compile(string code)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var references = GetTrustedReferences().Concat(ReferencedTypes.Select(GetReference));
            var compileOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithConcurrentBuild(true)
                .WithOptimizationLevel(OptimizationLevel.Release)
                .WithWarningLevel(0);
            var compilation = CSharpCompilation.Create($"{Guid.NewGuid():N}", syntaxTree.Yield(), references, compileOptions);
            using var ms = new MemoryStream();
            var result = compilation.Emit(ms);
            if (!result.Success)
            {
                throw new CompilationErrorException("Code failed to compile",
                    result.Diagnostics.Where(diagnostic => diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error).ToImmutableArray());
            }

            ms.Position = 0;
            return ms.ToArray();
        }

        private static IEnumerable<Type> ReferencedTypes
        {
            get
            {
                yield return typeof(object);
                yield return typeof(Enumerable);
                yield return typeof(JsonSerializer);
                yield return typeof(Uri);
                yield return typeof(HttpStatusCode);
                yield return typeof(HttpClient);
                yield return typeof(GeneratedCodeAttribute);
                yield return typeof(INotifyPropertyChanged);
                yield return typeof(MimeUtility);
                yield return typeof(Console);
                yield return typeof(EnumMemberAttribute);
                yield return typeof(MinLengthAttribute);
                yield return typeof(RequiredAttribute);
                yield return typeof(RegularExpressionAttribute);
            }
        }

        private static IEnumerable<string> CoreReferences
        {
            get
            {
                yield return "System.Runtime";
                yield return "netstandard";
                yield return "System.Collections";
                yield return "System.Runtime.Extensions";
            }
        }

        private static IEnumerable<MetadataReference> GetTrustedReferences()
        {
            var trustedAssembliesPaths = ((string) AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty).Split(Path.PathSeparator);
            var trustedReferences = trustedAssembliesPaths
                .Where(p => CoreReferences.Contains(Path.GetFileNameWithoutExtension(p)))
                .Select(p => MetadataReference.CreateFromFile(p));
            return trustedReferences;
        }

        private static MetadataReference GetReference(Type t)
        {
            return MetadataReference.CreateFromFile(t.Assembly.Location);
        }
    }
}