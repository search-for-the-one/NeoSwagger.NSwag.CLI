using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NeoSwagger.NSwag.CLI.Extensions
{
    public static class AssemblyExtensions
    {
        public static IEnumerable<Type> GetTypesThatImplement<T>(this Assembly assembly)
        {
            return GetTypesThatImplement(assembly, typeof(T));
        }

        public static IEnumerable<Type> GetTypesThatImplement(this Assembly assembly, Type type)
        {
            return assembly.GetTypes().Where(t => Implements(type, t));
        }

        private static bool Implements(Type baseType, Type type)
        {
            return baseType.IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract;
        }
    }
}