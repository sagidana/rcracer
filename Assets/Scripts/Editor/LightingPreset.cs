using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Tools > Visuals > Apply Cinematic Look : one menu item that raises the render quality (MSAA, shadow
// distance/resolution), tunes the shared post-processing volume (bloom, vignette, color, ACES tonemapping),
// and gives car paint / glass their own smoothness. Safe to run again - it only sets values, never duplicates.
//
// Every track scene shares PC_RPAsset (the render pipeline asset) and SampleSceneProfile (the Global Volume's
// profile), so one run here changes the look of all three tracks and any new one built later.
public static class LightingPreset
{
    const string PCAssetPath = "Assets/Settings/PC_RPAsset.asset";
    const string ProfilePath = "Assets/Settings/SampleSceneProfile.asset";

    [MenuItem("Tools/Visuals/Apply Cinematic Look")]
    public static void Apply()
    {
        ApplyRenderQuality();
        ApplyPostProcessing();
        AssetDatabase.SaveAssets();
        Debug.Log("Cinematic look applied (MSAA 4x, longer shadow distance, ACES tonemapping, bloom/vignette/color grading). Affects every scene using PC_RPAsset + SampleSceneProfile.");
    }

    static void ApplyRenderQuality()
    {
        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PCAssetPath);
        if (asset == null) { Debug.LogWarning("LightingPreset: PC_RPAsset not found at " + PCAssetPath); return; }

        asset.msaaSampleCount = 4;
        asset.shadowDistance = 220f;   // the desert loop is ~1.2 km; shadows must not pop in mid-lap

        SerializedObject so = new SerializedObject(asset);
        SetInt(so, "m_MainLightShadowmapResolution", 4096);
        SetInt(so, "m_AdditionalLightsShadowmapResolution", 2048);
        SetInt(so, "m_ShadowCascadeCount", 4);
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(asset);
    }

    static void SetInt(SerializedObject so, string field, int value)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p != null) p.intValue = value;
    }

    static void ApplyPostProcessing()
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null) { Debug.LogWarning("LightingPreset: volume profile not found at " + ProfilePath); return; }

        Bloom bloom = GetOrAdd<Bloom>(profile);
        bloom.threshold.Override(0.9f);
        bloom.intensity.Override(0.4f);
        bloom.scatter.Override(0.6f);

        Vignette vignette = GetOrAdd<Vignette>(profile);
        vignette.intensity.Override(0.28f);
        vignette.smoothness.Override(0.35f);

        Tonemapping tone = GetOrAdd<Tonemapping>(profile);
        tone.mode.Override(TonemappingMode.ACES);

        ColorAdjustments color = GetOrAdd<ColorAdjustments>(profile);
        color.postExposure.Override(0.15f);
        color.contrast.Override(8f);
        color.saturation.Override(10f);

        EditorUtility.SetDirty(profile);
    }

    static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
    {
        T comp;
        if (!profile.TryGet(out comp)) comp = profile.Add<T>(true);
        return comp;
    }
}
