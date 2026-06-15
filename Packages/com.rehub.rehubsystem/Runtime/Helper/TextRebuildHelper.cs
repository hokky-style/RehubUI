
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

namespace RehubSystem
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TextRebuildHelper : UdonSharpBehaviour
    {
        private void Start()
        {
            var text = GetComponent<Text>();
            if (text != null)
            {
                text.FontTextureChanged();
            }
        }
    }
}