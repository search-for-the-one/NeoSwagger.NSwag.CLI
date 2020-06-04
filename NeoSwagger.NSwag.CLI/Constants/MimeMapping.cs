using System;
using System.Collections.Immutable;
using System.Linq;
using MimeMapping;

namespace NeoSwagger.NSwag.CLI.Constants
{
    public static class MimeMapping
    {
        private static readonly Lazy<ImmutableDictionary<string, ImmutableArray<string>>> LazyReverseTypeMap =
            new Lazy<ImmutableDictionary<string, ImmutableArray<string>>>(() =>
                MimeUtility.TypeMap.GroupBy(kvp => kvp.Value).ToImmutableDictionary(g => g.Key, g => g.Select(kvp => kvp.Key).ToImmutableArray()));

        public static ImmutableDictionary<string, ImmutableArray<string>> ReverseTypeMap => LazyReverseTypeMap.Value;
    }
}