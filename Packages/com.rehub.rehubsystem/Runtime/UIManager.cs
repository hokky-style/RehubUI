
using System;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace RehubSystem
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UIManager : UdonSharpBehaviour
    {
        [SerializeField] private GameObject[] _canvas;
        [SerializeField] private Transform _rootCanvas;
        [SerializeField] private Transform _panel;
        [SerializeField] private GameObject _usingMessagePanel;
        [SerializeField] private ModuleMetadata _defaultOpenModule;
        [SerializeField] private Transform _moduleContentContainer;
        [SerializeField] private Animator _moduleContentAnimator;
        [SerializeField] private Animator _navigationMenuAnimator;
        [SerializeField] private ApplyTimeI18n _currentDate;
        [SerializeField] private ApplyTimeI18n _currentTime;
        [SerializeField] private Text _homeWelcomeText;
        [SerializeField] private string _customWelcomeText;
        [SerializeField] private ModuleManager _moduleManager;
        [SerializeField] private ThemeManager _themeManager;
        [SerializeField] private I18nManager _i18nManager;
        [SerializeField] private GameObject _titleTemplateA;
        [SerializeField] private GameObject _titleTemplateB;
        [SerializeField] private GameObject _linkTemplate;
        [SerializeField] private Transform _linkContainer;
        [SerializeField] private GameObject _navigationButtonTemplate;
        [SerializeField] private Transform _navigationButtonContainer;
        [SerializeField] private Sprite _homeNavigationIcon;
        [SerializeField] private GameObject _modalWindowTemplate;
        [SerializeField] private Transform _modalWindowContainer;
        [SerializeField] private VRCUrl _verifiedUsersUrl = new VRCUrl("");

        private const int DockSlotCount = 5;
        private const int PinnedNavigationButtonCount = DockSlotCount - 2;
        private const string HomeNavigationButtonName = "__home";
        private const string SystemSettingsModuleId = "SystemSettingsModule";
        private const string InstanceOwnerStatusName = "InstanceOwner";
        private const string VerifiedUserStatusName = "VerifiedUser";
        private const string WorldLicensedStatusName = "WorldLicensed";

        private ModuleMetadata _currentModule;
        private ModuleMetadata _nextModule;
        private ModuleMetadata[] _navigationButtonModules = new ModuleMetadata[0];
        private GameObject[] _navigationButtonObjects = new GameObject[0];
        private GameObject _homeNavigationButton;
        private GameObject _systemSettingsNavigationButton;
        private GameObject _instanceOwnerStatus;
        private GameObject _verifiedUserStatus;
        private GameObject _worldLicensedStatus;
        private ApplyTheme _instanceOwnerStatusTheme;
        private ApplyTheme _verifiedUserStatusTheme;
        private ApplyTheme _worldLicensedStatusTheme;
        private string _verifiedUsersRawList = "";
        private bool _verifiedUsersLoaded = false;
        private bool _localPlayerVerified = false;
        private bool _worldLicensed = false;

        private bool _titleTemplateASide = false;
        private Animator _titleTemplateAAnimator;
        private Animator _titleTemplateBAnimator;

        public GameObject[] Canvas
        {
            get
            {
                while (true)
                {
                    var nullCanvasIndex = -1;
                    for (int i = 0; i < _canvas.Length; i++)
                    {
                        if (_canvas[i] == null)
                        {
                            nullCanvasIndex = i;
                            break;
                        }
                    }

                    if (nullCanvasIndex == -1) break;
                    _canvas = ArrayUtils.RemoveAt(_canvas, nullCanvasIndex);
                }

                return _canvas;
            }
        }

        public ThemeManager ThemeManager => _themeManager;
        public bool LocalPlayerVerified => _localPlayerVerified;
        public bool WorldLicensed => _worldLicensed;

        private void Start()
        {
            UpdateCurrentDateTime();

            if (_moduleManager != null && !_moduleManager.Initialized)
            {
                _moduleManager.Initialize();
            }

            CreateHomeNavigationButton();

            var pinnedNavigationButtons = 0;
            foreach (var module in _moduleManager.Modules)
            {
                if (module == null)
                {
                    Debug.LogWarning("Module is null");
                    continue;
                }

                module.RegenerateUuid();

                if (!module.HideInMenu)
                {
                    var linkObject = Instantiate(_linkTemplate, _linkContainer);
                    linkObject.name = module.Uuid;
                    linkObject.SetActive(true);

                    var link = linkObject.GetComponent<HomeAppLinkHelper>();
                    link.icon.sprite = module.moduleIcon;

                    link.moduleExecutor.manager = this;
                    link.moduleExecutor.module = module;

                    var isSystemSettingsModule = IsSystemSettingsModule(module);
                    var pinnedNavigationButton = isSystemSettingsModule || pinnedNavigationButtons < PinnedNavigationButtonCount;
                    var navigationButton = CreateNavigationButton(module, pinnedNavigationButton);
                    if (isSystemSettingsModule)
                    {
                        _systemSettingsNavigationButton = navigationButton;
                    }
                    else if (pinnedNavigationButton)
                    {
                        pinnedNavigationButtons++;
                    }

                    if (!module.forceUseModuleName && module.i18nManager != null)
                    {
                        if (!module.i18nManager.Initialized) module.i18nManager.BuildLocalization();
                        if (module.i18nManager.HasLocalization)
                        {
                            link.titleI18n.manager = module.i18nManager;
                            navigationButton.transform.Find("Title").GetComponent<ApplyI18n>().manager = module.i18nManager;
                        }
                        else
                        {
                            link.title.text = module.moduleName;
                            navigationButton.transform.Find("Title").GetComponent<Text>().text = module.moduleName;
                        }
                    }
                    else
                    {
                        link.title.text = module.moduleName;
                        navigationButton.transform.Find("Title").GetComponent<Text>().text = module.moduleName;
                    }

                    if (module.instanceOwnerOnly)
                    {
                        link.permissionIconOwner.SetActive(true);
                    }

                    if (module.allowedUsersOnly)
                    {
                        link.permissionIconAllowedUser.SetActive(true);
                    }
                }

                module.content.name = module.Uuid;
                module.content.transform.SetParent(_moduleContentContainer, false);
                module.content.SetActive(false);
                module.Activate();

                var moduleI18n = module.GetComponent<I18nManager>();
                if (moduleI18n != null)
                {
                    moduleI18n.masterManager = _i18nManager;
                }
            }

            _linkTemplate.SetActive(false);
            _navigationButtonTemplate.SetActive(false);
            if (_systemSettingsNavigationButton != null)
            {
                _systemSettingsNavigationButton.transform.SetAsLastSibling();
            }
            NormalizeNavigationButtons();

            _usingMessagePanel.SetActive(false);
            SetTitle();

            _themeManager.ApplyTheme();
            _i18nManager.ApplyI18n();
            InitializeHeaderStatusIndicators();
            RequestVerifiedUsers();
            UpdateHeaderStatusIndicators();

            if (_defaultOpenModule != null)
            {
                UseModule(_defaultOpenModule);
            }

            if (!string.IsNullOrEmpty(_customWelcomeText))
            {
                var welcomeTextI18n = _homeWelcomeText.GetComponent<ApplyI18n>();
                if (welcomeTextI18n != null)
                {
                    welcomeTextI18n.key = null;
                }

                var text = _customWelcomeText.Replace("<EMPTY>", "");
                if (text.Contains("<NAME>"))
                {
                    text = text.Replace("<NAME>", Networking.LocalPlayer.displayName);
                }

                _homeWelcomeText.text = text;
            }
        }

        public bool IsLocalPlayerVerified()
        {
            return _localPlayerVerified;
        }

        public bool IsPlayerVerified(string playerName)
        {
            return IsNameInRemoteList(playerName);
        }

        public bool IsWorldLicensed()
        {
            return _worldLicensed;
        }

        public void RefreshHeaderStatusIndicators()
        {
            if (_instanceOwnerStatus == null && _verifiedUserStatus == null && _worldLicensedStatus == null)
            {
                InitializeHeaderStatusIndicators();
            }

            _localPlayerVerified = Networking.LocalPlayer != null && IsNameInRemoteList(Networking.LocalPlayer.displayName);
            UpdateHeaderStatusIndicators();
        }

        public void RequestVerifiedUsers()
        {
            if (string.IsNullOrEmpty(_verifiedUsersUrl.Get()))
            {
                _verifiedUsersLoaded = false;
                _localPlayerVerified = false;
                UpdateHeaderStatusIndicators();
                return;
            }

            VRCStringDownloader.LoadUrl(_verifiedUsersUrl, (IUdonEventReceiver)this);
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            if (result.Url != _verifiedUsersUrl) return;

            _verifiedUsersRawList = result.Result;
            _verifiedUsersLoaded = true;
            RefreshHeaderStatusIndicators();
            Debug.Log($"[UIManager] Verified users list loaded. Local player verified: {_localPlayerVerified}");
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            if (result.Url != _verifiedUsersUrl) return;

            Debug.LogWarning($"[UIManager] Failed to load verified users list: {result.ErrorCode} - {result.Error}");
            _verifiedUsersRawList = "";
            _verifiedUsersLoaded = false;
            _localPlayerVerified = false;
            UpdateHeaderStatusIndicators();
        }

        public void UseModule(ModuleMetadata module)
        {
            if (_currentModule != null && _currentModule.Uuid == module.Uuid) return;

#region Permission check
            var localPlayerName = Networking.LocalPlayer != null ? Networking.LocalPlayer.displayName : "";
            var isInstanceOwner = Networking.LocalPlayer != null && Networking.LocalPlayer.isInstanceOwner;
            var isAllowedUser = IsLocalAllowedUser(module.allowedUsers, localPlayerName) || IsPlayerVerified(localPlayerName);
            if (module.instanceOwnerOnly && module.allowedUsersOnly)
            {
                if (!isInstanceOwner && !isAllowedUser)
                {
                    ShowModalWindow(
                        _i18nManager.GetTranslation("noPermission"),
                        _i18nManager.GetTranslation("notAllowedToUseThisModuleBecauseYouAreNotTheInstanceOwner"),
                        _i18nManager.GetTranslation("close")
                    );
                    Debug.LogWarning("This module is only usable by the instance owner or allowed users.");
                    return;
                }
            }
            else
            {
                if (module.instanceOwnerOnly && !isInstanceOwner)
                {
                    ShowModalWindow(
                        _i18nManager.GetTranslation("noPermission"),
                        _i18nManager.GetTranslation("notAllowedToUseThisModuleBecauseYouAreNotTheInstanceOwner"),
                        _i18nManager.GetTranslation("close")
                    );
                    Debug.LogWarning("This module is only usable by the instance owner.");
                    return;
                }

                if (module.allowedUsersOnly && !isAllowedUser)
                {
                    ShowModalWindow(
                        _i18nManager.GetTranslation("noPermission"),
                        _i18nManager.GetTranslation("notAllowedToUseThisModuleBecauseYouAreNotInTheAllowedUsers"),
                        _i18nManager.GetTranslation("close")
                    );
                    Debug.LogWarning("You are not in the allowed users for this module.");
                    return;
                }
            }
#endregion

            Debug.Log("UsingModule: " + module.moduleName);
            _navigationMenuAnimator.SetBool("isShow", true);
            ShowNavigationButton(module);
            RefreshNavigationButtonInputStates();

            if (_currentModule != null)
            {
                SetBottomNavigationButtonSelected(_currentModule.Uuid, false);
            }

            SetBottomNavigationButtonSelected(module.Uuid, true);
            UpdateTitle(module);

            module.SendCustomEvent("OnModuleCalled");

            _nextModule = module;
            _moduleContentAnimator.SetBool("show", false);
            SendCustomEventDelayedSeconds(nameof(UpdateContent), 0.15f);
        }

        public void UpdateContent()
        {
            if (_currentModule != null)
            {
                _currentModule.content.SetActive(false);
            }

            if (_nextModule == null)
            {
                _currentModule = null;
                return;
            }

            _currentModule = _nextModule;
            _currentModule.content.SetActive(true);
            _nextModule = null;

            _moduleContentAnimator.SetBool("show", true);
        }

        private bool IsLocalAllowedUser(string[] allowedUsers, string playerName)
        {
            if (allowedUsers == null || string.IsNullOrEmpty(playerName)) return false;

            var normalizedPlayerName = playerName.Trim().ToLower();
            for (int i = 0; i < allowedUsers.Length; i++)
            {
                if (string.IsNullOrEmpty(allowedUsers[i])) continue;
                if (allowedUsers[i].Trim().ToLower() == normalizedPlayerName) return true;
            }

            return false;
        }

        public void CloseModuleMenu()
        {
            _moduleContentAnimator.SetBool("show", false);
            _navigationMenuAnimator.SetBool("isShow", false);

            if (_currentModule != null)
            {
                SetBottomNavigationButtonSelected(_currentModule.Uuid, false);
            }

            ClearBottomNavigationSelection();
            RefreshNavigationButtonInputStates();
            SendCustomEventDelayedSeconds(nameof(UpdateContent), 0.15f);
            SetTitle();
        }

        public void UpdateCurrentDateTime()
        {
            _currentDate.Apply();
            _currentTime.Apply();

            var nextUpdate = 60 - DateTime.Now.Second;
            SendCustomEventDelayedSeconds(nameof(UpdateCurrentDateTime), nextUpdate);
        }

        private void SetTitle(string title = null)
        {
            var titleTemplate = _titleTemplateASide ? _titleTemplateA : _titleTemplateB;
            titleTemplate.GetComponent<Text>().text = title ?? _i18nManager.GetTranslation("home");

            if (_titleTemplateAAnimator == null)
            {
                _titleTemplateAAnimator = _titleTemplateA.GetComponent<Animator>();
            }

            if (_titleTemplateBAnimator == null)
            {
                _titleTemplateBAnimator = _titleTemplateB.GetComponent<Animator>();
            }


            _titleTemplateAAnimator.SetBool("show", _titleTemplateASide);
            _titleTemplateBAnimator.SetBool("show", !_titleTemplateASide);

            _titleTemplateASide = !_titleTemplateASide;
        }

        public void UpdateTitle(ModuleMetadata targetModule = null)
        {
            var module = targetModule ?? _currentModule;
            if (module == null)
            {
                SetTitle();
                return;
            }

            if (!module.forceUseModuleName && module.i18nManager != null && module.i18nManager.HasLocalization && module.i18nManager.Initialized)
            {
                SetTitle(module.i18nManager.GetTranslation("$moduleName", _i18nManager.CurrentLanguage));
            }
            else
            {
                SetTitle(module.moduleName);
            }
        }

        private void SetBottomNavigationButtonSelected(string uuid, bool selected)
        {
            var targetModule = _navigationButtonContainer.Find(uuid);
            if (targetModule == null) return;
            targetModule.GetComponent<Animator>().SetBool("ModuleSelected", selected);
        }

        private void ClearBottomNavigationSelection()
        {
            SetNavigationButtonSelected(_homeNavigationButton, false);

            for (int i = 0; i < _navigationButtonObjects.Length; i++)
            {
                SetNavigationButtonSelected(_navigationButtonObjects[i], false);
            }

            SetNavigationButtonSelected(_systemSettingsNavigationButton, false);
        }

        private void SetNavigationButtonSelected(GameObject navigationButton, bool selected)
        {
            if (navigationButton == null) return;

            var animator = navigationButton.GetComponent<Animator>();
            if (animator != null)
            {
                animator.SetBool("ModuleSelected", selected);
            }
        }

        private void RefreshNavigationButtonInputStates()
        {
            SetNavigationButtonsInteractable(false);
            SendCustomEventDelayedFrames(nameof(EnableNavigationButtonInputStates), 1);
        }

        public void EnableNavigationButtonInputStates()
        {
            SetNavigationButtonsInteractable(true);
        }

        private void InitializeHeaderStatusIndicators()
        {
            _instanceOwnerStatus = FindChildGameObject(_panel, InstanceOwnerStatusName);
            _verifiedUserStatus = FindChildGameObject(_panel, VerifiedUserStatusName);
            _worldLicensedStatus = FindChildGameObject(_panel, WorldLicensedStatusName);

            _instanceOwnerStatusTheme = FindStatusTheme(_instanceOwnerStatus);
            _verifiedUserStatusTheme = FindStatusTheme(_verifiedUserStatus);
            _worldLicensedStatusTheme = FindStatusTheme(_worldLicensedStatus);
        }

        private GameObject FindChildGameObject(Transform root, string childName)
        {
            if (root == null || string.IsNullOrEmpty(childName)) return null;

            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null && transforms[i].name == childName)
                {
                    return transforms[i].gameObject;
                }
            }

            return null;
        }

        private ApplyTheme FindStatusTheme(GameObject statusObject)
        {
            if (statusObject == null) return null;

            var themes = statusObject.GetComponentsInChildren<ApplyTheme>(true);
            for (int i = 0; i < themes.Length; i++)
            {
                if (themes[i] != null)
                {
                    return themes[i];
                }
            }

            return null;
        }

        private void UpdateHeaderStatusIndicators()
        {
            var isInstanceOwner = Networking.LocalPlayer != null && Networking.LocalPlayer.isInstanceOwner;
            SetHeaderStatus(_instanceOwnerStatus, _instanceOwnerStatusTheme, isInstanceOwner ? ColorPalette.Success : ColorPalette.Error);

            var verifiedPalette = _verifiedUsersLoaded && _localPlayerVerified ? ColorPalette.Success : ColorPalette.Error;
            SetHeaderStatus(_verifiedUserStatus, _verifiedUserStatusTheme, verifiedPalette);

            SetHeaderStatus(_worldLicensedStatus, _worldLicensedStatusTheme, _worldLicensed ? ColorPalette.Success : ColorPalette.Error);
        }

        private void SetHeaderStatus(GameObject statusObject, ApplyTheme theme, ColorPalette palette)
        {
            if (statusObject != null)
            {
                statusObject.SetActive(true);
            }

            if (theme == null) return;
            theme.themeManager = _themeManager;
            theme.colorPalette = palette;
            theme.Apply();
        }

        private bool IsNameInRemoteList(string playerName)
        {
            if (string.IsNullOrEmpty(playerName) || string.IsNullOrEmpty(_verifiedUsersRawList)) return false;

            var source = NormalizeRemoteList(_verifiedUsersRawList);
            var target = NormalizeRemoteList(playerName).Trim();
            return source.Contains($"\n{target}\n");
        }

        private string NormalizeRemoteList(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\n";

            var normalized = value.ToLower();
            normalized = normalized.Replace("\r", "\n");
            normalized = normalized.Replace(",", "\n");
            normalized = normalized.Replace(";", "\n");
            normalized = normalized.Replace("[", "\n");
            normalized = normalized.Replace("]", "\n");
            normalized = normalized.Replace("{", "\n");
            normalized = normalized.Replace("}", "\n");
            normalized = normalized.Replace("\"", "\n");
            normalized = normalized.Replace("'", "\n");
            normalized = normalized.Replace("\t", " ");
            normalized = normalized.Trim();
            return $"\n{normalized}\n";
        }

        private void SetNavigationButtonsInteractable(bool interactable)
        {
            SetNavigationButtonInteractable(_homeNavigationButton, interactable);

            for (int i = 0; i < _navigationButtonObjects.Length; i++)
            {
                SetNavigationButtonInteractable(_navigationButtonObjects[i], interactable);
            }

            SetNavigationButtonInteractable(_systemSettingsNavigationButton, interactable);
        }

        private void SetNavigationButtonInteractable(GameObject navigationButton, bool interactable)
        {
            if (navigationButton == null) return;

            var button = navigationButton.GetComponent<Button>();
            if (button != null)
            {
                button.interactable = interactable;
            }
        }

        private void CreateHomeNavigationButton()
        {
            var navigationButton = Instantiate(_navigationButtonTemplate, _navigationButtonContainer);
            _homeNavigationButton = navigationButton;
            navigationButton.name = HomeNavigationButtonName;
            navigationButton.SetActive(true);

            var icon = _homeNavigationIcon;
            if (icon == null)
            {
                icon = _navigationButtonTemplate.transform.Find("Icon").GetComponent<Image>().sprite;
            }

            navigationButton.transform.Find("Icon").GetComponent<Image>().sprite = icon;
            navigationButton.transform.Find("Active/Icon").GetComponent<Image>().sprite = icon;

            var title = navigationButton.transform.Find("Title");
            title.GetComponent<Text>().text = "Home";
            title.GetComponent<ApplyI18n>().manager = null;

            var navigationButtonExecutor = navigationButton.GetComponent<ModuleExecutor>();
            navigationButtonExecutor.manager = this;
            navigationButtonExecutor.module = null;
        }

        private GameObject CreateNavigationButton(ModuleMetadata module, bool visible)
        {
            var navigationButton = Instantiate(_navigationButtonTemplate, _navigationButtonContainer);
            navigationButton.name = module.Uuid;
            navigationButton.SetActive(true);
            navigationButton.transform.Find("Icon").GetComponent<Image>().sprite = module.moduleIcon;
            navigationButton.transform.Find("Active/Icon").GetComponent<Image>().sprite = module.moduleIcon;

            var navigationButtonExecutor = navigationButton.GetComponent<ModuleExecutor>();
            navigationButtonExecutor.manager = this;
            navigationButtonExecutor.module = module;

            _navigationButtonModules = ArrayUtils.Add(_navigationButtonModules, module);
            _navigationButtonObjects = ArrayUtils.Add(_navigationButtonObjects, navigationButton);

            navigationButton.SetActive(visible);
            return navigationButton;
        }

        private bool IsSystemSettingsModule(ModuleMetadata module)
        {
            if (module == null) return false;
            if (module.ModuleId == SystemSettingsModuleId) return true;
            if (module.moduleName == "System Settings") return true;
            if (module.name == SystemSettingsModuleId) return true;
            return module.gameObject != null && module.gameObject.name == SystemSettingsModuleId;
        }

        private void NormalizeNavigationButtons(GameObject priorityButton = null)
        {
            if (_homeNavigationButton != null)
            {
                _homeNavigationButton.SetActive(true);
                _homeNavigationButton.transform.SetAsFirstSibling();
            }

            if (_systemSettingsNavigationButton != null)
            {
                _systemSettingsNavigationButton.SetActive(true);
            }

            var visibleModuleButtons = 0;
            for (int i = 0; i < _navigationButtonObjects.Length; i++)
            {
                var navigationButton = _navigationButtonObjects[i];
                if (navigationButton == null || navigationButton == _systemSettingsNavigationButton) continue;
                if (navigationButton.activeSelf) visibleModuleButtons++;
            }

            for (int i = _navigationButtonObjects.Length - 1; i >= 0 && visibleModuleButtons > PinnedNavigationButtonCount; i--)
            {
                var navigationButton = _navigationButtonObjects[i];
                if (navigationButton == null ||
                    navigationButton == _systemSettingsNavigationButton ||
                    navigationButton == priorityButton ||
                    !navigationButton.activeSelf)
                {
                    continue;
                }

                navigationButton.SetActive(false);
                visibleModuleButtons--;
            }

            if (_systemSettingsNavigationButton != null)
            {
                _systemSettingsNavigationButton.transform.SetAsLastSibling();
            }
        }

        private void ShowNavigationButton(ModuleMetadata module)
        {
            for (int i = 0; i < _navigationButtonModules.Length; i++)
            {
                if (_navigationButtonModules[i] == null || _navigationButtonModules[i].Uuid != module.Uuid) continue;
                var navigationButton = _navigationButtonObjects[i];
                if (navigationButton == null) return;
                var wasVisible = navigationButton.activeSelf;
                navigationButton.SetActive(true);
                if (!wasVisible)
                {
                    if (_systemSettingsNavigationButton != null && navigationButton != _systemSettingsNavigationButton)
                    {
                        navigationButton.transform.SetSiblingIndex(_systemSettingsNavigationButton.transform.GetSiblingIndex());
                    }
                    else
                    {
                        navigationButton.transform.SetAsLastSibling();
                    }
                }

                if (_systemSettingsNavigationButton != null)
                {
                    _systemSettingsNavigationButton.transform.SetAsLastSibling();
                }

                NormalizeNavigationButtons(navigationButton);
                return;
            }
        }

        public void SetMenuParent(Transform parent)
        {
            _usingMessagePanel.SetActive(parent != null && parent != _rootCanvas);
            _panel.SetParent(parent == null ? _rootCanvas : parent, false);
        }

        public void SetMenuParent()
        {
            SetMenuParent(null);
        }

        public void OpenCloudSyncModule()
        {
            var module = _moduleManager.GetModule("CloudSyncModule");
            if (module != null)
            {
                UseModule(module);
            }
        }

        public void ShowModalWindow(string title, string content, string closeButtonText)
        {
            var modalObject = Instantiate(_modalWindowTemplate, _modalWindowContainer);
            var modalWindow = modalObject.GetComponent<ModalWindowHelper>();
            modalWindow.title.text = title;
            modalWindow.content.text = content;
            modalWindow.closeButton.text = closeButtonText;
            modalWindow.gameObject.SetActive(true);
        }
    }
}
