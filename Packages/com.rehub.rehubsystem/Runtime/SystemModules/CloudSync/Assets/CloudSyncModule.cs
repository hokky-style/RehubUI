using System;
using UdonSharp;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;
using RehubSystem.EditorShared;

namespace RehubSystem
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class CloudSyncModule : UdonSharpBehaviour
    {
        [SerializeField] private CloudSyncManager _cloudSyncManager;
        [SerializeField] private Animator _saveStatusAnimator;
        [SerializeField] private ApplyTimeI18n _lastSaveTime;
        [SerializeField] private InputField _saveUrlCopyField;
        [SerializeField] private VRCUrlInputField _saveUrlPasteField;
        [SerializeField] private UIManager _uiManager;
        [SerializeField] private Text _syncStatusText;
        [SerializeField] private Text _masterStatusText;
        [SerializeField] private Text _ownerStatusText;
        [SerializeField] private Text _verifiedStatusText;
        private void Start()
        {
            if (_uiManager == null)
            {
                _uiManager = GetComponentInParent<UIManager>();
            }

            if (_saveStatusAnimator != null)
            {
                _saveStatusAnimator.keepAnimatorStateOnDisable = true;
            }

            ResolveStatusTexts();
            DisableLegacyControls();
            RefreshStatus();
        }

        public void RefreshStatus()
        {
            var hasSynced = _cloudSyncManager != null && _cloudSyncManager.Initialized;
            SetStatusText(_syncStatusText, "Synchronization", hasSynced);
            SetStatusText(_masterStatusText, "Instance master", Networking.LocalPlayer != null && Networking.LocalPlayer.isMaster);
            SetStatusText(_ownerStatusText, "Instance owner", Networking.LocalPlayer != null && Networking.LocalPlayer.isInstanceOwner);
            SetStatusText(_verifiedStatusText, "Verified user", _uiManager != null && _uiManager.LocalPlayerVerified);
        }

        public void OnModuleCalled()
        {
            RefreshStatus();
        }

        public void OnSaveRequested()
        {
            RefreshStatus();
        }

        private void ResolveStatusTexts()
        {
            if (_syncStatusText == null) _syncStatusText = FindTextByObjectName("SyncStatus");
            if (_masterStatusText == null) _masterStatusText = FindTextByObjectName("MasterStatus");
            if (_ownerStatusText == null) _ownerStatusText = FindTextByObjectName("OwnerStatus");
            if (_verifiedStatusText == null) _verifiedStatusText = FindTextByObjectName("VerifiedStatus");
        }

        private Text FindTextByObjectName(string objectName)
        {
            var texts = GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i].gameObject.name == objectName)
                {
                    return texts[i];
                }
            }

            return null;
        }

        private void SetStatusText(Text target, string label, bool enabled)
        {
            if (target == null) return;
            target.text = $"{label}: {(enabled ? "Yes" : "No")}";
        }

        private void DisableLegacyControls()
        {
            if (_saveUrlCopyField != null) _saveUrlCopyField.gameObject.SetActive(false);
            if (_saveUrlPasteField != null) _saveUrlPasteField.gameObject.SetActive(false);
            if (_lastSaveTime != null) _lastSaveTime.gameObject.SetActive(false);
            if (_saveStatusAnimator != null) _saveStatusAnimator.gameObject.SetActive(false);
        }
    }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
    [CustomEditor(typeof(CloudSyncModule))]
    public class CloudSyncModuleInspector : ModuleInspector
    {
        protected override string I18nUUID => "924493d0692e091469e86bb170d34d8e";
        protected override string[] ObjectProperties => new string[] { "_cloudSyncManager", "_saveStatusAnimator", "_lastSaveTime", "_saveUrlCopyField", "_saveUrlPasteField", "_uiManager", "_syncStatusText", "_masterStatusText", "_ownerStatusText", "_verifiedStatusText" };

        protected override void DrawModuleInspector()
        {
            EditorGUILayout.HelpBox(EditorI18n.GetTranslation("noSettings"), MessageType.Info);
        }
    }
#endif
}
