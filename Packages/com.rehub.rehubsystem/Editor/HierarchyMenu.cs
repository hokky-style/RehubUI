using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using RehubSystem.EditorShared;

namespace RehubSystem.Editor
{
    public static class HierachyMenu
    {
        private const string _menuParent = "GameObject/Rehub System/";
        private const string _menuPrefabGuid = "5d23638bac394824b8788f07f0d91c78";
        private static int _priority = 100;
        private static bool _initialized = false;

        private static void AddItem(string name, string prefabGuid, string shortcut = "", bool isChecked = false, Func<bool> validate = null)
        {
            MenuHelper.AddMenuItem(_menuParent + name, shortcut, isChecked, _priority++, () => GenerateObject(prefabGuid), validate);
        }

        private static void AddSeparator(string name = null)
        {
            MenuHelper.AddSeparator(_menuParent + (name ?? Guid.NewGuid().ToString()), _priority++);
        }

        public static void RemoveItem(string name)
        {
            MenuHelper.RemoveMenuItem(_menuParent + name);
        }

        public static void Update()
        {
            MenuHelper.Update();
        }

        private static void GenerateObject(string guid)
        {
            var item = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            GenerateObject(item);
        }

        private static void GenerateObject(GameObject item)
        {
            if (item == null) return;

            var prefab = PrefabUtility.InstantiatePrefab(item, Selection.activeTransform);
            if (prefab == null) return;

            Selection.activeGameObject = prefab as GameObject;
        }

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorApplication.delayCall += () => BuildMenu();
            ModuleRegistry.OnModuleRegistered(RebuildMenu);
        }

        private static void BuildMenu()
        {
            AddItem(EditorI18n.GetTranslation("menuItemRoot"), _menuPrefabGuid);
            AddSeparator();

            var installedModulePrefabs = ModuleSceneUtility.GetInstalledModulePrefabs()
                .Select(prefab => new { Prefab = prefab, Metadata = prefab.GetComponent<ModuleMetadata>() })
                .Where(item => item.Metadata != null)
                .OrderBy(item => GetModuleTitle(item.Metadata))
                .ToList();

            foreach (var module in installedModulePrefabs)
            {
                var prefab = module.Prefab;
                AddItem(EditorI18n.GetTranslation("modules") + "/" + GetModuleTitle(module.Metadata), prefab);
            }

            _initialized = true;
            Update();
        }

        private static void AddItem(string name, GameObject prefab, string shortcut = "", bool isChecked = false, Func<bool> validate = null)
        {
            MenuHelper.AddMenuItem(_menuParent + name, shortcut, isChecked, _priority++, () => GenerateObject(prefab), validate);
        }

        private static string GetModuleTitle(ModuleMetadata metadata)
        {
            if (metadata == null) return "Module";
            if (!metadata.forceUseModuleName && ModuleRegistry.ModuleList.TryGetValue(metadata.ModuleId, out var registryItem))
            {
                return registryItem.GetTitle();
            }

            return string.IsNullOrEmpty(metadata.moduleName) ? metadata.ModuleId : metadata.moduleName;
        }

        public static void RebuildMenu()
        {
            if (!_initialized) return;
            MenuHelper.RemoveAllMenuItems();
            BuildMenu();
        }
    }

    // https://qiita.com/Swanman/items/279b3b679f3f96a5f925
    internal static class MenuHelper
    {
        private static List<string> _registeredItems = new List<string>();

        public static void AddMenuItem(string name, string shortcut, bool isChecked, int priority, Action execute, Func<bool> validate)
        {
            _registeredItems.Add(name);
            var addMenuItemMethod = typeof(Menu).GetMethod("AddMenuItem", BindingFlags.Static | BindingFlags.NonPublic);
            addMenuItemMethod?.Invoke(null, new object[] { name, shortcut, isChecked, priority, execute, validate });
        }

        public static void AddSeparator(string name, int priority)
        {
            _registeredItems.Add(name);
            var addSeparatorMethod = typeof(Menu).GetMethod("AddSeparator", BindingFlags.Static | BindingFlags.NonPublic);
            addSeparatorMethod?.Invoke(null, new object[] { name, priority });
        }

        public static void RemoveMenuItem(string name, bool noRemoveItem = false)
        {
            if (_registeredItems.Contains(name) && !noRemoveItem)
            {
                _registeredItems.Remove(name);
            }

            var removeMenuItemMethod = typeof(Menu).GetMethod("RemoveMenuItem", BindingFlags.Static | BindingFlags.NonPublic);
            removeMenuItemMethod?.Invoke(null, new object[] { name });
        }

        public static void RemoveAllMenuItems()
        {
            foreach (var item in _registeredItems)
            {
                RemoveMenuItem(item, true);
            }

            _registeredItems.Clear();
        }

        public static void Update()
        {
            var internalUpdateAllMenus = typeof(EditorUtility).GetMethod("Internal_UpdateAllMenus", BindingFlags.Static | BindingFlags.NonPublic);
            internalUpdateAllMenus?.Invoke(null, null);

            var shortcutIntegrationType = Type.GetType("UnityEditor.ShortcutManagement.ShortcutIntegration, UnityEditor.CoreModule");
            var instanceProp = shortcutIntegrationType?.GetProperty("instance", BindingFlags.Static | BindingFlags.Public);
            var instance = instanceProp?.GetValue(null);
            var rebuildShortcutsMethod = instance?.GetType().GetMethod("RebuildShortcuts", BindingFlags.Instance | BindingFlags.NonPublic);
            rebuildShortcutsMethod?.Invoke(instance, null);
        }
    }
}
