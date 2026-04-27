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

    /// <summary>
    /// Controls what happens on re-import when a prefab already exists.
    /// </summary>
    public enum MergeMode
    {
        /// <summary>Merge with existing prefab. Preserve user-added components, children,
        /// and any components the user locked (either whole-GO or per-type). Default.</summary>
        SmartMerge,
        /// <summary>Blow the existing prefab away and save the freshly-built one as-is. Loses
        /// all user edits. Available for debugging or for intentional resets.</summary>
        FullReplace
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
        // Off by default: preserve Figma Group structure 1:1 in Unity hierarchy.
        // When on, empty Groups are removed and their children are placed under
        // the outer parent (positioning is recalculated to remain correct).
        public bool FlattenEmptyGroups = false;
        public bool ApplyConstraints = true;

        // Color
        public ColorSpaceMode ColorSpace = ColorSpaceMode.Auto;

        // Prefab (P1 features, kept as flags)
        public bool GeneratePrefabVariants = true;
        public bool MapComponentInstances = true;

        // Re-import behaviour
        public MergeMode MergeMode = MergeMode.SmartMerge;
        public int BackupRetentionCount = 5;

        // Output paths
        public string PrefabOutputPath = "Assets/UI/Prefabs";
        public string ScreenOutputPath = "Assets/UI/Screens";
        public string ImageOutputPath = "Assets/UI/Images";

        // Component auto-extraction (zero-config Figma component → Unity prefab)
        // When enabled, the import pre-pass walks the entire imported tree, builds
        // a dependency graph of every Figma COMPONENT/INSTANCE, and ensures each
        // one has a .prefab file in ComponentOutputPath before the main convert
        // runs. The screen prefab then ends up referencing those prefabs as
        // PrefabInstances rather than inlining everything.
        public string ComponentOutputPath = "Assets/UI/Components";
        public bool ExtractFigmaComponentsAsPrefabs = true;

        // FLAT rasterisation per-COMPONENT.
        //
        // When true, every Figma COMPONENT in the import tree is exported as a
        // single PNG of the *whole* component (everything inside baked together —
        // background, strokes, inner rectangles, text, the works) and the resulting
        // Unity prefab is just one GameObject with one Image. No recursive walk of
        // the COMPONENT's children, no per-rectangle sprites composited by UGUI.
        //
        // Why this matters: Figma's compositing (alpha, blend modes, mask
        // intersections, stroke offsets, drop-shadow under/over rules) does not
        // match UGUI's. Even when every individual element exports correctly the
        // assembled visual drifts — wavy lines flatten, strokes clip, layers mis-
        // overlap. A single PNG sidesteps the entire compositing question because
        // Figma rendered the final frame itself.
        //
        // Trade-off: the prefab loses editability of inner elements (text is baked
        // pixels, not a TextMeshProUGUI). For static design fidelity this is the
        // correct trade. For dynamic UI (per-instance text, runtime tinting),
        // disable this and accept the compositing drift, or design the dynamic
        // parts as their own Figma components and assemble them in Unity.
        //
        // Default: true. The package's primary use case is "Figma design lands in
        // Unity looking exactly like Figma"; designers asked for this explicitly
        // after structural mode produced visible mismatches on every iteration.
        public bool FlatRasterizeComponents = true;

        // Phase 2 (opt-in): structural duplicate detection — promote subtrees that
        // repeat 2+ times in a frame even when the designer didn't mark them as
        // Figma components. Off by default because false-positives (visually
        // similar but semantically distinct subtrees getting collapsed) are hard
        // to debug.
        public bool DetectStructuralDuplicates = false;
    }
}
