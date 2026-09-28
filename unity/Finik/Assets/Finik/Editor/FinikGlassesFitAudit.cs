using System;
using System.IO;
using System.Linq;
using System.Text;
using Finik.Accessories;
using Finik.Navigation;
using UnityEditor;
using UnityEngine;

namespace Finik.Editor
{
    public static class FinikGlassesFitAudit
    {
        public static string ValidatePoseStability(bool headwearAndWrist = false)
        {
            var source = UnityEngine.Object.FindFirstObjectByType<FinikCharacterSwitcher>(FindObjectsInactive.Include);
            var entries = new SerializedObject(source).FindProperty("characters");
            var catalog = source.GetComponent<FinikAccessoryRig>().Catalog;
            int checkedFits = 0;
            for (int i = 0; i < entries.arraySize; i++)
            {
                var original = (GameObject)entries.GetArrayElementAtIndex(i).FindPropertyRelative("root").objectReferenceValue;
                var preview = new PreviewRenderUtility();
                try
                {
                    var host = new GameObject("GlassesStabilityAudit");
                    preview.AddSingleGO(host);
                    var visual = UnityEngine.Object.Instantiate(original, host.transform, false);
                    visual.name = original.name;
                    visual.SetActive(true);
                    foreach (var behaviour in visual.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
                    var skin = visual.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(s => s.sharedMesh.vertexCount).First();
                    var head = skin.bones.First(b => b && b.name.Split(':').Last().Equals("Head", StringComparison.OrdinalIgnoreCase));
                    var hips = skin.bones.First(b => b && b.name.Split(':').Last().Equals("Hips", StringComparison.OrdinalIgnoreCase));
                    Quaternion headStart = head.localRotation, hipsStart = hips.localRotation;
                    Quaternion rootStart = visual.transform.localRotation;
                    var rig = host.AddComponent<FinikAccessoryRig>();
                    rig.Catalog = catalog;
                    var forearm = skin.bones.First(b => b && b.name.Split(':').Last() == "LeftForeArm");
                    Quaternion forearmStart = forearm.localRotation;
                    foreach (var entry in catalog.entries.Where(e => headwearAndWrist
                        ? (e.slot == "cap" && (original.name == "Cat_St2" || original.name == "Racoon" || original.name.EndsWith("_St3"))) ||
                          (e.slot == "watch" && (original.name.EndsWith("_St2") || original.name.EndsWith("_St3") || original.name == "Racoon"))
                        : e.slot == "glasses"))
                    {
                        forearm.localRotation = forearmStart;
                        head.localRotation = headStart;
                        hips.localRotation = hipsStart;
                        visual.transform.localRotation = rootStart;
                        var baseline = rig.Equip(entry.id).Select(p => Matrix4x4.TRS(p.transform.localPosition, p.transform.localRotation, p.transform.localScale)).ToArray();
                        head.localRotation = headStart * Quaternion.Euler(23, -31, 17);
                        hips.localRotation = hipsStart * Quaternion.Euler(8, 12, -5);
                        visual.transform.localRotation = rootStart * Quaternion.Euler(0, 47, 0);
                        forearm.localRotation = forearmStart * Quaternion.Euler(35, 22, -48);
                        var repeated = rig.Equip(entry.id);
                        for (int p = 0; p < baseline.Length; p++)
                        {
                            var t = repeated[p].transform;
                            var actual = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
                            float magnitude = Mathf.Max(.001f, ((Vector3)baseline[p].GetColumn(0)).magnitude);
                            for (int n = 0; n < 16; n++)
                                if (Mathf.Abs(baseline[p][n] - actual[n]) > magnitude * .0001f)
                                    throw new InvalidOperationException($"Unstable glasses fit: {original.name}/{entry.id}, matrix element {n}");
                        }
                        checkedFits++;
                    }
                }
                finally { preview.Cleanup(); }
            }
            return $"PASS: {checkedFits} fits keep the same bone-local matrix after head, forearm, hips and root rotations.";
        }

        public static string Render(string label = "before", string accessory = "glasses_round", float angle = 0)
        {
            var source = UnityEngine.Object.FindFirstObjectByType<FinikCharacterSwitcher>(FindObjectsInactive.Include);
            if (!source) throw new InvalidOperationException("Open the character scene first.");
            var entries = new SerializedObject(source).FindProperty("characters");
            var catalog = source.GetComponent<FinikAccessoryRig>().Catalog;
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../glasses-fit-audit"));
            Directory.CreateDirectory(output);
            var report = new StringBuilder();
            var atlas = new Texture2D(1152, 1152, TextureFormat.RGB24, false);
            try
            {
                for (int i = 0; i < entries.arraySize; i++)
                {
                    var entry = entries.GetArrayElementAtIndex(i);
                    string id = entry.FindPropertyRelative("id").stringValue;
                    int stage = entry.FindPropertyRelative("stage").intValue;
                    var original = (GameObject)entry.FindPropertyRelative("root").objectReferenceValue;
                    var preview = new PreviewRenderUtility();
                    try
                    {
                        var host = new GameObject("GlassesAudit");
                        preview.AddSingleGO(host);
                        var visual = UnityEngine.Object.Instantiate(original, host.transform, false);
                        visual.name = original.name;
                        visual.SetActive(true);
                        foreach (var behaviour in visual.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
                        foreach (var t in visual.GetComponentsInChildren<Transform>(true))
                            if (t.name.StartsWith("Acc_")) UnityEngine.Object.DestroyImmediate(t.gameObject);
                        var skin = visual.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(s => s.sharedMesh.vertexCount).First();
                        var head = skin.bones.First(b => b && b.name.Split(':').Last().Equals("Head", StringComparison.OrdinalIgnoreCase));
                        var rig = host.AddComponent<FinikAccessoryRig>();
                        rig.Catalog = catalog;
                        var parts = rig.Equip(accessory);
                        var bounds = skin.bounds;
                        bool wrist = accessory == "gear_watch";
                        Vector3 target = wrist
                            ? skin.bones.First(b => b && b.name.Split(':').Last() == "LeftHand").position
                            : bounds.center + visual.transform.up * bounds.size.y * .27f;
                        preview.camera.orthographic = true;
                        preview.camera.orthographicSize = bounds.size.y * (wrist ? .12f : .29f);
                        preview.camera.nearClipPlane = .01f;
                        preview.camera.farClipPlane = 1000f;
                        preview.camera.transform.position = target + Quaternion.AngleAxis(angle, visual.transform.up) * visual.transform.forward * bounds.size.y * 3;
                        preview.camera.transform.LookAt(target, visual.transform.up);
                        preview.camera.clearFlags = CameraClearFlags.SolidColor;
                        preview.camera.backgroundColor = new Color(.37f, .43f, .49f);
                        preview.lights[0].intensity = 1.3f;
                        preview.lights[0].transform.rotation = Quaternion.Euler(30, 190, 0);
                        preview.lights[1].intensity = .8f;
                        preview.ambientColor = Color.gray;
                        preview.BeginStaticPreview(new Rect(0, 0, 384, 384));
                        preview.Render(true);
                        var image = preview.EndStaticPreview();
                        File.WriteAllBytes(Path.Combine(output, $"{label}-{id}-{stage}.png"), image.EncodeToPNG());
                        int col = id == "fox" ? 0 : id == "cat" ? 1 : 2;
                        atlas.SetPixels(col * 384, (3 - stage) * 384, 384, 384, image.GetPixels());
                        UnityEngine.Object.DestroyImmediate(image);
                        report.AppendLine($"{id}/{stage} root={visual.name} head={head.localPosition} mesh={skin.name}");
                        var hips = skin.bones.First(b => b && b.name.Split(':').Last().Equals("Hips", StringComparison.OrdinalIgnoreCase));
                        float length = Vector3.Distance(head.position, hips.position);
                        var lens = (parts.FirstOrDefault(p => p.name.Contains("Lens")) ?? parts.First()).GetComponent<Renderer>();
                        report.AppendLine($"  lensScreen={preview.camera.WorldToViewportPoint(lens.bounds.center).ToString("F5")} lensSize={lens.bounds.size.ToString("F5")} length={length:F5} pixelsPerLength={384f * length / (2 * preview.camera.orthographicSize):F3}");
                        foreach (var b in skin.bones.Where(b => b && (b.name.ToLowerInvariant().Contains("eye") || b.name.ToLowerInvariant().Contains("head"))))
                            report.AppendLine($"  {b.name}: head-local {head.InverseTransformPoint(b.position).ToString("F5")}");
                        foreach (var part in parts)
                            report.AppendLine($"  {part.name}: {part.transform.localPosition.ToString("F5")} scale {part.transform.localScale.ToString("F5")}");
                    }
                    finally { preview.Cleanup(); }
                }
                atlas.Apply();
                File.WriteAllBytes(Path.Combine(output, $"{label}-all.png"), atlas.EncodeToPNG());
                File.WriteAllText(Path.Combine(output, $"{label}-report.txt"), report.ToString());
            }
            finally { UnityEngine.Object.DestroyImmediate(atlas); }
            return output + "\n" + report;
        }
    }
}
