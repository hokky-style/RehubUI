
using System;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Data;
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

        private const int DockSlotCount = 5;
        private const int PinnedNavigationButtonCount = DockSlotCount - 2;
        private const string PackageName = "com.rehub.rehubsystem";
        private const string VersionListingUrl = "https://raw.githubusercontent.com/hokky-style/RehubUI/main/version-listing.example.json";
        private const string HomeNavigationButtonName = "__home";
        private const string SystemSettingsModuleId = "SystemSettingsModule";
        private const string InstanceOwnerStatusName = "InstanceOwner";
        private const string VerifiedUserStatusName = "VerifiedUser";
        private const string WorldLicensedStatusName = "WorldLicensed";
        private const string CloudSyncStatusName = "CloudSyncStatus";

        private ModuleMetadata _currentModule;
        private ModuleMetadata _nextModule;
        private ModuleMetadata[] _navigationButtonModules = new ModuleMetadata[0];
        private GameObject[] _navigationButtonObjects = new GameObject[0];
        private GameObject _homeNavigationButton;
        private GameObject _systemSettingsNavigationButton;

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
        public bool LocalPlayerVerified => IsHeaderStatusSuccess(VerifiedUserStatusName);
        public bool VersionListingLoaded => false;
        public bool VersionUpdateAvailable => false;
        public string LatestSystemVersion => "";
        public bool WorldLicensed => Networking.LocalPlayer != null && Networking.LocalPlayer.isMaster;

        private void Start()
        {
            UpdateCurrentDateTime();

            if (_moduleManager != null && !_moduleManager.Initialized)
            {
                _moduleManager.Initialize();
            }

            CreateHomeNavigationButton();

            var pinnedNavigationButtons = 0;
            var modules = _moduleManager != null && _moduleManager.Modules != null ? _moduleManager.Modules : new ModuleMetadata[0];
            foreach (var module in modules)
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
                    if (link == null)
                    {
                        Debug.LogWarning("HomeAppLinkHelper is missing on link template.");
                        continue;
                    }

                    if (link.icon != null)
                    {
                        link.icon.sprite = module.moduleIcon;
                    }

                    if (link.moduleExecutor != null)
                    {
                        link.moduleExecutor.manager = this;
                        link.moduleExecutor.module = module;
                    }

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

                    ApplyModuleNavigationTitle(link, navigationButton, module);

                    if (module.instanceOwnerOnly)
                    {
                        SetPermissionIconActive(link.permissionIconOwner);
                    }

                    if (module.instanceMasterOnly)
                    {
                        var masterIcon = link.permissionIconMaster != null ? link.permissionIconMaster : link.permissionIconAllowedUser;
                        SetPermissionIconActive(masterIcon);
                    }

                    if (module.allowedUsersOnly)
                    {
                        SetPermissionIconActive(link.permissionIconAllowedUser);
                    }
                }

                if (module.content != null)
                {
                    module.content.name = module.Uuid;
                    module.content.transform.SetParent(_moduleContentContainer, false);
                    module.content.SetActive(false);
                }
                else
                {
                    Debug.LogWarning($"Module content is missing: {module.moduleName}");
                }

                module.Activate();

                if (module.i18nManager != null)
                {
                    module.i18nManager.masterManager = _i18nManager;
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
            RequestVersionListing();
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

        private void SetPermissionIconActive(GameObject permissionIcon)
        {
            if (permissionIcon != null)
            {
                permissionIcon.SetActive(true);
            }
        }

        private void ApplyModuleNavigationTitle(HomeAppLinkHelper link, GameObject navigationButton, ModuleMetadata module)
        {
            var moduleTitle = module.moduleName;
            var hasModuleLocalization = !module.forceUseModuleName && module.i18nManager != null;
            if (hasModuleLocalization)
            {
                if (!module.i18nManager.Initialized)
                {
                    module.i18nManager.BuildLocalization();
                }

                if (module.i18nManager.HasLocalization)
                {
                    var localizedTitle = module.i18nManager.GetTranslation("$moduleName", _i18nManager.CurrentLanguage);
                    if (!string.IsNullOrEmpty(localizedTitle))
                    {
                        moduleTitle = localizedTitle;
                    }
                }
            }

            if (link.title != null)
            {
                link.title.text = moduleTitle;
            }

            if (link.titleI18n != null && hasModuleLocalization && module.i18nManager.HasLocalization)
            {
                link.titleI18n.manager = module.i18nManager;
                link.titleI18n.key = "$moduleName";
            }
            else if (link.titleI18n != null)
            {
                link.titleI18n.manager = null;
                link.titleI18n.key = null;
            }

            var navigationTitle = navigationButton != null ? navigationButton.transform.Find("Title") : null;
            if (navigationTitle == null) return;

            var navigationTitleText = navigationTitle.GetComponent<Text>();
            if (navigationTitleText != null)
            {
                navigationTitleText.text = moduleTitle;
            }

            var navigationTitleI18n = navigationTitle.GetComponent<ApplyI18n>();
            if (navigationTitleI18n != null && hasModuleLocalization && module.i18nManager.HasLocalization)
            {
                navigationTitleI18n.manager = module.i18nManager;
                navigationTitleI18n.key = "$moduleName";
            }
            else if (navigationTitleI18n != null)
            {
                navigationTitleI18n.manager = null;
                navigationTitleI18n.key = null;
            }
        }

        public bool IsLocalPlayerVerified()
        {
            return LocalPlayerVerified;
        }

        public bool IsPlayerVerified(string playerName)
        {
            if (Networking.LocalPlayer == null || string.IsNullOrEmpty(playerName)) return false;
            return playerName == Networking.LocalPlayer.displayName && LocalPlayerVerified;
        }

        public bool IsWorldLicensed()
        {
            return WorldLicensed;
        }

        public bool HasVersionUpdate()
        {
            return VersionUpdateAvailable;
        }

        public void RefreshHeaderStatusIndicators()
        {
            UpdateHeaderStatusIndicators();
        }

        public void RequestVerifiedUsers()
        {
            var cloudSyncManager = GetCloudSyncManager();
            if (cloudSyncManager == null || string.IsNullOrEmpty(cloudSyncManager.GetVerifiedUsersUrlString()))
            {
                UpdateHeaderStatusIndicators();
                return;
            }

            VRCStringDownloader.LoadUrl(cloudSyncManager.GetVerifiedUsersUrl(), (IUdonEventReceiver)this);
        }

        public void RequestVersionListing()
        {
            UpdateHeaderStatusIndicators();
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            var cloudSyncManager = GetCloudSyncManager();
            if (cloudSyncManager != null && IsUrlString(result.Url, cloudSyncManager.GetVerifiedUsersUrlString()))
            {
                var verified = Networking.LocalPlayer != null && IsNameInRemoteList(result.Result, Networking.LocalPlayer.displayName);
                UpdateHeaderStatusIndicators(verified);
                Debug.Log($"[UIManager] Verified users list loaded. Local player verified: {verified}");
                return;
            }

            if (IsUrlString(result.Url, VersionListingUrl))
            {
                var versionListingLoaded = TryReadLatestSystemVersion(result.Result, out var latestSystemVersion);
                UpdateHeaderStatusIndicators();
                Debug.Log($"[UIManager] Version listing loaded: {versionListingLoaded}, latest: {latestSystemVersion}");
            }
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            var cloudSyncManager = GetCloudSyncManager();
            if (cloudSyncManager != null && IsUrlString(result.Url, cloudSyncManager.GetVerifiedUsersUrlString()))
            {
                Debug.LogWarning($"[UIManager] Failed to load verified users list: {result.ErrorCode} - {result.Error}");
                UpdateHeaderStatusIndicators();
                return;
            }

            if (IsUrlString(result.Url, VersionListingUrl))
            {
                Debug.LogWarning($"[UIManager] Failed to load version listing: {result.ErrorCode} - {result.Error}");
                UpdateHeaderStatusIndicators();
            }
        }

        private bool IsUrlString(VRCUrl url, string value)
        {
            if (url == null || string.IsNullOrEmpty(value)) return false;
            return url.Get() == value;
        }

        public void UseModule(ModuleMetadata module)
        {
            if (_currentModule != null && _currentModule.Uuid == module.Uuid) return;

#region Permission check
            var localPlayerName = Networking.LocalPlayer != null ? Networking.LocalPlayer.displayName : "";
            var isInstanceOwner = Networking.LocalPlayer != null && Networking.LocalPlayer.isInstanceOwner;
            var isInstanceMaster = Networking.LocalPlayer != null && Networking.LocalPlayer.isMaster;
            var isAllowedUser = IsLocalAllowedUser(module.allowedUsers, localPlayerName) || IsPlayerVerified(localPlayerName);
            var hasPermissionRestrictions = module.instanceOwnerOnly || module.instanceMasterOnly || module.allowedUsersOnly;
            var canUseRestrictedModule = (module.instanceOwnerOnly && isInstanceOwner) || (module.instanceMasterOnly && isInstanceMaster) || (module.allowedUsersOnly && isAllowedUser);
            if (hasPermissionRestrictions && !canUseRestrictedModule)
            {
                ShowModalWindow(
                    _i18nManager.GetTranslation("noPermission"),
                    _i18nManager.GetTranslation("notAllowedToUseThisModuleBecauseYouDoNotMatchPermissions"),
                    _i18nManager.GetTranslation("close")
                );
                Debug.LogWarning("You do not match the permission requirements for this module.");
                return;
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

            if (!module.forceUseModuleName && module.i18nManager != null)
            {
                if (!module.i18nManager.Initialized)
                {
                    module.i18nManager.BuildLocalization();
                }

                if (module.i18nManager.HasLocalization)
                {
                    SetTitle(module.i18nManager.GetTranslation("$moduleName", _i18nManager.CurrentLanguage));
                    return;
                }
            }

            SetTitle(module.moduleName);
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
            UpdateHeaderStatusIndicators();
        }

        private CloudSyncManager GetCloudSyncManager()
        {
            var root = _rootCanvas != null ? _rootCanvas.root : transform.root;
            var cloudSyncObject = FindChildGameObject(root, "CloudSyncManager");
            return cloudSyncObject != null ? cloudSyncObject.GetComponent<CloudSyncManager>() : null;
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

        private bool IsHeaderStatusSuccess(string statusName)
        {
            var statusObject = FindChildGameObject(_panel, statusName);
            var theme = FindStatusTheme(statusObject);
            return theme != null && theme.colorPalette == ColorPalette.Success;
        }

        private void UpdateHeaderStatusIndicators()
        {
            UpdateHeaderStatusIndicators(false);
        }

        private void UpdateHeaderStatusIndicators(bool verified)
        {
            var instanceOwnerStatus = FindChildGameObject(_panel, InstanceOwnerStatusName);
            var verifiedUserStatus = FindChildGameObject(_panel, VerifiedUserStatusName);
            var worldLicensedStatus = FindChildGameObject(_panel, WorldLicensedStatusName);
            var cloudSyncStatus = FindChildGameObject(_panel, CloudSyncStatusName);

            var isInstanceOwner = Networking.LocalPlayer != null && Networking.LocalPlayer.isInstanceOwner;
            var isInstanceMaster = Networking.LocalPlayer != null && Networking.LocalPlayer.isMaster;
            SetHeaderStatus(instanceOwnerStatus, FindStatusTheme(instanceOwnerStatus), isInstanceOwner ? ColorPalette.Success : ColorPalette.Error);

            SetHeaderStatus(verifiedUserStatus, FindStatusTheme(verifiedUserStatus), verified ? ColorPalette.Success : ColorPalette.Error);

            SetHeaderStatus(worldLicensedStatus, FindStatusTheme(worldLicensedStatus), isInstanceMaster ? ColorPalette.Success : ColorPalette.Error);

            var cloudSyncManager = GetCloudSyncManager();
            var cloudReady = cloudSyncManager != null && cloudSyncManager.Initialized;
            SetHeaderStatus(cloudSyncStatus, FindStatusTheme(cloudSyncStatus), cloudReady ? ColorPalette.Success : ColorPalette.Error);
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
            ApplyThemeComponent(theme);
        }

        private void ApplyThemeComponent(ApplyTheme theme)
        {
            if (theme == null || _themeManager == null) return;

            var color = _themeManager.GetColor(theme.colorPalette, theme.alpha);
            var image = theme.GetComponent<Image>();
            if (image != null) image.color = color;

            var text = theme.GetComponent<Text>();
            if (text != null) text.color = color;
        }

        private bool IsNameInRemoteList(string remoteList, string playerName)
        {
            if (string.IsNullOrEmpty(playerName) || string.IsNullOrEmpty(remoteList)) return false;

            var source = NormalizeRemoteList(remoteList);
            var target = NormalizeRemoteList(playerName).Trim();
            return source.Contains($"\n{target}\n");
        }

        private bool TryReadLatestSystemVersion(string json, out string latestVersion)
        {
            latestVersion = "";

            if (!VRCJson.TryDeserializeFromJson(json, out var listing) || listing.TokenType != TokenType.DataDictionary)
            {
                Debug.LogWarning("[UIManager] Failed to parse version listing JSON.");
                return false;
            }

            var root = listing.DataDictionary;
            if (root.TryGetValue(PackageName, TokenType.String, out var directPackageVersion))
            {
                latestVersion = directPackageVersion.String;
                return !string.IsNullOrEmpty(latestVersion);
            }

            if (root.TryGetValue(PackageName, TokenType.DataDictionary, out var packageListing))
            {
                var packageData = packageListing.DataDictionary;
                if (packageData.TryGetValue("system", TokenType.String, out var packageSystemVersion))
                {
                    latestVersion = packageSystemVersion.String;
                    return !string.IsNullOrEmpty(latestVersion);
                }
            }

            if (root.TryGetValue("system", TokenType.String, out var systemVersion))
            {
                latestVersion = systemVersion.String;
                return !string.IsNullOrEmpty(latestVersion);
            }

            if (root.TryGetValue("version", TokenType.String, out var version))
            {
                latestVersion = version.String;
                return !string.IsNullOrEmpty(latestVersion);
            }

            Debug.LogWarning("[UIManager] Version listing does not contain a system version.");
            return false;
        }

        private int CompareVersions(string left, string right)
        {
            for (int i = 0; i < 3; i++)
            {
                var leftPart = GetVersionPart(left, i);
                var rightPart = GetVersionPart(right, i);

                if (leftPart > rightPart) return 1;
                if (leftPart < rightPart) return -1;
            }

            return 0;
        }

        private int GetVersionPart(string version, int index)
        {
            if (string.IsNullOrEmpty(version)) return 0;

            var cleanVersion = version.Trim();
            var preReleaseIndex = cleanVersion.IndexOf("-");
            if (preReleaseIndex >= 0)
            {
                cleanVersion = cleanVersion.Substring(0, preReleaseIndex);
            }

            var parts = cleanVersion.Split('.');
            if (index >= parts.Length) return 0;

            int value;
            return int.TryParse(parts[index], out value) ? value : 0;
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
            ShowStatusModal();
        }

        public void ShowStatusModal()
        {
            var cloudSyncManager = GetCloudSyncManager();
            var synchronized = cloudSyncManager != null && cloudSyncManager.Initialized;
            var isInstanceMaster = Networking.LocalPlayer != null && Networking.LocalPlayer.isMaster;
            var isInstanceOwner = Networking.LocalPlayer != null && Networking.LocalPlayer.isInstanceOwner;

            var content =
                $"{GetSystemTranslation("statusSynchronization", "Synchronization")}: {FormatStatusValue(synchronized)}\n" +
                $"{GetSystemTranslation("statusInstanceMaster", "Instance master")}: {FormatStatusValue(isInstanceMaster)}\n" +
                $"{GetSystemTranslation("statusInstanceOwner", "Instance owner")}: {FormatStatusValue(isInstanceOwner)}\n" +
                $"{GetSystemTranslation("statusVerifiedUser", "Verified user")}: {FormatStatusValue(LocalPlayerVerified)}";

            ShowModalWindow(
                GetSystemTranslation("statusModalTitle", "Rehub System Status"),
                content,
                GetSystemTranslation("close", "Close"));
        }

        private string FormatStatusValue(bool value)
        {
            return value ? GetSystemTranslation("statusYes", "Yes") : GetSystemTranslation("statusNo", "No");
        }

        private string GetSystemTranslation(string key, string fallback)
        {
            if (_i18nManager == null) return fallback;
            if (!_i18nManager.Initialized)
            {
                _i18nManager.BuildLocalization();
            }

            var translation = _i18nManager.GetTranslation(key);
            return string.IsNullOrEmpty(translation) ? fallback : translation;
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
