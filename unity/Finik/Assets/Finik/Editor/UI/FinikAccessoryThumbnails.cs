using System.Collections.Generic;
using System.IO;
using Finik.Accessories;
using UnityEditor;
using UnityEngine;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Renders a transparent thumbnail for every accessory in the catalog straight from its 3D model,
    /// so the outfit picker always matches what Finik actually wears.
    /// </summary>
    public static class FinikAccessoryThumbnails
    {
        public const string Folder = "Assets/Finik/UI/Thumbnails";
        const int Size = 256;
        const int Layer = 31;
        static readonly Vector3 Stage = new(0f, -500f, 0f);

        [MenuItem("Finik/UI/Render Accessory Thumbnails")]
        static void RenderMenu()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<FinikAccessoryCatalog>("Assets/Finik/Accessories/FinikAccessoryCatalog.asset");
            Debug.Log($"[FinikUi] Rendered {RenderAll(catalog).Count} accessory thumbnails.");
        }

        public static Dictionary<string, Sprite> RenderAll(FinikAccessoryCatalog catalog)
        {
            var sprites = new Dictionary<string, Sprite>();
            if (!catalog) return sprites;
            Directory.CreateDirectory(Folder);

            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var cameraGo = new GameObject("ThumbnailCamera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = cameraGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = 1 << Layer;
            cam.fieldOfView = 24f;
            cam.targetTexture = rt;
            var lightGo = new GameObject("ThumbnailLight") { hideFlags = HideFlags.HideAndDontSave };
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.cullingMask = 1 << Layer;
            var paths = new List<(string id, string path)>();

            try
            {
                foreach (var entry in catalog.entries)
                {
                    if (entry?.model == null) continue;
                    var instance = Object.Instantiate(entry.model, Stage, Quaternion.identity);
                    instance.hideFlags = HideFlags.HideAndDontSave;
                    try
                    {
                        foreach (var t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
                        var renderers = instance.GetComponentsInChildren<Renderer>();
                        if (renderers.Length == 0) continue;
                        var bounds = renderers[0].bounds;
                        foreach (var r in renderers) bounds.Encapsulate(r.bounds);

                        // Backpacks are worn on the back, so show them from behind; everything else from the front.
                        bool fromBehind = entry.slot == "backpack";
                        Vector3 direction = new Vector3(0.55f, 0.35f, fromBehind ? -1f : 1f).normalized;
                        float radius = bounds.extents.magnitude;
                        float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;
                        cam.transform.position = bounds.center + direction * distance;
                        cam.transform.LookAt(bounds.center);
                        cam.nearClipPlane = Mathf.Max(0.01f, distance - radius * 2f);
                        cam.farClipPlane = distance + radius * 2f;
                        lightGo.transform.rotation = Quaternion.LookRotation(-direction + Vector3.down * 0.6f);

                        cam.Render();
                        var previous = RenderTexture.active;
                        RenderTexture.active = rt;
                        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                        texture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                        texture.Apply();
                        RenderTexture.active = previous;
                        string path = $"{Folder}/acc_{entry.id}.png";
                        File.WriteAllBytes(path, texture.EncodeToPNG());
                        Object.DestroyImmediate(texture);
                        paths.Add((entry.id, path));
                    }
                    finally
                    {
                        Object.DestroyImmediate(instance);
                    }
                }
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(cameraGo);
                Object.DestroyImmediate(lightGo);
                rt.Release();
                Object.DestroyImmediate(rt);
            }

            // Two batches instead of one refresh per thumbnail: the PNGs were written straight to disk,
            // so they have to be imported before their importers can be configured.
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var (_, path) in paths) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            FinikHudSpriteBaker.ConfigureAll(paths.ConvertAll(p => (path: p.path, border: Vector4.zero, mipmaps: true)));
            foreach (var (id, path) in paths) sprites[id] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            return sprites;
        }
    }
}
