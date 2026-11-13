namespace Armada.Core.Services
{
    using System;
    using System.Collections;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Enforces <see cref="CollectionBoundAttribute"/> limits on a model and every Armada model it contains. Call
    /// <see cref="Validate(object?, string?)"/> on each write path (create, update, import, handshake) before the model is
    /// stored, so every entry point rejects an oversized collection the same way. Thread safe: reflection metadata is
    /// cached in a concurrent dictionary.
    /// </summary>
    public static class CollectionBounds
    {
        #region Private-Members

        private static readonly ConcurrentDictionary<Type, List<BoundProperty>> _Properties = new ConcurrentDictionary<Type, List<BoundProperty>>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Throw when any collection property carrying <see cref="CollectionBoundAttribute"/>, on the value or on any
        /// Armada model reachable from it, holds more items than its limit. Null is valid.
        /// </summary>
        /// <param name="value">Model to validate, or null.</param>
        /// <param name="path">Field path prefix used in the error, or null for the model's own property names.</param>
        /// <exception cref="CollectionLimitExceededException">Thrown when a collection is over its limit.</exception>
        public static void Validate(object? value, string? path = null)
        {
            if (value == null) return;
            HashSet<object> visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            ValidateObject(value, path ?? "", visited);
        }

        /// <summary>
        /// The bound declared on a property, or null when it has none.
        /// </summary>
        /// <param name="property">Property.</param>
        /// <returns>The attribute, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="property"/> is null.</exception>
        public static CollectionBoundAttribute? BoundOf(PropertyInfo property)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            return property.GetCustomAttribute<CollectionBoundAttribute>(true);
        }

        /// <summary>
        /// Whether a property is never part of a serialized response: it carries <see cref="JsonIgnoreAttribute"/> with
        /// <see cref="JsonIgnoreCondition.Always"/>. A conditional ignore (for example WhenWritingNull) still serializes
        /// the value when it is set, so such a property counts.
        /// </summary>
        /// <param name="property">Property.</param>
        /// <returns>True when the property is never serialized.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="property"/> is null.</exception>
        public static bool IsNeverSerialized(PropertyInfo property)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            JsonIgnoreAttribute? ignore = property.GetCustomAttribute<JsonIgnoreAttribute>(true);
            return ignore != null && ignore.Condition == JsonIgnoreCondition.Always;
        }

        /// <summary>
        /// Whether a type is a collection the bound applies to: an array, or anything implementing
        /// <see cref="IEnumerable"/> other than a string.
        /// </summary>
        /// <param name="type">Type.</param>
        /// <returns>True for a collection type.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is null.</exception>
        public static bool IsCollectionType(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (type == typeof(string)) return false;
            if (type == typeof(byte[])) return false;
            return type.IsArray || typeof(IEnumerable).IsAssignableFrom(type);
        }

        #endregion

        #region Private-Methods

        private static void ValidateObject(object value, string path, HashSet<object> visited)
        {
            Type type = value.GetType();
            if (!IsArmadaModel(type)) return;
            if (!visited.Add(value)) return;

            foreach (BoundProperty bound in PropertiesOf(type))
            {
                object? child;
                try
                {
                    child = bound.Property.GetValue(value);
                }
                catch (TargetInvocationException)
                {
                    continue;
                }

                if (child == null) continue;
                string childPath = path.Length == 0 ? bound.Property.Name : path + "." + bound.Property.Name;

                if (child is IEnumerable enumerable && !(child is string))
                {
                    if (bound.Attribute != null)
                    {
                        long count = CountOf(enumerable);
                        if (count > bound.Attribute.Limit) throw new CollectionLimitExceededException(childPath, bound.Attribute.Limit, count);
                    }

                    if (child is IDictionary dictionary)
                    {
                        foreach (object? item in dictionary.Values)
                        {
                            if (item != null) ValidateObject(item, childPath, visited);
                        }
                    }
                    else
                    {
                        foreach (object? item in enumerable)
                        {
                            if (item != null) ValidateObject(item, childPath, visited);
                        }
                    }
                }
                else
                {
                    ValidateObject(child, childPath, visited);
                }
            }
        }

        private static long CountOf(IEnumerable enumerable)
        {
            if (enumerable is ICollection collection) return collection.Count;
            long count = 0;
            foreach (object? _ in enumerable) count++;
            return count;
        }

        private static bool IsArmadaModel(Type type)
        {
            if (type.IsPrimitive || type.IsEnum || type == typeof(string)) return false;
            string? ns = type.Namespace;
            return ns != null && ns.StartsWith("Armada.", StringComparison.Ordinal) && !type.IsDefined(typeof(CompilerGeneratedAttribute), false);
        }

        private static List<BoundProperty> PropertiesOf(Type type)
        {
            return _Properties.GetOrAdd(type, t => t
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .Where(p => !IsNeverSerialized(p))
                .Where(p => IsCollectionType(p.PropertyType) || IsArmadaModel(p.PropertyType) || p.PropertyType == typeof(object))
                .Select(p => new BoundProperty(p, BoundOf(p)))
                .ToList());
        }

        #endregion
    }
}
