using System;
using System.Collections.Generic;

namespace NeoSwagger.NSwag.CLI.Extensions
{
    public static class DictionaryExtensions
    {
        public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dic, TKey key, Func<TValue> valueFunc)
        {
            if (dic.TryGetValue(key, out var value))
                return value;

            return dic[key] = valueFunc();
        }
    }
}