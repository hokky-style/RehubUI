using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using RehubSystem.EditorShared;

namespace RehubSystem.Editor
{
    public class ModuleMarketplaceWindow : EditorWindow
    {
        private const string MarketplaceUrl = "https://raw.githubusercontent.com/hokky-style/RehubUI/main/module-marketplace.example.json";
        private const string DiscordUrl = "https://discord.gg/EKNUQDsKSK";
        private const string FallbackMarketplacePath = "Packages/com.rehub.rehubsystem/Assets/module-marketplace.example.json";

        private readonly List<MarketplaceModule> _modules = new List<MarketplaceModule>();
        private Vector2 _scroll;
        private string _status;
        private bool _isLoading;
        private AddRequest _installRequest;
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

        private void OnDisable()
        {
            EditorApplication.update -= WatchInstallRequest;
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

                var installedVersion = GetInstalledVersion(module.id);
                if (string.IsNullOrEmpty(installedVersion))
                {
                    installedVersion = GetEmbeddedModuleVersion(module.ModuleId);
                }

                if (!string.IsNullOrEmpty(installedVersion))
                {
                    EditorGUILayout.LabelField($"{EditorI18n.GetTranslation("installedVersion")}: v{installedVersion}");
                }

                EditorGUILayout.LabelField($"{EditorI18n.GetTranslation("installMode")}: {module.InstallModeLabel}", EditorStyles.miniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginDisabledGroup(!module.HasInstallSource || _installRequest != null || _isLoading);
                    var installLabel = string.IsNullOrEmpty(installedVersion)
                        ? EditorI18n.GetTranslation("installModule")
                        : EditorI18n.GetTranslation("updateModule");

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
                        result = await client.GetStringAsync(MarketplaceUrl);
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

                _status = string.Format(EditorI18n.GetTranslation(loadedFromFallback ? "marketplaceLoadedFromFallback" : "marketplaceLoaded"), _modules.Count);
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

        private async void InstallModule(MarketplaceModule module)
        {
            _installingModuleName = module.DisplayName;
            _status = string.Format(EditorI18n.GetTranslation("installingModule"), _installingModuleName);
            Repaint();

            if (!string.IsNullOrEmpty(module.unityPackageUrl))
            {
                await InstallUnityPackage(module);
                return;
            }

            if (!string.IsNullOrEmpty(module.packageUrl))
            {
                _installRequest = Client.Add(module.packageUrl);
                EditorApplication.update -= WatchInstallRequest;
                EditorApplication.update += WatchInstallRequest;
            }
        }

        private async System.Threading.Tasks.Task InstallUnityPackage(MarketplaceModule module)
        {
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
                _status = string.Format(EditorI18n.GetTranslation("moduleInstallSuccess"), module.DisplayName);
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

        private void WatchInstallRequest()
        {
            if (_installRequest == null || !_installRequest.IsCompleted) return;

            if (_installRequest.Status == StatusCode.Success)
            {
                _status = string.Format(EditorI18n.GetTranslation("moduleInstallSuccess"), _installingModuleName);
            }
            else
            {
                _status = $"{EditorI18n.GetTranslation("moduleInstallFailed")}: {_installRequest.Error?.message}";
            }

            _installRequest = null;
            _installingModuleName = null;
            EditorApplication.update -= WatchInstallRequest;
            Repaint();
        }

        private static string GetInstalledVersion(string packageName)
        {
            foreach (var package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
            {
                if (package.name == packageName)
                {
                    return package.version;
                }
            }

            return string.Empty;
        }

        private static string GetEmbeddedModuleVersion(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId)) return string.Empty;

            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Packages/com.rehub.rehubsystem/Runtime/Modules" });
            foreach (var guid in prefabGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                var metadata = prefab.GetComponent<ModuleMetadata>();
                if (metadata == null || metadata.ModuleId != moduleId) continue;

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
        public string packageUrl;
        public string unityPackageUrl;
        public string pageUrl;

        public string DisplayName => string.IsNullOrEmpty(name) ? id : name;
        public string ModuleId => string.IsNullOrEmpty(moduleId) ? id : moduleId;
        public bool HasInstallSource => !string.IsNullOrEmpty(packageUrl) || !string.IsNullOrEmpty(unityPackageUrl);
        public string InstallModeLabel => !string.IsNullOrEmpty(unityPackageUrl) ? "UnityPackage" : "UPM";
    }
}
