using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using RehubSystem.EditorShared;

namespace RehubSystem.Editor
{
    [CustomEditor(typeof(RehubSystemCore))]
    public class CoreMenu : UnityEditor.Editor
    {
        private Transform _moduleContainer;
        private ThemeManager _themeManager;
        private List<ModuleMetadata> _modulesCache;
        private ListDrawer _listDrawer;
        private ThemePreset[] _themes;
        private SerializedObject _themeManagerSerializedObject;
        private SerializedObject _uiManagerSerializedObject;
        private SerializedObject _quickMenuManagerSerializedObject;
        private List<string> _usedUniqueModuleIds;
        private List<string> _duplicatedUniqueModuleIds;

        private bool _isReferencedByProjectWindow = false;

        private int _selectedTabIndex = 0;
        private string[] _themeNames = new string[0];
        private int _selectedThemeIndex = 0;
        private bool _showThemeSettings = false;
        private bool _showExtensionModuleReference = false;
        private Dictionary<DesktopQuickMenuOpenMethod, string> _desktopOpenMethodNames = new Dictionary<DesktopQuickMenuOpenMethod, string>
        {
            { DesktopQuickMenuOpenMethod.Tab, "openByTab" },
            { DesktopQuickMenuOpenMethod.ShiftTab, "openByShiftTab" },
        };
        private Dictionary<VRQuickMenuOpenMethod, string> _vrOpenMethodNames = new Dictionary<VRQuickMenuOpenMethod, string>
        {
            { VRQuickMenuOpenMethod.Stick, "openByStick" },
            { VRQuickMenuOpenMethod.Trigger, "openByTrigger" },
            { VRQuickMenuOpenMethod.TriggerCombo, "openByTriggerCombo" },
        };
        private Dictionary<VRQuickMenuDominantHand, string> _vrDominantHandNames = new Dictionary<VRQuickMenuDominantHand, string>
        {
            { VRQuickMenuDominantHand.Left, "leftHand" },
            { VRQuickMenuDominantHand.Right, "rightHand" },
        };

        private void OnEnable()
        {
            if (Application.isPlaying) return;
            var gameObject = ((RehubSystemCore)target).gameObject;
            _isReferencedByProjectWindow = !gameObject.scene.IsValid();

            var moduleManager = gameObject.GetComponentInChildren<ModuleManager>(true);
            _moduleContainer = moduleManager == null ? null : moduleManager.ModulesRoot;

            _themeManager = gameObject.GetComponentInChildren<ThemeManager>(true);
            if (_themeManager != null)
            {
                _themeManagerSerializedObject = new SerializedObject(_themeManager);
                var propThemePreset = _themeManagerSerializedObject.FindProperty("_themePreset");
                if (propThemePreset.objectReferenceValue != null)
                {
                    var themePresetJson = propThemePreset.objectReferenceValue as TextAsset;
                    if (themePresetJson != null)
                    {
                        try
                        {
                            var themePresets = JsonUtility.FromJson<ThemePresetsRoot>($"{{\"presets\": {themePresetJson.text}}}");
                            _themes = themePresets.presets;
                            _themeNames = _themes.Select(t => t.name).ToArray();
                        }
                        catch (Exception)
                        {
                            Debug.LogError("Failed to parse theme presets.");
                        }
                    }
                }
            }

            var uiManager = gameObject.GetComponentInChildren<UIManager>(true);
            if (uiManager != null)
            {
                _uiManagerSerializedObject = new SerializedObject(uiManager);
            }

            var quickMenuManager = gameObject.GetComponentInChildren<QuickMenuManager>(true);
            if (quickMenuManager != null)
            {
                _quickMenuManagerSerializedObject = new SerializedObject(quickMenuManager);
            }

            GenerateList();
            ModuleVersionManager.RefreshCurrentVersions(_modulesCache);
        }

