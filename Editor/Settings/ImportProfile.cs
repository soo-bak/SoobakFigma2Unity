namespace SoobakFigma2Unity.Editor.Settings
{
    public enum ImportMode
    {
        ComponentsOnly,
        ScreenOnly,
        ScreenAndComponents
    }

    public enum ColorSpaceMode
    {
        Auto,
        Linear,
        Gamma
    }

    [System.Serializable]
    public sealed class ImportProfile
    {
        // Connection
        public string FigmaUrl = "";
        public string PersonalAccessToken = "";

        // Import mode
        public ImportMode Mode = ImportMode.ScreenAndComponents;

        // Image
        public float ImageScale = 2f;
        public bool AutoNineSlice = true;
        public bool SolidColorOptimization = true;

        // Layout
        public bool ConvertAutoLayout = true;
        public bool FlattenEmptyGroups = true;
        public bool ApplyConstraints = true;

        // Color
        public ColorSpaceMode ColorSpace = ColorSpaceMode.Auto;

        // Prefab (P1 features, kept as flags)
        public bool GeneratePrefabVariants = true;
        public bool MapComponentInstances = true;
        public bool PreserveOnReimport = true;

        // Output paths
        public string PrefabOutputPath = "Assets/UI/Prefabs";
        public string ScreenOutputPath = "Assets/UI/Screens";
        public string ImageOutputPath = "Assets/UI/Images";
    }
}
