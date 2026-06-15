using UdonSharp;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using RehubSystem.EditorShared;

namespace RehubSystem
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class AssetListModule : UdonSharpBehaviour
    {
        [SerializeField] private GameObject _assetItemTemplate;
        [SerializeField] private Transform _assetListContent;
        [SerializeField] private ModuleManager _moduleManager;
        [SerializeField] private string[] _assetList;

        private void Start()
        {
            if (_assetItemTemplate == null || _assetListContent == null)
            {
                Debug.LogError("ModuleListModule: Missing required components.");
                return;
            }

            if (_moduleManager == null)
            {
                Debug.LogError("ModuleListModule: Missing ModuleManager.");
                return;
            }

            if (!_moduleManager.Initialized)
            {
                _moduleManager.Initialize();
            }

            _assetItemTemplate.SetActive(false);

            var visibleIndex = 0;
            foreach (var module in _moduleManager.Modules)
            {
                if (module == null || module.HideInMenu) continue;

                var assetItem = Instantiate(_assetItemTemplate, _assetListContent);
                assetItem.SetActive(true);
                ConfigureItemLayout(assetItem, visibleIndex);
                visibleIndex++;

                var moduleTitle = module.moduleName;
                if (!module.forceUseModuleName && module.i18nManager != null)
                {
                    if (!module.i18nManager.Initialized) module.i18nManager.BuildLocalization();
                    if (module.i18nManager.HasLocalization)
                    {
                        moduleTitle = module.i18nManager.GetTranslation("$moduleName");
                    }
                }

                var icon = assetItem.transform.Find("Image");
                if (icon != null && module.moduleIcon != null)
                {
                    icon.GetComponent<Image>().sprite = module.moduleIcon;
                }

                var version = string.IsNullOrEmpty(module.moduleVersion) ? "1.0.0" : module.moduleVersion;
                ApplyItemText(assetItem, moduleTitle, $"Version {version}");
            }
        }

        private void ApplyItemText(GameObject assetItem, string moduleTitle, string moduleVersion)
        {
            var nameText = FindItemText(assetItem, "Text/Name");
            ApplyTextStyle(nameText, moduleTitle, 32, Color.white);

            var versionText = FindItemText(assetItem, "Text/Link");
            ApplyTextStyle(versionText, moduleVersion, 24, new Color(1f, 1f, 1f, 0.65f));
        }

        private Text FindItemText(GameObject assetItem, string path)
        {
            var textTransform = assetItem.transform.Find(path);
            if (textTransform != null)
            {
                var directText = textTransform.GetComponent<Text>();
                if (directText != null) return directText;

                var childText = textTransform.GetComponentInChildren<Text>();
                if (childText != null) return childText;
            }

            return null;
        }

        private void ApplyTextStyle(Text text, string value, int fontSize, Color color)
        {
            if (text == null) return;

            text.gameObject.SetActive(true);
            text.enabled = true;
            text.text = value;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private void ConfigureItemLayout(GameObject assetItem, int index)
        {
            var itemRect = assetItem.GetComponent<RectTransform>();
            if (itemRect != null)
            {
                itemRect.anchorMin = new Vector2(0f, 1f);
                itemRect.anchorMax = new Vector2(0f, 1f);
                itemRect.pivot = new Vector2(0f, 1f);
                itemRect.anchoredPosition = new Vector2(100f, -30f - index * 120f);
                itemRect.sizeDelta = new Vector2(900f, 100f);
            }

            var icon = assetItem.transform.Find("Image");
            if (icon != null)
            {
                var iconRect = icon.GetComponent<RectTransform>();
                if (iconRect != null)
                {
                    iconRect.anchorMin = new Vector2(0f, 1f);
                    iconRect.anchorMax = new Vector2(0f, 1f);
                    iconRect.pivot = new Vector2(0f, 1f);
                    iconRect.anchoredPosition = new Vector2(0f, -18f);
                    iconRect.sizeDelta = new Vector2(64f, 64f);
                }
            }

            var textRoot = assetItem.transform.Find("Text");
            if (textRoot == null) return;
            textRoot.gameObject.SetActive(true);

            var textRootRect = textRoot.GetComponent<RectTransform>();
            if (textRootRect != null)
            {
                textRootRect.anchorMin = new Vector2(0f, 1f);
                textRootRect.anchorMax = new Vector2(0f, 1f);
                textRootRect.pivot = new Vector2(0f, 1f);
                textRootRect.anchoredPosition = new Vector2(90f, -12f);
                textRootRect.sizeDelta = new Vector2(780f, 80f);
            }

            var name = textRoot.Find("Name");
            if (name != null)
            {
                var nameRect = name.GetComponent<RectTransform>();
                if (nameRect != null)
                {
                    nameRect.anchorMin = new Vector2(0f, 1f);
                    nameRect.anchorMax = new Vector2(0f, 1f);
                    nameRect.pivot = new Vector2(0f, 1f);
                    nameRect.anchoredPosition = new Vector2(0f, 0f);
                    nameRect.sizeDelta = new Vector2(780f, 40f);
                }

                var nameText = name.GetComponentInChildren<Text>();
                if (nameText != null) nameText.fontSize = 32;
            }

            var version = textRoot.Find("Link");
            if (version != null)
            {
                var versionRect = version.GetComponent<RectTransform>();
                if (versionRect != null)
                {
                    versionRect.anchorMin = new Vector2(0f, 1f);
                    versionRect.anchorMax = new Vector2(0f, 1f);
                    versionRect.pivot = new Vector2(0f, 1f);
                    versionRect.anchoredPosition = new Vector2(0f, -42f);
                    versionRect.sizeDelta = new Vector2(780f, 32f);
                }

                var versionText = version.GetComponentInChildren<Text>();
                if (versionText != null) versionText.fontSize = 24;
            }
        }
    }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
    [CustomEditor(typeof(AssetListModule))]
    internal class AssetListModuleInspector : ModuleInspector
    {
        protected override string I18nUUID => "abc798ea58083ae4d9834dc8fcf94586";
        protected override string[] ObjectProperties => new string[] { "_assetItemTemplate", "_assetListContent", "_moduleManager" };

        protected override void DrawModuleInspector()
        {
            EditorGUILayout.HelpBox(_i18n.GetTranslation("description"), MessageType.Info);
        }
    }
#endif
}
