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
        private void Start()
        {
            if (_saveStatusAnimator != null)
            {
                _saveStatusAnimator.keepAnimatorStateOnDisable = true;
            }

            DisableLegacyControls();
            RefreshStatus();
        }

        public void RefreshStatus()
        {
            var hasSynced = _cloudSyncManager != null && _cloudSyncManager.Initialized;
            SetStatusText(FindTextByObjectName("SyncStatus"), "Synchronization", hasSynced);
            SetStatusText(FindTextByObjectName("MasterStatus"), "Instance master", Networking.LocalPlayer != null && Networking.LocalPlayer.isMaster);
            SetStatusText(FindTextByObjectName("OwnerStatus"), "Instance owner", Networking.LocalPlayer != null && Networking.LocalPlayer.isInstanceOwner);
            SetStatusText(FindTextByObjectName("VerifiedStatus"), "Verified user", false);
        }

        public void OnModuleCalled()
        {
            RefreshStatus();
        }

        public void OnSaveRequested()
        {
            RefreshStatus();
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
        protected override string[] ObjectProperties => new string[] { "_cloudSyncManager", "_saveStatusAnimator", "_lastSaveTime", "_saveUrlCopyField", "_saveUrlPasteField" };

        protected override void DrawModuleInspector()
        {
            EditorGUILayout.HelpBox(EditorI18n.GetTranslation("noSettings"), MessageType.Info);
        }
    }
#endif
}
