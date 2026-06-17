
using System;
using System.Globalization;
using UdonSharp;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Data;
using VRC.SDKBase;
using VRC.Udon;
using RehubSystem.EditorShared;

// RFC 5646 Language Tags
// https://gist.github.com/msikma/8912e62ed866778ff8cd

namespace RehubSystem
{
    public enum I18nArgumentType
    {
        Dynamic,
        String,
        I18n,
    }

    public enum I18nDynamicArgumentType
    {
        None,
        VRCLocalPlayerName,
        CurrentTime,
        CurrentDate,
        CurrentDateTime,
    }

    [DisallowMultipleComponent]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class I18nManager : UdonSharpBehaviour
    {
        [SerializeField] private UIManager _controller;
        [SerializeField] private TextAsset _localizationJson;
        public I18nManager masterManager;

        private readonly string _fallbackLanguage = "en";
        private bool _isInitialized = false;
        private bool _isInitialChange = false;
        private bool _isAutoSet = false;
        private DataDictionary _localization;
        private string _currentLanguage;

        public bool Initialized => _isInitialized;
        public bool HasLocalization => _localization != null;
        public string CurrentLanguage
        {
            get
            {
                if (masterManager != null) return masterManager.CurrentLanguage;
                return _currentLanguage ?? _fallbackLanguage;
            }
        }

        public CultureInfo CurrentCulture => CultureInfo.GetCultureInfo(CurrentLanguage);

        private void Start()
        {
            BuildLocalization();
        }

        public override void OnLanguageChanged(string language)
        {
            if (!_isInitialChange)
            {
                _isInitialChange = true;
                return;
            }
            if (!_isAutoSet) return;

            SetLanguage(language, true);
        }

        public void BuildLocalization()
        {
            if (_isInitialized) return;
            if (_localizationJson != null)
            {
                VRCJson.TryDeserializeFromJson(_localizationJson.text, out var _loc);
                if (_loc.TokenType == TokenType.DataDictionary) _localization = _loc.DataDictionary;
            }
            if (masterManager == null)
            {
                _currentLanguage = _fallbackLanguage;
            }

            _isInitialized = true;
        }

        public string GetTranslation(string key, string language = null)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            var lang = language ?? CurrentLanguage;
            if (_localization == null) return string.Empty;
            if (!_localization.TryGetValue(lang, out var translation) || !translation.DataDictionary.TryGetValue(key, out var value))
            {
                if (lang == _fallbackLanguage) return string.Empty;
                return GetTranslation(key, _fallbackLanguage);
            }

            return value.String;
        }

        public string GetTranslationWithArgs(string key, I18nArgumentType[] args, string[] argValues, string language = null)
        {
            var translation = GetTranslation(key, language);
            for (int i = 0; i < args.Length; i++)
            {
                var argument = string.Empty;
                switch (args[i])
                {
                    case I18nArgumentType.Dynamic:
                        argument = GetDynamicArg(argValues[i]);
                        break;
                    case I18nArgumentType.String:
                        argument = argValues[i];
                        break;
                    case I18nArgumentType.I18n:
                        argument = GetTranslation(argValues[i], language);
                        break;
                }
                translation = translation.Replace($"{{{i}}}", argument);
            }

            return translation;
        }

        public string GetDynamicArg(string arg)
        {
            switch (arg)
            {
                case nameof(I18nDynamicArgumentType.VRCLocalPlayerName):
                    return Networking.LocalPlayer.displayName;
                case nameof(I18nDynamicArgumentType.CurrentTime):
                    return DateTimeOffset.Now.ToLocalTime().ToString("T", CurrentCulture);
                case nameof(I18nDynamicArgumentType.CurrentDate):
                    return DateTimeOffset.Now.ToLocalTime().ToString("d", CurrentCulture);
                case nameof(I18nDynamicArgumentType.CurrentDateTime):
                    return DateTimeOffset.Now.ToLocalTime().ToString("G", CurrentCulture);
                default:
                    return string.Empty;
            }
        }

        public void SetLanguage(string language = null, bool skipAutoSetFlag = false)
        {
            if (masterManager != null)
            {
                masterManager.SetLanguage(language, skipAutoSetFlag);
                return;
            }

            var isAutoSet = language == null || language == "auto";
            if (!skipAutoSetFlag) _isAutoSet = isAutoSet;
            _currentLanguage = isAutoSet ? _fallbackLanguage : language;
            Debug.Log($"[I18nManager] SetLanguage: {_currentLanguage}");
            ApplyI18n();
        }

