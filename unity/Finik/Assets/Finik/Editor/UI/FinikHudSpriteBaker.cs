using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using static Finik.Editor.UI.FinikSdfCanvas;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Bakes every HUD sprite procedurally at 2 px per UI unit. Icons are placeholders: dropping a PNG
    /// with the same name into <see cref="OverrideFolder"/> replaces the generated one.
    /// </summary>
    public static class FinikHudSpriteBaker
    {
        public const string GeneratedFolder = "Assets/Finik/UI/Generated";
        public const string OverrideFolder = "Assets/Finik/UI/Art";
        /// <summary>Accessory previews rendered from the 3D models (Finik/UI/Render Accessory Thumbnails).</summary>
        public const string ThumbnailFolder = "Assets/Finik/UI/Thumbnails";
        /// <summary>Texture pixels per UI unit. Images use pixelsPerUnitMultiplier = this.</summary>
        public const float PixelsPerUnit = 2f;

        static readonly Color White = Color.white;
        static readonly Dictionary<string, float> GeneratedHeights = new();

        /// <summary>
        /// Multiplier that keeps a sprite at the generated design scale. Hand-made overrides can have
        /// any resolution; they are scaled so their borders match the generated placeholder's.
        /// </summary>
        public static float PixelsPerUnitFor(string name, Sprite sprite)
        {
            if (!sprite || !GeneratedHeights.TryGetValue(name, out float generated) || generated <= 0f) return PixelsPerUnit;
            return PixelsPerUnit * sprite.rect.height / generated;
        }

        sealed class Spec
        {
            public string name;
            public int width;
            public int height;
            public Vector4 border; // left, bottom, right, top (px)
            public bool mipmaps;
            public Action<FinikSdfCanvas> draw;
        }

        public static Dictionary<string, Sprite> BakeAll()
        {
            Directory.CreateDirectory(GeneratedFolder);
            // Pick up art dropped into the override folder since the last import (e.g. by the slicer).
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var specs = BuildSpecs();
            var written = new List<(Spec spec, string path)>();
            // Drop sprites from earlier bakes that no spec produces any more.
            var expected = new HashSet<string>(specs.ConvertAll(spec => $"{GeneratedFolder}/{spec.name}.png"));
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { GeneratedFolder }))
            {
                string stale = AssetDatabase.GUIDToAssetPath(guid);
                if (!expected.Contains(stale)) AssetDatabase.DeleteAsset(stale);
            }
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var spec in specs)
                {
                    var canvas = new FinikSdfCanvas(spec.width, spec.height);
                    spec.draw(canvas);
                    var texture = canvas.ToTexture();
                    string path = $"{GeneratedFolder}/{spec.name}.png";
                    File.WriteAllBytes(path, texture.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(texture);
                    // Written behind the asset database's back: without this a brand-new sprite has no
                    // importer yet (the first rebuild after adding one failed), and a changed one keeps
                    // its old pixels until something else triggers a refresh. Queued in the batch.
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    written.Add((spec, path));
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            // Import settings first, as one batch, and load afterwards: an asset touched inside a
            // batch only becomes loadable once StopAssetEditing has run the imports.
            ConfigureAll(written.ConvertAll(w => (w.path, w.spec.border, w.spec.mipmaps)));
            var sprites = new Dictionary<string, Sprite>();
            foreach (var (spec, path) in written)
            {
                sprites[spec.name] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                GeneratedHeights[spec.name] = spec.height;
            }

            // Real art wins over generated placeholders: accessory renders first, then hand-made art,
            // so a hand-drawn icon can still replace the rendered preview of the same accessory.
            Adopt(sprites, ThumbnailFolder);
            Adopt(sprites, OverrideFolder);
            return sprites;
        }

        /// <summary>Takes every texture in <paramref name="folder"/> into the sprite table under its file name.</summary>
        static void Adopt(Dictionary<string, Sprite> sprites, string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return;
            var adopted = new List<(string name, string path, Vector4 border, bool mipmaps)>();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.GetSourceTextureWidthAndHeight(out _, out int height);
                Vector4 border = Vector4.zero;
                if (sprites.TryGetValue(name, out var generated) && generated && GeneratedHeights.TryGetValue(name, out float generatedHeight))
                    border = generated.border * (height / generatedHeight);
                adopted.Add((name, path, border, mipmaps: true));
            }

            ConfigureAll(adopted.ConvertAll(a => (a.path, a.border, a.mipmaps)));
            foreach (var (name, path, _, _) in adopted) sprites[name] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// Applies the sprite settings to every path inside a single import batch. Without the batch
        /// each SaveAndReimport triggers its own asset pipeline refresh, ~55 ms a piece.
        /// </summary>
        public static void ConfigureAll(IEnumerable<(string path, Vector4 border, bool mipmaps)> items)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var (path, border, mipmaps) in items) ConfigureSprite(path, border, mipmaps);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        /// <summary>Returns true when the importer had to be rewritten and reimported.</summary>
        public static bool ConfigureSprite(string path, Vector4 border, bool mipmaps)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!importer) throw new FileNotFoundException("Нет импортированной текстуры для спрайта", path);
            int maxSize = MaxSizeFor(path);
            if (Matches(importer, border, mipmaps, maxSize)) return false;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.spriteBorder = border;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = mipmaps;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = maxSize;
            importer.SaveAndReimport();
            return true;
        }

        /// <summary>
        /// Full-screen pictures that are shown far bigger than an icon: capped at 1024 px they come out
        /// soft once stretched over a phone-tall board.
        /// </summary>
        static readonly HashSet<string> LargeArt = new() { "game_catch_bg" };

        static int MaxSizeFor(string path) => LargeArt.Contains(Path.GetFileNameWithoutExtension(path)) ? 2048 : 1024;

        /// <summary>
        /// True when the importer already carries the settings below. Rebuilds run over every HUD
        /// sprite, and re-saving an unchanged importer costs a full reimport for nothing.
        /// </summary>
        static bool Matches(TextureImporter importer, Vector4 border, bool mipmaps, int maxSize) =>
            importer.textureType == TextureImporterType.Sprite
            && importer.spriteImportMode == SpriteImportMode.Single
            && Mathf.Approximately(importer.spritePixelsPerUnit, 100f)
            && importer.spriteBorder == border
            && importer.alphaIsTransparency
            && importer.mipmapEnabled == mipmaps
            && importer.wrapMode == TextureWrapMode.Clamp
            && importer.filterMode == FilterMode.Bilinear
            && importer.textureCompression == TextureImporterCompression.Uncompressed
            && importer.maxTextureSize == maxSize;

        // ------------------------------------------------------------------ specs

        static List<Spec> BuildSpecs()
        {
            var list = new List<Spec>
            {
                new() { name = "hud_pill_dark", width = 320, height = 152, border = Vector4.one * 76, draw = c => DarkGlass(c, 76) },
                new() { name = "hud_panel_dark", width = 240, height = 240, border = Vector4.one * 80, draw = c => DarkGlass(c, 72) },
                new() { name = "hud_bar_track", width = 200, height = 68, border = Vector4.one * 34, draw = BarTrack },
                new() { name = "hud_bar_fill", width = 200, height = 52, border = Vector4.one * 26, draw = BarFill },
                // XP bar of the level block: placeholders with the 9-slice borders the sliced art inherits.
                new() { name = "hud_xp_track", width = 200, height = 68, border = Vector4.one * 34, draw = BarTrack },
                new() { name = "hud_xp_fill", width = 200, height = 52, border = Vector4.one * 26, draw = BarFill },
                new() { name = "hud_glow", width = 560, height = 432, border = Vector4.one * 160, draw = c => c.Glow(RoundBox(new Rect(40, 40, 480, 352), 108), White, 38) },
                // Soft round halo behind the fridge bubble while Finik is hungry.
                new() { name = "hud_glow_round", width = 256, height = 256, mipmaps = true, draw = c => c.Glow(Circle(c.Center, 64), White, 56) },
                new() { name = "hud_btn_round", width = 200, height = 200, draw = RoundDarkButton },
                new() { name = "hud_btn_round_light", width = 200, height = 200, mipmaps = true, draw = RoundLightButton },
                new() { name = "hud_btn_plus", width = 120, height = 120, draw = PlusButton },
                // Pointer of the room hints (fridge, desk): points down, the hint turns it to its object.
                new() { name = "hud_pointer", width = 96, height = 96, mipmaps = true, draw = Pointer },
                // Tap hint (FinikTapHint): a gloved hand pointing up and the ripple under its fingertip.
                new() { name = "hud_hand", width = 256, height = 256, mipmaps = true, draw = Hand },
                new() { name = "hud_tap_ring", width = 256, height = 256, mipmaps = true, draw = TapRing },
                new() { name = "hud_gauge_disc", width = 256, height = 256, mipmaps = true, draw = GaugeDisc },
                new() { name = "hud_ring_track", width = 256, height = 256, mipmaps = true, draw = c => c.Fill(Stroke(Circle(c.Center, RingRadius), RingHalfWidth + 2), Hex("#22306E", 0.3f)) },
                new() { name = "hud_ring_fill", width = 256, height = 256, mipmaps = true, draw = RingFill },
                new() { name = "hud_badge", width = 72, height = 72, mipmaps = true, draw = Badge },
                Icon("icon_star", Star),
                Icon("icon_coin", Coin),
                Icon("icon_piggy_coin", c => Piggy(c, true)),
                Icon("icon_food", Food),
                Icon("icon_mood", Mood),
                Icon("icon_energy", Energy),
                Icon("icon_mail", Mail),
                Icon("icon_settings", Settings),
                Icon("icon_backpack", c => Sticker(c, RoundBox(new Rect(52, 30, 152, 176), 48), Hex("#12307A"), 8, Hex("#5AA2FF"), Hex("#1F5FD6"), 206, 30)),
                // Food screen: neutral placeholders until food_* / scene_* art is sliced in. They must not
                // hint at whether an option is smart or a trap.
                Icon("food_placeholder", c =>
                {
                    CandyCircle(c, 116, Hex("#FFFFFF"), 8, Hex("#C9641A"), 10, Hex("#FFD66B"), Hex("#FF9A2E"));
                    Food(c);
                }),
                Icon("scene_placeholder", c =>
                {
                    CandyCircle(c, 116, Hex("#FFFFFF"), 8, Hex("#1F5FD6"), 10, Hex("#9AD6FF"), Hex("#3FA7FF"));
                    var star = FinikSdfCanvas.Star(c.Center + new Vector2(0, 2), 62, 28, 5);
                    c.Fill(Offset(star, new Vector2(0, -5)), Hex("#0B1030", 0.25f), 2f);
                    c.Fill(star, Vertical(Hex("#FFFFFF"), Hex("#DCE6FF"), 190, 66));
                }),
                // Quests: neutral picture until quest_* art is sliced in, the board icon, the red flag
                // mark and the plain prize of the mystery box.
                Icon("quest_placeholder", c =>
                {
                    CandyCircle(c, 116, Hex("#FFFFFF"), 8, Hex("#6A34C9"), 10, Hex("#C9A8FF"), Hex("#8C5CF0"));
                    QuestMark(c);
                }),
                // Shop: neutral shopping bag until shop_* art is sliced in. Like the food placeholder it
                // must not hint at whether the thing on the shelf is a need or a want.
                Icon("shop_placeholder", c =>
                {
                    CandyCircle(c, 116, Hex("#FFFFFF"), 8, Hex("#1F5FD6"), 10, Hex("#9AD6FF"), Hex("#3FA7FF"));
                    // Handle first: the bag's body covers where it enters the sack.
                    var handle = Subtract(Stroke(Circle(new Vector2(128, 152), 32), 9), RoundBox(new Rect(0, 0, 256, 152), 0));
                    c.Fill(handle, Hex("#F4F6FF"));
                    var bag = RoundBox(new Rect(78, 62, 100, 92), 20);
                    c.Fill(Offset(bag, new Vector2(0, -6)), Hex("#0B1030", 0.25f), 2f);
                    c.Fill(Grow(bag, 5), Hex("#1F5FD6"));
                    c.Fill(bag, Vertical(Hex("#FFFFFF"), Hex("#DCE6FF"), 154, 62));
                }),
                Icon("icon_quest", c =>
                {
                    var board = RoundBox(new Rect(46, 30, 164, 196), 30);
                    Sticker(c, board, Hex("#3B1E8A"), 8, Hex("#B993FF"), Hex("#7B4DE8"), 226, 30);
                    QuestMark(c);
                }),
                Icon("quest_flag", c =>
                {
                    var pole = Capsule(new Vector2(78, 38), new Vector2(78, 222), 11);
                    var flag = Polygon(new Vector2(84, 222), new Vector2(206, 178), new Vector2(84, 128));
                    c.Fill(Offset(Union(pole, flag), new Vector2(0, -6)), Hex("#000000", 0.25f), 3f);
                    c.Fill(pole, Vertical(Hex("#F4F6FF"), Hex("#B9C3E6"), 222, 38));
                    c.Fill(Grow(flag, 6), Hex("#8E1330"));
                    c.Fill(flag, Vertical(Hex("#FF6B7F"), Hex("#E0283F"), 222, 128));
                }),
                Icon("quest_tag", c =>
                {
                    var tag = Subtract(RoundBox(new Rect(40, 70, 176, 116), 30), Circle(new Vector2(78, 128), 14));
                    Sticker(c, tag, Hex("#5B6488"), 8, Hex("#F2F4FA"), Hex("#C3CADF"), 186, 70, 0.4f);
                }),
                new() { name = "avatar_finik", width = 320, height = 320, mipmaps = true, draw = c => AvatarFallback(c, "#2E7BF6") },
                new() { name = "avatar_cat", width = 320, height = 320, mipmaps = true, draw = c => AvatarFallback(c, "#F2649B") },
                new() { name = "avatar_raccoon", width = 320, height = 320, mipmaps = true, draw = c => AvatarFallback(c, "#34B866") },
                // Onboarding kit (placeholders until the AI-generated sheet is sliced in).
                WideButton("ui_btn_green", "#FFFFFF", "#8CF29A", "#2EBE5A", "#17803F"),
                WideButton("ui_btn_orange", "#FFFFFF", "#FFD66B", "#FF9A2E", "#C9641A"),
                WideButton("ui_btn_white", "#BFD4FF", "#FFFFFF", "#DCE6FF", "#9FB2E0"),
                WideButton("ui_btn_red", "#FFFFFF", "#FF9FA3", "#E5484D", "#A8232B"),
                new() { name = "ui_card", width = 240, height = 240, border = Vector4.one * 84, draw = Card },
                // Soft colour variants of ui_card for the Нужно / Хочу / Коплю cards (art:
                // tools/ui/recolor_ui_card.py); the placeholders only carry the 9-slice border.
                new() { name = "ui_card_needs", width = 240, height = 240, border = Vector4.one * 84, draw = Card },
                new() { name = "ui_card_wants", width = 240, height = 240, border = Vector4.one * 84, draw = Card },
                new() { name = "ui_card_savings", width = 240, height = 240, border = Vector4.one * 84, draw = Card },
                // Home screen «Задание дня» card and its reward chip (same recolor script).
                new() { name = "ui_card_task", width = 240, height = 240, border = Vector4.one * 84, draw = Card },
                WideButton("ui_pill_gold", "#E0B23A", "#FFF6D0", "#FFE7A0", "#D9A520"),
                // Shop kit: the shelf's own furniture, built on the same candy construction as the
                // app's buttons — that shape already reads well, while the plain white Card() the
                // panels used did not. Generated UI sheets kept coming back as stock neon, so these
                // stay procedural: they are exact, symmetric and stretch cleanly as 9-slices.
                new() { name = "shop_panel", width = 480, height = 320, border = Vector4.one * 96,
                    draw = c => CandyPiece(c, 84, "#3F7FB4", "#5FA0D2", "#A8D6F5", "#74B6E4", rim: 9, lipHeight: 18, gloss: 0.28f) },
                new() { name = "shop_tile", width = 320, height = 260, border = new Vector4(72, 96, 72, 72),
                    draw = c =>
                    {
                        CandyPiece(c, 58, "#8FBEE4", "#C6DFF4", "#FFFFFF", "#EDF5FD", rim: 5, lipHeight: 12, gloss: 0.5f);
                        // Footer band: the strip the price sits on, so the foot of the tile reads as its own row.
                        var faceRect = new Rect(5, 17, c.Width - 10, c.Height - 22);
                        var face = RoundBox(faceRect, 53);
                        float bandTop = faceRect.yMin + 84f;
                        c.Fill(Intersect(face, p => p.y - bandTop), Hex("#DCEBF9"));
                        c.Fill(Intersect(face, p => Mathf.Abs(p.y - bandTop) - 1.2f), Hex("#B9D6EE"));
                    } },
                new() { name = "shop_tile_frame", width = 320, height = 260, border = new Vector4(72, 96, 72, 72),
                    draw = c =>
                    {
                        var body = RoundBox(new Rect(4, 8, c.Width - 8, c.Height - 14), 58);
                        c.Fill(Stroke(Grow(body, -1f), 7f), Hex("#FF8A1F"));
                        c.Fill(Stroke(Grow(body, -8f), 2.5f), Hex("#FFD79B", 0.9f));
                    } },
                new() { name = "shop_tab_on", width = 420, height = 140, border = Vector4.one * 62,
                    draw = c => CandyPiece(c, 58, "#C96A12", "#FFC65C", "#FFD27A", "#FF9A2E", rim: 6, lipHeight: 15) },
                new() { name = "shop_tab_off", width = 420, height = 140, border = Vector4.one * 62,
                    draw = c => CandyPiece(c, 58, "#7FB2DC", "#DCEDFB", "#FFFFFF", "#D3E6F8", rim: 6, lipHeight: 15, gloss: 0.4f) },
                new() { name = "shop_price", width = 260, height = 110, border = Vector4.one * 52,
                    draw = c => CandyPiece(c, 50, "#C9931A", "#FFE08A", "#FFF0BC", "#FFC94D", rim: 5, lipHeight: 13) },
                // Soft drop shadow with ui_card's geometry and 9-slice border, so it can be laid under a
                // card and stretched with it. Without it every panel and tile sits in the same plane.
                new() { name = "ui_card_shadow", width = 240, height = 240, border = Vector4.one * 84,
                    draw = c => c.Glow(RoundBox(new Rect(34, 34, c.Width - 68, c.Height - 68), 50), Hex("#0B1030", 0.3f), 30f) },
                // Selection frame drawn over ui_card: same geometry and 9-slice border, so it hugs the card.
                new() { name = "ui_card_selected", width = 240, height = 240, border = Vector4.one * 84,
                    draw = c => c.Fill(Stroke(Grow(RoundBox(new Rect(4, 10, c.Width - 8, c.Height - 14), 64), 1f), 5f), Hex("#FF8A1F")) },
                new() { name = "ui_input", width = 320, height = 112, border = Vector4.one * 44, draw = InputField },
                new() { name = "ui_bubble", width = 320, height = 220, border = new Vector4(120, 96, 64, 64), draw = Bubble },
                new() { name = "ui_check", width = 96, height = 96, mipmaps = true, draw = Check },

                // The mood games have a UI kit of their own, apart from the app's: a board is a place to
                // play, and the app's cards read as a settings screen behind one. These specs fix the
                // shapes and their 9-slice borders; art dropped into UI/Art replaces the look and
                // inherits the borders from here.
                new() { name = "gameui_bg", width = 256, height = 256, mipmaps = true, draw = GameUiBackdrop },
                new() { name = "gameui_board", width = 320, height = 320, border = Vector4.one * 100, draw = GameUiBoard },
                new() { name = "gameui_bar", width = 360, height = 120, border = new Vector4(58, 48, 58, 48), draw = GameUiBar },
                new() { name = "gameui_header", width = 360, height = 120, border = new Vector4(58, 48, 58, 48), draw = GameUiHeader },
                new() { name = "gameui_chip", width = 240, height = 110, border = new Vector4(52, 48, 52, 48), draw = GameUiChip },
                new() { name = "gameui_dock", width = 320, height = 220, border = new Vector4(96, 90, 96, 60), draw = GameUiDock },
                new() { name = "gameui_badge", width = 96, height = 96, mipmaps = true, draw = GameUiBadge },
                new() { name = "gameui_close", width = 160, height = 160, mipmaps = true, draw = GameUiClose },
                new() { name = "gameui_slot", width = 160, height = 160, mipmaps = true, draw = GameUiSlot },
                new() { name = "gameui_btn", width = 200, height = 200, border = Vector4.one * 62, draw = GameUiButton },
                new() { name = "game_cell", width = 128, height = 128, border = Vector4.one * 40, draw = GameCell },
                // Progress tracks: a glass one for the dark game HUD, a light one for the app's cards.
                // The HUD's own dark track read as a technical meter next to the toy-like art.
                new() { name = "gameui_track", width = 200, height = 68, border = Vector4.one * 34, draw = GameUiTrack },
                new() { name = "ui_track_light", width = 200, height = 68, border = Vector4.one * 34, draw = UiTrackLight },
                new() { name = "game_miss", width = 128, height = 128, mipmaps = true, draw = GameMiss },
                new() { name = "gameui_mask", width = 160, height = 160, border = Vector4.one * 56, draw = c => c.Fill(RoundBox(new Rect(2, 2, c.Width - 4, c.Height - 4), 50), White) },
                new() { name = "game_card_face", width = 240, height = 240, border = Vector4.one * 84, draw = GameCardFace },
                new() { name = "game_rays", width = 512, height = 512, mipmaps = true, draw = GameRays },
                new() { name = "ui_piece", width = 16, height = 24, draw = c => c.Fill(RoundBox(new Rect(0, 0, 16, 24), 3), White) },
                new() { name = "ui_dot", width = 48, height = 48, mipmaps = true, draw = c => c.Fill(Circle(c.Center, 22), White) },
                // Right-pointing chevron; mirrored in the UI for "previous".
                new() { name = "ui_chevron", width = 96, height = 96, mipmaps = true, draw = c =>
                {
                    var chevron = Union(Capsule(new Vector2(38, 22), new Vector2(64, 48), 9), Capsule(new Vector2(64, 48), new Vector2(38, 74), 9));
                    c.Fill(Offset(chevron, new Vector2(0, -3)), Hex("#0B1030", 0.25f), 2f);
                    c.Fill(chevron, Vertical(Hex("#3A5BD9"), Hex("#1F2F86"), 80, 16));
                } }
            };
            return list;
        }

        static Spec Icon(string name, Action<FinikSdfCanvas> draw) =>
            new() { name = name, width = 256, height = 256, mipmaps = true, draw = draw };

        // ------------------------------------------------------------------ panels & bars

        static void DarkGlass(FinikSdfCanvas c, float radius)
        {
            var rect = new Rect(0, 0, c.Width, c.Height);
            var body = RoundBox(rect, radius);
            c.Fill(body, Vertical(Hex("#2E3A86", 0.9f), Hex("#171C4A", 0.9f), rect.yMax, rect.yMin));
            // Soft light rim, brighter on top.
            c.Fill(Stroke(Grow(body, -2.5f), 1.6f), p => WithAlpha(White, Mathf.Lerp(0.08f, 0.3f, p.y / c.Height)));
            // Glass sheen on the upper third.
            var sheen = Intersect(Grow(body, -6f), Ellipse(new Vector2(c.Width * 0.5f, c.Height * 1.05f), new Vector2(c.Width * 0.7f, c.Height * 0.38f)));
            c.Fill(sheen, Vertical(WithAlpha(White, 0.13f), WithAlpha(White, 0.02f), c.Height, c.Height * 0.62f), 2f);
        }

        static void BarTrack(FinikSdfCanvas c)
        {
            var rect = new Rect(0, 0, c.Width, c.Height);
            var body = RoundBox(rect, c.Height * 0.5f);
            c.Fill(body, Hex("#0A0E2C", 0.92f));
            // Inner shadow on the top edge + faint light lip at the bottom = recessed groove.
            c.Fill(Intersect(Stroke(Grow(body, -3f), 3f), p => c.Height * 0.5f - p.y), Hex("#000000", 0.4f));
            c.Fill(Intersect(Stroke(Grow(body, -3f), 2f), p => p.y - c.Height * 0.35f), Hex("#FFFFFF", 0.12f));
        }

        static void BarFill(FinikSdfCanvas c)
        {
            var rect = new Rect(0, 0, c.Width, c.Height);
            var body = RoundBox(rect, c.Height * 0.5f);
            c.Fill(body, Vertical(Hex("#FFFFFF"), Hex("#BEBEBE"), c.Height, 0));
            var gloss = Intersect(Grow(body, -5f), p => c.Height * 0.55f - p.y);
            c.Fill(gloss, WithAlpha(White, 0.55f), 2f);
            c.Fill(Stroke(Grow(body, -1f), 1.2f), Hex("#000000", 0.14f));
        }

        // ------------------------------------------------------------------ buttons

        static void CandyCircle(FinikSdfCanvas c, float radius, Color outline, float outlineWidth, Color lip, float lipHeight, Color top, Color bottom, float sheen = 0.45f)
        {
            Vector2 center = c.Center;
            c.Fill(Circle(center, radius), outline);
            float r = radius - outlineWidth;
            c.Fill(Circle(center, r), lip);
            // The face is a circle nudged up so the lip shows as a crescent at the bottom.
            float faceR = r - lipHeight * 0.5f;
            var faceCenter = center + new Vector2(0, lipHeight * 0.5f);
            var face = Circle(faceCenter, faceR);
            c.Fill(face, Vertical(top, bottom, faceCenter.y + faceR, faceCenter.y - faceR));
            var gloss = Intersect(Grow(face, -faceR * 0.1f), Ellipse(faceCenter + new Vector2(0, faceR * 0.55f), new Vector2(faceR * 0.75f, faceR * 0.45f)));
            c.Fill(gloss, Vertical(WithAlpha(White, sheen), WithAlpha(White, 0.05f), faceCenter.y + faceR, faceCenter.y + faceR * 0.1f), 1.5f);
        }

        /// <summary>Frosted light disc: colourful 3D icons read far better on it than on navy.</summary>
        static void RoundLightButton(FinikSdfCanvas c)
        {
            Vector2 center = c.Center + new Vector2(0, 4);
            c.Glow(Circle(center + new Vector2(0, -6), 88), Hex("#0B1030", 0.35f), 12);
            var disc = Circle(center, 90);
            c.Fill(disc, Vertical(Hex("#FFFFFF", 0.96f), Hex("#D6E2FF", 0.94f), center.y + 90, center.y - 90));
            c.Fill(Stroke(Grow(disc, -3f), 2.5f), p => WithAlpha(Hex("#9FB6F0"), Mathf.Lerp(0.6f, 0.1f, (p.y - center.y + 90) / 180f)));
            var gloss = Intersect(Grow(disc, -8f), Ellipse(center + new Vector2(0, 52), new Vector2(66, 34)));
            c.Fill(gloss, WithAlpha(White, 0.7f), 2f);
        }

        /// <summary>Rounded triangle in the light button's colours, tip down, base at the top edge.</summary>
        static void Pointer(FinikSdfCanvas c)
        {
            var tip = Grow(Polygon(new Vector2(26, 80), new Vector2(70, 80), new Vector2(48, 30)), 8f);
            c.Fill(Offset(tip, new Vector2(0, -3)), Hex("#0B1030", 0.3f), 3f);
            c.Fill(tip, Vertical(Hex("#FFFFFF", 0.96f), Hex("#D6E2FF", 0.94f), 88, 22));
            c.Fill(Stroke(Grow(tip, -3f), 2.5f), WithAlpha(Hex("#9FB6F0"), 0.5f));
        }

        /// <summary>
        /// White cartoon glove (placeholder for the hud_hand art), index finger up, the other
        /// fingers curled into knuckles, thumb out to the left, a blue cuff at the wrist.
        /// </summary>
        static void Hand(FinikSdfCanvas c)
        {
            var outline = Hex("#12307A");
            var hand = Union(
                Capsule(new Vector2(100, 142), new Vector2(100, 214), 22),
                RoundBox(new Rect(78, 44, 124, 112), 42),
                Circle(new Vector2(136, 150), 21),
                Circle(new Vector2(168, 146), 20),
                Circle(new Vector2(194, 134), 17),
                Capsule(new Vector2(88, 98), new Vector2(54, 128), 19));
            Sticker(c, hand, outline, 8, Hex("#FFFFFF"), Hex("#DCE6FF"), 236, 44, 0.3f);
            // Creases where the index finger leaves the fist and between the curled fingers.
            c.Fill(Capsule(new Vector2(122, 168), new Vector2(120, 132), 3.5f), outline);
            c.Fill(Capsule(new Vector2(152, 162), new Vector2(150, 132), 3.5f), outline);
            c.Fill(Capsule(new Vector2(182, 158), new Vector2(180, 130), 3.5f), outline);
            var cuff = RoundBox(new Rect(84, 12, 112, 38), 14);
            c.Fill(Grow(cuff, 6), outline);
            c.Fill(cuff, Vertical(Hex("#5AA2FF"), Hex("#1F5FD6"), 50, 12));
        }

        /// <summary>Soft ring for the tap ripple; tinted and scaled by the hint.</summary>
        static void TapRing(FinikSdfCanvas c)
        {
            c.Glow(Stroke(Circle(c.Center, 104), 6), WithAlpha(White, 0.6f), 14);
            c.Fill(Stroke(Circle(c.Center, 104), 12), White, 2f);
        }

        static void RoundDarkButton(FinikSdfCanvas c) =>
            CandyCircle(c, 98, Hex("#FFFFFF", 0.28f), 4, Hex("#10133A"), 10, Hex("#3C4796"), Hex("#1D235C"), 0.28f);

        static void PlusButton(FinikSdfCanvas c)
        {
            CandyCircle(c, 58, Hex("#FFFFFF"), 5, Hex("#17803F"), 8, Hex("#8CF29A"), Hex("#2EBE5A"));
            Vector2 p0 = c.Center + new Vector2(0, 4);
            var plus = Union(Capsule(p0 + new Vector2(-22, 0), p0 + new Vector2(22, 0), 7.5f), Capsule(p0 + new Vector2(0, -22), p0 + new Vector2(0, 22), 7.5f));
            c.Fill(Offset(plus, new Vector2(0, -3)), Hex("#0B5A2A", 0.4f), 2f);
            c.Fill(plus, White);
        }

        static void Badge(FinikSdfCanvas c) =>
            CandyCircle(c, 34, Hex("#FFFFFF"), 5, Hex("#B3121B"), 4, Hex("#FF6B6B"), Hex("#E3222C"));

        const float RingRadius = 104f;
        const float RingHalfWidth = 12f;

        static void GaugeDisc(FinikSdfCanvas c)
        {
            var disc = Circle(c.Center, 126);
            c.Fill(disc, Vertical(Hex("#2E3A86", 0.9f), Hex("#161B47", 0.9f), 252, 4));
            c.Fill(Stroke(Grow(disc, -2.5f), 1.6f), p => WithAlpha(White, Mathf.Lerp(0.08f, 0.32f, p.y / c.Height)));
        }

        /// <summary>White glossy ring, tinted per need and cut by Image.fillAmount (radial).</summary>
        static void RingFill(FinikSdfCanvas c)
        {
            var ring = Stroke(Circle(c.Center, RingRadius), RingHalfWidth);
            c.Fill(ring, Vertical(Hex("#FFFFFF"), Hex("#C8C8C8"), c.Center.y + RingRadius, c.Center.y - RingRadius));
            c.Fill(Stroke(Circle(c.Center, RingRadius + 4), 3.5f), WithAlpha(White, 0.55f), 1.5f);
        }

        static void AvatarFallback(FinikSdfCanvas c, string color)
        {
            c.Fill(Circle(c.Center, 156), Hex("#FFFFFF"));
            c.Fill(Circle(c.Center, 140), Radial(Color.Lerp(Hex(color), White, 0.6f), Hex(color), c.Center + new Vector2(-30, 40), 200));
        }

        /// <summary>
        /// The candy construction the app's buttons use, as a reusable piece: dark outline, a lip that
        /// shows as a crescent along the bottom, a face with a top-down gradient, and a soft sheen over
        /// its upper half. The shop's panel, tile, tabs and price tag are all this shape recoloured.
        /// </summary>
        static void CandyPiece(FinikSdfCanvas c, float radius, string outline, string lip, string top, string bottom,
            float rim = 6f, float lipHeight = 14f, float gloss = 0.45f)
        {
            var rect = new Rect(0, 0, c.Width, c.Height);
            c.Fill(RoundBox(rect, radius), Hex(outline));
            var inner = new Rect(rim, rim, c.Width - 2 * rim, c.Height - 2 * rim);
            c.Fill(RoundBox(inner, radius - rim), Hex(lip));
            var faceRect = new Rect(inner.x, inner.y + lipHeight, inner.width, inner.height - lipHeight);
            var face = RoundBox(faceRect, radius - rim);
            c.Fill(face, Vertical(Hex(top), Hex(bottom), faceRect.yMax, faceRect.yMin));
            if (gloss <= 0f) return;
            var sheen = Intersect(Grow(face, -8f), p => (faceRect.yMax - faceRect.height * 0.42f) - p.y);
            c.Fill(sheen, p => WithAlpha(White, gloss * Mathf.InverseLerp(faceRect.yMax - faceRect.height * 0.45f, faceRect.yMax, p.y)), 2f);
        }

        static Spec WideButton(string name, string outline, string top, string bottom, string lip) => new()
        {
            name = name,
            width = 480,
            height = 160,
            border = new Vector4(80, 88, 80, 72),
            draw = c =>
            {
                var rect = new Rect(0, 0, c.Width, c.Height);
                c.Fill(RoundBox(rect, 72), Hex(outline));
                var inner = new Rect(6, 6, c.Width - 12, c.Height - 12);
                c.Fill(RoundBox(inner, 66), Hex(lip));
                var faceRect = new Rect(inner.x, inner.y + 14, inner.width, inner.height - 14);
                var face = RoundBox(faceRect, 66);
                c.Fill(face, Vertical(Hex(top), Hex(bottom), faceRect.yMax, faceRect.yMin));
                var gloss = Intersect(Grow(face, -8f), p => (faceRect.yMax - faceRect.height * 0.42f) - p.y);
                c.Fill(gloss, p => WithAlpha(White, 0.45f * Mathf.InverseLerp(faceRect.yMax - faceRect.height * 0.45f, faceRect.yMax, p.y)), 2f);
            }
        };

        static void Card(FinikSdfCanvas c)
        {
            var body = RoundBox(new Rect(4, 10, c.Width - 8, c.Height - 14), 64);
            c.Glow(Offset(body, new Vector2(0, -6)), Hex("#0B1030", 0.25f), 10);
            c.Fill(body, Hex("#FFFFFF"));
            c.Fill(Grow(body, -7f), Vertical(Hex("#FFFFFF", 0.97f), Hex("#EAF1FF", 0.97f), c.Height, 0));
        }

        // ------------------------------------------------------------------ mood games

        /// <summary>
        /// Backdrop of a full-screen level: a deep blue that lifts towards the middle, with the corners
        /// falling away. The warm wood and gold of the kit are accents on top of it — a whole screen of
        /// cream is what made the first version shout.
        /// </summary>
        static void GameUiBackdrop(FinikSdfCanvas c)
        {
            var rect = new Rect(0, 0, c.Width, c.Height);
            // Lighter at the top than at the bottom, so the screen has a sky rather than one flat wall.
            c.Fill(RoundBox(rect, 0f), Vertical(Hex("#4A63B8"), Hex("#1E2A62"), rect.yMax, rect.yMin), 0f);
            // The pool of light the board sits in: the one extra layer that groups the middle together.
            c.Fill(Ellipse(c.Center, new Vector2(c.Width * 0.66f, c.Height * 0.5f)), WithAlpha(Hex("#8FA8F0"), 0.3f), c.Width * 0.55f);
            c.Fill(Subtract(RoundBox(rect, 0f), Ellipse(c.Center, new Vector2(c.Width * 0.85f, c.Height * 0.8f))),
                Hex("#0E1743", 0.4f), c.Width * 0.4f);
        }

        /// <summary>
        /// The board the pieces sit on: a deep blue well inside a thick golden frame, so it reads
        /// apart from the warm panel behind it instead of being blue on blue.
        /// </summary>
        static void GameUiBoard(FinikSdfCanvas c)
        {
            var rect = new Rect(12, 14, c.Width - 24, c.Height - 26);
            var body = RoundBox(rect, 58);
            c.Glow(Offset(body, new Vector2(0, -10)), Hex("#0A1236", 0.35f), 16);
            // No gold: the frame's only job is to say where the board ends. A thin cool rim, lighter
            // along the top like a lit edge, and the gems stay the brightest thing on the screen.
            c.Fill(body, Vertical(Hex("#A9BCF5"), Hex("#6275BF"), rect.yMax, rect.yMin));
            var well = RoundBox(new Rect(rect.x + 8, rect.y + 8, rect.width - 16, rect.height - 16), 50);
            c.Fill(well, Vertical(Hex("#2E3C78"), Hex("#222D60"), rect.yMax, rect.yMin));
        }

        /// <summary>Counter strip: a cream pill with a honey rim.</summary>
        static void GameUiBar(FinikSdfCanvas c)
        {
            var rect = new Rect(4, 4, c.Width - 8, c.Height - 8);
            var body = RoundBox(rect, rect.height * 0.5f);
            c.Glow(Offset(body, new Vector2(0, -4)), Hex("#3A2410", 0.22f), 8);
            c.Fill(body, Vertical(Hex("#EDA45A"), Hex("#CE8B33"), rect.yMax, rect.yMin));
            c.Fill(Grow(body, -9f), Vertical(Hex("#FFFFFF"), Hex("#FFF2DC"), rect.yMax, rect.yMin));
        }

        /// <summary>Title plate: deep blue with a gold rim and a highlight along the top.</summary>
        static void GameUiHeader(FinikSdfCanvas c)
        {
            var rect = new Rect(4, 4, c.Width - 8, c.Height - 8);
            var body = RoundBox(rect, 34);
            c.Glow(Offset(body, new Vector2(0, -5)), Hex("#2A1A06", 0.25f), 9);
            c.Fill(body, Vertical(Hex("#F3B53F"), Hex("#C9862A"), rect.yMax, rect.yMin));
            c.Fill(Grow(body, -8f), Vertical(Hex("#3A62B8"), Hex("#274A94"), rect.yMax, rect.yMin));
            c.Fill(Intersect(Grow(body, -12f), p => p.y - (rect.yMax - rect.height * 0.34f)), WithAlpha(White, 0.16f), 2f);
        }

        /// <summary>
        /// Round slot a booster sits in: a narrow gold ring around a quiet blue face. A fat ring with
        /// a big glossy bubble makes the icon inside look like a toy in a plate.
        /// </summary>
        static void GameUiSlot(FinikSdfCanvas c)
        {
            // Small enough that the ring and its shadow both stay inside the sprite: a shape that
            // touches the bottom edge comes out looking cropped wherever it is drawn.
            float r = c.Width * 0.4f;
            // One flat gold ring and no drop shadow under it. A vertical gradient plus a shadow made
            // the lower half of the ring as dark as the shelf behind it, and the slot read as cropped.
            c.Fill(Circle(c.Center, r), Vertical(Hex("#C9D6FF"), Hex("#8093D8"), c.Height, 0));
            c.Fill(Circle(c.Center, r - 6), Vertical(Hex("#4A69BE"), Hex("#2E4489"), c.Height, 0));
            c.Fill(Intersect(Circle(c.Center, r - 11), p => p.y - c.Height * 0.66f), WithAlpha(White, 0.16f), 3f);
        }

        /// <summary>Square candy button of the games kit.</summary>
        static void GameUiButton(FinikSdfCanvas c)
        {
            var rect = new Rect(6, 8, c.Width - 12, c.Height - 16);
            var body = RoundBox(rect, 52);
            c.Glow(Offset(body, new Vector2(0, -8)), Hex("#123A16", 0.3f), 12);
            c.Fill(body, Vertical(Hex("#3FA94E"), Hex("#227A32"), rect.yMax, rect.yMin));
            c.Fill(Grow(body, -6f), Vertical(Hex("#6FD97B"), Hex("#339645"), rect.yMax, rect.yMin));
            c.Fill(Intersect(Grow(body, -12f), p => p.y - (rect.yMax - rect.height * 0.38f)), WithAlpha(White, 0.3f), 3f);
        }

        /// <summary>
        /// The dent a piece sits in: light enough to keep the board festive, and a touch warmer than
        /// the well behind it, so a blue or violet gem still separates from its own background.
        /// </summary>
        static void GameCell(FinikSdfCanvas c)
        {
            var body = RoundBox(new Rect(8, 8, c.Width - 16, c.Height - 16), 28);
            c.Fill(body, Vertical(Hex("#6C6FA6"), Hex("#56598E"), c.Height, 0));
            // Shadow along the top inner edge, light along the bottom one: a shallow dish, not a tile.
            c.Fill(Intersect(Stroke(Grow(body, -3f), 4f), p => p.y - c.Height * 0.5f), Hex("#1B2550", 0.3f));
            c.Fill(Intersect(Stroke(Grow(body, -3f), 3f), p => c.Height * 0.45f - p.y), WithAlpha(White, 0.12f));
        }

        /// <summary>Quiet capsule for a HUD counter: deep blue, no gold, so the gold stays an accent.</summary>
        static void GameUiChip(FinikSdfCanvas c)
        {
            var rect = new Rect(4, 4, c.Width - 8, c.Height - 8);
            var body = RoundBox(rect, rect.height * 0.5f);
            c.Glow(Offset(body, new Vector2(0, -4)), Hex("#0A1236", 0.3f), 8);
            c.Fill(body, Vertical(Hex("#2B3B7B"), Hex("#1A2554"), rect.yMax, rect.yMin));
            c.Fill(Stroke(Grow(body, -3f), 2f), WithAlpha(White, 0.16f));
        }

        /// <summary>
        /// The shelf the goal and the powers stand on. Without it the bottom of a level sinks into the
        /// backdrop and the two rows read as loose UI rather than as one module.
        /// </summary>
        static void GameUiDock(FinikSdfCanvas c)
        {
            var rect = new Rect(6, 0, c.Width - 12, c.Height - 6);
            var body = RoundBox(rect, 72);
            c.Fill(body, Vertical(Hex("#4A5DA8", 0.92f), Hex("#2B3872", 0.92f), rect.yMax, rect.yMin));
            c.Fill(Stroke(Grow(body, -4f), 2f), WithAlpha(White, 0.14f));
            // A soft light along the top edge, so the shelf catches the glow of the board above it.
            c.Fill(Intersect(Grow(body, -8f), p => p.y - (rect.yMax - rect.height * 0.3f)), WithAlpha(White, 0.1f), 4f);
        }

        /// <summary>Charge counter on a booster: a flat red dot, no white ring to shout with.</summary>
        static void GameUiBadge(FinikSdfCanvas c)
        {
            float r = c.Width * 0.42f;
            c.Glow(Offset(Circle(c.Center, r), new Vector2(0, -3)), Hex("#060A24", 0.35f), 6);
            c.Fill(Circle(c.Center, r), Hex("#DCE4FF"));
            c.Fill(Circle(c.Center, r - 5), Vertical(Hex("#2E3F86"), Hex("#1C2862"), c.Height, 0));
        }

        /// <summary>
        /// Face of an open memory card: near-white with a light blue edge. The app's glass card was
        /// light blue on a blue screen and lost most of its contrast.
        /// </summary>
        static void GameCardFace(FinikSdfCanvas c)
        {
            var rect = new Rect(8, 12, c.Width - 16, c.Height - 20);
            var body = RoundBox(rect, 56);
            c.Glow(Offset(body, new Vector2(0, -8)), Hex("#060A24", 0.35f), 10);
            c.Fill(body, Vertical(Hex("#BFD2FF"), Hex("#8FA6EA"), rect.yMax, rect.yMin));
            c.Fill(Grow(body, -6f), Vertical(Hex("#FFFFFF"), Hex("#E4ECFF"), rect.yMax, rect.yMin));
            c.Fill(Stroke(Grow(body, -16f), 2f), Hex("#C8D6FA", 0.8f));
        }

        /// <summary>Sun rays for the win: soft wedges fading out from the middle. Turned by the result card.</summary>
        static void GameRays(FinikSdfCanvas c)
        {
            const int rays = 12;
            float half = Mathf.PI / rays;
            float radius = c.Width * 0.5f;
            Sdf wedges = p =>
            {
                var d = p - c.Center;
                float angle = Mathf.Atan2(d.y, d.x);
                float local = Mathf.Repeat(angle, 2f * half) - half;
                // Angular distance to the wedge edge, scaled to pixels at this radius.
                return (Mathf.Abs(local) - half * 0.45f) * Mathf.Max(8f, d.magnitude);
            };
            c.Fill(Intersect(wedges, Circle(c.Center, radius - 2f)),
                p => WithAlpha(Hex("#FFE9A6"), 0.75f * Mathf.Clamp01(1f - (p - c.Center).magnitude / radius)), 3f);
        }

        /// <summary>Bar track on the dark HUD: frosted glass, lighter than the chip it sits on.</summary>
        static void GameUiTrack(FinikSdfCanvas c)
        {
            var rect = new Rect(3, 3, c.Width - 6, c.Height - 6);
            var body = RoundBox(rect, rect.height * 0.5f);
            c.Fill(body, WithAlpha(Hex("#AFC2FF"), 0.28f));
            c.Fill(Intersect(Stroke(Grow(body, -2f), 2.5f), p => rect.yMin + rect.height * 0.55f - p.y), WithAlpha(White, 0.18f));
            c.Fill(Intersect(Stroke(Grow(body, -2f), 2.5f), p => p.y - (rect.yMin + rect.height * 0.5f)), Hex("#0B1233", 0.25f));
        }

        /// <summary>Bar track on a light card: a pale blue groove with a soft inner shadow.</summary>
        static void UiTrackLight(FinikSdfCanvas c)
        {
            var rect = new Rect(3, 3, c.Width - 6, c.Height - 6);
            var body = RoundBox(rect, rect.height * 0.5f);
            c.Fill(body, Hex("#C9D8F6"));
            c.Fill(Intersect(Stroke(Grow(body, -2f), 3f), p => p.y - (rect.yMin + rect.height * 0.5f)), Hex("#7D93C9", 0.45f));
            c.Fill(Stroke(body, 1.5f), Hex("#9DB2E3"));
        }

        /// <summary>A mistake in «Найди пару»: a soft red disc with a rounded white cross.</summary>
        static void GameMiss(FinikSdfCanvas c)
        {
            float r = c.Width * 0.42f;
            c.Fill(Offset(Circle(c.Center, r), new Vector2(0, -4)), Hex("#40060A", 0.3f), 4f);
            c.Fill(Circle(c.Center, r), Vertical(Hex("#FF7A6B"), Hex("#D8392B"), c.Height, 0));
            c.Fill(Intersect(Circle(c.Center, r - 5), p => p.y - c.Height * 0.62f), WithAlpha(White, 0.25f), 3f);
            float arm = r * 0.42f;
            c.Fill(Union(
                Capsule(c.Center + new Vector2(-arm, -arm), c.Center + new Vector2(arm, arm), 8f),
                Capsule(c.Center + new Vector2(-arm, arm), c.Center + new Vector2(arm, -arm), 8f)), White);
        }

        /// <summary>Way out of a level: a quiet dark disc, not the loudest thing on the screen.</summary>
        static void GameUiClose(FinikSdfCanvas c)
        {
            float r = c.Width * 0.4f;
            c.Fill(Offset(Circle(c.Center, r), new Vector2(0, -4)), Hex("#060A24", 0.35f), 5f);
            c.Fill(Circle(c.Center, r), Vertical(Hex("#6F86D6"), Hex("#3C4F9A"), c.Height, 0));
            c.Fill(Intersect(Circle(c.Center, r - 4), p => p.y - c.Height * 0.6f), WithAlpha(White, 0.2f), 3f);
            float arm = r * 0.34f;
            var cross = Union(
                Capsule(c.Center + new Vector2(-arm, -arm), c.Center + new Vector2(arm, arm), 7.5f),
                Capsule(c.Center + new Vector2(-arm, arm), c.Center + new Vector2(arm, -arm), 7.5f));
            c.Fill(cross, WithAlpha(White, 0.85f));
        }

        static void InputField(FinikSdfCanvas c)
        {
            var body = RoundBox(new Rect(2, 2, c.Width - 4, c.Height - 4), 40);
            c.Fill(body, Hex("#9EC2FF"));
            c.Fill(Grow(body, -4f), Hex("#FFFFFF"));
            c.Fill(Intersect(Stroke(Grow(body, -7f), 3f), p => c.Height * 0.55f - p.y), Hex("#1B2A6B", 0.12f));
        }

        static void Bubble(FinikSdfCanvas c)
        {
            var body = RoundBox(new Rect(4, 40, c.Width - 8, c.Height - 44), 52);
            var tail = Grow(Polygon(new Vector2(54, 60), new Vector2(110, 60), new Vector2(40, 8)), 4);
            var shape = Union(body, tail);
            c.Glow(Offset(shape, new Vector2(0, -5)), Hex("#0B1030", 0.22f), 8);
            c.Fill(shape, Vertical(Hex("#FFFFFF"), Hex("#EEF3FF"), c.Height, 0));
        }

        static void Check(FinikSdfCanvas c)
        {
            CandyCircle(c, 44, Hex("#FFFFFF"), 5, Hex("#17803F"), 6, Hex("#8CF29A"), Hex("#2EBE5A"));
            var mark = Union(Capsule(new Vector2(30, 50), new Vector2(43, 37), 6), Capsule(new Vector2(43, 37), new Vector2(67, 62), 6));
            c.Fill(mark, White);
        }

        // ------------------------------------------------------------------ icons (256 px)

        /// <summary>Sticker style: drop shadow, dark outline, gradient body, top sheen.</summary>
        static void Sticker(FinikSdfCanvas c, Sdf shape, Color outline, float outlineWidth, Color top, Color bottom, float yTop, float yBottom, float sheen = 0.45f)
        {
            c.Fill(Offset(Grow(shape, outlineWidth), new Vector2(0, -6)), Hex("#000000", 0.22f), 3f);
            c.Fill(Grow(shape, outlineWidth), outline);
            c.Fill(shape, Vertical(top, bottom, yTop, yBottom));
            if (sheen <= 0f) return;
            float h = yTop - yBottom;
            var gloss = Intersect(Grow(shape, -7f), p => (yTop - h * 0.42f) - p.y);
            c.Fill(Offset(gloss, Vector2.zero), p => WithAlpha(White, sheen * Mathf.InverseLerp(yTop - h * 0.45f, yTop, p.y)), 2f);
        }

        static void Star(FinikSdfCanvas c)
        {
            var star = Grow(FinikSdfCanvas.Star(c.Center + new Vector2(0, -4), 100, 48, 5), 12);
            Sticker(c, star, Hex("#B35A00"), 8, Hex("#FFEE70"), Hex("#FFA412"), 240, 20, 0.5f);
        }

        static void Coin(FinikSdfCanvas c)
        {
            Vector2 center = c.Center + new Vector2(0, 3);
            var rim = Circle(center, 108);
            Sticker(c, rim, Hex("#A85800"), 8, Hex("#FFE56E"), Hex("#F29A18"), 236, 20, 0f);
            c.Fill(Circle(center, 82), Vertical(Hex("#FFC93A"), Hex("#FFE98A"), 210, 46));
            c.Fill(Stroke(Circle(center, 82), 4), Hex("#D17F0B", 0.9f));
            var star = Grow(FinikSdfCanvas.Star(center, 50, 23, 5), 5);
            c.Fill(Offset(star, new Vector2(0, 3)), Hex("#FFF6C4"));
            c.Fill(star, Hex("#E99214"));
            var gloss = Intersect(Grow(rim, -8), Ellipse(center + new Vector2(-28, 52), new Vector2(64, 34)));
            c.Fill(gloss, WithAlpha(White, 0.45f), 2f);
        }

        static void Piggy(FinikSdfCanvas c, bool withCoin)
        {
            Vector2 center = new(128, withCoin ? 104 : 118);
            Color outline = Hex("#B63F6E");
            float s = withCoin ? 0.88f : 1f;
            var body = Ellipse(center, new Vector2(108, 86) * s);
            var earL = Grow(Polygon(center + new Vector2(-78, 44) * s, center + new Vector2(-40, 70) * s, center + new Vector2(-72, 104) * s), 10);
            var earR = Grow(Polygon(center + new Vector2(78, 44) * s, center + new Vector2(40, 70) * s, center + new Vector2(72, 104) * s), 10);
            var legL = RoundBox(new Rect(center.x - 70 * s, center.y - 104 * s, 36 * s, 40 * s), 12);
            var legR = RoundBox(new Rect(center.x + 34 * s, center.y - 104 * s, 36 * s, 40 * s), 12);
            var silhouette = Union(body, earL, earR, legL, legR);
            if (withCoin)
            {
                var coinCenter = new Vector2(128, 206);
                c.Fill(Grow(Circle(coinCenter, 38), 7), Hex("#A85800"));
                c.Fill(Circle(coinCenter, 38), Vertical(Hex("#FFE56E"), Hex("#F29A18"), 244, 168));
                c.Fill(Stroke(Circle(coinCenter, 26), 3), Hex("#D17F0B"));
            }
            Sticker(c, silhouette, outline, 8, Hex("#FFC4DA"), Hex("#F2769F"), center.y + 110 * s, center.y - 104 * s, 0.4f);
            c.Fill(Grow(earL, -10), Hex("#E75A8C", 0.8f));
            c.Fill(Grow(earR, -10), Hex("#E75A8C", 0.8f));
            // Coin slot
            c.Fill(RoundBox(new Rect(center.x - 30 * s, center.y + 62 * s, 60 * s, 12 * s), 6), Hex("#9E2F5B"));
            // Snout, nostrils, eyes, cheeks
            Vector2 snout = center + new Vector2(0, -18) * s;
            c.Fill(Grow(Ellipse(snout, new Vector2(40, 28) * s), 5), outline);
            c.Fill(Ellipse(snout, new Vector2(40, 28) * s), Vertical(Hex("#FFB1CC"), Hex("#F58AB0"), snout.y + 28, snout.y - 28));
            c.Fill(Ellipse(snout + new Vector2(-14, 0) * s, new Vector2(7, 10) * s), Hex("#9E2F5B"));
            c.Fill(Ellipse(snout + new Vector2(14, 0) * s, new Vector2(7, 10) * s), Hex("#9E2F5B"));
            c.Fill(Ellipse(center + new Vector2(-40, 26) * s, new Vector2(9, 13) * s), Hex("#3A1A2A"));
            c.Fill(Ellipse(center + new Vector2(40, 26) * s, new Vector2(9, 13) * s), Hex("#3A1A2A"));
            c.Fill(Circle(center + new Vector2(-37, 31) * s, 3.5f * s), White);
            c.Fill(Circle(center + new Vector2(43, 31) * s, 3.5f * s), White);
            c.Fill(Circle(center + new Vector2(-66, -8) * s, 14 * s), Hex("#FF6F9C", 0.45f));
            c.Fill(Circle(center + new Vector2(66, -8) * s, 14 * s), Hex("#FF6F9C", 0.45f));
        }

        static void Food(FinikSdfCanvas c)
        {
            var spoon = Union(Ellipse(new Vector2(162, 176), new Vector2(34, 48)), Capsule(new Vector2(162, 140), new Vector2(162, 36), 12));
            var fork = Union(
                Capsule(new Vector2(74, 222), new Vector2(74, 168), 8),
                Capsule(new Vector2(96, 222), new Vector2(96, 168), 8),
                Capsule(new Vector2(118, 222), new Vector2(118, 168), 8),
                RoundBox(new Rect(64, 132, 64, 46), 22),
                Capsule(new Vector2(96, 140), new Vector2(96, 36), 12));
            var both = Union(spoon, fork);
            c.Fill(Offset(both, new Vector2(0, -6)), Hex("#000000", 0.3f), 3f);
            c.Fill(both, Vertical(Hex("#FFFFFF"), Hex("#D5DDF2"), 230, 30));
        }

        /// <summary>White exclamation mark: "something to do here".</summary>
        static void QuestMark(FinikSdfCanvas c)
        {
            var mark = Union(Capsule(new Vector2(128, 186), new Vector2(128, 104), 20), Circle(new Vector2(128, 66), 20));
            c.Fill(Offset(mark, new Vector2(0, -5)), Hex("#0B1030", 0.28f), 2f);
            c.Fill(mark, Vertical(Hex("#FFFFFF"), Hex("#E4DAFF"), 206, 46));
        }

        static void Mood(FinikSdfCanvas c)
        {
            Vector2 center = c.Center;
            var face = Circle(center, 104);
            Sticker(c, face, Hex("#B86A00"), 8, Hex("#FFEB6B"), Hex("#FFAE1C"), 232, 24, 0.45f);
            Color ink = Hex("#4A2A08");
            c.Fill(Ellipse(center + new Vector2(-36, 26), new Vector2(12, 19)), ink);
            c.Fill(Ellipse(center + new Vector2(36, 26), new Vector2(12, 19)), ink);
            c.Fill(Circle(center + new Vector2(-32, 34), 4.5f), White);
            c.Fill(Circle(center + new Vector2(40, 34), 4.5f), White);
            var smile = Intersect(Stroke(Circle(center + new Vector2(0, 8), 58), 8), p => p.y - (center.y - 8));
            c.Fill(smile, ink);
            c.Fill(Circle(center + new Vector2(-66, -14), 16), Hex("#FF7A4D", 0.45f));
            c.Fill(Circle(center + new Vector2(66, -14), 16), Hex("#FF7A4D", 0.45f));
        }

        static void Energy(FinikSdfCanvas c)
        {
            var bolt = Grow(Polygon(
                new Vector2(146, 238), new Vector2(60, 122), new Vector2(118, 122), new Vector2(92, 18),
                new Vector2(198, 146), new Vector2(138, 146), new Vector2(188, 238)), 6);
            Sticker(c, bolt, Hex("#B35A00"), 8, Hex("#FFF07A"), Hex("#FFA412"), 244, 12, 0.5f);
        }

        static void Mail(FinikSdfCanvas c)
        {
            var envelope = RoundBox(new Rect(34, 58, 188, 140), 20);
            c.Fill(Offset(envelope, new Vector2(0, -6)), Hex("#000000", 0.3f), 3f);
            c.Fill(envelope, Vertical(Hex("#FFFFFF"), Hex("#DCE4F7"), 198, 58));
            Color flap = Hex("#3D6FE0");
            c.Fill(Union(Capsule(new Vector2(56, 176), new Vector2(128, 122), 8), Capsule(new Vector2(128, 122), new Vector2(200, 176), 8)), flap);
        }

        static void Settings(FinikSdfCanvas c)
        {
            var gear = Gear(c.Center, 78, 8, 24, 20, 32);
            c.Fill(Offset(gear, new Vector2(0, -6)), Hex("#000000", 0.3f), 3f);
            c.Fill(gear, Vertical(Hex("#FFFFFF"), Hex("#D5DDF2"), 230, 26));
        }

    }
}