        private void GenerateList()
        {
            _modulesCache = ModuleSceneUtility.GetSceneModules(_moduleContainer);

            _listDrawer = new ListDrawer(_modulesCache, new ListDrawerCallbacks() {
                drawHeader = () => EditorI18n.GetTranslation("modules"),
                drawElement = (rect, index, isActive, isFocused) =>
                {
                    var xSpacing = 0;
                    var module = _modulesCache[index];
                    var moduleRegistryItem = ModuleRegistry.ModuleList.TryGetValue(module.ModuleId, out var item) ? item : null;

                    if (module.moduleIcon != null)
                    {
                        rect.x += 4;
                        GUI.DrawTexture(new Rect(rect.x, rect.y + 1, 16, 16), module.moduleIcon.texture);
                        rect.x += 24;
                        xSpacing += 28;
                    }

                    var title = moduleRegistryItem != null && !module.forceUseModuleName ? moduleRegistryItem.GetTitle() : module.moduleName;
                    var style = new GUIStyle(EditorStyles.label)
                    {
                        richText = true
                    };

                    if (module.instanceOwnerOnly || module.allowedUsersOnly)
                    {
                        title += " <color=\"#7dacf1\">[!]</color>";
                    }

                    var label = new GUIContent(title, module.instanceOwnerOnly || module.allowedUsersOnly ? EditorI18n.GetTranslation("modulePermissionsEnabled") : null);
                    EditorGUI.LabelField(rect, label, style);

                    if (GUI.Button(new Rect(rect.x + rect.width - xSpacing - 120, rect.y, 120, EditorGUIUtility.singleLineHeight), EditorI18n.GetTranslation("moduleSettingsButton")))
                    {
                        Selection.activeObject = module.gameObject;
                    }
                },
                onAddDropdown = (rect, list) =>
                {
                    var menu = new GenericMenu();
                    UniqueModuleDuplicateCheck();

                    var installedModulePrefabs = AssetDatabase.FindAssets("t:Prefab", new[] { "Packages/com.rehub.rehubsystem/Runtime/Modules" })
                        .Select(AssetDatabase.GUIDToAssetPath)
                        .Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(path))
                        .Where(prefab => prefab != null && prefab.GetComponent<ModuleMetadata>() != null)
                        .Select(prefab => new { Prefab = prefab, Metadata = prefab.GetComponent<ModuleMetadata>() })
                        .OrderBy(item => ModuleRegistry.ModuleList.TryGetValue(item.Metadata.ModuleId, out var registryItem) ? registryItem.GetTitle() : item.Metadata.moduleName)
                        .ToList();

                    foreach (var m in installedModulePrefabs)
                    {
                        var moduleId = m.Metadata.ModuleId;
                        var moduleTitle = ModuleRegistry.ModuleList.TryGetValue(moduleId, out var registryItem) ? registryItem.GetTitle() : m.Metadata.moduleName;

                        if (!_usedUniqueModuleIds.Contains(moduleId))
                        {
                            var modulePrefab = m.Prefab;
                            menu.AddItem(new GUIContent(moduleTitle), false, () =>
                            {
                                var prefab = PrefabUtility.InstantiatePrefab(modulePrefab, _moduleContainer.transform) as GameObject;
                                if (prefab == null) return;
                                var moduleMetadata = prefab.GetComponent<ModuleMetadata>();
                                _modulesCache.Add(moduleMetadata);
                                ModuleSceneUtility.ConfigureModuleInstance(prefab, _moduleContainer);

                                UniqueModuleDuplicateCheck(true);
                            });
                        }
                        else
                        {
                            menu.AddDisabledItem(new GUIContent(moduleTitle));
                        }
                    }

                    menu.DropDown(rect);
                },
                onRemove = (list) =>
                {
                    var module = _modulesCache[list.index];
                    DestroyImmediate(module.gameObject);
                    _modulesCache.RemoveAt(list.index);
                    UniqueModuleDuplicateCheck(true);
                },
                onReorder = (list) =>
                {
                    var module = _modulesCache[list.index];
                    module.transform.SetSiblingIndex(list.index);
                },
                elementCount = index => 1
            });
        }