        public void ApplyI18n(bool isGlobal = false)
        {
            if (masterManager != null)
            {
                if (isGlobal) masterManager.ApplyI18n();
                return;
            }

            if (_controller == null) return;

            if (!_isInitialized)
            {
                BuildLocalization();
            }

            _controller.UpdateTitle();

            foreach (var canvas in _controller.Canvas)
            {
                if (canvas == null) continue;
                foreach (var component in canvas.GetComponentsInChildren<ApplyI18n>(true))
                {
                    ApplyTextComponent(component);
                }

                foreach (var component in canvas.GetComponentsInChildren<ApplyTimeI18n>(true))
                {
                    ApplyTimeComponent(component);
                }
            }
        }

        private void ApplyTextComponent(ApplyI18n component)
        {
            if (component == null || string.IsNullOrEmpty(component.key)) return;

            if (component.manager == null) component.manager = this;
            if (component.manager == null || !component.manager.Initialized) return;

            var text = component.GetComponent<Text>();
            if (text == null) return;

            text.text = component.args != null
                ? component.manager.GetTranslationWithArgs(component.key, component.args, component.argValues, _currentLanguage)
                : component.manager.GetTranslation(component.key, _currentLanguage);
        }

        private void ApplyTimeComponent(ApplyTimeI18n component)
        {
            if (component == null) return;

            if (component.manager == null) component.manager = this;
            if (component.manager == null || !component.manager.Initialized) return;

            var text = component.GetComponent<Text>();
            if (text == null) return;

            var time = component.time != null ? component.time : DateTimeOffset.Now.ToLocalTime();
            var timeFormat = string.Empty;

            switch (component.format)
            {
                case I18nTimeFormat.DateTimeFull:
                    timeFormat = "G";
                    break;
                case I18nTimeFormat.DateTimeShort:
                    timeFormat = "g";
                    break;
                case I18nTimeFormat.TimeFull:
                    timeFormat = "T";
                    break;
                case I18nTimeFormat.TimeShort:
                    timeFormat = "t";
                    break;
                case I18nTimeFormat.Date:
                    timeFormat = "d";
                    break;
            }

            text.text = time.ToString(timeFormat, component.manager.CurrentCulture);
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        [CustomEditor(typeof(I18nManager))]
        internal class I18nManagerInspector : Editor
        {
            private I18nManager _i18nManager;
            private bool _hasModuleMetadata = false;

            private void OnEnable()
            {
                _i18nManager = target as I18nManager;
                _hasModuleMetadata = _i18nManager.gameObject.GetComponent<ModuleMetadata>() != null;
            }

            public override void OnInspectorGUI()
            {
                serializedObject.Update();

                if (EditorPrefs.GetBool("ynworks_devmode", false))
                {
                    EditorGUILayout.LabelField("I18n Manager", EditorStyles.boldLabel);
                    EditorGUILayout.Space();

                    if (!_hasModuleMetadata && _i18nManager._controller == null)
                    {
                        EditorGUILayout.HelpBox(EditorI18n.GetTranslation("controllerMissing"), MessageType.Warning);
                    }

                    if (_i18nManager._localizationJson == null)
                    {
                        EditorGUILayout.HelpBox(EditorI18n.GetTranslation("localizeFileMissing"), MessageType.Warning);
                    }

                    EditorGUI.BeginChangeCheck();

                    if (!_hasModuleMetadata)
                    {
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("_controller"), new GUIContent(EditorI18n.GetTranslation("controller")));
                    }
                    else if (_i18nManager._controller != null)
                    {
                        EditorGUILayout.HelpBox(EditorI18n.GetTranslation("i18nManagerControllerWarning"), MessageType.Warning);
                        if (GUILayout.Button(EditorI18n.GetTranslation("removeController")))
                        {
                            _i18nManager._controller = null;
                        }
                    }

                    EditorGUILayout.PropertyField(serializedObject.FindProperty("_localizationJson"), new GUIContent(EditorI18n.GetTranslation("localizeFile")));
                }
                else
                {
                    if (
                        (!_hasModuleMetadata && _i18nManager._controller == null) ||
                        (_hasModuleMetadata && _i18nManager._controller != null) ||
                        _i18nManager._localizationJson == null
                    )
                    {
                        EditorGUILayout.LabelField(EditorI18n.GetTranslation("setupIssuesFound"));
                    }
                    else
                    {
                        EditorGUILayout.LabelField(EditorI18n.GetTranslation("setupSuccessfully"));
                    }
                }

                if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.ApplyModifiedProperties();
                }
            }
        }
#endif
    }
}
