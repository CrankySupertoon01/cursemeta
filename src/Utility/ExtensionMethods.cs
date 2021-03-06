using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using Cursemeta.AddOnService;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cursemeta {
    public static class ExtensionMethods {
        public static void ClearReadOnly (this DirectoryInfo parentDirectory) {
            if (parentDirectory != null) {
                parentDirectory.Attributes = FileAttributes.Normal;
                foreach (FileInfo fi in parentDirectory.GetFiles ()) {
                    fi.Attributes = FileAttributes.Normal;
                }
                foreach (DirectoryInfo di in parentDirectory.GetDirectories ()) {
                    di.ClearReadOnly ();
                }
            }
        }

        private static readonly JsonSerializerSettings settings =
            new JsonSerializerSettings {
                Formatting = Formatting.None,
                NullValueHandling = NullValueHandling.Ignore,
                Converters = { new StringEnumConverter { CamelCaseText = true } }
            };

        private static readonly JsonSerializerSettings settingsPretty =
            new JsonSerializerSettings {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                Converters = { new StringEnumConverter { CamelCaseText = true } }
            };

        public static string ToPrettyJson (this object obj, bool pretty = true) => JsonConvert.SerializeObject (obj, pretty ? settingsPretty : settings);

        public static T FromJson<T> (this string jsonText) {
            return JsonConvert.DeserializeObject<T> (jsonText, settingsPretty);
        }

        public static T FromJsonFile<T> (this string path) {
            if (File.Exists (path)) {
                string text = File.ReadAllText (path);
                return text.FromJson<T> ();
            }
            return default (T);
        }

        public static string FromTextFile (this string path) {
            if (File.Exists (path)) {
                string text = File.ReadAllText (path);
                return text;
            }
            return null;
        }

        private static readonly Serializer serializer =
            new SerializerBuilder ()
            .WithNamingConvention (new CamelCaseNamingConvention ())
            .EmitDefaults ()
            .Build ();

        private static readonly Deserializer deserializer =
            new DeserializerBuilder ()
            .IgnoreUnmatchedProperties ()
            .WithNamingConvention (new CamelCaseNamingConvention ())
            .Build ();

        public static string ToPrettyYaml (this object obj) => serializer.Serialize (obj);

        public static bool parseBool (this string input, bool defaultValue = false) {
            int status;
            if (int.TryParse (input, out status)) {
                return Convert.ToBoolean (status);
            } else {
                bool ret;
                if (bool.TryParse (input, out ret)) {
                    return ret;
                }
            }
            return defaultValue;
        }

        public static T CloneJson<T> (this T source) {
            // Don't serialize a null object, simply return the default for that object
            if (Object.ReferenceEquals (source, null)) {
                return default (T);
            }

            // initialize inner objects individually
            // for example in default constructor some list property initialized with some values,
            // but in 'source' these items are cleaned -
            // without ObjectCreationHandling.Replace default constructor values will be added to result
            var deserializeSettings = new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace };

            return JsonConvert.DeserializeObject<T> (JsonConvert.SerializeObject (source), deserializeSettings);
        }

        public static Object GetPropValue (this Object source, String name) {
            object obj = source;
            var rest = new List<string> (name.Split ('.'));
            foreach (String part in name.Split ('.')) {
                if (obj == null) { return null; }

                Type type = obj.GetType ();
                if (type.IsArray) {
                    object[] values = (object[]) obj;
                    string path = string.Join (".", rest);
                    return values.Select (o => o.GetPropValue (path));
                }
                PropertyInfo info = type.GetProperty (part, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
                if (info == null) { return null; }

                obj = info.GetValue (obj, null);
                rest.RemoveAt (0);
            }
            return obj;
        }

        public static dynamic ToDynamic<T> (this T obj) {
            IDictionary<string, object> expando = new ExpandoObject ();

            foreach (var propertyInfo in typeof (T).GetProperties ()) {
                var currentValue = propertyInfo.GetValue (obj);
                expando.Add (propertyInfo.Name, currentValue);
            }
            return expando as ExpandoObject;
        }

        // return true if the value did not exist before, TODO: compare objects
        public static bool UpdateOrAdd<K, V> (this ConcurrentDictionary<K, V> dictionary, K key,
            V value) {
            V old;
            dictionary.TryGetValue (key, out old);

            dictionary[key] = value;
            return old == null;
        }

        public static IEnumerable<IEnumerable<T>> Split<T> (this IEnumerable<T> source, int nSize = 30) {
            var temp = source;
            int i = 0;
            while (temp.ElementAtOrDefault (0) != null) {
                temp = temp.Skip (nSize);
                i += nSize;
                yield return temp.Take (nSize);
            }
            // for (int i = 0; i < source.Count (); i += nSize) {
            //     // yield return source.Skip (i).Take (Math.Min (nSize, source.Count () - i));
            //     yield return source.Skip (i).Take (nSize);
            // }
        }

        public static IEnumerable<IEnumerable<T>> Batch<T> (this IEnumerable<T> source, int size) {
            T[] bucket = null;
            var count = 0;

            foreach (var item in source) {
                if (bucket == null)
                    bucket = new T[size];

                bucket[count++] = item;

                if (count != size)
                    continue;

                // yield return bucket.Select (x => x);
                yield return bucket;

                bucket = null;
                count = 0;
            }

            // Return the last bucket with all remaining elements
            if (bucket != null && count > 0)
                yield return bucket.Take (count);
        }

        public static T ElementAtOr<T> (this IEnumerable<T> source, int index, T defaultValue = default (T)) {
            if (index >= source.Count ()) {
                return defaultValue;
            }
            return source.ElementAt (index);
        }

        public static string[] GetString (this IQueryCollection query, string name) {
            StringValues argumentValues;
            if (query.TryGetValue (name, out argumentValues)) {
                return argumentValues.ToArray ();
            }
            return new string[0];
        }

        public static int[] GetInt (this IQueryCollection query, string name, int defaultValue = default (int)) {
            StringValues argumentValues;
            if (query.TryGetValue (name, out argumentValues)) {
                var ret = argumentValues.Select (argumentValue => {
                    int value = defaultValue;
                    if (int.TryParse (argumentValue, out value)) {
                        return value;
                    }
                    return defaultValue;
                });
                return ret.ToArray ();
            }
            return new int[0];
        }

        public static bool[] GetBool (this IQueryCollection query, string name, bool defaultValue = default (bool)) {
            StringValues argumentValues;
            if (query.TryGetValue (name, out argumentValues)) {
                var ret = argumentValues.Select (argumentValue => {
                    return argumentValue.parseBool (defaultValue);
                });
                return ret.ToArray ();
            }
            return new bool[0];
        }

        public static bool isHidden (this AddOnService.FileStatus fileStatus) {
            return fileStatus == FileStatus.Deleted ||
                fileStatus != FileStatus.Normal ||
                fileStatus == FileStatus.NeedsApproval ||
                fileStatus == FileStatus.UnderReview ||
                fileStatus == FileStatus.Locked;
        }
    }
}