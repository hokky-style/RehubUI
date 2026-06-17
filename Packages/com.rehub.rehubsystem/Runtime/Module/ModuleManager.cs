
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;

namespace RehubSystem
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ModuleManager : UdonSharpBehaviour
    {
        [SerializeField] private Transform _modulesRoot;
        [SerializeField] private ModuleMetadata[] _systemModules;
        private ModuleMetadata[] _modules = new ModuleMetadata[0];
        private DataList _availableModules = new DataList();
        private bool _isInitialized = false;

        public Transform ModulesRoot => _modulesRoot;
        public ModuleMetadata[] Modules => _modules;
        public bool Initialized => _isInitialized;

        private void Start()
        {
            Initialize();
        }

        public void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            var installedModules = new ModuleMetadata[0];
            if (_modulesRoot != null)
            {
                installedModules = new ModuleMetadata[_modulesRoot.childCount];
                for (int i = 0; i < _modulesRoot.childCount; i++)
                {
                    var child = _modulesRoot.GetChild(i);
                    installedModules[i] = child != null ? child.GetComponent<ModuleMetadata>() : null;
                }
            }

            var systemModules = _systemModules != null ? _systemModules : new ModuleMetadata[0];
            _modules = ArrayUtils.Concat(installedModules, systemModules);

            foreach (var module in _modules)
            {
                if (module == null) continue;
                if (_availableModules.Contains(module.ModuleId) && module.IsUnique)
                {
                    Debug.LogWarning($"[ModuleManager] Destroying duplicated module: {module.ModuleId}");
                    Destroy(module.gameObject);
                    continue;
                }

                _availableModules.Add(module.ModuleId);
            }
        }

        public ModuleMetadata GetModule(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId)) return null;

            foreach (var m in _modules)
            {
                if (m == null) continue;
                if (m.ModuleId == moduleId)
                {
                    return m;
                }
            }

            return null;
        }
    }
}
