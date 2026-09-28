using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Finik.Accessories;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Импорт аксессуаров Финика из Blender-пайплайна (art/blender/scripts):
/// URP-материалы по манифесту, ремап материалов FBX, каталог, установка FinikAccessoryRig
/// и батч-проверка посадки (Unity -batchmode -executeMethod FinikAccessorySetup.BatchVerify).
/// </summary>
public static class FinikAccessorySetup
{
    const string Root = "Assets/Finik/Accessories";
    const string ManifestPath = Root + "/accessories_manifest.json";
    const string CatalogPath = Root + "/FinikAccessoryCatalog.asset";
    const string MaterialsDir = Root + "/Materials";
    const string ScenePath = "Assets/Finik/Scenes/FinikRoomNavigationPrototype.unity";
    const float VerifyToleranceMm = 10f;

    [Serializable] sealed class Manifest { public int schema; public string[] alignJoints; public Joint[] joints; public Item[] items; }
    [Serializable] sealed class Joint { public string name; public float[] position; }
    [Serializable] sealed class Item
    {
        public string id, name, title, vibe, slot, bone, file;
        public string[] parts;
        public int triangles;
        public Mat[] materials;
        public float[] centroid;
    }
    [Serializable] sealed class Mat
    {
        public string name;
        public float[] color;
        public float alpha, smoothness, metallic, emissionStrength;
        public float[] emission;
    }

