
using UdonSharp;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using RehubSystem.EditorShared;

namespace RehubSystem
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class VersionInfoModule : UdonSharpBehaviour
    {
        public string version = "0.0.0";
        [SerializeField] private Text _versionText;
        [SerializeField] private Text _statusText;
        [SerializeField] private UIManager _uiManager;

        private void Start()
        {
            if (_uiManager == null)
            {
                _uiManager = GetComponentInParent<UIManager>();
            }

            if (_statusText == null)
            {
                _statusText = FindStatusText();
            }

            RefreshVersionStatus();
        }

        public void RefreshVersionStatus()
        {
            if (_uiManager == null)
            {
                _uiManager = GetComponentInParent<UIManager>();
            }

            if (_statusText == null)
            {
                _statusText = FindStatusText();
            }

            if (_versionText != null)
            {
                _versionText.text = $"Version {version}";
            }

            if (_statusText == null) return;

            _statusText.text = "Rehub System";
        }

        private Text FindStatusText()
        {
            var texts = GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i] != _versionText)
                {
                    return texts[i];
                }
            }

            return null;
        }

        public void OnModuleCalled()
        {
            RefreshVersionStatus();
        }
    }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
    [CustomEditor(typeof(VersionInfoModule))]
    public class VersionInfoModuleInspector : ModuleInspector
    {
        protected override string I18nUUID => "4808d31699fba654f86b406d56d0e5c7";
        protected override string[] ObjectProperties => new string[] { "version", "_versionText", "_statusText", "_uiManager" };

        protected override void DrawModuleInspector()
        {
            EditorGUILayout.HelpBox(EditorI18n.GetTranslation("noSettings"), MessageType.Info);
            EditorGUILayout.HelpBox(_i18n.GetTranslation("warning"), MessageType.Warning);
        }
    }
#endif
}
