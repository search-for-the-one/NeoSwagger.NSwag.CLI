using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NeoSwagger.NSwag.CLI.Extensions
{
    public static class EnumerableExtensions
    {
        public static T[] ToArray<T>(this T item)
        {
            return new[] {item};
        }

        public static IEnumerable<T> Yield<T>(this T item)
        {
            yield return item;
        }

        public static Task<T[]> ToArrayAsync<T>(this IEnumerable<Task<T>> source)
        {
            return Task.WhenAll(source);
        }

        /// <summary>
        ///     Returns the only element of a sequence, or a default value if the sequence is empty or contains more than one
        ///     element.
        /// </summary>
        public static TSource OneOrDefault<TSource>(this IEnumerable<TSource> source)
        {
            var elements = Enumerable.ToArray(source.Take(2));
            return elements.Length == 1 ? elements[0] : default;
        }

        /// <summary>
        ///     Returns the only element of a sequence, or a default value if the sequence is empty or contains more than one
        ///     element.
        /// </summary>
        public static TSource OneOrDefault<TSource>(this IQueryable<TSource> source)
        {
            var elements = Enumerable.ToArray(source.Take(2));
            return elements.Length == 1 ? elements[0] : default;
        }
    }
}