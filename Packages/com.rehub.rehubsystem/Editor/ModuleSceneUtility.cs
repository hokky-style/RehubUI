using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RehubSystem.Editor
{
    public static class ModuleSceneUtility
    {
        private const string ModulesSearchRoot = "Packages/com.rehub.rehubsystem/Runtime/Modules";

        public static List<ModuleMetadata> SyncInstalledModulesToScene(Transform moduleContainer)
        {
            var modules = GetSceneModules(moduleContainer);
            if (moduleContainer == null) return modules;

            foreach (var prefab in GetInstalledModulePrefabs())
            {
                if (prefab == null) continue;

                var prefabMetadata = prefab.GetComponent<ModuleMetadata>();
                if (prefabMetadata == null || string.IsNullOrEmpty(prefabMetadata.ModuleId)) continue;

                var alreadyExists = modules.Any(module => module != null && module.ModuleId == prefabMetadata.ModuleId);
                if (alreadyExists && prefabMetadata.IsUnique) continue;

                var instance = PrefabUtility.InstantiatePrefab(prefab, moduleContainer) as GameObject;
                if (instance == null) continue;

                var metadata = instance.GetComponent<ModuleMetadata>();
                if (metadata == null) continue;

                ConfigureModuleInstance(instance, moduleContainer);
                modules.Add(metadata);
            }

            return modules;
        }

        public static List<ModuleMetadata> GetSceneModules(Transform moduleContainer)
        {
            if (moduleContainer == null) return new List<ModuleMetadata>();
            return moduleContainer.GetComponentsInChildren<ModuleMetadata>(true).ToList();
        }

        public static void ConfigureModuleInstance(GameObject moduleObject, Transform moduleContainer)
        {
            if (moduleObject == null || moduleContainer == null) return;

            var moduleList = moduleObject.GetComponent<AssetListModule>();
            if (moduleList == null) return;

            var moduleManager = moduleContainer.root.GetComponentInChildren<ModuleManager>(true);
            if (moduleManager == null) return;

            var serializedModuleList = new SerializedObject(moduleList);
            serializedModuleList.FindProperty("_moduleManager").objectReferenceValue = moduleManager;
            serializedModuleList.ApplyModifiedProperties();
        }

        public static string GetEmbeddedModuleVersion(string moduleId)
        {
            var prefab = FindInstalledModulePrefab(moduleId);
            if (prefab == null) return string.Empty;

            var metadata = prefab.GetComponent<ModuleMetadata>();
            if (metadata == null) return string.Empty;

            return string.IsNullOrEmpty(metadata.moduleVersion) ? "1.0.0" : metadata.moduleVersion;
        }

        public static GameObject FindInstalledModulePrefab(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId)) return null;

            var normalizedTargetId = NormalizeModuleId(moduleId);
            foreach (var prefab in GetInstalledModulePrefabs())
            {
                if (prefab == null) continue;

                var metadata = prefab.GetComponent<ModuleMetadata>();
                if (metadata == null) continue;

                var normalizedInstalledId = NormalizeModuleId(metadata.ModuleId);
                if (metadata.ModuleId == moduleId || normalizedInstalledId == normalizedTargetId)
                {
                    return prefab;
                }
            }

            return null;
        }

        private static IEnumerable<GameObject> GetInstalledModulePrefabs()
        {
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { ModulesSearchRoot });
            foreach (var guid in prefabGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && prefab.GetComponent<ModuleMetadata>() != null)
                {
                    yield return prefab;
                }
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
}
