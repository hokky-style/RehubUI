using System;
using UdonSharp;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDK3.Persistence;
using VRC.SDKBase;
using RehubSystem.EditorShared;

namespace RehubSystem
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class CloudSyncManager : UdonSharpBehaviour
    {
        private const string PlayerDataKey = "rehubsystem:settings";

        [SerializeField, HideInInspector] private string _apiBaseUrl = "";
        [SerializeField, HideInInspector] private string _apiSchemaRev = "1";
        [SerializeField, HideInInspector] private VRCUrl _apiLoadUrl = new VRCUrl("");
        [SerializeField, HideInInspector] private GameObject _syncStatus;
        [SerializeField, HideInInspector] private Sprite _syncStatusUnknownIcon;
        [SerializeField, HideInInspector] private Sprite _syncStatusSuccessIcon;
        [SerializeField, HideInInspector] private Sprite _syncStatusErrorIcon;
        [HideInInspector] private UnityEngine.UI.Image _syncStatusImage;
        [HideInInspector] private ApplyTheme _syncStatusTheme;
        [HideInInspector] private CloudSyncUtils _cloudSyncUtils;
        [HideInInspector] private string _uid = "";
        [HideInInspector] private string _key = "";

        private DataDictionary _data = new DataDictionary();
        private bool _initializedInternal = false;
        private bool _usingPersistenceData = false;
        private string _lastState = "unknown";
        private DateTimeOffset _lastSaveTime = DateTimeOffset.MinValue;
        private UdonSharpBehaviour[] _onLoadCallbackBehaviours = new UdonSharpBehaviour[0];
        private string[] _onLoadCallbackMethods = new string[0];
        private DataDictionary _saveQueue = new DataDictionary();

        public bool Initialized => _initializedInternal;
        public DataDictionary SyncData => _data;
        public string LastState => _lastState;
        public string LastSaveTime => _lastSaveTime == DateTimeOffset.MinValue ? "" : _lastSaveTime.ToString("o");
        public bool UsingPersistenceData => _usingPersistenceData;
        public bool HasSavedData => _usingPersistenceData;

        private void Start()
        {
            _initializedInternal = true;
            _lastState = "ready";
            NotifyLoadCallbacks();
        }

        public void OnLoad(UdonSharpBehaviour behaviour, string method)
        {
            if (behaviour == null || string.IsNullOrEmpty(method)) return;

            _onLoadCallbackBehaviours = ArrayUtils.Add(_onLoadCallbackBehaviours, behaviour);
            _onLoadCallbackMethods = ArrayUtils.Add(_onLoadCallbackMethods, method);

            if (_initializedInternal)
            {
                behaviour.SendCustomEvent(method);
            }
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (player == null || !player.isLocal) return;

            if (PlayerData.TryGetString(player, PlayerDataKey, out var savedata) && VRCJson.TryDeserializeFromJson(savedata, out var data))
            {
                var root = data.DataDictionary;
                _data = root.TryGetValue("config", out var config) ? config.DataDictionary : new DataDictionary();
                _usingPersistenceData = true;
                _lastState = "success";
                NotifyLoadCallbacks();
            }
        }

        public void Save(string key, DataToken value)
        {
            if (string.IsNullOrEmpty(key)) return;

            _data.SetValue(key, value);
            _lastSaveTime = DateTimeOffset.Now;
            _usingPersistenceData = true;
            _lastState = "success";

            var root = new DataDictionary();
            root.SetValue("config", _data);
            root.SetValue("updatedAt", LastSaveTime);

            if (VRCJson.TrySerializeToJson(root, JsonExportType.Minify, out var result))
            {
                PlayerData.SetString(PlayerDataKey, result.String);
            }
        }

        public string GetSaveUrl()
        {
            return string.Empty;
        }

        public void RequestSave(VRCUrl url)
        {
            // Legacy external cloud import is intentionally disabled.
        }

        private void NotifyLoadCallbacks()
        {
            for (int i = 0; i < _onLoadCallbackBehaviours.Length; i++)
            {
                if (_onLoadCallbackBehaviours[i] == null || string.IsNullOrEmpty(_onLoadCallbackMethods[i])) continue;
                _onLoadCallbackBehaviours[i].SendCustomEvent(_onLoadCallbackMethods[i]);
            }
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        [CustomEditor(typeof(CloudSyncManager))]
        internal class CloudSyncManagerInspector : Editor
        {
            public override void OnInspectorGUI()
            {
                EditorGUILayout.LabelField("Persistence Manager", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(EditorI18n.GetTranslation("noSettings"), MessageType.Info);
            }
        }
#endif
    }
}
