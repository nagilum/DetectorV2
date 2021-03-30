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
        /// Get value from config.
        /// </summary>
        /// <param name="keys">Key, with depths, to fetch for.</param>
        /// <returns>Value.</returns>
        public static string Get(params string[] keys)
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
                    Cache ??= new Dictionary<string, object>();

                    if (Cache.ContainsKey(cacheKey))
                    {
                        Cache[cacheKey] = dict[keys[i]];
                    }
                    else
                    {
                        Cache.Add(cacheKey, dict[keys[i]]);
                    }

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
    }
}