        private void UniqueModuleDuplicateCheck(bool force = false)
        {
            if (_usedUniqueModuleIds != null && !force) return;
            _usedUniqueModuleIds = new List<string>();
            _duplicatedUniqueModuleIds = new List<string>();

            foreach (var module in _modulesCache)
            {
                if (!module.IsUnique) continue;
                if (!_usedUniqueModuleIds.Contains(module.ModuleId))
                {
                    _usedUniqueModuleIds.Add(module.ModuleId);
                }
                else if (!_duplicatedUniqueModuleIds.Contains(module.ModuleId))
                {
                    _duplicatedUniqueModuleIds.Add(module.ModuleId);
                }
            }
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.LabelField("RehubUI", UIStyles.header, GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField("v" + Updater.CurrentVersion, UIStyles.center, GUILayout.ExpandWidth(true));
            EditorGUILayout.Space();

            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox(EditorI18n.GetTranslation("playModeWarning"), MessageType.Warning);
                return;
            }

            var currentIndex = InternalEditorI18n.availableLanguages.Keys.ToList().IndexOf(InternalEditorI18n.CurrentLanguage);
            var availableLanguages = InternalEditorI18n.availableLanguages.Values.ToArray();
            var langIndex = EditorGUILayout.Popup(EditorI18n.GetTranslation("language"), currentIndex, availableLanguages);

            if (langIndex != currentIndex)
            {
                InternalEditorI18n.CurrentLanguage = InternalEditorI18n.availableLanguages.ElementAt(langIndex).Key;
                HierachyMenu.RebuildMenu();
            }

            if (Updater.AvailableUpdate)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(EditorI18n.GetTranslation("rehubUIUpdateAvailable"), MessageType.Info);
            }

