using SoobakFigma2Unity.Editor.Util;
using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Settings
{
    /// <summary>
    /// Lazy loader for the single <see cref="FigmaManagedTypesRegistry"/> asset. Creates
    /// it on first access and seeds it with the default managed types list.
    /// </summary>
    internal static class FigmaManagedTypesRegistryProvider
    {
        private const string AssetFolder = "Assets/Soobak/Settings";
        private const string AssetPath = AssetFolder + "/FigmaManagedTypes.asset";

        private static FigmaManagedTypesRegistry _cached;

        public static FigmaManagedTypesRegistry Get()
        {
            if (_cached != null) return _cached;

            _cached = AssetDatabase.LoadAssetAtPath<FigmaManagedTypesRegistry>(AssetPath);
            if (_cached != null) return _cached;

            AssetFolderUtil.EnsureFolder(AssetFolder);
            _cached = ScriptableObject.CreateInstance<FigmaManagedTypesRegistry>();
            _cached.ResetToDefaults();
            AssetDatabase.CreateAsset(_cached, AssetPath);
            AssetDatabase.SaveAssets();
            return _cached;
        }
    }
}
