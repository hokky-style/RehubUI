using System;
using UnityEngine;
using System.Net.Http;
using System.Collections.Generic;
using VRC.SDK3.Data;
using UnityEditor;

namespace RehubSystem.Editor
{
    public static class ModuleVersionManager
    {
        public const string ListingUrl = "https://raw.githubusercontent.com/hokky-style/RehubUI/main/version-listing.example.json";
        public const string ReleasesUrl = "https://github.com/hokky-style/RehubUI/releases";
        private static readonly Dictionary<string, Version> _latestVersions = new Dictionary<string, Version>();
        private static readonly Dictionary<string, Version> _currentVersions = new Dictionary<string, Version>();
        private static readonly List<string> _updateAvailableModules = new List<string>();

        private static readonly Dictionary<string, string> _displayNameCache = new Dictionary<string, string>();

        public static Dictionary<string, Version> LatestVersions => _latestVersions;
        public static Dictionary<string, Version> CurrentVersions => _currentVersions;
        public static List<string> UpdateAvailableModules => _updateAvailableModules;
        public static bool AvailableUpdate => _updateAvailableModules.Count > 0;
        public static bool AvailableModules => _currentVersions.Count > 0;
        static ModuleVersionManager()
        {
            CheckLatestVersions();
        }

        public static void RefreshCurrentVersions(IEnumerable<ModuleMetadata> modules)
        {
            _currentVersions.Clear();
            _displayNameCache.Clear();

            var self = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ModuleVersionManager).Assembly);
            if (self != null)
            {
                _currentVersions[self.name] = new Version(self.version);
                _displayNameCache[self.name] = self.displayName;
            }

            if (modules != null)
            {
                foreach (var module in modules)
                {
                    if (module == null || string.IsNullOrEmpty(module.ModuleId)) continue;

                    var version = string.IsNullOrEmpty(module.moduleVersion) ? "1.0.0" : module.moduleVersion;
                    if (Version.TryParse(version, out var parsedVersion))
                    {
                        _currentVersions[module.ModuleId] = parsedVersion;
                    }
                    else
                    {
                        _currentVersions[module.ModuleId] = new Version(1, 0, 0);
                    }

                    _displayNameCache[module.ModuleId] = module.moduleName;
                }
            }

            RefreshUpdateState();
        }

        public static async void CheckLatestVersions()
        {
            var listingUrl = ListingUrl;
            if (string.IsNullOrEmpty(listingUrl)) return;

            using var client = new HttpClient();
            var result = string.Empty;

            try
            {
                result = await client.GetStringAsync(listingUrl).ContinueWith(task =>
                {
                    if (!task.IsCompletedSuccessfully) throw task.Exception;
                    return task.Result;
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"[ModuleVersionManager] Request Failed: {e.Message}");
            }

            if (!VRCJson.TryDeserializeFromJson(result, out var listing) || listing.TokenType != TokenType.DataDictionary)
            {
                Debug.LogError($"[ModuleVersionManager] Failed to parse JSON: {result}");
                return;
            }

            _latestVersions.Clear();
            var self = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ModuleVersionManager).Assembly);
            var root = listing.DataDictionary;

            if (self != null && root.TryGetValue(self.name, TokenType.String, out var systemVersion))
            {
                _latestVersions[self.name] = new Version(systemVersion.String);
            }

            if (self != null && root.TryGetValue(self.name, TokenType.DataDictionary, out var packageListing))
            {
                ReadPackageListing(self.name, packageListing.DataDictionary);
            }

            if (root.TryGetValue("modules", TokenType.DataDictionary, out var moduleListing))
            {
                ReadModuleListing(moduleListing.DataDictionary);
            }

            RefreshUpdateState();
        }

        private static void ReadPackageListing(string packageName, DataDictionary packageListing)
        {
            if (packageListing.TryGetValue("system", TokenType.String, out var systemVersion))
            {
                _latestVersions[packageName] = new Version(systemVersion.String);
            }

            if (packageListing.TryGetValue("modules", TokenType.DataDictionary, out var modules))
            {
                ReadModuleListing(modules.DataDictionary);
                return;
            }

            ReadModuleListing(packageListing);
        }

        private static void ReadModuleListing(DataDictionary modules)
        {
            foreach (var module in modules)
            {
                if (module.Key.TokenType == TokenType.String && module.Key.String == "system") continue;

                if (module.Key.TokenType != TokenType.String || module.Value.TokenType != TokenType.String)
                {
                    Debug.LogError($"[ModuleVersionManager] Invalid module format: {module.Key}");
                    continue;
                }

                _latestVersions[module.Key.String] = new Version(module.Value.String);
            }
        }

        private static void RefreshUpdateState()
        {
            _updateAvailableModules.Clear();
            foreach (var module in _latestVersions.Keys)
            {
                if (_currentVersions.TryGetValue(module, out var currentVersion) && _latestVersions[module] > currentVersion)
                {
                    _updateAvailableModules.Add(module);
                }
            }
        }

        public static string GetPackageName(string moduleName)
        {
            if (_displayNameCache.TryGetValue(moduleName, out var packageName))
            {
                return packageName;
            }

            return moduleName;
        }

        public static bool HasUpdate(string moduleName)
        {
            return _updateAvailableModules.Contains(moduleName);
        }

        public static string GetLatestVersion(string moduleName)
        {
            if (_latestVersions.TryGetValue(moduleName, out var version))
            {
                return version.ToString();
            }

            return string.Empty;
        }
    }
}