            if (ModuleVersionManager.AvailableUpdate)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(EditorI18n.GetTranslation("moduleUpdateAvailable"), MessageType.Info);
            }

            if (_isReferencedByProjectWindow)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(EditorI18n.GetTranslation("pleasePlaceInScene"), MessageType.Error);
                return;
            }

            EditorGUILayout.Space(24);
            var tabs = new[]
            {
                EditorI18n.GetTranslation("mainSettings"),
                EditorI18n.GetTranslation("themeSettings"),
                EditorI18n.GetTranslation("versionInfo")
            };

            _selectedTabIndex = GUILayout.Toolbar(_selectedTabIndex, tabs, "LargeButton", GUILayout.ExpandWidth(true));
            EditorGUILayout.Space(12);

            switch (_selectedTabIndex)
            {
                case 0:
                    TabMainSettings();
                    break;
                case 1:
                    TabThemeSettings();
                    break;
                case 2:
                    TabVersionInfo();
                    break;
            }
        }

        private void TabMainSettings()
        {
            UIStyles.TitleBox(EditorI18n.GetTranslation("moduleSettings"), EditorI18n.GetTranslation("moduleSettingsDescription"), false);
            EditorGUILayout.Space();

            if (_moduleContainer == null)
            {
                EditorGUILayout.HelpBox(EditorI18n.GetTranslation("moduleContainerNotFound"), MessageType.Error);
            }
            else
            {
                _listDrawer.Draw();

                if (GUILayout.Button(EditorI18n.GetTranslation("syncInstalledModules")))
                {
                    _modulesCache = ModuleSceneUtility.SyncInstalledModulesToScene(_moduleContainer);
                    GenerateList();
                    UniqueModuleDuplicateCheck(true);
                    EditorUtility.SetDirty(((RehubSystemCore)target).gameObject);
                }

                UniqueModuleDuplicateCheck();
                if (_duplicatedUniqueModuleIds != null && _duplicatedUniqueModuleIds.Count > 0)
                {
                    var duplicatedModules = string.Join("\n", _duplicatedUniqueModuleIds.Select(id => "- " + (ModuleRegistry.ModuleList.TryGetValue(id, out var m) ? m.GetTitle() : id)).ToArray());
                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox($"{EditorI18n.GetTranslation("uniqueModuleDuplicatedError")}\n{duplicatedModules}", MessageType.Error);
                }

                _showExtensionModuleReference = EditorGUILayout.Foldout(_showExtensionModuleReference, EditorI18n.GetTranslation("extensionModuleDescription"));
                if (_showExtensionModuleReference)
                {
                    EditorGUILayout.Space();
                    if (GUILayout.Button(EditorI18n.GetTranslation("goModuleListPage")))
                    {
                        ModuleMarketplaceWindow.OpenWindow();
                    }

                    EditorGUILayout.LabelField(EditorI18n.GetTranslation("extensionMarketplaceNotConfigured"));
                }
            }

            UIStyles.TitleBox(EditorI18n.GetTranslation("homeSettings"), EditorI18n.GetTranslation("homeSettingsDescription"));
            if (_uiManagerSerializedObject == null)
            {
                EditorGUILayout.HelpBox(EditorI18n.GetTranslation("uiManagerNotFound"), MessageType.Error);
            }
            else
            {
                var customWelcomeText = _uiManagerSerializedObject.FindProperty("_customWelcomeText");
                var enableCustomWelcomeText = EditorGUILayout.ToggleLeft(EditorI18n.GetTranslation("enableCustomWelcomeText"), !string.IsNullOrEmpty(customWelcomeText.stringValue));
                if (enableCustomWelcomeText)
                {
                    customWelcomeText.stringValue = EditorGUILayout.TextField(customWelcomeText.stringValue.Replace("<EMPTY>", ""));
                    if (string.IsNullOrEmpty(customWelcomeText.stringValue))
                    {
                        customWelcomeText.stringValue = "<EMPTY>";
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(customWelcomeText.stringValue))
                    {
                        if (EditorUtility.DisplayDialog(EditorI18n.GetTranslation("warning"), EditorI18n.GetTranslation("beforeDisableCustomWelcomeText"), EditorI18n.GetTranslation("delete"), EditorI18n.GetTranslation("cancel")))
                        {
                            customWelcomeText.stringValue = null;
                        }
                    }
                }

                _uiManagerSerializedObject.ApplyModifiedProperties();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(EditorI18n.GetTranslation("defaultOpenModule"));
            if (_moduleContainer != null && _modulesCache != null && _uiManagerSerializedObject != null)
            {
                var currentModule = _uiManagerSerializedObject.FindProperty("_defaultOpenModule").objectReferenceValue as ModuleMetadata;
                var moduleNames = _modulesCache.Select(m =>
                {
                    var moduleName = !m.forceUseModuleName && ModuleRegistry.ModuleList.TryGetValue(m.ModuleId, out var item) ? item.GetTitle() : m.moduleName;
                    return moduleName.Replace("/", "\u2215");
                }).Prepend(EditorI18n.GetTranslation("none")).ToArray();
                var selectedModuleIndex = currentModule == null ? 0 : _modulesCache.IndexOf(currentModule) + 1;
                var newModuleIndex = EditorGUILayout.Popup(selectedModuleIndex, moduleNames);

                if (newModuleIndex != selectedModuleIndex)
                {
                    _uiManagerSerializedObject.FindProperty("_defaultOpenModule").objectReferenceValue = newModuleIndex == 0 ? null : _modulesCache[newModuleIndex - 1];
                    _uiManagerSerializedObject.ApplyModifiedProperties();
                }
            }
            else
            {
                EditorGUILayout.HelpBox(EditorI18n.GetTranslation("uiManagerNotFound"), MessageType.Error);
            }

            UIStyles.TitleBox(EditorI18n.GetTranslation("quickMenuSettings"), EditorI18n.GetTranslation("quickMenuSettingsDescription"));

            if (_quickMenuManagerSerializedObject == null)
            {
                EditorGUILayout.HelpBox(EditorI18n.GetTranslation("quickMenuManagerNotFound"), MessageType.Error);
            }
            else
            {
                EditorGUILayout.LabelField(EditorI18n.GetTranslation("menuOpenMethodInDesktop"));

                var desktopMethodEnumCount = Enum.GetNames(typeof(DesktopQuickMenuOpenMethod)).Length;
                var desktopMethodEnumNames = new string[desktopMethodEnumCount];
                for (var i = 0; i < desktopMethodEnumCount; i++)
                {
                    desktopMethodEnumNames[i] = EditorI18n.GetTranslation(_desktopOpenMethodNames[(DesktopQuickMenuOpenMethod)i]);
                }

                var selectedDesktopOpenMethod = _quickMenuManagerSerializedObject.FindProperty("_desktopOpenMethod").enumValueIndex;
                var newDesktopOpenMethod = EditorGUILayout.Popup(selectedDesktopOpenMethod, desktopMethodEnumNames);
                if (newDesktopOpenMethod != selectedDesktopOpenMethod)
                {
                    _quickMenuManagerSerializedObject.FindProperty("_desktopOpenMethod").enumValueIndex = newDesktopOpenMethod;
                    _quickMenuManagerSerializedObject.ApplyModifiedProperties();
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField(EditorI18n.GetTranslation("menuOpenMethodInVR"));

                var vrMethodEnumCount = Enum.GetNames(typeof(VRQuickMenuOpenMethod)).Length;
                var vrMethodEnumNames = new string[vrMethodEnumCount];

                for (var i = 0; i < vrMethodEnumCount; i++)
                {
                    vrMethodEnumNames[i] = EditorI18n.GetTranslation(_vrOpenMethodNames[(VRQuickMenuOpenMethod)i]);
                }

                var selectedVrOpenMethod = _quickMenuManagerSerializedObject.FindProperty("_vrOpenMethod").enumValueIndex;
                var newVrOpenMethod = EditorGUILayout.Popup(selectedVrOpenMethod, vrMethodEnumNames);
                if (newVrOpenMethod != selectedVrOpenMethod)
                {
                    _quickMenuManagerSerializedObject.FindProperty("_vrOpenMethod").enumValueIndex = newVrOpenMethod;
                    _quickMenuManagerSerializedObject.ApplyModifiedProperties();
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField(EditorI18n.GetTranslation("quickmenuDominantHandInVR"));

                var dominantHandEnumCount = Enum.GetNames(typeof(VRQuickMenuDominantHand)).Length;
                var dominantHandEnumNames = new string[dominantHandEnumCount];

                for (var i = 0; i < dominantHandEnumCount; i++)
                {
                    dominantHandEnumNames[i] = EditorI18n.GetTranslation(_vrDominantHandNames[(VRQuickMenuDominantHand)i]);
                }

                var selectedDominantHand = _quickMenuManagerSerializedObject.FindProperty("_dominantHand").enumValueIndex;
                var newDominantHand = EditorGUILayout.Popup(selectedDominantHand, dominantHandEnumNames);
                if (newDominantHand != selectedDominantHand)
                {
                    _quickMenuManagerSerializedObject.FindProperty("_dominantHand").enumValueIndex = newDominantHand;
                    _quickMenuManagerSerializedObject.ApplyModifiedProperties();
                }
            }

            EditorGUILayout.Space();
        }

        private void TabThemeSettings()
        {
            if (_themeManager == null || _themeManagerSerializedObject == null)
            {
                EditorGUILayout.HelpBox(EditorI18n.GetTranslation("themeManagerNotFound"), MessageType.Error);
            }
            else
            {
                EditorGUILayout.LabelField(EditorI18n.GetTranslation("chooseFromPresets"));
                if (_themes == null)
                {
                    EditorGUILayout.HelpBox(EditorI18n.GetTranslation("presetsNotFound"), MessageType.Error);
                }
                else
                {
                    EditorGUILayout.BeginHorizontal();
                    _selectedThemeIndex = EditorGUILayout.Popup(_selectedThemeIndex, _themeNames);

                    if (GUILayout.Button(EditorI18n.GetTranslation("useThisTheme")))
                    {
                        var theme = _themes[_selectedThemeIndex]?.color;
                        if (theme == null) return;

                        _themeManagerSerializedObject.FindProperty("_accentColor").colorValue = theme.GetColor(ColorPalette.Accent);
                        _themeManagerSerializedObject.FindProperty("_baseColor").colorValue = theme.GetColor(ColorPalette.Base);
                        _themeManagerSerializedObject.FindProperty("_surfaceColor").colorValue = theme.GetColor(ColorPalette.Surface);
                        _themeManagerSerializedObject.FindProperty("_textColor").colorValue = theme.GetColor(ColorPalette.Text);
                        _themeManagerSerializedObject.FindProperty("_successColor").colorValue = theme.GetColor(ColorPalette.Success);
                        _themeManagerSerializedObject.FindProperty("_warningColor").colorValue = theme.GetColor(ColorPalette.Warning);
                        _themeManagerSerializedObject.FindProperty("_errorColor").colorValue = theme.GetColor(ColorPalette.Error);
                        _themeManagerSerializedObject.FindProperty("_infoColor").colorValue = theme.GetColor(ColorPalette.Info);

                        _themeManagerSerializedObject.ApplyModifiedProperties();
                        _themeManager.ApplyTheme();

                        EditorApplication.delayCall += () =>
                        {
                            SceneView.RepaintAll();
                        };
                    }
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.Space();

                _showThemeSettings = EditorGUILayout.Foldout(_showThemeSettings, EditorI18n.GetTranslation("setAsSeparately"));
                if (_showThemeSettings)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_themeManagerSerializedObject.FindProperty("_accentColor"), new GUIContent("Accent"));
                    EditorGUILayout.PropertyField(_themeManagerSerializedObject.FindProperty("_baseColor"), new GUIContent("Base"));
                    EditorGUILayout.PropertyField(_themeManagerSerializedObject.FindProperty("_surfaceColor"), new GUIContent("Surface"));
                    EditorGUILayout.PropertyField(_themeManagerSerializedObject.FindProperty("_textColor"), new GUIContent("Text"));
                    EditorGUILayout.PropertyField(_themeManagerSerializedObject.FindProperty("_successColor"), new GUIContent("Success"));
                    EditorGUILayout.PropertyField(_themeManagerSerializedObject.FindProperty("_warningColor"), new GUIContent("Warning"));
                    EditorGUILayout.PropertyField(_themeManagerSerializedObject.FindProperty("_errorColor"), new GUIContent("Error"));
                    EditorGUILayout.PropertyField(_themeManagerSerializedObject.FindProperty("_infoColor"), new GUIContent("Info"));
                    _themeManagerSerializedObject.ApplyModifiedProperties();

                    EditorGUILayout.Space();
                    if (GUILayout.Button(EditorI18n.GetTranslation("refrectSettings")))
                    {
                        _themeManager.ApplyTheme();
                        EditorApplication.delayCall += () =>
                        {
                            SceneView.RepaintAll();
                        };
                    }
                    EditorGUI.indentLevel--;
                }
            }
        }

        private void TabVersionInfo()
        {
            UIStyles.TitleBox("RehubUI", margin: false);
            EditorGUILayout.Space();

#pragma warning disable CS0162
            if (Updater.availableVpmResolver)
            {
                if (Updater.AvailableUpdate)
                {
                    EditorGUILayout.LabelField($"{EditorI18n.GetTranslation("updateAvailable")} (v{Updater.CurrentVersion} -> v{Updater.LatestVersion})");
                }
                else if(Updater.LatestVersion == null)
                {
                    EditorGUILayout.LabelField(EditorI18n.GetTranslation("checkingForUpdate"));
                }
                else
                {
                    EditorGUILayout.LabelField($"{EditorI18n.GetTranslation("upToDate")} (v{Updater.CurrentVersion})");
                }

                
                EditorGUILayout.Space();

                using (var x = new EditorGUI.ChangeCheckScope())
                {
                    Updater.UseUnstableVersion = EditorGUILayout.ToggleLeft(EditorI18n.GetTranslation("useUnstableVersion"), Updater.UseUnstableVersion);
                    if (x.changed)
                    {
                        Updater.CheckForUpdate();
                    }
                }

                if (Updater.AvailableUpdate)
                {
                    EditorGUILayout.Space();
                    if (GUILayout.Button(EditorI18n.GetTranslation("update"), GUILayout.Height(32)))
                    {
                        Updater.RunUpdate();
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField(EditorI18n.GetTranslation("currentVersion"), Updater.CurrentVersion);
                
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(EditorI18n.GetTranslation("vpmResolverNotImported"), MessageType.Warning);
            }
#pragma warning restore CS0162

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(EditorI18n.GetTranslation("versionListingSource"), ModuleVersionManager.ListingUrl);

            if (GUILayout.Button(EditorI18n.GetTranslation("checkForUpdates")))
            {
                ModuleVersionManager.CheckLatestVersions();
            }

            if (ModuleVersionManager.AvailableModules)
            {
                UIStyles.TitleBox(EditorI18n.GetTranslation("modules"));
                
                EditorGUILayout.Space();

                foreach (var item in ModuleVersionManager.CurrentVersions)
                {
                    var packageName = item.Key;
                    EditorGUILayout.LabelField(ModuleVersionManager.GetPackageName(packageName), EditorStyles.boldLabel);

                    if (ModuleVersionManager.HasUpdate(packageName))
                    {
                        EditorGUILayout.LabelField($"{EditorI18n.GetTranslation("updateAvailable")} (v{item.Value} -> v{ModuleVersionManager.GetLatestVersion(packageName)})");
                    }
                    else
                    {
                        EditorGUILayout.LabelField($"{EditorI18n.GetTranslation("upToDate")} (v{item.Value})");
                    }

                    EditorGUILayout.Space();
                }

                if (ModuleVersionManager.AvailableUpdate)
                {
                    if (GUILayout.Button(EditorI18n.GetTranslation("downloadLatestVersion"), GUILayout.Height(32)))
                    {
                        Application.OpenURL(ModuleVersionManager.ReleasesUrl);
                    }
                }
            }

            UIStyles.TitleBox(EditorI18n.GetTranslation("links"));

            if (_uiManagerSerializedObject != null)
            {
                _uiManagerSerializedObject.Update();

                var verifiedUsersUrl = _uiManagerSerializedObject.FindProperty("_verifiedUsersUrl");
                if (verifiedUsersUrl != null)
                {
                    EditorGUILayout.PropertyField(verifiedUsersUrl, new GUIContent(EditorI18n.GetTranslation("verifiedUsersUrl")));
                    EditorGUILayout.HelpBox(EditorI18n.GetTranslation("verifiedUsersUrlDescription"), MessageType.Info);
                }

                _uiManagerSerializedObject.ApplyModifiedProperties();
            }

            UIStyles.TitleBox(EditorI18n.GetTranslation("otherSettings"));
            var isDevMode = EditorPrefs.GetBool("ynworks_devmode", false);
            using (var x = new EditorGUI.ChangeCheckScope())
            {
                isDevMode = EditorGUILayout.ToggleLeft(EditorI18n.GetTranslation("enableDevMode"), isDevMode);
                if (x.changed)
                {
                    EditorPrefs.SetBool("ynworks_devmode", isDevMode);
                }
            }
        }
    }

    [Serializable]
    internal class ThemePresetsRoot
    {
        public ThemePreset[] presets;
    }

    [Serializable]
    internal class ThemePreset
    {
        public string name;
        public PresetColor color;
    }

    [Serializable]
    internal class PresetColor
    {
        public string accent;
        public string bg;
        public string surface;
        public string text;
        public string success;
        public string warning;
        public string error;
        public string info;

        public Color GetColor(ColorPalette colorPalette)
        {
            switch (colorPalette)
            {
                case ColorPalette.Accent:
                    return ColorUtility.TryParseHtmlString(accent, out var accentColor) ? accentColor : Color.white;
                case ColorPalette.Base:
                    return ColorUtility.TryParseHtmlString(bg, out var bgColor) ? bgColor : Color.white;
                case ColorPalette.Surface:
                    return ColorUtility.TryParseHtmlString(surface, out var surfaceColor) ? surfaceColor : Color.white;
                case ColorPalette.Text:
                    return ColorUtility.TryParseHtmlString(text, out var textColor) ? textColor : Color.white;
                case ColorPalette.Success:
                    return ColorUtility.TryParseHtmlString(success, out var successColor) ? successColor : Color.white;
                case ColorPalette.Warning:
                    return ColorUtility.TryParseHtmlString(warning, out var warningColor) ? warningColor : Color.white;
                case ColorPalette.Error:
                    return ColorUtility.TryParseHtmlString(error, out var errorColor) ? errorColor : Color.white;
                case ColorPalette.Info:
                    return ColorUtility.TryParseHtmlString(info, out var infoColor) ? infoColor : Color.white;
                default:
                    return Color.white;
            }
        }
    }
}
