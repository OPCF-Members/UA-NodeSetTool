using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;

namespace NodeSetTool
{
    public static class EventDrivenParser
    {
        public static T ParseJson<T>(string filePath) where T : class
        {
            using (var reader = new JsonTextReader(new StreamReader(filePath)))
            {
                return ParseJson<T>(reader);
            }
        }

        public static T ParseJson<T>(JsonTextReader reader) where T : class
        {
            var type = typeof(T);
            T instance = (T)CreateInstance(type);
            var props = ReflectionCache.GetDataMemberProperties(typeof(T));

            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.PropertyName)
                {
                    string propName = reader.Value.ToString();

                    if (props.TryGetValue(propName, out PropertyInfo propInfo))
                    {
                        reader.Read(); // move to value
                        object value = ConvertValue(reader, propName, propInfo.PropertyType);
                        propInfo.SetValue(instance, value);
                    }
                    else
                    {
                        reader.Skip(); // skip unknown property
                    }
                }
            }

            return instance;
        }

        public static T ParseObject<T>(JsonTextReader reader) where T : class
        {
            if (reader.TokenType != JsonToken.StartObject)
                throw new JsonException($"Expected StartObject but got {reader.TokenType}");
            return (T)ConvertValue(reader, typeof(T).Name, typeof(T));
        }

        public static object ParseObject(JsonTextReader reader, Type type)
        {
            if (reader.TokenType != JsonToken.StartObject)
                throw new JsonException($"Expected StartObject but got {reader.TokenType}");
            return ConvertValue(reader, type.Name, type);
        }

        private static object CreateInstance(Type type)
        {  
            var cs = type.GetConstructors()
                .Where(x => x.GetParameters().Where(y => !y.IsOptional).Count() == 0)
                .FirstOrDefault();

            if (cs != null)
            {
                var p = cs.GetParameters();

                List<object> values = new();

                foreach (var x in p)
                {
                    var ptype = x.ParameterType;

                    if (ptype.IsGenericType && ptype.GetGenericTypeDefinition() == typeof(Nullable<>))
                    {
                        ptype = Nullable.GetUnderlyingType(ptype);
                    }

                    if (x.HasDefaultValue && x.DefaultValue != DBNull.Value && x.DefaultValue != null)
                    {
                        if (ptype.IsEnum)
                        {
                            var y = Enum.ToObject(ptype, x.DefaultValue);
                            values.Add(y);
                        }
                        else
                        {
                            var y = Convert.ChangeType(x.DefaultValue, ptype, CultureInfo.InvariantCulture);
                            values.Add(y);
                        }
                    }
                    else
                    {
                        values.Add(x.ParameterType.IsValueType ? Activator.CreateInstance(ptype) : null);
                    }
                }

                return Activator.CreateInstance(type, values.ToArray());
            }

            return Activator.CreateInstance(type);
        }

        private static object ConvertValue(JsonTextReader reader, string path, Type targetType)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            if (targetType == typeof(object))
            {
                if (reader.TokenType == JsonToken.StartArray)
                {
                    var value = JArray.Load(reader);
                    return value;
                }
                else if (reader.TokenType == JsonToken.StartObject)
                {
                    var value = JObject.Load(reader);
                    return value;
                }
                else
                {
                    return reader.Value;
                }
            }

            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(targetType) && targetType != typeof(string))
            {
                if (reader.TokenType != JsonToken.StartArray)
                    throw new JsonException($"Expected StartArray for collection type {targetType}");

                var elementType = GetCollectionElementType(targetType);
                var listType = typeof(List<>).MakeGenericType(elementType);
                var list = (System.Collections.IList)Activator.CreateInstance(listType);

                int count = 0; 

                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.EndArray)
                    {
                        break;
                    }

                    var item = ConvertValue(reader, $"{path}.[{count++}]", elementType);
                    list.Add(item);
                }

                if (targetType.IsArray)
                {
                    var array = Array.CreateInstance(elementType, list.Count);
                    list.CopyTo(array, 0);
                    return array;
                }

                return list;
            }

            if (reader.TokenType == JsonToken.StartObject && IsComplexType(targetType))
            {
                if (targetType == typeof(object))
                {
                    var value = JObject.Load(reader);
                    return value;
                }

                var instance = CreateInstance(targetType);
                var props = ReflectionCache.GetDataMemberProperties(targetType);

                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.EndObject)
                        break;

                    if (reader.TokenType == JsonToken.PropertyName)
                    {
                        string propName = reader.Value.ToString();
                        if (props.TryGetValue(propName, out var prop))
                        {
                            reader.Read(); // move to value
                            var value = ConvertValue(reader, $"{path}.{propName}", prop.PropertyType);
                            prop.SetValue(instance, value);
                        }
                        else
                        {
                            reader.Skip(); // skip unknown property
                        }
                    }
                }

                return instance;
            }

            // Handle scalar values
            return ConvertSingleValue(reader, targetType);
        }

        private static bool IsComplexType(Type type)
        {
            return type.IsClass && type != typeof(string);
        }

        public static Enum ParseEnumeration(Type type, object value)
        {
            if (value is string text)
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    var attribute = field.GetCustomAttribute<EnumMemberAttribute>();

                    if (attribute?.Value == text)
                    {
                        return (Enum)field.GetValue(null);
                    }

                    if (field.Name.Equals(text, StringComparison.OrdinalIgnoreCase))
                    {
                        return (Enum)field.GetValue(null);
                    }
                }
            }

            if (value == null)
            {
                return (Enum)Enum.GetValues(type).GetValue(0);
            }

            return (Enum)Enum.ToObject(type, value);
        }

        private static object ConvertSingleValue(JsonTextReader reader, Type targetType)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                targetType = Nullable.GetUnderlyingType(targetType);
            }

            if (targetType.IsEnum)
            {
                return ParseEnumeration(targetType, reader.Value);
            }

            TypeCode code = Type.GetTypeCode(targetType);

            switch (code)
            {
                case TypeCode.String:
                    return reader.Value?.ToString();
                case TypeCode.Boolean:
                    return Convert.ToBoolean(reader.Value);
                case TypeCode.Byte:
                    return Convert.ToByte(reader.Value);
                case TypeCode.SByte:
                    return Convert.ToSByte(reader.Value);
                case TypeCode.Int16:
                    return Convert.ToInt16(reader.Value);
                case TypeCode.UInt16:
                    return Convert.ToUInt16(reader.Value);
                case TypeCode.Int32:
                    return Convert.ToInt32(reader.Value);
                case TypeCode.UInt32:
                    return Convert.ToUInt32(reader.Value);
                case TypeCode.Int64:
                    return Convert.ToInt64(reader.Value);
                case TypeCode.UInt64:
                    return Convert.ToUInt64(reader.Value);
                case TypeCode.Single:
                    return Convert.ToSingle(reader.Value);
                case TypeCode.Double:
                    return Convert.ToDouble(reader.Value);
                case TypeCode.Decimal:
                    return Convert.ToDecimal(reader.Value);
                case TypeCode.DateTime:
                    var raw = reader.Value?.ToString();
                    return DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                case TypeCode.Char:
                    return Convert.ToChar(reader.Value);
            }

            throw new NotSupportedException($"Unsupported value type: {targetType}");
        }

        private static Type GetCollectionElementType(Type collectionType)
        {
            if (collectionType.IsArray)
                return collectionType.GetElementType();

            if (collectionType.IsGenericType && collectionType.GetGenericTypeDefinition() == typeof(List<>))
                return collectionType.GetGenericArguments()[0];

            var iface = collectionType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            if (iface != null)
                return iface.GetGenericArguments()[0];

            throw new NotSupportedException($"Cannot determine element type for {collectionType}");
        }
    }

    public static class ReflectionCache
    {
        private static readonly ConcurrentDictionary<Type, ReadOnlyDictionary<string, PropertyInfo>> _cache = new();

        public static ReadOnlyDictionary<string, PropertyInfo> GetDataMemberProperties(Type type)
        {
            return _cache.GetOrAdd(type, t =>
            {
                return new ReadOnlyDictionary<string, PropertyInfo>(t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetCustomAttribute<DataMemberAttribute>() != null)
                    .ToDictionary(
                        p => p.GetCustomAttribute<DataMemberAttribute>()?.Name ?? p.Name,
                        p => p,
                        StringComparer.OrdinalIgnoreCase
                ));
            });
        }
    }
}
