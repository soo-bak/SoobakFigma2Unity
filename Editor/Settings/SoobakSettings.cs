using UnityEditor;
using UnityEngine;

namespace SoobakFigma2Unity.Editor.Settings
{
    /// <summary>
    /// Persistent settings stored in EditorPrefs.
    /// </summary>
    internal static class SoobakSettings
    {
        private const string TokenKey = "SoobakFigma2Unity_Token";
        private const string PrefabPathKey = "SoobakFigma2Unity_PrefabPath";
        private const string ScreenPathKey = "SoobakFigma2Unity_ScreenPath";
        private const string ImagePathKey = "SoobakFigma2Unity_ImagePath";
        private const string ImageScaleKey = "SoobakFigma2Unity_ImageScale";
        private const string FontAssetGuidsKey = "SoobakFigma2Unity_FontAssetGuids";
        private const string ImportBoldFromFigmaKey = "SoobakFigma2Unity_ImportBoldFromFigma";

        public static string Token
        {
            get => EditorPrefs.GetString(TokenKey, "");
            set => EditorPrefs.SetString(TokenKey, value);
        }

        public static string PrefabPath
        {
            get => EditorPrefs.GetString(PrefabPathKey, "Assets/UI/Prefabs");
            set => EditorPrefs.SetString(PrefabPathKey, value);
        }

        public static string ScreenPath
        {
            get => EditorPrefs.GetString(ScreenPathKey, "Assets/UI/Screens");
            set => EditorPrefs.SetString(ScreenPathKey, value);
        }

        public static string ImagePath
        {
            get => EditorPrefs.GetString(ImagePathKey, "Assets/UI/Images");
            set => EditorPrefs.SetString(ImagePathKey, value);
        }

        public static float ImageScale
        {
            get => EditorPrefs.GetFloat(ImageScaleKey, 2f);
            set => EditorPrefs.SetFloat(ImageScaleKey, value);
        }

        public static string FontAssetGuids
        {
            get => EditorPrefs.GetString(FontAssetGuidsKey, "");
            set => EditorPrefs.SetString(FontAssetGuidsKey, value ?? "");
        }

        public static bool ImportBoldFromFigma
        {
            get => EditorPrefs.GetBool(ImportBoldFromFigmaKey, false);
            set => EditorPrefs.SetBool(ImportBoldFromFigmaKey, value);
        }
    }
}
