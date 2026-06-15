
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace RehubSystem
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ModuleExecutor : UdonSharpBehaviour
    {
        public ModuleMetadata module;
        public UIManager manager;

        public void Execute()
        {
            if (manager == null) return;
            if (module == null)
            {
                manager.CloseModuleMenu();
                return;
            }

            manager.UseModule(module);
        }
    }
}