    static Manifest LoadManifest()
    {
        if (!File.Exists(ManifestPath))
            throw new FileNotFoundException("Нет манифеста аксессуаров", ManifestPath);
        var m = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath, Encoding.UTF8));
        if (m?.items == null || m.items.Length == 0)
            throw new InvalidDataException("Манифест аксессуаров пуст.");
        return m;
    }

    // ------------------------------------------------------------------ импорт

    [MenuItem("Finik/Accessories/Import From Manifest")]
    public static string ImportAll()
    {
        var manifest = LoadManifest();
        EnsureFolder(MaterialsDir);

        var materials = new Dictionary<string, Material>();
        foreach (var item in manifest.items)
            foreach (var m in item.materials ?? Array.Empty<Mat>())
                if (!materials.ContainsKey(m.name))
                    materials[m.name] = CreateOrUpdateMaterial(m);

        var catalog = AssetDatabase.LoadAssetAtPath<FinikAccessoryCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<FinikAccessoryCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        catalog.alignJoints = manifest.alignJoints is { Length: >= 3 } ? manifest.alignJoints : catalog.alignJoints;
        catalog.entries.Clear();

        foreach (var item in manifest.items)
        {
            var path = $"{Root}/Models/{item.file}";
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(path) as ModelImporter
                           ?? throw new FileNotFoundException("Нет FBX аксессуара", path);
            }
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            foreach (var m in item.materials ?? Array.Empty<Mat>())
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), materials[m.name]);
            importer.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path)
                        ?? throw new InvalidOperationException($"FBX не импортировался: {path}");
            catalog.entries.Add(new FinikAccessoryCatalog.Entry
            {
                id = item.id, title = item.title, slot = item.slot, bone = item.bone, model = model
            });
        }
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        return $"Finik accessories: {catalog.entries.Count} предметов, {materials.Count} материалов.";
    }

    static Material CreateOrUpdateMaterial(Mat src)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? throw new InvalidOperationException("URP Lit shader missing.");
        var path = $"{MaterialsDir}/{src.name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader) { name = src.name };
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        var c = src.color is { Length: >= 3 } ? new Color(src.color[0], src.color[1], src.color[2], src.alpha) : Color.white;
        mat.SetColor("_BaseColor", c);
        mat.SetFloat("_Metallic", src.metallic);
        mat.SetFloat("_Smoothness", src.smoothness);

        bool transparent = src.alpha < 0.999f;
        mat.SetFloat("_Surface", transparent ? 1f : 0f);
        mat.SetFloat("_Blend", 0f);
        mat.SetOverrideTag("RenderType", transparent ? "Transparent" : "Opaque");
        mat.SetFloat("_SrcBlend", (float)(transparent ? BlendMode.SrcAlpha : BlendMode.One));
        mat.SetFloat("_DstBlend", (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
        mat.SetFloat("_ZWrite", transparent ? 0f : 1f);
        if (transparent) mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        else mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = transparent ? (int)RenderQueue.Transparent : -1;

        if (src.emissionStrength > 0f && src.emission is { Length: >= 3 })
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(src.emission[0], src.emission[1], src.emission[2]) * src.emissionStrength);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            mat.DisableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // ------------------------------------------------------------------ сцена

    [MenuItem("Finik/Accessories/Add Rig To Finik_Root")]
    public static string AddRig()
    {
        var root = GameObject.Find("Finik_Root") ?? throw new InvalidOperationException("Finik_Root missing.");
        var catalog = AssetDatabase.LoadAssetAtPath<FinikAccessoryCatalog>(CatalogPath)
                      ?? throw new InvalidOperationException("Сначала Finik/Accessories/Import From Manifest.");
        var rig = root.GetComponent<FinikAccessoryRig>();
        if (rig == null)
            rig = root.AddComponent<FinikAccessoryRig>();
        rig.Catalog = catalog;
        EditorUtility.SetDirty(rig);
        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        return "FinikAccessoryRig добавлен на Finik_Root.";
    }

    // ------------------------------------------------------------------ проверка

    /// <summary>CLI: надевает каждый предмет и сверяет центроид с эталоном из Blender.
    /// Сцену не сохраняет. Код выхода 0 — всё в допуске.</summary>
    public static void BatchVerify()
    {
        int exit = 0;
        try
        {
            Debug.Log(ImportAll());
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var report = Verify(out bool ok);
            Debug.Log(report);
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "finik_accessory_verify.txt"), report, Encoding.UTF8);
            exit = ok ? 0 : 1;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            exit = 2;
        }
        EditorApplication.Exit(exit);
    }

    [MenuItem("Finik/Accessories/Verify Fit")]
    public static string VerifyMenu() => Verify(out _);

    static string Verify(out bool ok)
    {
        var manifest = LoadManifest();
        var catalog = AssetDatabase.LoadAssetAtPath<FinikAccessoryCatalog>(CatalogPath)
                      ?? throw new InvalidOperationException("Нет каталога аксессуаров.");
        var root = GameObject.Find("Finik_Root") ?? throw new InvalidOperationException("Finik_Root missing.");
        var skin = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                       .Where(s => s.sharedMesh != null).OrderByDescending(s => s.sharedMesh.vertexCount).First();

        // временный риг на Finik_Root: тело он найдёт среди детей; после проверки удаляется
        var rig = root.AddComponent<FinikAccessoryRig>();
        rig.Catalog = catalog;

        var names = manifest.alignJoints;
        var bj = names.Select(n => ToV(manifest.joints.First(j => j.name == n).position)).ToArray();
        var cj = names.Select(n => BindPos(skin, n)).ToArray();
        float lenB = (bj[1] - bj[0]).magnitude, lenC = (cj[1] - cj[0]).magnitude;
        var fb = Frame(bj);
        var fc = Frame(cj);

        var sb = new StringBuilder("FINIK_ACCESSORY_VERIFY\n");
        ok = true;
        foreach (var item in manifest.items)
        {
            var parts = rig.Equip(item.id);
            var bone = skin.bones.First(b => b != null && b.name == item.bone);
            // кости в сцене стоят в позе Idle, а эталон задан в bind-позе: переводим фактическую
            // точку в bind-пространство через ту же кость — так же, как скинится само тело
            var toBind = BindWorld(skin, item.bone) * bone.worldToLocalMatrix;
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var p in parts)
            {
                var mesh = p.GetComponent<MeshFilter>().sharedMesh;
                var m = p.transform.localToWorldMatrix;
                foreach (var v in mesh.vertices) { sum += m.MultiplyPoint3x4(v); n++; }
            }
            var actual = toBind.MultiplyPoint3x4(sum / Mathf.Max(n, 1));
            // эталон: центроид из Blender в репере суставов; при переходе в левостороннюю
            // систему Unity третья ось репера (векторное произведение) меняет знак
            var u = fb.inverse.MultiplyPoint3x4(ToV(item.centroid)) / lenB;
            var expected = fc.MultiplyPoint3x4(new Vector3(u.x, u.y, -u.z) * lenC);
            float errMm = (actual - expected).magnitude / lenC * lenB * 1000f;
            bool pass = errMm <= VerifyToleranceMm;
            ok &= pass;
            sb.AppendLine($"{(pass ? "OK  " : "FAIL")} {item.id,-18} bone={item.bone,-12} error={errMm,7:F2} mm parts={parts.Count}");
            rig.Unequip(item.slot);
        }

        // визуальное доказательство: Финик в текущей позе (Idle) с комплектом
        if (!Application.isBatchMode || SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
        {
            foreach (var id in PreviewOutfit)
                foreach (var part in rig.Equip(id))
                {
                    var mat = part.GetComponent<MeshRenderer>().sharedMaterial;
                    sb.AppendLine($"  {part.name}: {(mat ? $"{mat.name} [{mat.shader.name}] base={mat.GetColor("_BaseColor")}" : "НЕТ МАТЕРИАЛА")}");
                }
            foreach (var file in RenderPreviews(skin, "finik_unity_preview"))
                sb.AppendLine("preview: " + file);
        }
        UnityEngine.Object.DestroyImmediate(rig);
        sb.AppendLine(ok ? "RESULT: PASS" : "RESULT: FAIL");
        return sb.ToString();
    }

    static readonly string[] PreviewOutfit = { "gear_cap", "glasses_nerd", "backpack_school", "gear_watch", "gear_badge" };

    static List<string> RenderPreviews(SkinnedMeshRenderer skin, string prefix)
    {
        var root = skin.transform.root;
        var head = skin.bones.First(b => b != null && b.name == "Head");
        var front = skin.bones.FirstOrDefault(b => b != null && b.name == "headfront");
        var fwd = front != null ? Vector3.ProjectOnPlane(front.position - head.position, Vector3.up).normalized : root.forward;
        var body = skin.bounds;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            if (r.name.StartsWith("Acc_", StringComparison.Ordinal))
                body.Encapsulate(r.bounds);
        float h = body.size.y;

        // комната не должна загораживать камеру: на время съёмки прячем всё, кроме персонажа
        var hidden = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                                .Where(r => r.enabled && !r.transform.IsChildOf(root)).ToList();
        hidden.ForEach(r => r.enabled = false);
        // первый кадр иначе снимается, пока URP-шейдеры компилируются асинхронно (заглушки)
        bool wasAsync = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        try
        {
            File.Delete(Shot(prefix + "_warmup.png", body.center, body.extents.magnitude, fwd, 25f));
            return new List<string>
            {
                Shot(prefix + "_full.png", body.center, body.extents.magnitude, fwd, 25f),
                Shot(prefix + "_back.png", body.center, body.extents.magnitude, fwd, 200f),
                Shot(prefix + "_head.png", head.position + Vector3.up * h * 0.10f, h * 0.20f, fwd, 20f),
            };
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = wasAsync;
            hidden.ForEach(r => r.enabled = true);
        }
    }

    static string Shot(string file, Vector3 center, float radius, Vector3 fwd, float yaw)
    {
        const int size = 1024;
        var go = new GameObject("FinikPreviewCamera");
        var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
        var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.94f, 0.92f, 0.89f);
            cam.fieldOfView = 30f;
            float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            cam.transform.position = center + Quaternion.AngleAxis(yaw, Vector3.up) * fwd * dist + Vector3.up * radius * 0.15f;
            cam.transform.LookAt(center);
            cam.nearClipPlane = dist * 0.05f;
            cam.farClipPlane = dist * 4f;
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            var path = Path.Combine(Path.GetTempPath(), file);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            return path;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }

    static Matrix4x4 BindWorld(SkinnedMeshRenderer skin, string bone)
    {
        int i = Array.FindIndex(skin.bones, b => b != null && b.name == bone);
        if (i < 0) throw new InvalidOperationException($"Нет кости {bone}.");
        return skin.transform.localToWorldMatrix * skin.sharedMesh.bindposes[i].inverse;
    }

    static Vector3 BindPos(SkinnedMeshRenderer skin, string bone) => BindWorld(skin, bone).GetColumn(3);

    static Matrix4x4 Frame(Vector3[] j)
    {
        var y = (j[1] - j[0]).normalized;
        var x = Vector3.ProjectOnPlane(j[2] - j[0], y).normalized;
        var z = Vector3.Cross(x, y);
        var m = Matrix4x4.identity;
        m.SetColumn(0, x); m.SetColumn(1, y); m.SetColumn(2, z);
        m.SetColumn(3, new Vector4(j[0].x, j[0].y, j[0].z, 1f));
        return m;
    }

    static Vector3 ToV(float[] a) => new Vector3(a[0], a[1], a[2]);
}
