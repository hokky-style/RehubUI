using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UdonSharp;
using UnityEditor;
using VRC.SDK3.Data;
using System.Linq;

namespace RehubSystem
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ThemeManager : UdonSharpBehaviour
    {
        [SerializeField] private TextAsset _themePreset;
        [SerializeField] private UIManager _controller;
        [SerializeField] private Color _accentColor = new Color(117f / 255f, 167f / 255f, 255f / 255f, 1.0f);
        [SerializeField] private Color _baseColor = new Color(23f / 255f, 25f / 255f, 38f / 255f, 1.0f);
        [SerializeField] private Color _surfaceColor = new Color(43f / 255f, 46f / 255f, 67f / 255f, 1.0f);
        [SerializeField] private Color _textColor = new Color(230f / 255f, 233f / 255f, 248f / 255f, 1.0f);
        [SerializeField] private Color _successColor = new Color(118f / 255f, 216f / 255f, 154f / 255f, 1.0f);
        [SerializeField] private Color _warningColor = new Color(244f / 255f, 183f / 255f, 110f / 255f, 1.0f);
        [SerializeField] private Color _errorColor = new Color(239f / 255f, 111f / 255f, 139f / 255f, 1.0f);
        [SerializeField] private Color _infoColor = new Color(94f / 255f, 201f / 255f, 230f / 255f, 1.0f);

        public string ThemePreset => _themePreset.text;

        // private DataDictionary _themes;

        // void Start()
        // {
        // if (themePreset == null) return;
        // VRCJson.TryDeserializeFromJson(themePreset.text, out var _th);
        // if (_th.TokenType != TokenType.DataDictionary) return;
        // _themes = _th.DataDictionary;
        // ApplyTheme();
        // }

        public Color GetColor(ColorPalette colorPalette, float alpha = 1.0f)
        {
            switch (colorPalette)
            {
                case ColorPalette.Accent:
                    return new Color(_accentColor.r, _accentColor.g, _accentColor.b, alpha);
                case ColorPalette.Base:
                    return new Color(_baseColor.r, _baseColor.g, _baseColor.b, alpha);
                case ColorPalette.Surface:
                    return new Color(_surfaceColor.r, _surfaceColor.g, _surfaceColor.b, alpha);
                case ColorPalette.Text:
                    return new Color(_textColor.r, _textColor.g, _textColor.b, alpha);
                case ColorPalette.Success:
                    return new Color(_successColor.r, _successColor.g, _successColor.b, alpha);
                case ColorPalette.Warning:
                    return new Color(_warningColor.r, _warningColor.g, _warningColor.b, alpha);
                case ColorPalette.Error:
                    return new Color(_errorColor.r, _errorColor.g, _errorColor.b, alpha);
                case ColorPalette.Info:
                    return new Color(_infoColor.r, _infoColor.g, _infoColor.b, alpha);
                default:
                    return new Color(0, 0, 0, 1.0f);
            }
        }

        public void ApplyTheme()
        {
            if (_controller == null) return;

            foreach (var canvas in _controller.Canvas)
            {
                if (canvas == null) continue;
                foreach (var component in canvas.GetComponentsInChildren<ApplyTheme>(true))
                {
                    if (component == null) continue;
                    if (component.themeManager == null) component.themeManager = this;
                    ApplyToComponent(component);
                }
            }
        }

        private void ApplyToComponent(ApplyTheme component)
        {
            if (component == null) return;

            var color = GetColor(component.colorPalette, component.alpha);
            var image = component.GetComponent<Image>();
            if (image != null) image.color = color;

            var text = component.GetComponent<Text>();
            if (text != null) text.color = color;
        }
    }
}
