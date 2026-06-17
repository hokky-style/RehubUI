using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using UnityEditor;
using UnityEngine;
using RehubSystem.EditorShared;

namespace RehubSystem.Editor
{
    public class ModuleMarketplaceWindow : EditorWindow
    {
        private const string MarketplaceUrl = "https://raw.githubusercontent.com/hokky-style/RehubUI/refs/heads/main/module-marketplace.example.json";
        private const string DiscordUrl = "https://discord.gg/EKNUQDsKSK";
        private const string FallbackMarketplacePath = "Packages/com.rehub.rehubsystem/Assets/module-marketplace.example.json";

        private readonly List<MarketplaceModule> _modules = new List<MarketplaceModule>();
        private Vector2 _scroll;
        private string _status;
        private bool _isLoading;
        private string _installingModuleName;

        [MenuItem("Window/RehubUI/Module Marketplace")]
        public static void OpenWindow()
        {
            var window = GetWindow<ModuleMarketplaceWindow>("Module Marketplace");
            window.minSize = new Vector2(520, 420);
            window.Show();
        }

        private void OnEnable()
        {
            if (_modules.Count == 0 && !_isLoading)
            {
                RefreshMarketplace();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(EditorI18n.GetTranslation("moduleMarketplace"), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(EditorI18n.GetTranslation("moduleMarketplaceDescription"), EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(EditorI18n.GetTranslation("refreshMarketplace"), GUILayout.Height(28)))
                {
                    RefreshMarketplace();
                }

                if (GUILayout.Button(EditorI18n.GetTranslation("openDiscord"), GUILayout.Height(28)))
                {
                    Application.OpenURL(DiscordUrl);
                }
            }

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.HelpBox(_status, MessageType.Info);
            }

            EditorGUILayout.Space();

            if (_isLoading)
            {
                EditorGUILayout.LabelField(EditorI18n.GetTranslation("loadingMarketplace"));
                return;
            }

            if (_modules.Count == 0)
            {
                EditorGUILayout.HelpBox(EditorI18n.GetTranslation("marketplaceEmpty"), MessageType.Warning);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var module in _modules)
            {
                DrawModule(module);
                EditorGUILayout.Space(8);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawModule(MarketplaceModule module)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(module.DisplayName, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField($"v{module.version}", GUILayout.Width(70));
                }

                if (!string.IsNullOrEmpty(module.description))
                {
                    EditorGUILayout.LabelField(module.description, EditorStyles.wordWrappedLabel);
                }

                EditorGUILayout.LabelField(module.id, EditorStyles.miniLabel);

                var installedVersion = GetEmbeddedModuleVersion(module.ModuleId);
                var installState = GetInstallState(installedVersion, module.version);
                if (!string.IsNullOrEmpty(installedVersion))
                {
                    EditorGUILayout.LabelField($"{EditorI18n.GetTranslation("installedVersion")}: v{installedVersion}");
                }

                EditorGUILayout.LabelField(GetVersionStatusText(installState, installedVersion, module.version), EditorStyles.miniLabel);

                EditorGUILayout.LabelField($"{EditorI18n.GetTranslation("installMode")}: UnityPackage", EditorStyles.miniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(module.unityPackageUrl) || _isLoading || installState == MarketplaceInstallState.InstalledLatest);
                    var installLabel = installState == MarketplaceInstallState.NotInstalled
                        ? EditorI18n.GetTranslation("installModule")
                        : installState == MarketplaceInstallState.UpdateAvailable
                            ? EditorI18n.GetTranslation("updateModule")
                            : EditorI18n.GetTranslation("installedLatestVersion");

                    if (GUILayout.Button(installLabel))
                    {
                        InstallModule(module);
                    }
                    EditorGUI.EndDisabledGroup();

                    EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(module.pageUrl));
                    if (GUILayout.Button(EditorI18n.GetTranslation("openModulePage")))
                    {
                        Application.OpenURL(module.pageUrl);
                    }
                    EditorGUI.EndDisabledGroup();
                }
            }
        }

        private async void RefreshMarketplace()
        {
            _isLoading = true;
            _status = EditorI18n.GetTranslation("loadingMarketplace");
            Repaint();

            try
            {
                var result = string.Empty;
                var loadedFromFallback = false;

                try
                {
                    using (var client = new HttpClient())
                    {
                        result = await client.GetStringAsync(BuildMarketplaceRequestUrl());
                    }
                }
                catch
                {
                    result = LoadFallbackMarketplaceJson();
                    loadedFromFallback = true;
                }

                var root = JsonUtility.FromJson<MarketplaceRoot>(result);
                _modules.Clear();

                if (root != null && root.modules != null)
                {
                    foreach (var module in root.modules)
                    {
                        if (module != null && !string.IsNullOrEmpty(module.id))
                        {
                            _modules.Add(module);
                        }
                    }
                }

                _status = string.Format(EditorI18n.GetTranslation(loadedFromFallback ? "marketplaceLoadedFromFallback" : "marketplaceLoaded"), _modules.Count, MarketplaceUrl);
            }
            catch (Exception e)
            {
                _status = $"{EditorI18n.GetTranslation("marketplaceLoadFailed")}: {e.Message}";
            }
            finally
            {
                _isLoading = false;
                Repaint();
            }
        }

        private static string LoadFallbackMarketplaceJson()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(FallbackMarketplacePath);
            if (asset != null)
            {
                return asset.text;
            }

            if (File.Exists(FallbackMarketplacePath))
            {
                return File.ReadAllText(FallbackMarketplacePath);
            }

            return "{\"version\":1,\"modules\":[]}";
        }

        private static string BuildMarketplaceRequestUrl()
        {
            return MarketplaceUrl + "?rehubCacheBust=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private async void InstallModule(MarketplaceModule module)
        {
            if (string.IsNullOrEmpty(module.unityPackageUrl)) return;

            _installingModuleName = module.DisplayName;
            _status = string.Format(EditorI18n.GetTranslation("installingModule"), _installingModuleName);
            Repaint();

            try
            {
                var directory = Path.Combine(Path.GetTempPath(), "RehubUI");
                Directory.CreateDirectory(directory);

                var fileName = SanitizeFileName(module.ModuleId) + "-" + SanitizeFileName(module.version) + ".unitypackage";
                var filePath = Path.Combine(directory, fileName);

                using (var client = new HttpClient())
                {
                    var bytes = await client.GetByteArrayAsync(module.unityPackageUrl);
                    File.WriteAllBytes(filePath, bytes);
                }

                AssetDatabase.ImportPackage(filePath, false);
                AssetDatabase.Refresh();
                _status = string.Format(EditorI18n.GetTranslation("moduleInstallSuccess"), _installingModuleName);
            }
            catch (Exception e)
            {
                _status = $"{EditorI18n.GetTranslation("moduleInstallFailed")}: {e.Message}";
            }
            finally
            {
                _installingModuleName = null;
                Repaint();
            }
        }

        private static string GetEmbeddedModuleVersion(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId)) return string.Empty;

            var normalizedTargetId = NormalizeModuleId(moduleId);
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Packages/com.rehub.rehubsystem/Runtime/Modules" });
            foreach (var guid in prefabGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                var metadata = prefab.GetComponent<global::RehubSystem.ModuleMetadata>();
                if (metadata == null) continue;

                var normalizedInstalledId = NormalizeModuleId(metadata.ModuleId);
                if (metadata.ModuleId != moduleId && normalizedInstalledId != normalizedTargetId) continue;

                return string.IsNullOrEmpty(metadata.moduleVersion) ? "1.0.0" : metadata.moduleVersion;
            }

            return string.Empty;
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "module";

            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidChar, '-');
            }

            return value;
        }

        private static MarketplaceInstallState GetInstallState(string installedVersion, string marketplaceVersion)
        {
            if (string.IsNullOrEmpty(installedVersion)) return MarketplaceInstallState.NotInstalled;

            if (!Version.TryParse(installedVersion, out var installed))
            {
                installed = new Version(1, 0, 0);
            }

            if (!Version.TryParse(marketplaceVersion, out var latest))
            {
                return MarketplaceInstallState.InstalledLatest;
            }

            return latest > installed ? MarketplaceInstallState.UpdateAvailable : MarketplaceInstallState.InstalledLatest;
        }

        private static string GetVersionStatusText(MarketplaceInstallState state, string installedVersion, string marketplaceVersion)
        {
            switch (state)
            {
                case MarketplaceInstallState.NotInstalled:
                    return EditorI18n.GetTranslation("moduleNotInstalled");
                case MarketplaceInstallState.UpdateAvailable:
                    return $"{EditorI18n.GetTranslation("updateAvailable")} (v{installedVersion} -> v{marketplaceVersion})";
                default:
                    return $"{EditorI18n.GetTranslation("upToDate")} (v{installedVersion})";
            }
        }

        private static string NormalizeModuleId(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var normalized = value.ToLowerInvariant()
                .Replace(" ", string.Empty)
                .Replace("-", string.Empty)
                .Replace("_", string.Empty)
                .Replace(".", string.Empty);

            return normalized.EndsWith("module") ? normalized.Substring(0, normalized.Length - "module".Length) : normalized;
        }
    }

    internal enum MarketplaceInstallState
    {
        NotInstalled,
        InstalledLatest,
        UpdateAvailable
    }

    [Serializable]
    public class MarketplaceRoot
    {
        public int version;
        public MarketplaceModule[] modules;
    }

    [Serializable]
    public class MarketplaceModule
    {
        public string id;
        public string name;
        public string version;
        public string description;
        public string author;
        public string moduleId;
        public string unityPackageUrl;
        public string pageUrl;

        public string DisplayName => string.IsNullOrEmpty(name) ? id : name;
        public string ModuleId => string.IsNullOrEmpty(moduleId) ? id : moduleId;
    }
}
