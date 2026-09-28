using System;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

public static class FinikAndroidIconSetup
{
    const string AdaptiveBackgroundPath = "Assets/Finik/AppIcon/finik_adaptive_background.png";
    const string AdaptiveForegroundPath = "Assets/Finik/AppIcon/finik_adaptive_foreground.png";

    public static void Apply()
    {
        var platform = NamedBuildTarget.Android;
        var background = Load(AdaptiveBackgroundPath);
        var foreground = Load(AdaptiveForegroundPath);

        var adaptive = PlayerSettings.GetPlatformIcons(platform, AndroidPlatformIconKind.Adaptive);
        for (int i = 0; i < adaptive.Length; i++)
        {
            if (adaptive[i].maxLayerCount < 2)
                throw new InvalidOperationException("Android adaptive icon slot does not expose two layers.");

            // Unity/Android adaptive icon layer order: background = 0, foreground = 1.
            adaptive[i].SetTexture(background, 0);
            adaptive[i].SetTexture(foreground, 1);
        }

        PlayerSettings.SetPlatformIcons(platform, AndroidPlatformIconKind.Adaptive, adaptive);
        AssetDatabase.SaveAssets();
        Debug.Log($"[FinikIcon] Android adaptive Finik icon configured for {adaptive.Length} slots.");
    }

    static Texture2D Load(string path)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (!texture)
            throw new InvalidOperationException("Missing Android icon texture: " + path);
        return texture;
    }
}
