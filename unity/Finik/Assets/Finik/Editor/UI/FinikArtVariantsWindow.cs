using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Switch between generated art variants to compare them in the real UI. Variants are sliced by
    /// tools/ui/slice_ui_sheets.py into art/ui/variants/&lt;set&gt;/&lt;variant&gt;/ under the sprite names the game
    /// uses; applying one copies its files into Assets/Finik/UI/Art and runs Rebuild All UI. Before the
    /// first switch the art in use is saved as variant "0", so the original is always one click away.
    /// </summary>
    public sealed class FinikArtVariantsWindow : EditorWindow
    {
        const string ArtFolder = "Assets/Finik/UI/Art";
        const string OriginalVariant = "0";

        static string VariantsRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "art", "ui", "variants"));

        Vector2 scroll;
        string status;

        [MenuItem("Finik/UI/Art Variants")]
        static void Open() => GetWindow<FinikArtVariantsWindow>("Art Variants");

        void OnGUI()
        {
            if (!Directory.Exists(VariantsRoot))
            {
                EditorGUILayout.HelpBox($"No variants yet: {VariantsRoot}\nSlice them with python tools/ui/slice_ui_sheets.py.", MessageType.Info);
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorGUILayout.HelpBox("Stop Play Mode to switch: the UI is rebuilt into the scene.", MessageType.Warning);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (string setDir in Directory.GetDirectories(VariantsRoot).OrderBy(d => d, StringComparer.Ordinal))
            {
                string set = Path.GetFileName(setDir);
                string current = EditorPrefs.GetString(PrefKey(set), OriginalVariant);
                EditorGUILayout.LabelField(set, EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Sprites: " + string.Join(", ", SpriteNames(setDir)), EditorStyles.miniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                    {
                        foreach (string variant in Variants(setDir))
                        {
                            string label = variant == OriginalVariant ? "0 (исходный)" : variant;
                            var style = variant == current ? EditorStyles.toolbarButton : EditorStyles.miniButton;
                            if (GUILayout.Button(variant == current ? $"● {label}" : label, style, GUILayout.MinWidth(70)))
                                status = Apply(set, variant);
                        }
                    }
                }
                EditorGUILayout.Space();
            }
            EditorGUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.None);
        }

        /// <summary>Copies the variant's sprites into the art folder and rebuilds every UI screen.</summary>
        public static string Apply(string set, string variant)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Stop Play Mode first.";
            string setDir = Path.Combine(VariantsRoot, set);
            string source = Path.Combine(setDir, variant);
            if (!Directory.Exists(source)) return $"Unknown variant {set}/{variant}.";
            SaveOriginal(setDir);

            int copied = 0;
            foreach (string file in Directory.GetFiles(source, "*.png"))
            {
                File.Copy(file, Path.Combine(ArtFolder, Path.GetFileName(file)), overwrite: true);
                copied++;
            }
            // A sprite the original never had in the art folder (a procedural placeholder) goes away again.
            if (variant == OriginalVariant)
                foreach (string name in SpriteNames(setDir))
                    if (!File.Exists(Path.Combine(source, name + ".png")))
                        AssetDatabase.DeleteAsset($"{ArtFolder}/{name}.png");

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorPrefs.SetString(PrefKey(set), variant);
            string rebuilt = FinikRebuildAllUi.Build();
            Debug.Log($"[ArtVariants] {set} → {variant}: {copied} sprites.\n{rebuilt}");
            return $"{set} → {variant}: {copied} sprites, UI rebuilt.";
        }

        /// <summary>Keeps the art in use before the first switch as variant 0.</summary>
        static void SaveOriginal(string setDir)
        {
            string original = Path.Combine(setDir, OriginalVariant);
            if (Directory.Exists(original)) return;
            Directory.CreateDirectory(original);
            foreach (string name in SpriteNames(setDir))
            {
                string art = Path.Combine(ArtFolder, name + ".png");
                if (File.Exists(art)) File.Copy(art, Path.Combine(original, name + ".png"));
            }
        }

        static string[] Variants(string setDir) => Directory.GetDirectories(setDir)
            .Select(Path.GetFileName)
            .OrderBy(v => int.TryParse(v, out int n) ? n : int.MaxValue).ThenBy(v => v, StringComparer.Ordinal)
            .ToArray();

        /// <summary>Every sprite name any variant of the set provides.</summary>
        static string[] SpriteNames(string setDir) => Directory.GetDirectories(setDir)
            .SelectMany(d => Directory.GetFiles(d, "*.png"))
            .Select(Path.GetFileNameWithoutExtension)
            .Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();

        static string PrefKey(string set) => "Finik.ArtVariant." + set;
    }
}
