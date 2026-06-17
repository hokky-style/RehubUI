using System;
using System.IO;
using System.Net.Http;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Data;
using RehubSystem.EditorShared;

namespace RehubSystem.Editor
{
    public static class Updater
    {
        public const string ListingUrl = "https://raw.githubusercontent.com/hokky-style/RehubUI/refs/heads/main/version-listing.example.json";

        private static readonly UnityEditor.PackageManager.PackageInfo _packageInfo;
        private static Version _latestVersion;
        private static string _updatePackageUrl = string.Empty;
        private static bool _checkingForUpdate = false;
        private static bool _availableUpdate = false;

        public static bool AvailableUpdate => _availableUpdate;
        public static bool CheckingForUpdate => _checkingForUpdate;
        public static string LatestVersion => _latestVersion?.ToString();
        public static string CurrentVersion => _packageInfo == null ? "1.0.0" : _packageInfo.version;
        public static bool CanInstallUpdate => _availableUpdate && !string.IsNullOrEmpty(_updatePackageUrl);

        static Updater()
        {
            _packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(CoreMenu).Assembly);
            CheckForUpdate();
        }

        public static async void CheckForUpdate()
        {
            _checkingForUpdate = true;

            try
            {
                using (var client = new HttpClient())
                {
                    var result = await client.GetStringAsync(BuildListingRequestUrl());
                    ReadListing(result);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Rehub System Updater] Failed to check updates: {e.Message}");
                _latestVersion = null;
                _updatePackageUrl = string.Empty;
                _availableUpdate = false;
            }
            finally
            {
                _checkingForUpdate = false;
            }
        }

        public static async void RunUpdate()
        {
            if (!CanInstallUpdate || EditorApplication.isPlaying || EditorApplication.isCompiling)
            {
                Debug.LogError("[Rehub System Updater] Update was not executed. There is no update package or Unity is busy.");
                return;
            }

            try
            {
                var directory = Path.Combine(Path.GetTempPath(), "RehubSystem");
                Directory.CreateDirectory(directory);

                var filePath = Path.Combine(directory, "RehubSystem-" + LatestVersion + ".unitypackage");
                using (var client = new HttpClient())
                {
                    var bytes = await client.GetByteArrayAsync(_updatePackageUrl);
                    File.WriteAllBytes(filePath, bytes);
                }

                AssetDatabase.ImportPackage(filePath, false);
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Rehub System Updater", EditorI18n.GetTranslation("updateSuccessfull"), "OK");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Rehub System Updater] Failed to install update: {e.Message}");
                EditorUtility.DisplayDialog("Rehub System Updater", EditorI18n.GetTranslation("updateFailed"), "OK");
            }
        }

        private static void ReadListing(string result)
        {
            _latestVersion = null;
            _updatePackageUrl = string.Empty;
            _availableUpdate = false;

            if (!VRCJson.TryDeserializeFromJson(result, out var listing) || listing.TokenType != TokenType.DataDictionary)
            {
                Debug.LogError($"[Rehub System Updater] Failed to parse version listing: {result}");
                return;
            }

            var root = listing.DataDictionary;
            if (!root.TryGetValue("com.rehub.rehubsystem", TokenType.DataDictionary, out var packageListing))
            {
                Debug.LogError("[Rehub System Updater] Version listing does not contain com.rehub.rehubsystem.");
                return;
            }

            var package = packageListing.DataDictionary;
            if (!package.TryGetValue("system", TokenType.String, out var systemVersion))
            {
                Debug.LogError("[Rehub System Updater] Version listing does not contain system version.");
                return;
            }

            if (!Version.TryParse(systemVersion.String, out _latestVersion))
            {
                Debug.LogError($"[Rehub System Updater] Invalid system version: {systemVersion.String}");
                return;
            }

            if (package.TryGetValue("unityPackageUrl", TokenType.String, out var unityPackageUrl))
            {
                _updatePackageUrl = unityPackageUrl.String;
            }

            if (!Version.TryParse(CurrentVersion, out var currentVersion))
            {
                currentVersion = new Version(1, 0, 0);
            }

            _availableUpdate = _latestVersion > currentVersion;
        }

        private static string BuildListingRequestUrl()
        {
            return ListingUrl + "?rehubCacheBust=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }
}
