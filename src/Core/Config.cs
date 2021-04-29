using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DetectorWorker.Core
{
    public class Config
    {
        #region Local storage

        /// <summary>
        /// Full path where config is located.
        /// </summary>
        private static string StoragePath =>
            Path.Combine(
                Directory.GetCurrentDirectory(),
                "config.json");

        /// <summary>
        /// Cache for faster lookups.
        /// </summary>
        private static Dictionary<string, object> Cache { get; set; }

        /// <summary>
        /// Internal storage.
        /// </summary>
        private static Dictionary<string, object> Storage { get; set; }

        #endregion

        #region IO functions

        /// <summary>
        /// Load config from disk.
        /// </summary>
        public static void Load()
        {
            if (!File.Exists(StoragePath))
            {
                throw new FileNotFoundException($"Unable to find config file: {StoragePath}");
            }

            Storage = JsonSerializer.Deserialize<Dictionary<string, object>>(
                File.ReadAllText(StoragePath),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }

        #endregion

        #region Getters

        /// <summary>
        /// Get value from environment or config.
        /// </summary>
        /// <param name="keys">Key, with depths, to fetch for.</param>
        /// <returns>Value, if found.</returns>
        public static string Get(params string[] keys)
        {
            return GetEnv(keys) ??
                   GetStorage(keys);
        }

        /// <summary>
        /// Get value purely from environment variables.
        /// </summary>
        /// <param name="keys">Key, with depths, to fetch for.</param>
        /// <returns>Value, if found.</returns>
        public static string GetEnv(params string[] keys)
        {
            if (keys.Length == 0)
            {
                return null;
            }

            var appName = GetStorage("app", "name");
            var envKey = $"{appName}_{string.Join("_", keys)}".ToUpper();
            var envValue = Environment.GetEnvironmentVariable(envKey);

            return envValue;
        }

        /// <summary>
        /// Get calue from storage.
        /// </summary>
        /// <param name="keys">Key, with depths, to fetch for.</param>
        /// <returns>Value, if found.</returns>
        public static string GetStorage(params string[] keys)
        {
            if (keys.Length == 0)
            {
                return null;
            }

            var cacheKey = string.Join("::", keys);

            if (Cache != null &&
                Cache.ContainsKey(cacheKey))
            {
                return Cache[cacheKey].ToString();
            }

            var dict = Storage;

            for (var i = 0; i < keys.Length; i++)
            {
                if (dict == null)
                {
                    return null;
                }

                if (!dict.ContainsKey(keys[i]))
                {
                    return null;
                }

                if (i == keys.Length - 1)
                {
                    Set(cacheKey, dict[keys[i]]);

                    return dict[keys[i]].ToString();
                }

                try
                {
                    var json = dict[keys[i]].ToString();

                    if (json == null)
                    {
                        throw new NullReferenceException($"Config for key {keys[i]} is empty.");
                    }

                    dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        #endregion

        #region Setters

        /// <summary>
        /// Add value to cache for quick fetch.
        /// </summary>
        /// <param name="key">Key to store under.</param>
        /// <param name="value">Value to set.</param>
        public static void Set(string key, object value)
        {
            Cache ??= new Dictionary<string, object>();

            if (Cache.ContainsKey(key))
            {
                Cache[key] = value;
            }
            else
            {
                Cache.Add(key, value);
            }
        }

        #endregion
    }
}
