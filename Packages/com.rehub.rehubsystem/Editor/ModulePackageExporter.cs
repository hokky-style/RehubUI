using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RehubSystem.Editor
{
    public static class ModulePackageExporter
    {
        private const string ModulesRoot = "Packages/com.rehub.rehubsystem/Runtime/Modules";

        [MenuItem("Assets/RehubUI/Export Selected Module UnityPackage", false, 2000)]
        public static void ExportSelectedModule()
        {
            var selectedPaths = GetSelectedModulePaths();
            if (selectedPaths.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "RehubUI Module Exporter",
                    "Select a module folder and/or prefab inside Packages/com.rehub.rehubsystem/Runtime/Modules first.",
                    "OK");
                return;
            }

            var defaultName = GetDefaultPackageName(selectedPaths);
            var outputPath = EditorUtility.SaveFilePanel(
                "Export RehubUI module",
                "",
                defaultName,
                "unitypackage");

            if (string.IsNullOrEmpty(outputPath)) return;

            AssetDatabase.ExportPackage(
                selectedPaths,
                outputPath,
                ExportPackageOptions.Recurse | ExportPackageOptions.IncludeDependencies);

            EditorUtility.RevealInFinder(outputPath);
        }

        [MenuItem("Assets/RehubUI/Export Selected Module UnityPackage", true)]
        public static bool ValidateExportSelectedModule()
        {
            return GetSelectedModulePaths().Length > 0;
        }

        private static string[] GetSelectedModulePaths()
        {
            var paths = new List<string>();

            foreach (var guid in Selection.assetGUIDs)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (!path.Replace('\\', '/').StartsWith(ModulesRoot)) continue;

                paths.Add(path);
            }

            return paths.Distinct().OrderBy(path => path).ToArray();
        }

        private static string GetDefaultPackageName(string[] selectedPaths)
        {
            var firstPath = selectedPaths.FirstOrDefault();
            if (string.IsNullOrEmpty(firstPath)) return "RehubModule.unitypackage";

            var name = Path.GetFileNameWithoutExtension(firstPath);
            if (string.IsNullOrEmpty(name))
            {
                name = "RehubModule";
            }

            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalidChar, '-');
            }

            return name + ".unitypackage";
        }
    }
}
