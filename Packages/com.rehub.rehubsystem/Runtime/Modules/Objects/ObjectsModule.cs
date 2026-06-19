using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDK3.Persistence;
using VRC.SDKBase;
using RehubSystem.EditorShared;

#if !COMPILER_UDONSHARP && UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Events;
using VRC.Udon;
#endif

namespace RehubSystem
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ObjectsModule : UdonSharpBehaviour
    {
        [SerializeField] private Transform _objectRoot;
        [SerializeField] private Transform _toggleListRoot;
        [SerializeField] private GameObject _toggleSwitchTextTemplate;
        [SerializeField] private Button _respawnButton;
        [SerializeField] private Text _respawnButtonLabel;
        [SerializeField] private UIManager _uiManager;
        [SerializeField] private GameObject[] _objects = new GameObject[0];
        [SerializeField] private Toggle[] _toggles = new Toggle[0];
        [SerializeField] private Text[] _toggleLabels = new Text[0];
        [SerializeField] private bool[] _enabledByDefault = new bool[0];
        [SerializeField] private bool[] _localOnly = new bool[0];
        [SerializeField] private bool[] _globalToggle = new bool[0];
        [SerializeField] private bool[] _rememberToggleState = new bool[0];
        [SerializeField] private string[] _persistenceKeys = new string[0];
        [SerializeField] private bool[] _instanceOwnerOnly = new bool[0];
        [SerializeField] private bool[] _instanceMasterOnly = new bool[0];
        [SerializeField] private bool[] _verifiedUserOnly = new bool[0];
        [SerializeField] private bool[] _toggleInstanceOwnerOnly = new bool[0];
        [SerializeField] private bool[] _toggleInstanceMasterOnly = new bool[0];
        [SerializeField] private bool[] _toggleVerifiedUserOnly = new bool[0];
        [SerializeField] private string[] _groups = new string[0];
        [SerializeField] private int[] _groupIndexes = new int[0];
        [SerializeField] private bool[] _useChildVariants = new bool[0];
        [SerializeField] private RadioButtonHelper[] _variantControls = new RadioButtonHelper[0];
        [SerializeField] private GameObject[] _groupHeaders = new GameObject[0];
        [SerializeField] private bool _respawnInstanceOwnerOnly;
        [SerializeField] private bool _respawnInstanceMasterOnly;
        [SerializeField] private bool _respawnVerifiedUserOnly;

        [UdonSynced] private string _syncedGlobalStates = "";
        [UdonSynced] private string _syncedGlobalVariants = "";

        private Vector3[] _initialPositions = new Vector3[0];
        private Quaternion[] _initialRotations = new Quaternion[0];
        private Vector3[] _initialScales = new Vector3[0];
        private bool[] _lastToggleStates = new bool[0];
        private string[] _lastVariantValues = new string[0];
        private Transform[] _variantTransforms = new Transform[0];
        private int[] _variantParentIndexes = new int[0];
        private Vector3[] _initialVariantPositions = new Vector3[0];
        private Quaternion[] _initialVariantRotations = new Quaternion[0];
        private Vector3[] _initialVariantScales = new Vector3[0];
        private bool _initialized;
        private bool _playerDataReady;

        private void Start()
        {
            InitializeObjects();
            ApplyRuntimeLabels();
            ApplyPermissions();
            SendCustomEventDelayedSeconds(nameof(RefreshPermissions), 1f);
        }

        public void OnModuleCalled()
        {
            ApplyRuntimeLabels();
            ApplyPermissions();
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (player == null || !player.isLocal) return;

            _playerDataReady = true;
            if (_initialized) RestorePersistentToggleStates(player);
        }

        public void RefreshPermissions()
        {
            ApplyPermissions();
            SendCustomEventDelayedSeconds(nameof(RefreshPermissions), 1f);
        }

        private void Update()
        {
            if (!_initialized) return;

            var count = GetObjectCount();
            var layoutChanged = false;
            for (int i = 0; i < count; i++)
            {
                var toggle = _toggles[i];
                if (toggle == null) continue;

                var state = toggle.isOn;
                if (_lastToggleStates[i] == state) continue;

                _lastToggleStates[i] = state;
                SetObjectActive(i, state);
                layoutChanged = true;

                SavePersistentToggleState(i, state);

                if (IsGlobalToggle(i))
                {
                    PushGlobalState();
                }
            }

            for (int i = 0; i < count; i++)
            {
                if (!UsesChildVariants(i) || _variantControls == null || i >= _variantControls.Length || _variantControls[i] == null) continue;
                var value = _variantControls[i].Value;
                if (string.IsNullOrEmpty(value) || value == _lastVariantValues[i]) continue;
                _lastVariantValues[i] = value;
                ApplyVariant(i, value);
                if (!IsLocalOnly(i)) PushGlobalState();
            }
            if (layoutChanged) RefreshRuntimeLayout();
        }

        public void InitializeObjects()
        {
            var count = GetObjectCount();
            _initialPositions = new Vector3[count];
            _initialRotations = new Quaternion[count];
            _initialScales = new Vector3[count];
            _lastToggleStates = new bool[count];
            _lastVariantValues = new string[count];

            for (int i = 0; i < count; i++)
            {
                var target = _objects[i];
                if (target != null)
                {
                    var targetTransform = target.transform;
                    _initialPositions[i] = targetTransform.position;
                    _initialRotations[i] = targetTransform.rotation;
                    _initialScales[i] = targetTransform.localScale;
                }

                var defaultState = IsEnabledByDefault(i);
                _lastToggleStates[i] = defaultState;
                _lastVariantValues[i] = "0";

                if (_toggles[i] != null)
                {
                    _toggles[i].isOn = defaultState;
                }

                SetObjectActive(i, defaultState);
                SetVariantControlVisible(i, defaultState);
                ApplyVariant(i, "0");
                ApplyLabel(i);
            }

            InitializeVariantTransforms(count);

            if (Networking.LocalPlayer == null || Networking.IsOwner(gameObject))
            {
                _syncedGlobalStates = BuildGlobalStateString();
                _syncedGlobalVariants = BuildGlobalVariantString();
                RequestSerialization();
            }

            if (!string.IsNullOrEmpty(_syncedGlobalStates))
            {
                ApplyGlobalStateString(_syncedGlobalStates);
            }
            if (!string.IsNullOrEmpty(_syncedGlobalVariants)) ApplyGlobalVariantString(_syncedGlobalVariants);

            _initialized = true;
            if (_playerDataReady && Networking.LocalPlayer != null)
            {
                RestorePersistentToggleStates(Networking.LocalPlayer);
            }
            RefreshRuntimeLayout();
        }

        private void RestorePersistentToggleStates(VRCPlayerApi player)
        {
            var count = GetObjectCount();
            for (int i = 0; i < count; i++)
            {
                if (!ShouldPersistToggle(i)) continue;

                var key = GetPersistenceKey(i);
                if (string.IsNullOrEmpty(key) || !PlayerData.HasKey(player, key)) continue;

                var state = PlayerData.GetBool(player, key);
                _lastToggleStates[i] = state;
                if (_toggles[i] != null) _toggles[i].isOn = state;
                SetObjectActive(i, state);
                SetVariantControlVisible(i, state);
            }

            RefreshRuntimeLayout();
        }

        private void SavePersistentToggleState(int index, bool state)
        {
            if (!_playerDataReady || !ShouldPersistToggle(index)) return;

            var key = GetPersistenceKey(index);
            if (!string.IsNullOrEmpty(key)) PlayerData.SetBool(key, state);
        }

        private bool ShouldPersistToggle(int index)
        {
            return !IsGlobalToggle(index)
                && _rememberToggleState != null
                && index >= 0
                && index < _rememberToggleState.Length
                && _rememberToggleState[index];
        }

        private string GetPersistenceKey(int index)
        {
            if (_persistenceKeys == null || index < 0 || index >= _persistenceKeys.Length) return "";
            if (string.IsNullOrEmpty(_persistenceKeys[index])) return "";
            return "rehub.objects.toggle." + _persistenceKeys[index];
        }

        public override void OnDeserialization()
        {
            ApplyGlobalStateString(_syncedGlobalStates);
            ApplyGlobalVariantString(_syncedGlobalVariants);
        }

        public void RespawnAllObjects()
        {
            var count = GetObjectCount();
            for (int i = 0; i < count; i++)
            {
                RespawnObject(i);
            }
        }

        private void RespawnObject(int index)
        {
            if (index < 0 || index >= GetObjectCount()) return;

            var target = _objects[index];
            if (target == null) return;

            var pickup = target.GetComponent<VRCPickup>();
            if (pickup != null) pickup.Drop();

            if (!IsLocalOnly(index) && Networking.LocalPlayer != null && !Networking.IsOwner(target))
            {
                Networking.SetOwner(Networking.LocalPlayer, target);
            }

            var rigidbody = target.GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.velocity = Vector3.zero;
                rigidbody.angularVelocity = Vector3.zero;
            }

            var targetTransform = target.transform;
            var objectSync = target.GetComponent<VRCObjectSync>();
            if (!IsLocalOnly(index) && objectSync != null)
            {
                objectSync.Respawn();
            }
            else
            {
                targetTransform.position = _initialPositions[index];
                targetTransform.rotation = _initialRotations[index];
            }
            targetTransform.localScale = _initialScales[index];
            RespawnVariantObjects(index);
        }

        private void InitializeVariantTransforms(int objectCount)
        {
            var total = 0;
            for (int i = 0; i < objectCount; i++)
            {
                if (UsesChildVariants(i) && _objects[i] != null) total += _objects[i].transform.childCount;
            }

            _variantTransforms = new Transform[total];
            _variantParentIndexes = new int[total];
            _initialVariantPositions = new Vector3[total];
            _initialVariantRotations = new Quaternion[total];
            _initialVariantScales = new Vector3[total];
            var cursor = 0;
            for (int i = 0; i < objectCount; i++)
            {
                if (!UsesChildVariants(i) || _objects[i] == null) continue;
                for (int j = 0; j < _objects[i].transform.childCount; j++)
                {
                    var child = _objects[i].transform.GetChild(j);
                    _variantTransforms[cursor] = child;
                    _variantParentIndexes[cursor] = i;
                    _initialVariantPositions[cursor] = child.position;
                    _initialVariantRotations[cursor] = child.rotation;
                    _initialVariantScales[cursor] = child.localScale;
                    cursor++;
                }
            }
        }

        private void RespawnVariantObjects(int parentIndex)
        {
            for (int i = 0; i < _variantTransforms.Length; i++)
            {
                if (_variantParentIndexes[i] != parentIndex || _variantTransforms[i] == null) continue;
                var child = _variantTransforms[i].gameObject;
                var pickup = child.GetComponent<VRCPickup>();
                if (pickup != null) pickup.Drop();
                if (!IsLocalOnly(parentIndex) && Networking.LocalPlayer != null && !Networking.IsOwner(child))
                {
                    Networking.SetOwner(Networking.LocalPlayer, child);
                }
                var rigidbody = child.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    rigidbody.velocity = Vector3.zero;
                    rigidbody.angularVelocity = Vector3.zero;
                }
                var objectSync = child.GetComponent<VRCObjectSync>();
                if (!IsLocalOnly(parentIndex) && objectSync != null)
                {
                    objectSync.Respawn();
                }
                else
                {
                    _variantTransforms[i].position = _initialVariantPositions[i];
                    _variantTransforms[i].rotation = _initialVariantRotations[i];
                }
                _variantTransforms[i].localScale = _initialVariantScales[i];
            }
        }

        private void ApplyRuntimeLabels()
        {
            if (_respawnButtonLabel == null) return;
            var manager = GetComponent<I18nManager>();
            if (manager == null || !manager.Initialized) return;
            _respawnButtonLabel.text = manager.GetTranslation("respawnObjects");
        }

        private void ApplyPermissions()
        {
            var count = GetObjectCount();
            for (int i = 0; i < count; i++)
            {
                var canUseToggle = HasPermission(
                    GetPermission(_toggleInstanceOwnerOnly, i),
                    GetPermission(_toggleInstanceMasterOnly, i),
                    GetPermission(_toggleVerifiedUserOnly, i));
                var canUsePickup = HasPermission(
                    GetPermission(_instanceOwnerOnly, i),
                    GetPermission(_instanceMasterOnly, i),
                    GetPermission(_verifiedUserOnly, i));

                if (_toggles[i] != null) _toggles[i].interactable = canUseToggle;
                if (_variantControls != null && i < _variantControls.Length && _variantControls[i] != null)
                {
                    var variantToggles = _variantControls[i].GetComponentsInChildren<Toggle>(true);
                    for (int j = 0; j < variantToggles.Length; j++) variantToggles[j].interactable = canUseToggle;
                }
                var pickup = _objects[i] != null ? _objects[i].GetComponent<VRCPickup>() : null;
                if (pickup != null) pickup.pickupable = canUsePickup;
            }

            if (_respawnButton != null)
            {
                _respawnButton.interactable = HasPermission(
                    _respawnInstanceOwnerOnly,
                    _respawnInstanceMasterOnly,
                    _respawnVerifiedUserOnly);
            }
        }

        private bool HasPermission(bool ownerOnly, bool masterOnly, bool verifiedOnly)
        {
            if (!ownerOnly && !masterOnly && !verifiedOnly) return true;
            if (Networking.LocalPlayer == null) return false;
            return (ownerOnly && Networking.LocalPlayer.isInstanceOwner)
                || (masterOnly && Networking.LocalPlayer.isMaster)
                || (verifiedOnly && _uiManager != null && _uiManager.IsLocalPlayerVerified());
        }

        private bool GetPermission(bool[] values, int index)
        {
            return values != null && index >= 0 && index < values.Length && values[index];
        }

        private void SetObjectActive(int index, bool active)
        {
            if (index < 0 || index >= GetObjectCount()) return;
            if (_objects[index] == null) return;
            _objects[index].SetActive(active);
            SetVariantControlVisible(index, active);
        }

        private void SetVariantControlVisible(int index, bool visible)
        {
            if (!UsesChildVariants(index) || _variantControls == null || index < 0 || index >= _variantControls.Length) return;
            if (_variantControls[index] != null) _variantControls[index].gameObject.SetActive(visible);
        }

        private void ApplyVariant(int index, string value)
        {
            if (!UsesChildVariants(index) || index < 0 || index >= GetObjectCount()) return;
            var target = _objects[index];
            if (target == null) return;
            for (int i = 0; i < target.transform.childCount; i++)
            {
                target.transform.GetChild(i).gameObject.SetActive(value == i.ToString());
            }
        }

        private bool UsesChildVariants(int index)
        {
            return _useChildVariants != null && index >= 0 && index < _useChildVariants.Length && _useChildVariants[index];
        }

        private void RefreshRuntimeLayout()
        {
            if (_toggleListRoot == null) return;

            var cursorY = 40f;
            var count = GetObjectCount();
            for (int i = 0; i < count; i++)
            {
                if (_groupHeaders != null && i < _groupHeaders.Length && _groupHeaders[i] != null)
                {
                    var headerRect = _groupHeaders[i].GetComponent<RectTransform>();
                    if (headerRect != null) headerRect.anchoredPosition = new Vector2(100f, -cursorY);
                    cursorY += 70f;
                }

                if (_toggles[i] != null)
                {
                    var toggleRect = _toggles[i].GetComponent<RectTransform>();
                    if (toggleRect != null) toggleRect.anchoredPosition = new Vector2(100f, -cursorY);
                }
                cursorY += 90f;

                if (_variantControls != null && i < _variantControls.Length && _variantControls[i] != null && _variantControls[i].gameObject.activeSelf)
                {
                    var variantRect = _variantControls[i].GetComponent<RectTransform>();
                    if (variantRect != null)
                    {
                        variantRect.anchoredPosition = new Vector2(100f, -cursorY);
                        cursorY += variantRect.sizeDelta.y + 20f;
                    }
                }
            }

            var listRect = _toggleListRoot.GetComponent<RectTransform>();
            if (listRect != null) listRect.sizeDelta = new Vector2(listRect.sizeDelta.x, Mathf.Max(620f, cursorY + 40f));

            var viewport = _toggleListRoot.parent;
            if (viewport != null)
            {
                var scrollView = viewport.parent;
                if (scrollView != null)
                {
                    var scrollbar = scrollView.Find("Scrollbar Vertical (Template)");
                    if (scrollbar != null) scrollbar.gameObject.SetActive(cursorY > 620f);
                }
            }
        }

        private void PushGlobalState()
        {
            if (Networking.LocalPlayer != null && !Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            _syncedGlobalStates = BuildGlobalStateString();
            _syncedGlobalVariants = BuildGlobalVariantString();
            RequestSerialization();
        }

        private string BuildGlobalVariantString()
        {
            var result = "";
            var count = GetObjectCount();
            for (int i = 0; i < count; i++)
            {
                if (IsLocalOnly(i) || !UsesChildVariants(i) || _variantControls == null || i >= _variantControls.Length || _variantControls[i] == null)
                {
                    result += "-";
                    continue;
                }
                var value = _lastVariantValues != null && i < _lastVariantValues.Length ? _lastVariantValues[i] : "0";
                result += string.IsNullOrEmpty(value) ? "0" : value.Substring(0, 1);
            }
            return result;
        }

        private void ApplyGlobalVariantString(string states)
        {
            if (string.IsNullOrEmpty(states)) return;
            var count = GetObjectCount();
            for (int i = 0; i < count && i < states.Length; i++)
            {
                if (IsLocalOnly(i) || !UsesChildVariants(i)) continue;
                var value = states.Substring(i, 1);
                if (value == "-") continue;
                _lastVariantValues[i] = value;
                ApplyVariant(i, value);
                if (_variantControls != null && i < _variantControls.Length && _variantControls[i] != null)
                {
                    _variantControls[i].SetValue(value);
                }
            }
        }

        private string BuildGlobalStateString()
        {
            var count = GetObjectCount();
            var result = "";
            for (int i = 0; i < count; i++)
            {
                if (!IsGlobalToggle(i))
                {
                    result += "-";
                    continue;
                }

                var state = _toggles[i] != null ? _toggles[i].isOn : IsEnabledByDefault(i);
                result += state ? "1" : "0";
            }

            return result;
        }

        private void ApplyGlobalStateString(string states)
        {
            if (string.IsNullOrEmpty(states)) return;

            var count = GetObjectCount();
            for (int i = 0; i < count && i < states.Length; i++)
            {
                if (!IsGlobalToggle(i)) continue;

                var stateChar = states.Substring(i, 1);
                if (stateChar != "0" && stateChar != "1") continue;

                var state = stateChar == "1";
                SetObjectActive(i, state);
                _lastToggleStates[i] = state;

                if (_toggles[i] != null)
                {
                    _toggles[i].isOn = state;
                }
            }
            RefreshRuntimeLayout();
        }

        private void ApplyLabel(int index)
        {
            if (index < 0 || _toggleLabels == null || index >= _toggleLabels.Length) return;
            if (_toggleLabels[index] == null || _objects[index] == null) return;
            _toggleLabels[index].text = _objects[index].name;
        }

        private int GetObjectCount()
        {
            var count = _objects != null ? _objects.Length : 0;
            if (_toggles == null || _enabledByDefault == null || _localOnly == null) return 0;
            if (_toggles.Length < count) count = _toggles.Length;
            if (_enabledByDefault.Length < count) count = _enabledByDefault.Length;
            if (_localOnly.Length < count) count = _localOnly.Length;
            return count;
        }

        private bool IsEnabledByDefault(int index)
        {
            return _enabledByDefault != null && index >= 0 && index < _enabledByDefault.Length && _enabledByDefault[index];
        }

        private bool IsLocalOnly(int index)
        {
            return _localOnly != null && index >= 0 && index < _localOnly.Length && _localOnly[index];
        }

        private bool IsGlobalToggle(int index)
        {
            return _globalToggle != null && index >= 0 && index < _globalToggle.Length && _globalToggle[index];
        }
    }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
    [CustomEditor(typeof(ObjectsModule))]
    internal class ObjectsModuleInspector : ModuleInspector
    {
        protected override string I18nUUID => "a8718e5d92dd4694afee0304c164dba4";
        protected override string[] ObjectProperties => new[] { "_objectRoot", "_toggleListRoot", "_toggleSwitchTextTemplate", "_respawnButton", "_respawnButtonLabel", "_uiManager" };

        protected override void OnEnable()
        {
            base.OnEnable();
            ObjectsModuleEditorUtility.RepairModule((ObjectsModule)target);
        }

        protected override void DrawModuleInspector()
        {
            EditorGUILayout.HelpBox(_i18n.GetTranslation("description"), MessageType.Info);
            EditorGUILayout.Space();

            if (GUILayout.Button(_i18n.GetTranslation("repairModule")))
            {
                ObjectsModuleEditorUtility.RepairModule((ObjectsModule)target);
            }

            if (GUILayout.Button(_i18n.GetTranslation("addObject")))
            {
                ObjectsModuleEditorUtility.AddObject((ObjectsModule)target);
            }

            if (GUILayout.Button(_i18n.GetTranslation("addPickup")))
            {
                ObjectsModuleEditorUtility.AddPickup((ObjectsModule)target);
            }

            if (GUILayout.Button(_i18n.GetTranslation("addGroup")))
            {
                ObjectsModuleEditorUtility.AddGroup((ObjectsModule)target);
            }

            if (GUILayout.Button(_i18n.GetTranslation("rebuildToggles")))
            {
                ObjectsModuleEditorUtility.RebuildToggles((ObjectsModule)target);
            }

            if (GUILayout.Button(_i18n.GetTranslation("respawnObjects")))
            {
                ObjectsModuleEditorUtility.RespawnObjects((ObjectsModule)target);
            }

            EditorGUILayout.LabelField(_i18n.GetTranslation("respawnPermissions"), EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_respawnInstanceOwnerOnly"), new GUIContent(_i18n.GetTranslation("instanceOwnerOnly")));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_respawnInstanceMasterOnly"), new GUIContent(_i18n.GetTranslation("instanceMasterOnly")));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_respawnVerifiedUserOnly"), new GUIContent(_i18n.GetTranslation("verifiedUserOnly")));

            EditorGUILayout.Space();
            DrawGroups();
            DrawObjectsList();
        }

        private void DrawGroups()
        {
            var groups = serializedObject.FindProperty("_groups");
            if (groups.arraySize == 0) return;
            EditorGUILayout.LabelField(_i18n.GetTranslation("groups"), EditorStyles.boldLabel);
            for (int i = 0; i < groups.arraySize; i++)
            {
                using (new EditorGUILayout.VerticalScope(GUI.skin.box))
                {
                    EditorGUILayout.PropertyField(groups.GetArrayElementAtIndex(i), new GUIContent(_i18n.GetTranslation("groupName")));
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(_i18n.GetTranslation("addObject"))) ObjectsModuleEditorUtility.AddObject((ObjectsModule)target, i);
                        if (GUILayout.Button(_i18n.GetTranslation("addPickup"))) ObjectsModuleEditorUtility.AddPickup((ObjectsModule)target, i);
                        if (GUILayout.Button(_i18n.GetTranslation("removeGroup")))
                        {
                            ObjectsModuleEditorUtility.RemoveGroup((ObjectsModule)target, i);
                            break;
                        }
                    }
                }
            }
        }

        private void DrawObjectsList()
        {
            var objects = serializedObject.FindProperty("_objects");
            var toggles = serializedObject.FindProperty("_toggles");
            var labels = serializedObject.FindProperty("_toggleLabels");
            var defaults = serializedObject.FindProperty("_enabledByDefault");
            var localOnly = serializedObject.FindProperty("_localOnly");
            var globalToggle = serializedObject.FindProperty("_globalToggle");
            var rememberToggleState = serializedObject.FindProperty("_rememberToggleState");
            var persistenceKeys = serializedObject.FindProperty("_persistenceKeys");
            var ownerOnly = serializedObject.FindProperty("_instanceOwnerOnly");
            var masterOnly = serializedObject.FindProperty("_instanceMasterOnly");
            var verifiedOnly = serializedObject.FindProperty("_verifiedUserOnly");
            var toggleOwnerOnly = serializedObject.FindProperty("_toggleInstanceOwnerOnly");
            var toggleMasterOnly = serializedObject.FindProperty("_toggleInstanceMasterOnly");
            var toggleVerifiedOnly = serializedObject.FindProperty("_toggleVerifiedUserOnly");
            var groupIndexes = serializedObject.FindProperty("_groupIndexes");
            var useChildVariants = serializedObject.FindProperty("_useChildVariants");
            var variantControls = serializedObject.FindProperty("_variantControls");
            var groupHeaders = serializedObject.FindProperty("_groupHeaders");

            ObjectsModuleEditorUtility.NormalizeArrays(
                objects, toggles, labels, defaults, localOnly, globalToggle, rememberToggleState, persistenceKeys,
                ownerOnly, masterOnly, verifiedOnly,
                toggleOwnerOnly, toggleMasterOnly, toggleVerifiedOnly,
                groupIndexes, useChildVariants, variantControls, groupHeaders);

            EditorGUILayout.LabelField(_i18n.GetTranslation("objects"), EditorStyles.boldLabel);
            for (int i = 0; i < objects.arraySize; i++)
            {
                using (new EditorGUILayout.VerticalScope(GUI.skin.box))
                {
                    EditorGUILayout.PropertyField(objects.GetArrayElementAtIndex(i), new GUIContent(_i18n.GetTranslation("object")));
                    EditorGUILayout.PropertyField(defaults.GetArrayElementAtIndex(i), new GUIContent(_i18n.GetTranslation("enabledByDefault")));
                    EditorGUILayout.PropertyField(globalToggle.GetArrayElementAtIndex(i), new GUIContent(_i18n.GetTranslation("globalToggle")));
                    using (new EditorGUI.DisabledScope(globalToggle.GetArrayElementAtIndex(i).boolValue))
                    {
                        EditorGUILayout.PropertyField(rememberToggleState.GetArrayElementAtIndex(i), new GUIContent(_i18n.GetTranslation("rememberToggleState")));
                    }
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(localOnly.GetArrayElementAtIndex(i), new GUIContent(_i18n.GetTranslation("localOnly")));
                    if (EditorGUI.EndChangeCheck())
                    {
                        serializedObject.ApplyModifiedProperties();
                        ObjectsModuleEditorUtility.ConfigurePickupNetworking(
                            objects.GetArrayElementAtIndex(i).objectReferenceValue as GameObject,
                            localOnly.GetArrayElementAtIndex(i).boolValue);
                        serializedObject.Update();
                    }
                    EditorGUILayout.PropertyField(toggles.GetArrayElementAtIndex(i), new GUIContent(_i18n.GetTranslation("toggle")));
                    EditorGUILayout.PropertyField(labels.GetArrayElementAtIndex(i), new GUIContent(_i18n.GetTranslation("label")));
                    ObjectsModuleEditorUtility.DrawGroupPopup(serializedObject, groupIndexes.GetArrayElementAtIndex(i), _i18n);
                    EditorGUILayout.PropertyField(useChildVariants.GetArrayElementAtIndex(i), new GUIContent(_i18n.GetTranslation("useChildVariants")));
                    if (GUILayout.Button(_i18n.GetTranslation("addVariant")))
                    {
                        ObjectsModuleEditorUtility.AddVariant((ObjectsModule)target, i);
                    }
                    EditorGUILayout.LabelField(_i18n.GetTranslation("togglePermissions"), EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(toggleOwnerOnly.GetArrayElementAtIndex(i), new GUIContent(_i18n.GetTranslation("instanceOwnerOnly")));
                    EditorGUILayout.PropertyField(toggleMasterOnly.GetArrayElementAtIndex(i), new GUIContent(_i18n.GetTranslation("instanceMasterOnly")));
                    EditorGUILayout.PropertyField(toggleVerifiedOnly.GetArrayElementAtIndex(i), new GUIContent(_i18n.GetTranslation("verifiedUserOnly")));
                    EditorGUILayout.LabelField(_i18n.GetTranslation("pickupPermissions"), EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(ownerOnly.GetArrayElementAtIndex(i), new GUIContent(_i18n.GetTranslation("instanceOwnerOnly")));
                    EditorGUILayout.PropertyField(masterOnly.GetArrayElementAtIndex(i), new GUIContent(_i18n.GetTranslation("instanceMasterOnly")));
                    EditorGUILayout.PropertyField(verifiedOnly.GetArrayElementAtIndex(i), new GUIContent(_i18n.GetTranslation("verifiedUserOnly")));

                    if (GUILayout.Button(_i18n.GetTranslation("removeObject")))
                    {
                        ObjectsModuleEditorUtility.RemoveObjectAt((ObjectsModule)target, serializedObject, i);
                        break;
                    }
                }
            }
        }
    }

    internal static class ObjectsModuleEditorUtility
    {
        private const string ModuleTemplatePath = "Packages/com.rehub.rehubsystem/Assets/Templates/ModuleTemplate.prefab";
        private const string ToggleTemplatePath = "Packages/com.rehub.rehubsystem/Assets/Templates/UI/ToggleSwitch.prefab";
        private const string UiElementsModulePath = "Packages/com.rehub.rehubsystem/Runtime/Modules/UIElementsTestModule.prefab";
        private const string VerticalContentTemplatePath = "Packages/com.rehub.rehubsystem/Assets/Templates/ModuleVerticalContentTemplate.prefab";
        private const string RadioButtonsTemplatePath = "Packages/com.rehub.rehubsystem/Assets/Templates/UI/RadioButtons.prefab";
        private const string TextTemplatePath = "Packages/com.rehub.rehubsystem/Assets/Templates/UI/Text.prefab";
        private const string ModuleIconPath = "Packages/com.rehub.rehubsystem/Assets/Icons/Plus.png";
        private const string I18nPath = "Packages/com.rehub.rehubsystem/Runtime/Modules/Objects/i18n.json";

        [MenuItem("GameObject/Rehub System/Modules/Object Module", false, 10)]
        private static void CreateObjectModule()
        {
            var systemRoot = FindSystemRoot();
            if (systemRoot == null)
            {
                EditorUtility.DisplayDialog("Rehub System", "Select RehubSystem in the hierarchy first.", "OK");
                return;
            }

            var modulesRoot = systemRoot.Find("Modules");
            if (modulesRoot == null)
            {
                var modulesObject = new GameObject("Modules");
                Undo.RegisterCreatedObjectUndo(modulesObject, "Create Modules root");
                modulesObject.transform.SetParent(systemRoot, false);
                modulesRoot = modulesObject.transform;
            }

            var template = AssetDatabase.LoadAssetAtPath<GameObject>(ModuleTemplatePath);
            if (template == null)
            {
                EditorUtility.DisplayDialog("Rehub System", "ModuleTemplate.prefab was not found.", "OK");
                return;
            }

            var moduleObject = (GameObject)PrefabUtility.InstantiatePrefab(template);
            Undo.RegisterCreatedObjectUndo(moduleObject, "Create Objects module");
            moduleObject.name = "ObjectsModule";
            moduleObject.transform.SetParent(modulesRoot, false);

            var metadata = moduleObject.GetComponent<ModuleMetadata>();
            if (metadata != null)
            {
                metadata.moduleName = "Objects";
                metadata.moduleVersion = "1.0.0";
                metadata.forceUseModuleName = false;
                metadata.moduleIcon = AssetDatabase.LoadAssetAtPath<Sprite>(ModuleIconPath);

                var metadataObject = new SerializedObject(metadata);
                metadataObject.FindProperty("_moduleId").stringValue = "com.rehub.objects";
                metadataObject.FindProperty("_isUnique").boolValue = true;
                metadataObject.ApplyModifiedPropertiesWithoutUndo();
            }

            var objectsModule = AddObjectsModuleComponent(moduleObject);
            if (objectsModule == null)
            {
                Undo.DestroyObjectImmediate(moduleObject);
                EditorUtility.DisplayDialog(
                    "Rehub System",
                    "UdonSharp could not create the Objects module component. Reimport the VRChat Worlds package and try again.",
                    "OK");
                return;
            }

            SetupModuleUi(objectsModule, metadata);
            RepairModule(objectsModule);

            Selection.activeObject = moduleObject;
            EditorUtility.SetDirty(moduleObject);
        }

        private static ObjectsModule AddObjectsModuleComponent(GameObject moduleObject)
        {
            const string extensionTypeName = "UdonSharpEditor.UdonSharpComponentExtensions";
            Type extensionType = null;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                extensionType = assemblies[i].GetType(extensionTypeName);
                if (extensionType != null) break;
            }

            if (extensionType == null) return null;

            var method = extensionType.GetMethod(
                "AddUdonSharpComponent",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(GameObject), typeof(Type) },
                null);
            if (method == null) return null;

            return method.Invoke(null, new object[] { moduleObject, typeof(ObjectsModule) }) as ObjectsModule;
        }

        public static void RepairModule(ObjectsModule module)
        {
            if (module == null) return;

            var metadata = module.GetComponent<ModuleMetadata>();
            if (metadata != null)
            {
                metadata.moduleName = "Objects";
                metadata.moduleVersion = string.IsNullOrEmpty(metadata.moduleVersion) ? "1.0.0" : metadata.moduleVersion;
                if (metadata.moduleIcon == null)
                {
                    metadata.moduleIcon = AssetDatabase.LoadAssetAtPath<Sprite>(ModuleIconPath);
                }

                var metadataObject = new SerializedObject(metadata);
                var id = metadataObject.FindProperty("_moduleId");
                if (id != null && string.IsNullOrEmpty(id.stringValue)) id.stringValue = "com.rehub.objects";
                metadataObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(metadata);
            }

            AssignLocalization(module, metadata);
            AssignUiManager(module);
            EnsureModuleRoots(module, metadata);
            EnsurePersistenceSettings(module);
        }

        private static void EnsurePersistenceSettings(ObjectsModule module)
        {
            var serializedModule = new SerializedObject(module);
            var objects = serializedModule.FindProperty("_objects");
            var rememberStates = serializedModule.FindProperty("_rememberToggleState");
            var persistenceKeys = serializedModule.FindProperty("_persistenceKeys");
            var objectCount = objects.arraySize;
            var previousRememberCount = rememberStates.arraySize;

            rememberStates.arraySize = objectCount;
            persistenceKeys.arraySize = objectCount;
            for (int i = 0; i < objectCount; i++)
            {
                if (i >= previousRememberCount) rememberStates.GetArrayElementAtIndex(i).boolValue = true;

                var keyProperty = persistenceKeys.GetArrayElementAtIndex(i);
                var duplicate = false;
                if (!string.IsNullOrEmpty(keyProperty.stringValue))
                {
                    for (int j = 0; j < i; j++)
                    {
                        if (persistenceKeys.GetArrayElementAtIndex(j).stringValue == keyProperty.stringValue)
                        {
                            duplicate = true;
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(keyProperty.stringValue) || duplicate)
                {
                    keyProperty.stringValue = Guid.NewGuid().ToString("N");
                }
            }

            serializedModule.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(module);
        }

        public static void AddObject(ObjectsModule module, int groupIndex = -1)
        {
            var serializedModule = new SerializedObject(module);
            var objectRoot = EnsureObjectRoot(module, serializedModule);
            var createdObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(createdObject, "Create Rehub object");
            createdObject.name = $"Cube Global {GetNextObjectIndex(serializedModule):00}";
            createdObject.transform.SetParent(objectRoot, false);
            createdObject.transform.localPosition = new Vector3(0f, 0.5f, 0f);

            AddObjectReference(serializedModule, createdObject, false, true);
            serializedModule.FindProperty("_groupIndexes").GetArrayElementAtIndex(serializedModule.FindProperty("_objects").arraySize - 1).intValue = groupIndex;
            serializedModule.ApplyModifiedProperties();
            RebuildToggles(module);
        }

        public static void AddPickup(ObjectsModule module, int groupIndex = -1)
        {
            var serializedModule = new SerializedObject(module);
            var objectRoot = EnsureObjectRoot(module, serializedModule);
            var createdObject = new GameObject($"Pickup Global {GetNextObjectIndex(serializedModule):00}");
            Undo.RegisterCreatedObjectUndo(createdObject, "Create Rehub pickup");
            createdObject.transform.SetParent(objectRoot, false);
            createdObject.transform.localPosition = new Vector3(0f, 0.5f, 0f);

            Undo.AddComponent<Rigidbody>(createdObject);
            Undo.AddComponent<VRCPickup>(createdObject);
            Undo.AddComponent<VRCObjectSync>(createdObject);

            AddObjectReference(serializedModule, createdObject, false, true);
            serializedModule.FindProperty("_groupIndexes").GetArrayElementAtIndex(serializedModule.FindProperty("_objects").arraySize - 1).intValue = groupIndex;
            serializedModule.ApplyModifiedProperties();
            RebuildToggles(module);
            Selection.activeObject = createdObject;
        }

        public static void AddGroup(ObjectsModule module)
        {
            var serializedModule = new SerializedObject(module);
            var groups = serializedModule.FindProperty("_groups");
            var index = groups.arraySize;
            groups.InsertArrayElementAtIndex(index);
            groups.GetArrayElementAtIndex(index).stringValue = $"Group {index + 1:00}";
            serializedModule.ApplyModifiedProperties();
        }

        public static void RemoveGroup(ObjectsModule module, int groupIndex)
        {
            var serializedModule = new SerializedObject(module);
            var groups = serializedModule.FindProperty("_groups");
            var groupIndexes = serializedModule.FindProperty("_groupIndexes");
            for (int i = 0; i < groupIndexes.arraySize; i++)
            {
                var value = groupIndexes.GetArrayElementAtIndex(i).intValue;
                if (value == groupIndex) groupIndexes.GetArrayElementAtIndex(i).intValue = -1;
                else if (value > groupIndex) groupIndexes.GetArrayElementAtIndex(i).intValue = value - 1;
            }
            groups.DeleteArrayElementAtIndex(groupIndex);
            serializedModule.ApplyModifiedProperties();
            RebuildToggles(module);
        }

        public static void DrawGroupPopup(SerializedObject serializedModule, SerializedProperty groupIndex, InternalEditorI18n i18n)
        {
            var groups = serializedModule.FindProperty("_groups");
            var labels = new string[groups.arraySize + 1];
            labels[0] = i18n.GetTranslation("noGroup");
            for (int i = 0; i < groups.arraySize; i++) labels[i + 1] = groups.GetArrayElementAtIndex(i).stringValue;
            groupIndex.intValue = EditorGUILayout.Popup(i18n.GetTranslation("group"), groupIndex.intValue + 1, labels) - 1;
        }

        public static void AddVariant(ObjectsModule module, int objectIndex)
        {
            var serializedModule = new SerializedObject(module);
            var target = serializedModule.FindProperty("_objects").GetArrayElementAtIndex(objectIndex).objectReferenceValue as GameObject;
            if (target == null) return;
            if (target.transform.childCount >= 10)
            {
                EditorUtility.DisplayDialog("Rehub System", "A single object currently supports up to 10 variants.", "OK");
                return;
            }
            var variant = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(variant, "Create object variant");
            variant.name = $"Variant {target.transform.childCount + 1:00}";
            variant.transform.SetParent(target.transform, false);
            variant.transform.localPosition = Vector3.zero;
            serializedModule.FindProperty("_useChildVariants").GetArrayElementAtIndex(objectIndex).boolValue = true;
            serializedModule.ApplyModifiedProperties();
            RebuildToggles(module);
            Selection.activeObject = variant;
        }

        public static void ConfigurePickupNetworking(GameObject target, bool localOnly)
        {
            if (target == null) return;
            ConfigurePickupNetworkingRecursive(target.transform, localOnly);
        }

        private static void ConfigurePickupNetworkingRecursive(Transform target, bool localOnly)
        {
            var pickup = target.GetComponent<VRCPickup>();
            if (pickup != null)
            {
                if (target.GetComponent<Rigidbody>() == null) Undo.AddComponent<Rigidbody>(target.gameObject);
                var objectSync = target.GetComponent<VRCObjectSync>();
                if (localOnly && objectSync != null) Undo.DestroyObjectImmediate(objectSync);
                else if (!localOnly && objectSync == null) Undo.AddComponent<VRCObjectSync>(target.gameObject);
            }

            for (int i = 0; i < target.childCount; i++)
            {
                ConfigurePickupNetworkingRecursive(target.GetChild(i), localOnly);
            }
        }

        public static void RebuildToggles(ObjectsModule module)
        {
            EnsurePersistenceSettings(module);
            var serializedModule = new SerializedObject(module);
            var listRootProp = serializedModule.FindProperty("_toggleListRoot");
            var templateProp = serializedModule.FindProperty("_toggleSwitchTextTemplate");
            var respawnButton = serializedModule.FindProperty("_respawnButton").objectReferenceValue as Button;
            var listRoot = listRootProp.objectReferenceValue as Transform;
            var template = templateProp.objectReferenceValue as GameObject;

            if (listRoot == null || template == null)
            {
                var metadata = module.GetComponent<ModuleMetadata>();
                SetupModuleUi(module, metadata);
                serializedModule.Update();
                listRoot = serializedModule.FindProperty("_toggleListRoot").objectReferenceValue as Transform;
                template = serializedModule.FindProperty("_toggleSwitchTextTemplate").objectReferenceValue as GameObject;
            }

            if (listRoot == null || template == null) return;

            for (int i = listRoot.childCount - 1; i >= 0; i--)
            {
                var child = listRoot.GetChild(i);
                if (child.gameObject == template) continue;
                if (respawnButton != null && child.gameObject == respawnButton.gameObject) continue;
                Undo.DestroyObjectImmediate(child.gameObject);
            }

            var objects = serializedModule.FindProperty("_objects");
            var toggles = serializedModule.FindProperty("_toggles");
            var labels = serializedModule.FindProperty("_toggleLabels");
            var defaults = serializedModule.FindProperty("_enabledByDefault");
            var localOnly = serializedModule.FindProperty("_localOnly");
            var globalToggle = serializedModule.FindProperty("_globalToggle");
            var rememberToggleState = serializedModule.FindProperty("_rememberToggleState");
            var persistenceKeys = serializedModule.FindProperty("_persistenceKeys");
            var ownerOnly = serializedModule.FindProperty("_instanceOwnerOnly");
            var masterOnly = serializedModule.FindProperty("_instanceMasterOnly");
            var verifiedOnly = serializedModule.FindProperty("_verifiedUserOnly");
            var toggleOwnerOnly = serializedModule.FindProperty("_toggleInstanceOwnerOnly");
            var toggleMasterOnly = serializedModule.FindProperty("_toggleInstanceMasterOnly");
            var toggleVerifiedOnly = serializedModule.FindProperty("_toggleVerifiedUserOnly");
            var groupIndexes = serializedModule.FindProperty("_groupIndexes");
            var groups = serializedModule.FindProperty("_groups");
            var useChildVariants = serializedModule.FindProperty("_useChildVariants");
            var variantControls = serializedModule.FindProperty("_variantControls");
            var groupHeaders = serializedModule.FindProperty("_groupHeaders");
            NormalizeArrays(
                objects, toggles, labels, defaults, localOnly, globalToggle, rememberToggleState, persistenceKeys,
                ownerOnly, masterOnly, verifiedOnly,
                toggleOwnerOnly, toggleMasterOnly, toggleVerifiedOnly,
                groupIndexes, useChildVariants, variantControls, groupHeaders);

            var cursorY = 40f;
            var lastGroupIndex = -2;
            for (int i = 0; i < objects.arraySize; i++)
            {
                var objectRef = objects.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                ConfigurePickupNetworking(objectRef, localOnly.GetArrayElementAtIndex(i).boolValue);
                var groupIndex = groupIndexes.GetArrayElementAtIndex(i).intValue;
                if (groupIndex >= 0 && groupIndex < groups.arraySize && groupIndex != lastGroupIndex)
                {
                    groupHeaders.GetArrayElementAtIndex(i).objectReferenceValue = CreateGroupHeader(listRoot, groups.GetArrayElementAtIndex(groupIndex).stringValue, cursorY);
                    cursorY += 70f;
                }
                else
                {
                    groupHeaders.GetArrayElementAtIndex(i).objectReferenceValue = null;
                }
                lastGroupIndex = groupIndex;

                var toggleObject = (GameObject)PrefabUtility.InstantiatePrefab(template, listRoot);
                if (toggleObject == null) toggleObject = UnityEngine.Object.Instantiate(template, listRoot);

                Undo.RegisterCreatedObjectUndo(toggleObject, "Create object toggle");
                toggleObject.name = objectRef != null ? $"Toggle - {objectRef.name}" : $"Toggle - Object {i + 1:00}";
                toggleObject.SetActive(true);

                var rect = toggleObject.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchorMin = new Vector2(0f, 1f);
                    rect.anchorMax = new Vector2(0f, 1f);
                    rect.pivot = new Vector2(0f, 1f);
                    rect.anchoredPosition = new Vector2(100f, -cursorY);
                    rect.sizeDelta = new Vector2(900f, 72f);
                }

                var toggle = toggleObject.GetComponent<Toggle>();
                if (toggle != null)
                {
                    toggle.isOn = defaults.GetArrayElementAtIndex(i).boolValue;
                    toggles.GetArrayElementAtIndex(i).objectReferenceValue = toggle;
                }

                var label = toggleObject.GetComponentInChildren<Text>(true);
                if (label != null)
                {
                    label.text = objectRef != null ? objectRef.name : $"Object {i + 1:00}";
                    label.fontSize = 28;
                    labels.GetArrayElementAtIndex(i).objectReferenceValue = label;
                }
                cursorY += 90f;

                if (useChildVariants.GetArrayElementAtIndex(i).boolValue && objectRef != null && objectRef.transform.childCount > 0)
                {
                    var rows = (objectRef.transform.childCount + 1) / 2;
                    var variantHeight = rows * 110f;
                    var helper = CreateVariantControl(listRoot, objectRef, cursorY, variantHeight, defaults.GetArrayElementAtIndex(i).boolValue);
                    variantControls.GetArrayElementAtIndex(i).objectReferenceValue = helper;
                    cursorY += variantHeight + 20f;
                }
                else
                {
                    variantControls.GetArrayElementAtIndex(i).objectReferenceValue = null;
                }
            }

            var listRect = listRoot as RectTransform;
            if (listRect != null)
            {
                listRect.sizeDelta = new Vector2(listRect.sizeDelta.x, Mathf.Max(620f, cursorY + 40f));
            }

            var scrollView = listRoot.parent != null ? listRoot.parent.parent : null;
            if (scrollView != null)
            {
                var scrollbar = scrollView.Find("Scrollbar Vertical (Template)");
                if (scrollbar != null) scrollbar.gameObject.SetActive(cursorY > 620f);
            }

            serializedModule.ApplyModifiedProperties();
            EditorUtility.SetDirty(module);
        }

        private static GameObject CreateGroupHeader(Transform parent, string title, float cursorY)
        {
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(TextTemplatePath);
            if (template == null) return null;
            var header = (GameObject)PrefabUtility.InstantiatePrefab(template, parent);
            if (header == null) header = UnityEngine.Object.Instantiate(template, parent);
            header.name = $"Group - {title}";
            header.SetActive(true);
            var rect = header.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(100f, -cursorY);
            rect.sizeDelta = new Vector2(900f, 60f);
            var text = header.GetComponent<Text>();
            if (text != null)
            {
                text.text = title;
                text.fontSize = 32;
            }
            return header;
        }

        private static RadioButtonHelper CreateVariantControl(Transform parent, GameObject target, float cursorY, float height, bool visible)
        {
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(RadioButtonsTemplatePath);
            if (template == null) return null;
            var control = (GameObject)PrefabUtility.InstantiatePrefab(template, parent);
            if (control == null) control = UnityEngine.Object.Instantiate(template, parent);
            control.name = $"Variants - {target.name}";
            control.SetActive(visible);
            var rect = control.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(100f, -cursorY);
            rect.sizeDelta = new Vector2(1100f, height);

            var grid = control.GetComponent<GridLayoutGroup>();
            if (grid != null)
            {
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = 2;
                grid.cellSize = new Vector2(500f, 100f);
                grid.spacing = new Vector2(20f, 10f);
            }

            var helper = control.GetComponent<RadioButtonHelper>();
            if (helper == null) return null;
            var helperObject = new SerializedObject(helper);
            var labels = helperObject.FindProperty("_radioButtonLabels");
            var values = helperObject.FindProperty("_radioButtonValues");
            labels.arraySize = target.transform.childCount;
            values.arraySize = target.transform.childCount;
            for (int i = 0; i < target.transform.childCount; i++)
            {
                labels.GetArrayElementAtIndex(i).stringValue = target.transform.GetChild(i).name;
                values.GetArrayElementAtIndex(i).stringValue = i.ToString();
            }
            helperObject.FindProperty("_defaultSelectedIndex").intValue = 0;
            helperObject.ApplyModifiedPropertiesWithoutUndo();
            return helper;
        }

        public static void NormalizeArrays(params SerializedProperty[] arrays)
        {
            var size = arrays[0].arraySize;
            for (int i = 1; i < arrays.Length; i++)
            {
                arrays[i].arraySize = size;
            }
        }

        public static void RemoveObjectAt(ObjectsModule module, SerializedObject serializedObject, int index)
        {
            var toggleObject = serializedObject.FindProperty("_toggles").GetArrayElementAtIndex(index).objectReferenceValue as Toggle;
            var arrays = new[]
            {
                serializedObject.FindProperty("_objects"),
                serializedObject.FindProperty("_toggles"),
                serializedObject.FindProperty("_toggleLabels"),
                serializedObject.FindProperty("_enabledByDefault"),
                serializedObject.FindProperty("_localOnly"),
                serializedObject.FindProperty("_globalToggle"),
                serializedObject.FindProperty("_rememberToggleState"),
                serializedObject.FindProperty("_persistenceKeys"),
                serializedObject.FindProperty("_instanceOwnerOnly"),
                serializedObject.FindProperty("_instanceMasterOnly"),
                serializedObject.FindProperty("_verifiedUserOnly"),
                serializedObject.FindProperty("_toggleInstanceOwnerOnly"),
                serializedObject.FindProperty("_toggleInstanceMasterOnly"),
                serializedObject.FindProperty("_toggleVerifiedUserOnly"),
                serializedObject.FindProperty("_groupIndexes"),
                serializedObject.FindProperty("_useChildVariants"),
                serializedObject.FindProperty("_variantControls"),
                serializedObject.FindProperty("_groupHeaders")
            };

            foreach (var array in arrays)
            {
                var sizeBefore = array.arraySize;
                array.DeleteArrayElementAtIndex(index);
                if (array.arraySize == sizeBefore)
                {
                    array.DeleteArrayElementAtIndex(index);
                }
            }

            serializedObject.ApplyModifiedProperties();
            if (toggleObject != null) Undo.DestroyObjectImmediate(toggleObject.gameObject);
            RebuildToggles(module);
        }

        public static void RespawnObjects(ObjectsModule module)
        {
            var serializedModule = new SerializedObject(module);
            var objects = serializedModule.FindProperty("_objects");

            for (int i = 0; i < objects.arraySize; i++)
            {
                var target = objects.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
                if (target == null) continue;

                Undo.RecordObject(target.transform, "Respawn Rehub object");
                target.transform.localPosition = Vector3.zero;
                target.transform.localRotation = Quaternion.identity;
                target.transform.localScale = Vector3.one;
            }
        }

        private static void SetupModuleUi(ObjectsModule module, ModuleMetadata metadata)
        {
            var serializedModule = new SerializedObject(module);
            var content = metadata != null ? metadata.content : null;
            if (content == null) content = module.transform.Find("Content")?.gameObject;

            if (content != null)
            {
                for (int i = content.transform.childCount - 1; i >= 0; i--)
                {
                    Undo.DestroyObjectImmediate(content.transform.GetChild(i).gameObject);
                }

                var listRoot = EnsureScrollLayout(content.transform);
                serializedModule.FindProperty("_toggleListRoot").objectReferenceValue = listRoot;

                var templateAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ToggleTemplatePath);
                if (templateAsset != null)
                {
                    var template = (GameObject)PrefabUtility.InstantiatePrefab(templateAsset, listRoot);
                    if (template == null) template = UnityEngine.Object.Instantiate(templateAsset, listRoot);

                    template.name = "ToggleSwitchTextTemplate";
                    template.SetActive(false);
                    serializedModule.FindProperty("_toggleSwitchTextTemplate").objectReferenceValue = template;
                }

                EnsureRespawnButton(module, content.transform, serializedModule);
            }

            EnsureObjectRoot(module, serializedModule);
            serializedModule.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(module);
        }

        private static void EnsureModuleRoots(ObjectsModule module, ModuleMetadata metadata)
        {
            var serializedModule = new SerializedObject(module);
            if (serializedModule.FindProperty("_toggleListRoot").objectReferenceValue == null ||
                serializedModule.FindProperty("_toggleSwitchTextTemplate").objectReferenceValue == null)
            {
                SetupModuleUi(module, metadata);
            }
            else
            {
                EnsureObjectRoot(module, serializedModule);
                var content = metadata != null ? metadata.content : null;
                if (content != null)
                {
                    var listRoot = EnsureScrollLayout(content.transform);
                    var oldRoot = serializedModule.FindProperty("_toggleListRoot").objectReferenceValue as Transform;
                    if (oldRoot != listRoot)
                    {
                        if (oldRoot != null)
                        {
                            for (int i = oldRoot.childCount - 1; i >= 0; i--)
                            {
                                var child = oldRoot.GetChild(i);
                                if (listRoot.IsChildOf(child)) continue;
                                child.SetParent(listRoot, false);
                            }
                        }
                        serializedModule.FindProperty("_toggleListRoot").objectReferenceValue = listRoot;
                    }
                    EnsureRespawnButton(module, content.transform, serializedModule);
                }
                serializedModule.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static Transform EnsureScrollLayout(Transform parent)
        {
            var existing = parent.Find("Objects Scroll View/Viewport/Objects List");
            if (existing != null)
            {
                EnsureStyledScrollbar(existing.parent.parent);
                ConfigureScrollbarOnlyScroll(existing);
                return existing;
            }

            var scrollObject = new GameObject("Objects Scroll View", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(scrollObject, "Create objects scroll view");
            scrollObject.transform.SetParent(parent, false);
            var scrollRectTransform = scrollObject.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = Vector2.zero;
            scrollRectTransform.anchorMax = Vector2.one;
            scrollRectTransform.offsetMin = new Vector2(40f, 90f);
            scrollRectTransform.offsetMax = new Vector2(-40f, -20f);

            var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObject.transform.SetParent(scrollObject.transform, false);
            var viewport = viewportObject.GetComponent<RectTransform>();
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(32f, 0f);
            viewport.offsetMax = Vector2.zero;

            var listObject = new GameObject("Objects List", typeof(RectTransform));
            listObject.transform.SetParent(viewport, false);
            var list = listObject.GetComponent<RectTransform>();
            list.anchorMin = new Vector2(0f, 1f);
            list.anchorMax = new Vector2(1f, 1f);
            list.pivot = new Vector2(0.5f, 1f);
            list.anchoredPosition = Vector2.zero;
            list.sizeDelta = new Vector2(0f, 620f);

            var scrollbar = EnsureStyledScrollbar(scrollObject.transform);

            var driverObject = new GameObject("Scroll Driver", typeof(RectTransform), typeof(ScrollRect));
            driverObject.transform.SetParent(scrollObject.transform, false);
            var scrollRect = driverObject.GetComponent<ScrollRect>();
            scrollRect.content = list;
            scrollRect.viewport = viewport;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scrollRect.scrollSensitivity = 45f;
            return list;
        }

        private static Scrollbar EnsureStyledScrollbar(Transform scrollView)
        {
            var existing = scrollView.Find("Scrollbar Vertical (Template)");
            if (existing == null) existing = scrollView.Find("Scrollbar Vertical");
            if (existing != null && existing.gameObject.name == "Scrollbar Vertical (Template)")
            {
                ConfigureScrollbarRect(existing.GetComponent<RectTransform>());
                return existing.GetComponent<Scrollbar>();
            }
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

            var templateRoot = AssetDatabase.LoadAssetAtPath<GameObject>(VerticalContentTemplatePath);
            var template = FindChildRecursive(templateRoot != null ? templateRoot.transform : null, "Scrollbar Vertical");
            if (template == null) return null;

            var scrollbarObject = UnityEngine.Object.Instantiate(template.gameObject, scrollView);
            Undo.RegisterCreatedObjectUndo(scrollbarObject, "Create objects scrollbar");
            scrollbarObject.name = "Scrollbar Vertical (Template)";
            scrollbarObject.SetActive(true);
            var rect = scrollbarObject.GetComponent<RectTransform>();
            ConfigureScrollbarRect(rect);
            return scrollbarObject.GetComponent<Scrollbar>();
        }

        private static void ConfigureScrollbarRect(RectTransform rect)
        {
            if (rect == null) return;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(24f, 0f);
            rect.anchoredPosition = Vector2.zero;
        }

        private static void ConfigureScrollbarOnlyScroll(Transform list)
        {
            var viewport = list.parent as RectTransform;
            var scrollView = viewport != null ? viewport.parent : null;
            if (scrollView == null) return;
            viewport.offsetMin = new Vector2(32f, 0f);
            viewport.offsetMax = Vector2.zero;

            var oldScrollRect = scrollView.GetComponent<ScrollRect>();
            if (oldScrollRect != null) Undo.DestroyObjectImmediate(oldScrollRect);

            var scrollbarTransform = scrollView.Find("Scrollbar Vertical (Template)");
            if (scrollbarTransform == null) return;
            var scrollbar = scrollbarTransform.GetComponent<Scrollbar>();
            var scrollbarScrollRect = scrollbarTransform.GetComponent<ScrollRect>();
            if (scrollbarScrollRect != null) Undo.DestroyObjectImmediate(scrollbarScrollRect);

            var driver = scrollView.Find("Scroll Driver");
            if (driver == null)
            {
                var driverObject = new GameObject("Scroll Driver", typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(driverObject, "Create scroll driver");
                driverObject.transform.SetParent(scrollView, false);
                driver = driverObject.transform;
            }

            var scrollRect = driver.GetComponent<ScrollRect>();
            if (scrollRect == null) scrollRect = Undo.AddComponent<ScrollRect>(driver.gameObject);

            scrollRect.content = list as RectTransform;
            scrollRect.viewport = viewport;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scrollRect.scrollSensitivity = 45f;
        }

        private static void EnsureRespawnButton(ObjectsModule module, Transform parent, SerializedObject serializedModule)
        {
            var buttonProperty = serializedModule.FindProperty("_respawnButton");
            var labelProperty = serializedModule.FindProperty("_respawnButtonLabel");
            var button = buttonProperty.objectReferenceValue as Button;
            if (button != null && button.gameObject.name == "Respawn Objects (Template)")
            {
                button.transform.SetParent(parent, false);
                ConfigureRespawnButtonRect(button.GetComponent<RectTransform>());
                return;
            }
            if (button != null) Undo.DestroyObjectImmediate(button.gameObject);

            var uiElementsModule = AssetDatabase.LoadAssetAtPath<GameObject>(UiElementsModulePath);
            var buttonTemplate = FindChildRecursive(uiElementsModule != null ? uiElementsModule.transform : null, "ButtonTemplateActive");
            if (buttonTemplate == null) return;

            var buttonObject = UnityEngine.Object.Instantiate(buttonTemplate.gameObject, parent);
            Undo.RegisterCreatedObjectUndo(buttonObject, "Create respawn button");
            buttonObject.name = "Respawn Objects (Template)";
            buttonObject.SetActive(true);

            var rect = buttonObject.GetComponent<RectTransform>();
            ConfigureRespawnButtonRect(rect);

            button = buttonObject.GetComponent<Button>();
            if (button != null) button.onClick.RemoveAllListeners();

            var label = buttonObject.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.text = "Respawn objects";
                label.fontSize = 28;
                label.color = Color.white;
            }

            var moduleObject = new SerializedObject(module);
            var backingProperty = moduleObject.FindProperty("_udonSharpBackingUdonBehaviour");
            var backing = backingProperty != null ? backingProperty.objectReferenceValue as UdonBehaviour : null;
            if (button != null && backing != null)
            {
                UnityEventTools.AddStringPersistentListener(
                    button.onClick,
                    backing.SendCustomEvent,
                    nameof(ObjectsModule.RespawnAllObjects));
            }

            buttonProperty.objectReferenceValue = button;
            labelProperty.objectReferenceValue = label;
        }

        private static void ConfigureRespawnButtonRect(RectTransform rect)
        {
            if (rect == null) return;
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-70f, -30f);
            rect.sizeDelta = new Vector2(500f, 100f);
        }

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null) return null;
            if (root.name == childName) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindChildRecursive(root.GetChild(i), childName);
                if (found != null) return found;
            }
            return null;
        }

        private static void AssignLocalization(ObjectsModule module, ModuleMetadata metadata)
        {
            var i18n = module.GetComponent<I18nManager>();
            if (i18n == null) return;

            var localization = AssetDatabase.LoadAssetAtPath<TextAsset>(I18nPath);
            var i18nObject = new SerializedObject(i18n);
            var localizationProperty = i18nObject.FindProperty("_localizationJson");
            if (localizationProperty != null && localizationProperty.objectReferenceValue == null)
            {
                localizationProperty.objectReferenceValue = localization;
                i18nObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(i18n);
            }

            if (metadata != null)
            {
                metadata.i18nManager = i18n;
                EditorUtility.SetDirty(metadata);
            }
        }

        private static void AssignUiManager(ObjectsModule module)
        {
            var serializedModule = new SerializedObject(module);
            var property = serializedModule.FindProperty("_uiManager");
            if (property.objectReferenceValue != null) return;
            property.objectReferenceValue = UnityEngine.Object.FindObjectOfType<UIManager>();
            serializedModule.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform EnsureObjectRoot(ObjectsModule module, SerializedObject serializedModule)
        {
            var rootProperty = serializedModule.FindProperty("_objectRoot");
            var root = rootProperty.objectReferenceValue as Transform;
            if (root != null) return root;

            var systemRoot = FindSystemRoot(module.transform);
            if (systemRoot == null) systemRoot = module.transform.root;

            root = systemRoot.Find("Object");
            if (root == null)
            {
                var rootObject = new GameObject("Object");
                Undo.RegisterCreatedObjectUndo(rootObject, "Create Object root");
                rootObject.transform.SetParent(systemRoot, false);
                root = rootObject.transform;
            }

            rootProperty.objectReferenceValue = root;
            return root;
        }

        private static void AddObjectReference(SerializedObject serializedModule, GameObject target, bool localOnly, bool enabledByDefault)
        {
            var objects = serializedModule.FindProperty("_objects");
            var toggles = serializedModule.FindProperty("_toggles");
            var labels = serializedModule.FindProperty("_toggleLabels");
            var defaults = serializedModule.FindProperty("_enabledByDefault");
            var local = serializedModule.FindProperty("_localOnly");
            var globalToggle = serializedModule.FindProperty("_globalToggle");
            var rememberToggleState = serializedModule.FindProperty("_rememberToggleState");
            var persistenceKeys = serializedModule.FindProperty("_persistenceKeys");
            var ownerOnly = serializedModule.FindProperty("_instanceOwnerOnly");
            var masterOnly = serializedModule.FindProperty("_instanceMasterOnly");
            var verifiedOnly = serializedModule.FindProperty("_verifiedUserOnly");
            var toggleOwnerOnly = serializedModule.FindProperty("_toggleInstanceOwnerOnly");
            var toggleMasterOnly = serializedModule.FindProperty("_toggleInstanceMasterOnly");
            var toggleVerifiedOnly = serializedModule.FindProperty("_toggleVerifiedUserOnly");
            var groupIndexes = serializedModule.FindProperty("_groupIndexes");
            var useChildVariants = serializedModule.FindProperty("_useChildVariants");
            var variantControls = serializedModule.FindProperty("_variantControls");
            var groupHeaders = serializedModule.FindProperty("_groupHeaders");

            var index = objects.arraySize;
            objects.InsertArrayElementAtIndex(index);
            toggles.InsertArrayElementAtIndex(index);
            labels.InsertArrayElementAtIndex(index);
            defaults.InsertArrayElementAtIndex(index);
            local.InsertArrayElementAtIndex(index);
            globalToggle.InsertArrayElementAtIndex(index);
            rememberToggleState.InsertArrayElementAtIndex(index);
            persistenceKeys.InsertArrayElementAtIndex(index);
            ownerOnly.InsertArrayElementAtIndex(index);
            masterOnly.InsertArrayElementAtIndex(index);
            verifiedOnly.InsertArrayElementAtIndex(index);
            toggleOwnerOnly.InsertArrayElementAtIndex(index);
            toggleMasterOnly.InsertArrayElementAtIndex(index);
            toggleVerifiedOnly.InsertArrayElementAtIndex(index);
            groupIndexes.InsertArrayElementAtIndex(index);
            useChildVariants.InsertArrayElementAtIndex(index);
            variantControls.InsertArrayElementAtIndex(index);
            groupHeaders.InsertArrayElementAtIndex(index);

            objects.GetArrayElementAtIndex(index).objectReferenceValue = target;
            toggles.GetArrayElementAtIndex(index).objectReferenceValue = null;
            labels.GetArrayElementAtIndex(index).objectReferenceValue = null;
            defaults.GetArrayElementAtIndex(index).boolValue = enabledByDefault;
            local.GetArrayElementAtIndex(index).boolValue = localOnly;
            globalToggle.GetArrayElementAtIndex(index).boolValue = false;
            rememberToggleState.GetArrayElementAtIndex(index).boolValue = true;
            persistenceKeys.GetArrayElementAtIndex(index).stringValue = Guid.NewGuid().ToString("N");
            ownerOnly.GetArrayElementAtIndex(index).boolValue = false;
            masterOnly.GetArrayElementAtIndex(index).boolValue = false;
            verifiedOnly.GetArrayElementAtIndex(index).boolValue = false;
            toggleOwnerOnly.GetArrayElementAtIndex(index).boolValue = false;
            toggleMasterOnly.GetArrayElementAtIndex(index).boolValue = false;
            toggleVerifiedOnly.GetArrayElementAtIndex(index).boolValue = false;
            groupIndexes.GetArrayElementAtIndex(index).intValue = -1;
            useChildVariants.GetArrayElementAtIndex(index).boolValue = false;
            variantControls.GetArrayElementAtIndex(index).objectReferenceValue = null;
            groupHeaders.GetArrayElementAtIndex(index).objectReferenceValue = null;
        }

        private static int GetNextObjectIndex(SerializedObject serializedModule)
        {
            return serializedModule.FindProperty("_objects").arraySize + 1;
        }

        private static Transform FindSystemRoot(Transform start = null)
        {
            var current = start != null ? start : Selection.activeTransform;
            while (current != null)
            {
                if (current.name == "RehubSystem") return current;
                current = current.parent;
            }

            var metadata = UnityEngine.Object.FindObjectOfType<ModuleManager>();
            if (metadata != null) return metadata.transform.root;

            return null;
        }
    }
#endif
}
