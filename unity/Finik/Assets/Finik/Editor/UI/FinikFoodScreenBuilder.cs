using System.Collections.Generic;
using System.Linq;
using Finik.Core;
using Finik.Navigation;
using Finik.UI;
using Finik.UI.Food;
using Finik.UI.Onboarding;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static Finik.Editor.UI.FinikUiKit;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Builds the food screen (Screen_Food) over the room: a card with the life scene, a hint line,
    /// four options and the buttons (the primary one carries the price), placed beside Finik in landscape and under him in portrait (same column as
    /// onboarding, framed by FinikShowcaseCamera). Also links the 3D Finik to the game state.
    /// </summary>
    public static class FinikFoodScreenBuilder
    {
        const string RootName = "Screen_Food";
        const float Gap = 14f;
        const float PadX = 40f, PadY = 34f;
        const float HeaderHeight = 104f, SetupHeight = 76f, HintHeight = 80f, ButtonsHeight = 112f;
        // Landscape: 2x2 cards. Portrait: the column is wide but short on height, so one row per option.
        const float CardHeight = 204f, RowHeight = 104f;
        const float LandscapeWidth = 860f, PortraitWidth = 1010f;

        static float ColumnHeight(int rows, float rowHeight) =>
            2 * PadY + HeaderHeight + SetupHeight + HintHeight + rows * rowHeight + (rows - 1) * Gap + ButtonsHeight + 4 * Gap;

        static readonly FinikRectLayout LandscapeColumn = FinikRectLayout.At(LeftMiddle, LeftMiddle, new Vector2(72, 0), new Vector2(LandscapeWidth, ColumnHeight(2, CardHeight)));
        static readonly FinikRectLayout PortraitColumn = FinikRectLayout.At(BottomCenter, BottomCenter, new Vector2(0, 36), new Vector2(PortraitWidth, ColumnHeight(4, RowHeight)));
        // The result card has less content: keep it compact so more of Finik stays visible.
        static readonly FinikRectLayout ResultLandscape = FinikRectLayout.At(LeftMiddle, LeftMiddle, new Vector2(72, 0), new Vector2(860, 880));
        static readonly FinikRectLayout ResultPortrait = FinikRectLayout.At(BottomCenter, BottomCenter, new Vector2(0, 36), new Vector2(1010, 880));

        static readonly Color Good = new(0.13f, 0.62f, 0.3f, 1f);
        static readonly Color Accent = new(0.95f, 0.5f, 0.1f, 1f);

        [MenuItem("Finik/UI/Rebuild Food Screen")]
        public static void RebuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            if (!Prepare(out string message)) return message;
            var scene = EditorSceneManager.GetActiveScene();

            var finik = GameObject.Find("Finik_Root") ?? throw new System.InvalidOperationException("Finik_Root is missing from the scene.");
            var cameraGo = GameObject.Find("camera_gameplay") ?? throw new System.InvalidOperationException("camera_gameplay is missing from the scene.");
            var showcase = cameraGo.GetComponent<FinikShowcaseCamera>();
            if (!showcase) return "FinikShowcaseCamera is missing: run Finik/UI/Rebuild Onboarding first.";
            var hud = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "HUD_Home");
            if (!hud) return "Build the home HUD first (Finik/UI/Rebuild Home HUD).";

            if (!finik.GetComponent<FinikPetGameLink>()) Undo.AddComponent<FinikPetGameLink>(finik);

            // Above the HUD (20), below onboarding (30).
            var root = ReuseOrCreateCanvas(RootName, 25);
            var screen = GetOrAdd<FinikFoodScreen>(root);
            var safe = Rect("SafeArea", root.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<FinikSafeArea>();

            var panelRoot = Rect("Panel", safe);
            Stretch(panelRoot);
            panelRoot.gameObject.AddComponent<CanvasGroup>();
            var panel = panelRoot.gameObject.AddComponent<FinikScreenPanel>();
            SetField(screen, "panel", panel);

            var column = Rect("Column", panelRoot);
            column.gameObject.AddComponent<FinikFitInside>();
            column.gameObject.AddComponent<FinikOrientationLayout>().Configure(LandscapeColumn, PortraitColumn);
            PanelCard(column);
            var content = Rect("Content", column);
            Stretch(content, PadX, PadY, PadX, PadY);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = Gap;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            BuildHeader(content, screen);
            BuildOptions(content, screen);
            Dismiss(panel, BuildButtons(content, screen));
            WireIcons(screen);
            BuildResult(safe, screen);

            SetField(screen, "showcase", showcase);
            SetField(screen, "movement", finik.GetComponent<FinikMovementController>());
            SetField(screen, "activity", finik.GetComponent<FinikActivityController>());
            SetField(screen, "hudRoot", hud);
            var paused = new List<Object>();
            foreach (var behaviour in new Behaviour[] { finik.GetComponent<FinikInputController>(), finik.GetComponent<FinikWanderController>() })
                if (behaviour) paused.Add(behaviour);
            SetField(screen, "pauseWhileOpen", paused.ToArray());

            var binder = hud.GetComponent<FinikHudBinder>();
            if (binder) SetField(binder, "foodScreen", screen);

            panelRoot.gameObject.SetActive(false);
            // Every button answers at least a 48 dp tap; the component is a no-op on the big ones.
            EnsureTapTargets(root);
            EnsureEventSystem();
            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"Built {RootName}: {FinikFoodCatalog.Plans.Count} food plans, {FinikFoodCatalog.Scenes.Count} scenes, {CountArt()} with final art.";
        }

        // ------------------------------------------------------------------ sections

        static void BuildHeader(Transform content, FinikFoodScreen screen)
        {
            var row = Rect("Header", content);
            Size(row, height: HeaderHeight);
            var icon = Img("SceneIcon", row, "scene_placeholder", preserveAspect: true);
            Place(icon.rectTransform, LeftMiddle, LeftMiddle, Vector2.zero, new Vector2(104, 104));
            icon.gameObject.AddComponent<FinikIdleMotion>().Configure(3f, 3f, 0f, 1.8f, 0f);

            var tag = Text("Tag", row, "ЕДА · ЖИЗНЕННАЯ СИТУАЦИЯ", 24, Accent, TextAlignmentOptions.BottomLeft, outlined: false);
            tag.rectTransform.anchorMin = new Vector2(0, 0.62f);
            tag.rectTransform.anchorMax = new Vector2(1, 1f);
            tag.rectTransform.offsetMin = new Vector2(124, 0);
            tag.rectTransform.offsetMax = new Vector2(-300, -4);
            tag.characterSpacing = 4;

            var title = Text("Title", row, "Вечер дома", 48, Ink, TextAlignmentOptions.TopLeft, outlined: false);
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = new Vector2(1, 0.62f);
            title.rectTransform.offsetMin = new Vector2(124, 0);
            title.rectTransform.offsetMax = Vector2.zero;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            AutoSize(title, 34, 44);
            // Leave room for the coin pill, whose icon overhangs it on the left.
            title.rectTransform.offsetMax = new Vector2(-300, 0);
            BuildWallet(row, screen);

            var setup = Text("Setup", content, "Финик голодный.", 28, Ink, TextAlignmentOptions.TopLeft, outlined: false);
            Wrap(setup);
            Size(setup, height: SetupHeight);
            AutoSize(setup, 22, 28);

            // The scene's advice, or why the picked option cannot be paid: a quiet line, not a box.
            var hint = Text("Hint", content, "Смотри на цену, время и то, что уже есть дома.", 26, InkSoft, TextAlignmentOptions.TopLeft, outlined: false);
            Wrap(hint);
            Size(hint, height: HintHeight);
            AutoSize(hint, 22, 26);

            SetField(screen, "sceneIcon", icon);
            SetField(screen, "sceneHint", hint);
            SetField(screen, "sceneTitle", title);
            SetField(screen, "sceneSetup", setup);
        }

        /// <summary>Same coin pill as the HUD, top-right of the header; it rolls down when the player pays.</summary>
        static void BuildWallet(Transform header, FinikFoodScreen screen)
        {
            var root = Rect("Coins", header);
            Place(root, RightMiddle, RightMiddle, new Vector2(0, 8), new Vector2(230, 76));
            var body = Rect("Body", root);
            Stretch(body);
            SetField(screen, "coins", CurrencyPill(body, "Coins", "icon_coin"));
        }

        static void BuildOptions(Transform content, FinikFoodScreen screen)
        {
            var grid = Rect("Options", content);
            grid.gameObject.AddComponent<FinikFitGridLayout>().Configure(2, CardHeight, 1, RowHeight, new Vector2(Gap, Gap));

            var views = new Object[4];
            for (int i = 0; i < 4; i++) views[i] = Option(grid, i);
            SetField(screen, "options", views);
        }

        static FinikFoodOptionView Option(Transform grid, int index)
        {
            var slot = Rect("Option" + index, grid);
            var body = Rect("Body", slot);
            Stretch(body);
            var press = body.gameObject.AddComponent<FinikPressFeedback>();

            var face = Img("Face", body, "ui_card", sliced: true, raycast: true);
            Stretch(face.rectTransform);
            face.pixelsPerUnitMultiplier *= 1.6f;
            var button = MakeButton(body, face);
            // Crisp orange frame on top of the card when selected (no glow, no growing).
            var frame = Img("Selected", body, "ui_card_selected", sliced: true, color: new Color(1, 1, 1, 0));
            Stretch(frame.rectTransform);
            frame.pixelsPerUnitMultiplier *= 1.6f;

            var icon = Img("Icon", body, "food_placeholder", preserveAspect: true);
            Orient(icon.rectTransform,
                FinikRectLayout.At(LeftMiddle, LeftMiddle, new Vector2(10, 4), new Vector2(100, 100)),
                FinikRectLayout.At(LeftMiddle, LeftMiddle, new Vector2(16, 0), new Vector2(84, 84)));

            // Card: title (up to two lines) and trade-off above the price row; the right inset keeps
            // the text clear of the selection badge. Row: one line each, price column on the right.
            var texts = Rect("Texts", body);
            Orient(texts, Inset(118, 52, 40, 14), Inset(118, 10, 250, 10));
            var stack = texts.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.spacing = 2;
            stack.childAlignment = TextAnchor.UpperLeft;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;

            var title = Text("Title", texts, "Приготовить самому", 25, Ink, TextAlignmentOptions.TopLeft, outlined: false);
            Wrap(title);
            title.overflowMode = TextOverflowModes.Ellipsis;
            title.lineSpacing = -8;

            var subtitle = Text("Subtitle", texts, "продукты из «Нужно»", 19, InkSoft, TextAlignmentOptions.TopLeft, outlined: false);
            Wrap(subtitle);
            subtitle.lineSpacing = -6;
            subtitle.overflowMode = TextOverflowModes.Ellipsis;

            var coin = Img("Coin", body, "icon_coin", preserveAspect: true);
            Orient(coin.rectTransform,
                FinikRectLayout.At(BottomLeft, BottomLeft, new Vector2(114, 10), new Vector2(38, 38)),
                FinikRectLayout.At(RightMiddle, LeftMiddle, new Vector2(-232, 17), new Vector2(34, 34)));
            var cost = Text("Cost", body, "60", 26, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            Orient(cost.rectTransform,
                FinikRectLayout.At(BottomLeft, BottomLeft, new Vector2(154, 12), new Vector2(90, 34)),
                FinikRectLayout.At(RightMiddle, LeftMiddle, new Vector2(-192, 17), new Vector2(140, 34)));
            var afford = Text("Afford", body, "по карману", 20, Good, TextAlignmentOptions.MidlineRight, outlined: false);
            Orient(afford.rectTransform,
                FinikRectLayout.At(BottomRight, BottomRight, new Vector2(-18, 12), new Vector2(150, 34)),
                FinikRectLayout.At(RightMiddle, LeftMiddle, new Vector2(-232, -19), new Vector2(190, 32)));

            var check = Img("Check", body, "ui_check", preserveAspect: true);
            Place(check.rectTransform, TopRight, Center, new Vector2(-14, -14), new Vector2(52, 52));
            check.gameObject.SetActive(false);

            var choice = slot.gameObject.AddComponent<FinikChoiceItem>();
            choice.Configure("option" + index, button, press, frame, check.gameObject, null);
            SetField(choice, "selectedScale", 1f);
            var view = slot.gameObject.AddComponent<FinikFoodOptionView>();
            view.Configure(choice, icon, title, subtitle, cost, afford, stack);
            return view;
        }

        /// <summary>Returns the «Не сейчас» button: the panel's catcher forwards a tap to it.</summary>
        static Button BuildButtons(Transform content, FinikFoodScreen screen)
        {
            var row = Rect("Buttons", content);
            Size(row, height: 112);
            var group = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 18;
            group.childAlignment = TextAnchor.MiddleCenter;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = group.childForceExpandHeight = false;

            var close = WideButton(row, "Close", "ui_btn_white", "Не сейчас", 32, Ink, 112, 330);
            var primary = WideButton(row, "Primary", "ui_btn_orange", "Выбрать за 150", 32, Color.white, 112, 430, "ui_decor_star_orange");
            // Second line: where the coins come from ("Нужно 80 · Хочу 70").
            var split = Text("Split", primary.transform, "Нужно 80 · Хочу 70", 30, Color.white, TextAlignmentOptions.Midline);
            Stretch(split.rectTransform, 24, 18, 24, 64);
            split.enableAutoSizing = true;
            split.fontSizeMin = MinimumReadableFontSize;
            split.fontSizeMax = MinimumReadableFontSize;
            SetField(screen, "closeButton", close);
            SetField(screen, "closeLabel", close.GetComponentInChildren<TMP_Text>(true));
            SetField(screen, "primaryButton", primary);
            SetField(screen, "primaryRoot", primary.transform.parent.gameObject);
            SetField(screen, "primaryLabel", primary.transform.Find("Label").GetComponent<TMP_Text>());
            SetField(screen, "primarySplit", split);
            SetField(screen, "disabledFace", SpriteOrNull("ui_btn_white"));
            SetField(screen, "primaryFace", primary.transform.Find("Face").GetComponent<Image>());
            return close;
        }

        /// <summary>Outcome card shown in place of the choice card after paying.</summary>
        static void BuildResult(Transform safe, FinikFoodScreen screen)
        {
            var panelRoot = Rect("Result", safe);
            Stretch(panelRoot);
            panelRoot.gameObject.AddComponent<CanvasGroup>();
            var panel = panelRoot.gameObject.AddComponent<FinikScreenPanel>();
            var view = panelRoot.gameObject.AddComponent<FinikFoodResultView>();

            var column = Rect("Column", panelRoot);
            column.gameObject.AddComponent<FinikFitInside>();
            column.gameObject.AddComponent<FinikOrientationLayout>().Configure(ResultLandscape, ResultPortrait);
            PanelCard(column);
            var content = Rect("Content", column);
            Stretch(content, 40, 34, 40, 34);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = Gap;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            layout.spacing = 18;

            // Hero: big dish with the verdict as a small tag pinned to its bottom edge.
            var hero = Rect("Hero", content);
            Size(hero, height: 236);
            var dish = Img("Dish", hero, "food_placeholder", preserveAspect: true);
            Place(dish.rectTransform, TopCenter, TopCenter, Vector2.zero, new Vector2(200, 200));
            dish.gameObject.AddComponent<FinikIdleMotion>().Configure(3f, 5f, 0.015f, 1.8f, 0f);
            HeroSparkles(hero, 100f);
            var pill = Img("Verdict", hero, "ui_btn_white", sliced: true);
            Place(pill.rectTransform, BottomCenter, BottomCenter, Vector2.zero, new Vector2(330, 58));
            var verdict = Text("Label", pill.transform, "Умный выбор", 28, Color.white, TextAlignmentOptions.Midline);
            Stretch(verdict.rectTransform, 18, 7, 18, 0);
            AutoSize(verdict, 20, 28);

            // The verdict tag already judges the choice; the heading just names the dish.
            var title = Text("Title", content, "Приготовить самому", 42, Ink, TextAlignmentOptions.Top, outlined: false);
            AutoSize(title, 30, 42);
            Size(title, height: 54);

            var explanation = Text("Explanation", content, "…", 26, Ink, TextAlignmentOptions.Center, outlined: false);
            Wrap(explanation);
            AutoSize(explanation, 20, 26);
            explanation.lineSpacing = 6;
            Size(explanation, height: 106);

            // What changed: one calm light strip, icon + number per item, no dark pills.
            var strip = Img("Changes", content, "ui_card", sliced: true, color: FinikSdfCanvas.Hex("#EEF4FF"));
            strip.pixelsPerUnitMultiplier *= 1.6f;
            Size(strip, height: 96);
            var items = Rect("Items", strip.transform);
            Stretch(items, 12, 10, 12, 10);
            var row = items.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = true;
            var coins = Change(items, "Coins", "icon_coin");
            var food = Change(items, "Food", "icon_food");
            var mood = Change(items, "Mood", "icon_mood");
            var xp = Change(items, "Xp", "icon_star");

            // Finik's comment: small avatar + one quiet line.
            var petRow = Rect("Pet", content);
            Size(petRow, height: 72);
            var avatar = Img("Avatar", petRow, "avatar_finik", preserveAspect: true);
            Place(avatar.rectTransform, LeftMiddle, LeftMiddle, new Vector2(20, 0), new Vector2(72, 72));
            var line = Text("Line", petRow, "«…»", 25, InkSoft, TextAlignmentOptions.MidlineLeft, outlined: false);
            Wrap(line);
            AutoSize(line, 18, 25);
            line.fontStyle = FontStyles.Italic;
            Stretch(line.rectTransform, 108, 0, 20, 0);

            var buttons = Rect("Buttons", content);
            Size(buttons, height: 112);
            var close = WideButton(buttons, "Close", "ui_btn_green", "Вернуться в комнату", 36, Color.white, 112, 520, "ui_decor_paw_green");
            var closeRoot = (RectTransform)close.transform.parent;
            Place(closeRoot, Center, Center, Vector2.zero, new Vector2(520, 112));

            var confettiLayer = Rect("Confetti", panelRoot);
            Stretch(confettiLayer);
            var confetti = confettiLayer.gameObject.AddComponent<FinikConfettiBurst>();
            SetField(confetti, "pieceSprite", SpriteOrNull("ui_piece"));

            SetField(view, "panel", panel);
            SetField(view, "verdictBackground", pill);
            SetField(view, "verdict", verdict);
            SetField(view, "dishIcon", dish);
            SetField(view, "title", title);
            SetField(view, "explanation", explanation);
            SetField(view, "coinsDelta", coins);
            SetField(view, "foodDelta", food);
            SetField(view, "moodDelta", mood);
            SetField(view, "xpChip", xp.transform.parent.gameObject);
            SetField(view, "xpDelta", xp);
            SetField(view, "petLine", line);
            SetField(view, "petAvatar", avatar);
            SetField(view, "closeButton", close);
            SetField(view, "confetti", confetti);
            SetField(screen, "result", view);
            Dismiss(panel, close);
            panelRoot.gameObject.SetActive(false);
        }

        /// <summary>One "what changed" item: HUD icon and its delta.</summary>
        static TMP_Text Change(Transform row, string id, string icon)
        {
            // Icon + number as one group centred in its cell, so the strip is balanced on both sides.
            var item = Rect(id, row);
            var group = item.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 6;
            group.childAlignment = TextAnchor.MiddleCenter;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = group.childForceExpandHeight = false;
            var image = Img("Icon", item, icon, preserveAspect: true);
            Size(image, 52, 52);
            var value = Text("Value", item, "+0", 30, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            value.textWrappingMode = TextWrappingModes.NoWrap;
            return value;
        }

        static void WireIcons(FinikFoodScreen screen)
        {
            var names = FinikFoodCatalog.Plans.Select(p => p.Icon).Concat(FinikFoodCatalog.Scenes.Select(s => s.Icon));
            var so = new SerializedObject(screen);
            var icons = so.FindProperty("icons");
            icons.arraySize = 0;
            foreach (string name in names)
            {
                var sprite = SpriteOrNull(name);
                if (!sprite) continue;
                int i = icons.arraySize++;
                var element = icons.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("name").stringValue = name;
                element.FindPropertyRelative("sprite").objectReferenceValue = sprite;
            }
            so.FindProperty("fallbackFoodIcon").objectReferenceValue = SpriteOrNull("food_placeholder");
            so.FindProperty("fallbackSceneIcon").objectReferenceValue = SpriteOrNull("scene_placeholder");
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static int CountArt() =>
            FinikFoodCatalog.Plans.Count(p => SpriteOrNull(p.Icon)) + FinikFoodCatalog.Scenes.Count(s => SpriteOrNull(s.Icon));
    }
}
