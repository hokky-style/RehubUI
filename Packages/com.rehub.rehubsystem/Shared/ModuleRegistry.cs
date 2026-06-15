using System;
using System.Collections.Generic;

namespace RehubSystem.EditorShared
{
    public class ModuleRegistryItem
    {
        public string PrefabGuid { get; private set; }

        private readonly string _i18nKey;
        private readonly InternalEditorI18n _internalEditorI18n;

        public ModuleRegistryItem(InternalEditorI18n i18nJsonGuid, string i18nKey, string prefabGuid)
        {
            PrefabGuid = prefabGuid;
            _internalEditorI18n = i18nJsonGuid;
            _i18nKey = i18nKey;
        }

        public string GetTitle()
        {
            return _internalEditorI18n.GetTranslation(_i18nKey);
        }
    }

    public static class ModuleRegistry
    {
        private static readonly List<Action> _onModuleRegistered = new List<Action>();
        private static readonly Dictionary<string, ModuleRegistryItem> _moduleList = new Dictionary<string, ModuleRegistryItem>() {
            { "ModuleListModule", new ModuleRegistryItem(EditorI18n.InternalEditorI18n, "moduleList", "ed33d988b3d716742ab7e0adade59fcc") },
            { "WorldChangelogModule", new ModuleRegistryItem(EditorI18n.InternalEditorI18n, "worldChangelog", "fb42e405e8429a640a8888686a29bc4b") },
            { "FreeTextModule", new ModuleRegistryItem(EditorI18n.InternalEditorI18n, "freeText", "e2c7e8e62ca41ed46908f8a196e671ab") },
            { "UIElementsTestModule", new ModuleRegistryItem(EditorI18n.InternalEditorI18n, "uiElementsTest", "9a7e31cc0f654f3a9d7d4c4f08d824a1") },
            { "VideoPlayerModule", new ModuleRegistryItem(EditorI18n.InternalEditorI18n, "videoPlayer", "5c1a34c6d5f64d54a9c57c4d6b72a35d") }
        };

        public static Dictionary<string, ModuleRegistryItem> ModuleList => _moduleList;

        public static void RegisterModule(Dictionary<string, ModuleRegistryItem> moduleRegistryItem)
        {
            foreach (var item in moduleRegistryItem)
            {
                _moduleList.Add(item.Key, item.Value);
            }

            foreach (var action in _onModuleRegistered)
            {
                action.Invoke();
            }
        }

        public static void OnModuleRegistered(Action action)
        {
            _onModuleRegistered.Add(action);
        }
    }
}
