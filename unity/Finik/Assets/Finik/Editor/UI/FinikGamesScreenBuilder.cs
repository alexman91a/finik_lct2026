using System.Collections.Generic;
using System.Linq;
using Finik.Core;
using Finik.Navigation;
using Finik.UI;
using Finik.UI.Games;
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
    /// Builds the games screen (Screen_Games) over the room: the hub with the three free mood games
    /// and their difficulty steps, the board a level is played on, and the result card. Opened by the
    /// sofa and by the backpack.
    ///
    /// The hub is a sheet like the other room menus — under Finik in portrait, beside him in
    /// landscape — so the pet whose mood the games lift stays in view above it. A level takes the
    /// whole screen: header, goal, board and powers as one stack with no dead bands between them.
    /// </summary>
    public static class FinikGamesScreenBuilder
    {
        const string RootName = "Screen_Games";

        // ------------------------------------------------------------------ hub metrics
        // The same column as the food, quest and savings screens.
        // Landscape is wider than the other menus: three level buttons with a medal and a reward each
        // must still fit their names at the readable minimum (FinikUiKit.MinimumReadableFontSize).
        const float HubWidthPortrait = 1010f, HubWidthLandscape = 980f;
        const float HubPadX = 40f, HubPadY = 34f, HubSpacing = 14f;
        const float HubHeader = 104f, HubHint = 44f, HubMood = 104f, HubCard = 286f, HubBack = 112f;
        const float HubChip = 82f, HubPicture = 150f;
        static float HubHeight => HubPadY * 2 + HubHeader + HubHint + HubMood + HubCard * 3 + HubBack + HubSpacing * 6;
        static readonly Color GamesTag = new(0.46f, 0.32f, 0.9f, 1f);

        // ------------------------------------------------------------------ level metrics
        /// <summary>One padding for the whole level screen, so every block sits on the same axis.</summary>
        const float PadSide = 28f, PadTop = 22f, PadBottom = 28f, Gap = 16f;
        const float HudHeight = 112f, CloseSize = 72f;
        const float GoalHeight = 118f, CounterWidth = 250f;
        /// <summary>Landscape: the goal, the counter and the powers stand in a column right of the board.</summary>
        const float SideWidth = 500f, SideCounterHeight = 104f;
        // 12% lower than it was: the powers still take a thumb, the board gets the height.
        const float DockHeight = 176f, BoosterRing = 112f;

        const float ResultWidth = 880f, ResultHeight = 660f, ResultShelf = 150f;
        const float ButtonHeight = 92f;

        static readonly Color Soft = new(0.8f, 0.85f, 1f, 1f);
        static readonly Color Good = new(0.13f, 0.62f, 0.3f, 1f);
        static readonly Color RewardGreen = new(0.62f, 1f, 0.72f, 1f);

        /// <summary>Everything the three boards draw with, wired into the shared art table.</summary>
        static IEnumerable<string> ArtNames() =>
            FinikMiniGameCatalog.GemSprites
                .Concat(FinikMiniGameCatalog.CardSprites)
                .Concat(FinikMiniGameCatalog.JunkSprites)
                .Concat(FinikMiniGameCatalog.Games.Select(g => g.Icon))
                .Concat(FinikBoosterCatalog.Icons())
                .Concat(FinikMatch3Board.ArtNames)
                .Concat(new[]
                {
                    "game_card_back", "game_card_back_tall", "game_card_face", "game_basket", "game_cell", "game_catch_bg",
                    "game_heart", "game_moves", "game_miss",
                    "gameui_bg", "gameui_board", "gameui_mask",
                    "game_medal", "game_trophy", "game_lock",
                    "icon_games", "icon_coin", "icon_mood", "icon_star",
                    "ui_card_selected", "ui_piece", "ui_decor_star_small", "hud_glow_round"
                })
                .Distinct();

        /// <summary>
        /// Builds the screen once after a compile when the scene has a HUD but no games screen yet, so
        /// a fresh checkout does not leave the sofa and the backpack leading nowhere.
        /// </summary>
        [InitializeOnLoadMethod]
        static void EnsureBuiltAfterCompile()
        {
            EditorApplication.delayCall += TryBuild;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += TryBuild;
        }

        static void TryBuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || GameObject.Find(RootName)) return;
            if (!GameObject.Find("HUD_Home")) return;
            Debug.Log(Build());
        }

        [MenuItem("Finik/UI/Rebuild Games Screen")]
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

            // Same layer as the other room screens: they never open together.
            var root = ReuseOrCreateCanvas(RootName, 25);
            var screen = GetOrAdd<FinikGamesScreen>(root);
            var art = GetOrAdd<FinikGameArt>(root);
            WireArt(art);
            SetField(screen, "art", art);

            var safe = Rect("SafeArea", root.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<FinikSafeArea>();

            BuildHub(safe, screen);
            BuildPlay(safe, screen, art);
            BuildResult(safe, screen);

            SetField(screen, "showcase", showcase);
            SetField(screen, "movement", finik.GetComponent<FinikMovementController>());
            SetField(screen, "activity", finik.GetComponent<FinikActivityController>());
            SetField(screen, "hudRoot", hud);
            var paused = new List<Object>();
            foreach (var behaviour in new Behaviour[] { finik.GetComponent<FinikInputController>(), finik.GetComponent<FinikWanderController>() })
                if (behaviour) paused.Add(behaviour);
            SetField(screen, "pauseWhileOpen", paused.ToArray());

            EnsureTapTargets(root);
            EnsureEventSystem();
            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            int withArt = ArtNames().Count(n => SpriteOrNull(n));
            return $"Built {RootName}: {FinikMiniGameCatalog.Games.Count} games x {FinikMiniGameCatalog.LevelCount} levels, " +
                   $"{withArt}/{ArtNames().Count()} pictures found.";
        }

        // ------------------------------------------------------------------ art

        static void WireArt(FinikGameArt art)
        {
            var so = new SerializedObject(art);
            var icons = so.FindProperty("icons");
            icons.arraySize = 0;
            foreach (string name in ArtNames())
            {
                var sprite = SpriteOrNull(name);
                if (!sprite) continue;
                var element = icons.GetArrayElementAtIndex(icons.arraySize++);
                element.FindPropertyRelative("name").stringValue = name;
                element.FindPropertyRelative("sprite").objectReferenceValue = sprite;
                // Hand-made art comes in any resolution; keep a sliced sprite's borders at design scale.
                element.FindPropertyRelative("pixelsPerUnit").floatValue = FinikHudSpriteBaker.PixelsPerUnitFor(name, sprite);
            }
            so.FindProperty("fallback").objectReferenceValue = SpriteOrNull("ui_dot");
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ shared blocks

        /// <summary>Full-screen panel root with its CanvasGroup and FinikScreenPanel.</summary>
        static RectTransform PanelRoot(Transform safe, string name, out FinikScreenPanel panel)
        {
            var panelRoot = Rect(name, safe);
            Stretch(panelRoot);
            panelRoot.gameObject.AddComponent<CanvasGroup>();
            panel = panelRoot.gameObject.AddComponent<FinikScreenPanel>();
            return panelRoot;
        }

        /// <summary>Round close button: a quiet disc with a cross, never the loudest thing on the screen.</summary>
        static Button CloseButton(RectTransform parent, Vector2 anchor, Vector2 position, float size)
        {
            var closeRoot = Rect("Close", parent);
            Place(closeRoot, anchor, anchor, position, new Vector2(size, size));
            closeRoot.gameObject.AddComponent<FinikPressFeedback>();
            var face = Img("Face", closeRoot, "gameui_close", raycast: true, preserveAspect: true);
            Stretch(face.rectTransform);
            return MakeButton(closeRoot, face);
        }

        static FinikFillBar Bar(string name, Transform parent, string color, string trackSprite = "hud_bar_track")
        {
            var track = Img(name, parent, trackSprite, sliced: true);
            var fill = Img("Fill", track.transform, "hud_bar_fill", sliced: true, color: FinikSdfCanvas.Hex(color));
            fill.rectTransform.pivot = new Vector2(0, 0.5f);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = new Vector2(5, 5);
            fill.rectTransform.offsetMax = new Vector2(-5, -5);
            var bar = track.gameObject.AddComponent<FinikFillBar>();
            SetField(bar, "fill", fill.rectTransform);
            SetField(bar, "fillImage", fill);
            SetField(bar, "lowThreshold", 0f);
            return bar;
        }

        /// <summary>Anchors a child to the top of its parent: full width minus the insets, at a fixed height.</summary>
        static void AnchorTop(RectTransform rect, float left, float right, float top, float height)
        {
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2((left - right) / 2f, top);
            rect.sizeDelta = new Vector2(-(left + right), height);
        }

        /// <summary>Anchors a child to a horizontal band of its parent, from <paramref name="yMin"/> to <paramref name="yMax"/> (0..1).</summary>
        static void Band(RectTransform rect, float yMin, float yMax, float left, float right)
        {
            rect.anchorMin = new Vector2(0, yMin);
            rect.anchorMax = new Vector2(1, yMax);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, 0);
            rect.offsetMax = new Vector2(-right, 0);
        }

        /// <summary>Top band of the parent at a fixed height, full width minus the insets, as a layout.</summary>
        static FinikRectLayout TopBand(float left, float right, float top, float height) => new()
        {
            anchorMin = new Vector2(0, 1),
            anchorMax = new Vector2(1, 1),
            pivot = new Vector2(0.5f, 1),
            anchoredPosition = new Vector2((left - right) / 2f, -top),
            sizeDelta = new Vector2(-(left + right), height)
        };

        static FinikRectLayout BottomBand(float height) => new()
        {
            anchorMin = Vector2.zero,
            anchorMax = new Vector2(1, 0),
            pivot = new Vector2(0.5f, 0),
            anchoredPosition = Vector2.zero,
            sizeDelta = new Vector2(0, height)
        };

        // ------------------------------------------------------------------ hub

        /// <summary>
        /// The hub is a room menu like the food, quest and savings screens, and wears the same kit: the
        /// light glass card with the paw, the picture-tag-title header, a hint line, inner cards for the
        /// choices and the blue "Назад" at the bottom. Beside Finik in landscape, under him in portrait,
        /// so the pet whose mood the games lift stays in view. Only the levels themselves switch to the
        /// games' own dark kit — a board is a place to play, not a menu.
        /// </summary>
        static void BuildHub(Transform safe, FinikGamesScreen screen)
        {
            var panelRoot = PanelRoot(safe, "Hub", out var panel);
            SetField(screen, "hubPanel", panel);

            var column = Rect("Column", panelRoot);
            column.gameObject.AddComponent<FinikFitInside>();
            column.gameObject.AddComponent<FinikOrientationLayout>().Configure(
                FinikRectLayout.At(LeftMiddle, LeftMiddle, new Vector2(72, 0), new Vector2(HubWidthLandscape, HubHeight)),
                FinikRectLayout.At(BottomCenter, BottomCenter, new Vector2(0, 36), new Vector2(HubWidthPortrait, HubHeight)));
            PanelCard(column);

            var content = Rect("Content", column);
            Stretch(content, HubPadX, HubPadY, HubPadX, HubPadY);
            var stack = content.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.spacing = HubSpacing;
            stack.childAlignment = TextAnchor.UpperCenter;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;

            BuildHubHeader(content, screen);
            BuildHubMood(content, screen);

            var cards = new Object[FinikMiniGameCatalog.Games.Count];
            for (int i = 0; i < cards.Length; i++) cards[i] = BuildGameCard(content, FinikMiniGameCatalog.Games[i]);
            SetField(screen, "cards", cards);

            var buttons = Rect("Buttons", content);
            Size(buttons, height: HubBack);
            var row = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            var back = WideButton(buttons, "Back", "ui_btn_white", "Назад", 32, Ink, HubBack, 320);
            SetField(screen, "hubCloseButton", back);
            // A tap on the room beside the card closes it too, like every other room menu.
            Dismiss(panel, back);
            panelRoot.gameObject.SetActive(false);
        }

        /// <summary>Picture, small caps tag and title, and the hint line under them — the savings screen's header.</summary>
        static void BuildHubHeader(Transform content, FinikGamesScreen screen)
        {
            var header = Rect("Header", content);
            Size(header, height: HubHeader);
            var icon = Img("Icon", header, "icon_games", preserveAspect: true);
            Place(icon.rectTransform, LeftMiddle, LeftMiddle, Vector2.zero, new Vector2(104, 104));
            icon.gameObject.AddComponent<FinikIdleMotion>().Configure(3f, 3f, 0f, 1.8f, 0f);

            var tag = Text("Tag", header, "ИГРЫ", 24, GamesTag, TextAlignmentOptions.BottomLeft, outlined: false);
            tag.rectTransform.anchorMin = new Vector2(0, 0.66f);
            tag.rectTransform.anchorMax = Vector2.one;
            tag.rectTransform.offsetMin = new Vector2(124, 0);
            tag.rectTransform.offsetMax = new Vector2(0, -2);
            tag.characterSpacing = 4;

            var title = Text("Title", header, "Поиграем с Фиником?", 40, Ink, TextAlignmentOptions.TopLeft, outlined: false);
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = new Vector2(1, 0.66f);
            title.rectTransform.offsetMin = new Vector2(124, -4);
            title.rectTransform.offsetMax = Vector2.zero;
            AutoSize(title, 26, 40);
            SetField(screen, "hubTitle", title);

            var hint = Text("Hint", content, "Играй и поднимай Финику настроение", 27, InkSoft, TextAlignmentOptions.MidlineLeft, outlined: false);
            Size(hint, height: HubHint);
            AutoSize(hint, 20, 27);
            SetField(screen, "hubSubtitle", hint);
        }

        /// <summary>
        /// How Finik feels, on an inner card of its own with a bar: the reason the games exist, so it
        /// gets the card's width — not a small chip in a corner.
        /// </summary>
        static void BuildHubMood(Transform content, FinikGamesScreen screen)
        {
            var strip = InnerCard("Mood", content);
            Size(strip, height: HubMood);
            var rt = strip.rectTransform;

            var icon = Img("Icon", rt, "icon_mood", preserveAspect: true);
            Place(icon.rectTransform, LeftMiddle, LeftMiddle, new Vector2(18, 0), new Vector2(66, 66));

            var label = Text("Label", rt, "Настроение Финика", 24, InkSoft, TextAlignmentOptions.BottomLeft, outlined: false);
            Band(label.rectTransform, 0.5f, 1f, 100, 176);
            label.rectTransform.offsetMin = new Vector2(100, 0);
            label.rectTransform.offsetMax = new Vector2(-176, -10);
            AutoSize(label, 18, 24);

            // Thick and on a light track: the mood is the meta-game's main resource, not a settings slider.
            var bar = Bar("Bar", rt, "#FFB938", "ui_track_light");
            var barRect = bar.GetComponent<RectTransform>();
            Band(barRect, 0f, 0.5f, 100, 176);
            barRect.offsetMin = new Vector2(100, 12);
            barRect.offsetMax = new Vector2(-176, 4);
            SetField(screen, "moodBar", bar);

            var value = Text("Value", rt, "55 / 100", 44, Ink, TextAlignmentOptions.MidlineRight, outlined: false);
            value.rectTransform.anchorMin = new Vector2(1, 0);
            value.rectTransform.anchorMax = new Vector2(1, 1);
            value.rectTransform.pivot = new Vector2(1, 0.5f);
            value.rectTransform.anchoredPosition = new Vector2(-24, 0);
            value.rectTransform.sizeDelta = new Vector2(150, 0);
            value.richText = true;
            AutoSize(value, 26, 44);
            SetField(screen, "moodValue", value);
        }

        /// <summary>An inner card of the app's kit, like the goal tiles of the savings screen.</summary>
        static Image InnerCard(string name, Transform parent, bool raycast = false)
        {
            var card = Img(name, parent, "ui_card", sliced: true, raycast: raycast);
            // Inner cards use a tighter corner than the big panel, as the goal tiles do.
            card.pixelsPerUnitMultiplier *= 1.6f;
            return card;
        }

        static FinikGameCardView BuildGameCard(Transform list, FinikMiniGame game)
        {
            var card = InnerCard("Game_" + game.Id, list, raycast: true);
            var element = card.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = element.minHeight = HubCard;
            element.flexibleHeight = 0f;
            var rt = card.rectTransform;

            // Picture on the left, words beside it, the three steps along the bottom.
            float top = HubChip + 30;
            var icon = Img("Icon", rt, game.Icon, preserveAspect: true);
            icon.rectTransform.anchorMin = new Vector2(0, 0);
            icon.rectTransform.anchorMax = new Vector2(0, 1);
            icon.rectTransform.pivot = new Vector2(0, 0.5f);
            icon.rectTransform.offsetMin = new Vector2(18, top - 4);
            icon.rectTransform.offsetMax = new Vector2(18 + HubPicture, -10);
            icon.gameObject.AddComponent<FinikIdleMotion>().Configure(3f, 3f, 0f, 1.8f, game.Id.Length);

            var texts = Rect("Texts", rt);
            texts.anchorMin = Vector2.zero;
            texts.anchorMax = Vector2.one;
            texts.offsetMin = new Vector2(HubPicture + 34, top);
            texts.offsetMax = new Vector2(-24, -12);
            var words = texts.gameObject.AddComponent<VerticalLayoutGroup>();
            words.spacing = 2;
            words.childAlignment = TextAnchor.MiddleLeft;
            words.childControlWidth = words.childControlHeight = true;
            words.childForceExpandWidth = true;
            words.childForceExpandHeight = false;

            var title = Text("Title", texts, game.Title, 36, Ink, TextAlignmentOptions.BottomLeft, outlined: false);
            Size(title, height: 46);
            AutoSize(title, 26, 36);

            var subtitle = Text("Subtitle", texts, game.Subtitle, 25, InkSoft, TextAlignmentOptions.TopLeft, outlined: false);
            Wrap(subtitle);
            Size(subtitle, height: 68);
            AutoSize(subtitle, 20, 25);

            var chipRow = Rect("Levels", rt);
            chipRow.anchorMin = new Vector2(0, 0);
            chipRow.anchorMax = new Vector2(1, 0);
            chipRow.pivot = new Vector2(0.5f, 0);
            chipRow.anchoredPosition = new Vector2(0, 18);
            chipRow.sizeDelta = new Vector2(-36, HubChip);
            var row = chipRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 12;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = !game.Endless;
            row.childForceExpandHeight = true;

            // An endless game has no steps to pick from: the level the child is on, and «Играть».
            var chips = game.Endless ? System.Array.Empty<FinikGameCardView.LevelChip>() : new FinikGameCardView.LevelChip[FinikMiniGameCatalog.LevelCount];
            for (int i = 0; i < chips.Length; i++) chips[i] = BuildLevelChip(chipRow, FinikMiniGameCatalog.Level(game, i + 1));
            var next = game.Endless ? BuildNextLevel(chipRow, game) : default;

            var view = card.gameObject.AddComponent<FinikGameCardView>();
            // Green is "play this", a light tile with a medal is "done", a faded tile with a lock is
            // "not yet". The blue candy stays for «Назад» alone, so it never reads as a fourth level.
            view.Configure(game.Id, icon, title, subtitle, chips, next);
            SetField(view, "lockedTint", new Color(1f, 1f, 1f, 0.5f));
            SetField(view, "labelOnButton", FinikUiKit.OutlinedMaterial);
            SetField(view, "labelOnTile", FinikUiKit.Font.material);
            return view;
        }

        /// <summary>
        /// The endless game's row: a light tile with the current level and its reward, and the one
        /// green button with a soft glow — the same accent the next step of the other games wears.
        /// </summary>
        static FinikGameCardView.NextLevel BuildNextLevel(Transform row, FinikMiniGame game)
        {
            var level = game.Level(1);
            var info = InnerCard("Level", row);
            var infoLayout = info.gameObject.AddComponent<LayoutElement>();
            infoLayout.flexibleWidth = 1f;
            var label = Text("Label", info.rectTransform, level.Heading, 28, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            Stretch(label.rectTransform, 22, 8, 92, 0);
            AutoSize(label, 18, 28);
            var reward = Rect("Reward", info.rectTransform);
            Place(reward, RightMiddle, RightMiddle, new Vector2(-12, 3), new Vector2(74, 44));
            var mood = Img("Icon", reward, "icon_mood", preserveAspect: true);
            Place(mood.rectTransform, LeftMiddle, LeftMiddle, Vector2.zero, new Vector2(30, 30));
            var amount = Text("Value", reward, $"+{level.Mood}", 28, Ink, TextAlignmentOptions.MidlineRight, outlined: false);
            Stretch(amount.rectTransform, 31, 2, 0, 0);

            var slot = Rect("Play", row);
            var slotLayout = slot.gameObject.AddComponent<LayoutElement>();
            slotLayout.preferredWidth = slotLayout.minWidth = 300f;
            slotLayout.flexibleWidth = 0f;
            slot.gameObject.AddComponent<FinikPressFeedback>();
            var glow = Img("Glow", slot, "hud_glow", sliced: true, color: new Color(0.45f, 1f, 0.5f, 0.55f));
            Stretch(glow.rectTransform, -22, -22, -22, -18);
            glow.gameObject.AddComponent<FinikIdleMotion>().Configure(0f, 0f, 0.035f, 1.1f, 0);
            var face = Img("Face", slot, "ui_btn_green", sliced: true, raycast: true);
            Stretch(face.rectTransform);
            var play = Text("Label", slot, "Играть", 32, Color.white, TextAlignmentOptions.Midline);
            Stretch(play.rectTransform, 12, 6, 12, 0);
            AutoSize(play, 22, 32);

            return new FinikGameCardView.NextLevel { play = MakeButton(slot, face), title = label, reward = amount };
        }

        /// <summary>
        /// One difficulty step: a medal inside the button once cleared, the name, and what it pays as the
        /// mood icon with the amount — the same icon as the mood card above, so "+3" reads as mood.
        /// </summary>
        static FinikGameCardView.LevelChip BuildLevelChip(Transform row, FinikMiniGameLevel level)
        {
            var slot = Rect("Level" + level.Number, row);
            slot.gameObject.AddComponent<FinikPressFeedback>();

            // A soft green light behind the step to play next, breathing slowly: "this one".
            var glow = Img("Glow", slot, "hud_glow", sliced: true, color: new Color(0.45f, 1f, 0.5f, 0.55f));
            Stretch(glow.rectTransform, -22, -22, -22, -18);
            glow.gameObject.AddComponent<FinikIdleMotion>().Configure(0f, 0f, 0.035f, 1.1f, level.Number);
            glow.gameObject.SetActive(false);

            var tile = InnerCard("Tile", slot, raycast: true);
            Stretch(tile.rectTransform);
            var face = Img("Face", slot, "ui_btn_green", sliced: true, raycast: true);
            Stretch(face.rectTransform);

            var done = Img("Done", slot, "game_medal", preserveAspect: true);
            Place(done.rectTransform, LeftMiddle, LeftMiddle, new Vector2(6, 2), new Vector2(48, 48));
            done.gameObject.SetActive(false);

            // The name gets whatever the reward leaves, and shrinks before it can touch it.
            var label = Text("Label", slot, level.Title, 28, Color.white, TextAlignmentOptions.MidlineLeft);
            Stretch(label.rectTransform, 22, 8, 92, 0);
            AutoSize(label, 16, 28);

            var reward = Rect("Reward", slot);
            Place(reward, RightMiddle, RightMiddle, new Vector2(-12, 3), new Vector2(74, 44));
            var mood = Img("Icon", reward, "icon_mood", preserveAspect: true);
            Place(mood.rectTransform, LeftMiddle, LeftMiddle, Vector2.zero, new Vector2(30, 30));
            var amount = Text("Value", reward, $"+{level.Mood}", 28, Color.white, TextAlignmentOptions.MidlineRight);
            Stretch(amount.rectTransform, 31, 2, 0, 0);

            var locked = Img("Locked", slot, "game_lock", preserveAspect: true);
            Place(locked.rectTransform, RightMiddle, RightMiddle, new Vector2(-18, 3), new Vector2(38, 38));
            locked.gameObject.SetActive(false);

            return new FinikGameCardView.LevelChip
            {
                button = MakeButton(slot, face),
                face = face,
                tile = tile,
                glow = glow.gameObject,
                label = label,
                rewardGroup = reward.gameObject,
                rewardIcon = mood.gameObject,
                reward = amount,
                done = done.gameObject,
                locked = locked.gameObject
            };
        }

        // ------------------------------------------------------------------ level

        /// <summary>
        /// The level takes the whole screen as one composition. Portrait, top to bottom: the header,
        /// the goal with its progress and the counter, the board, and the powers right under it.
        /// Landscape: the header across the top, the board on the left, and the goal, the counter and
        /// the powers in a column on the right — the board keeps the full height it needs.
        /// </summary>
        static void BuildPlay(Transform safe, FinikGamesScreen screen, FinikGameArt art)
        {
            var panelRoot = PanelRoot(safe, "Play", out var panel);
            SetField(screen, "playPanel", panel);

            // Oversized on purpose: the panel slides in from an offset, and no strip of the room may
            // show along an edge — or stay tappable — while the board is on its way.
            var wash = Rect("Wash", panelRoot);
            Stretch(wash, -400, -400, -400, -400);
            var washImage = wash.gameObject.AddComponent<Image>();
            washImage.color = new Color(0.05f, 0.07f, 0.22f, 0.94f);
            washImage.raycastTarget = true;

            var column = Rect("Column", panelRoot);
            Stretch(column);
            var back = Img("Back", column, "gameui_bg", raycast: true);
            Stretch(back.rectTransform);

            var content = Rect("Content", column);
            Stretch(content, PadSide, PadBottom, PadSide, PadTop);

            BuildPlayHud(content, screen);
            BuildGoal(content, screen);
            BuildCounter(content, screen);
            BuildIntro(content, screen);

            var dock = BuildDock(content, screen);

            var board = Rect("Board", content);
            Orient(board,
                Inset(0, 0, SideWidth + 24, HudHeight + Gap),
                Inset(0, DockHeight + Gap, 0, HudHeight + Gap + GoalHeight + Gap));
            BuildBoardArea(board, screen, art);
            // The dock draws over the board's edge, never under a piece that bounced past it.
            dock.SetAsLastSibling();
            panelRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// Name of the game, its difficulty as a status line (a coloured word, not a button),
        /// what winning pays, and a quiet way out.
        /// </summary>
        static void BuildPlayHud(RectTransform content, FinikGamesScreen screen)
        {
            var hud = Rect("Hud", content);
            AnchorTop(hud, 0, 0, 0, HudHeight);

            var title = Text("Title", hud, "Три в ряд", 54, Color.white, TextAlignmentOptions.TopLeft);
            Place(title.rectTransform, TopLeft, TopLeft, new Vector2(4, 0), new Vector2(560, 62));
            AutoSize(title, 34, 54);
            SetField(screen, "playTitle", title);


            var difficulty = Text("Difficulty", hud, "Сложный уровень", 28, Color.white, TextAlignmentOptions.MidlineLeft);
            Place(difficulty.rectTransform, BottomLeft, LeftMiddle, new Vector2(6, 22), new Vector2(460, 40));
            AutoSize(difficulty, 20, 28);
            SetField(screen, "playDifficulty", difficulty);

            // What winning pays: the mood icon, the amount, and the words "за победу" so it is not
            // mistaken for a score.
            var reward = Img("Prize", hud, "gameui_chip", sliced: true);
            Place(reward.rectTransform, RightMiddle, RightMiddle, new Vector2(-(CloseSize + 18), 0), new Vector2(236, 88));
            var mood = Img("Icon", reward.rectTransform, "icon_mood", preserveAspect: true);
            Place(mood.rectTransform, LeftMiddle, LeftMiddle, new Vector2(14, 0), new Vector2(56, 56));
            var value = Text("Value", reward.rectTransform, "+9", 40, RewardGreen, TextAlignmentOptions.BottomLeft);
            Band(value.rectTransform, 0.42f, 1f, 82, 14);
            AutoSize(value, 26, 40);
            SetField(screen, "playReward", value);
            var caption = Text("Caption", reward.rectTransform, "за победу", 22, Soft, TextAlignmentOptions.TopLeft, outlined: false);
            Band(caption.rectTransform, 0f, 0.44f, 84, 14);
            caption.rectTransform.offsetMin = new Vector2(84, 4);

            SetField(screen, "playCloseButton", CloseButton(hud, RightMiddle, Vector2.zero, CloseSize));
        }

        /// <summary>
        /// What to do in words ("Найди 8 пар"), how far it is as a bar and a number. The bar is the
        /// feeling of progress the counter alone did not give.
        /// </summary>
        static void BuildGoal(RectTransform content, FinikGamesScreen screen)
        {
            var goal = Img("Goal", content, "gameui_chip", sliced: true);
            Orient(goal.rectTransform,
                FinikRectLayout.At(TopRight, TopRight, new Vector2(0, -(HudHeight + Gap)), new Vector2(SideWidth, GoalHeight)),
                TopBand(0, CounterWidth + Gap, HudHeight + Gap, GoalHeight));
            var rt = goal.rectTransform;

            var icon = Img("Icon", rt, "icon_games", preserveAspect: true);
            Place(icon.rectTransform, LeftMiddle, LeftMiddle, new Vector2(18, 0), new Vector2(78, 78));
            SetField(screen, "goalIcon", icon);

            var text = Text("Text", rt, "Собери 50 кубиков", 30, Color.white, TextAlignmentOptions.BottomLeft);
            Band(text.rectTransform, 0.5f, 1f, 112, 26);
            text.rectTransform.offsetMin = new Vector2(112, 0);
            text.rectTransform.offsetMax = new Vector2(-26, -12);
            AutoSize(text, 20, 30);
            SetField(screen, "goalText", text);
            BuildGoalItems(rt, screen);

            var bar = Bar("Bar", rt, "#6FD97B", "gameui_track");
            var barRect = bar.GetComponent<RectTransform>();
            Band(barRect, 0f, 0.5f, 112, 134);
            barRect.offsetMin = new Vector2(112, 18);
            barRect.offsetMax = new Vector2(-134, -4);
            SetField(screen, "goalBar", bar);

            var value = Text("Value", rt, "0/50", 34, Color.white, TextAlignmentOptions.MidlineRight);
            value.rectTransform.anchorMin = new Vector2(1, 0);
            value.rectTransform.anchorMax = new Vector2(1, 0.5f);
            value.rectTransform.pivot = new Vector2(1, 0.5f);
            value.rectTransform.anchoredPosition = new Vector2(-26, 6);
            value.rectTransform.sizeDelta = new Vector2(104, 0);
            AutoSize(value, 22, 34);
            SetField(screen, "goalValue", value);
        }

        /// <summary>
        /// Several goals at once (crates, ice and a colour): a row of pictures, each with what is left
        /// of it and a tick once it is done, where the one-line goal would be.
        /// </summary>
        static void BuildGoalItems(RectTransform goal, FinikGamesScreen screen)
        {
            const int count = 3;
            var row = Rect("Items", goal);
            Band(row, 0.5f, 1f, 112, 26);
            row.offsetMin = new Vector2(112, -2);
            row.offsetMax = new Vector2(-26, -8);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 18;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            var icons = new Object[count];
            var values = new Object[count];
            var checks = new Object[count];
            string check = SpriteOrNull("ui_check") ? "ui_check" : "food_combo_check";
            for (int i = 0; i < count; i++)
            {
                var item = Rect("Item" + i, row);
                var element = item.gameObject.AddComponent<LayoutElement>();
                element.preferredWidth = element.minWidth = 132;

                var icon = Img("Icon", item, "game_crate", preserveAspect: true);
                Place(icon.rectTransform, LeftMiddle, LeftMiddle, Vector2.zero, new Vector2(60, 60));
                icons[i] = icon;

                var value = Text("Value", item, "12", 38, Color.white, TextAlignmentOptions.MidlineLeft);
                Stretch(value.rectTransform, 66, 0, 0, 0);
                AutoSize(value, 24, 38);
                values[i] = value;

                var tick = Img("Done", item, check, preserveAspect: true, color: new Color(0.45f, 0.95f, 0.5f));
                Place(tick.rectTransform, LeftMiddle, LeftMiddle, new Vector2(66, 0), new Vector2(44, 44));
                tick.gameObject.SetActive(false);
                checks[i] = tick.gameObject;
            }
            row.gameObject.SetActive(false);
            SetField(screen, "goalItemsRow", row.gameObject);
            SetField(screen, "goalItemIcons", icons);
            SetField(screen, "goalItemValues", values);
            SetField(screen, "goalItemChecks", checks);
        }

        /// <summary>What is running out, with the word that says what it is: "14 ходов", "3 жизни".</summary>
        static void BuildCounter(RectTransform content, FinikGamesScreen screen)
        {
            var counter = Img("Counter", content, "gameui_chip", sliced: true);
            Orient(counter.rectTransform,
                FinikRectLayout.At(TopRight, TopRight, new Vector2(0, -(HudHeight + Gap + GoalHeight + Gap)), new Vector2(SideWidth, SideCounterHeight)),
                FinikRectLayout.At(TopRight, TopRight, new Vector2(0, -(HudHeight + Gap)), new Vector2(CounterWidth, GoalHeight)));
            var rt = counter.rectTransform;

            var icon = Img("Icon", rt, "game_moves", preserveAspect: true);
            Place(icon.rectTransform, LeftMiddle, LeftMiddle, new Vector2(18, 0), new Vector2(72, 72));
            SetField(screen, "counterIcon", icon);

            var value = Text("Value", rt, "22", 50, Color.white, TextAlignmentOptions.BottomLeft);
            Band(value.rectTransform, 0.38f, 1f, 104, 16);
            value.rectTransform.offsetMax = new Vector2(-16, -6);
            AutoSize(value, 30, 50);
            SetField(screen, "counterValue", value);

            var label = Text("Label", rt, "ходов", 24, Soft, TextAlignmentOptions.TopLeft, outlined: false);
            Band(label.rectTransform, 0f, 0.4f, 106, 16);
            label.rectTransform.offsetMin = new Vector2(106, 8);
            AutoSize(label, 18, 24);
            SetField(screen, "counterLabel", label);
        }

        /// <summary>How to play, laid over the goal and the counter while a level opens, then gone.</summary>
        static void BuildIntro(RectTransform content, FinikGamesScreen screen)
        {
            var banner = Img("Intro", content, "gameui_bar", sliced: true);
            Orient(banner.rectTransform,
                // Exactly over the goal row it replaces for a moment: anything smaller let the edges of the
                // cards under it peek out above and below.
                FinikRectLayout.At(TopRight, TopRight, new Vector2(0, -(HudHeight + Gap)), new Vector2(SideWidth, GoalHeight + Gap + SideCounterHeight)),
                TopBand(0, 0, HudHeight + Gap, GoalHeight));
            var group = banner.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            var text = Text("Text", banner.rectTransform, "Проведи кубик к соседнему — собирай по три в ряд", 30, Ink, TextAlignmentOptions.Midline, outlined: false);
            Stretch(text.rectTransform, 36, 8, 36, 8);
            Wrap(text);
            AutoSize(text, 22, 32);
            banner.gameObject.SetActive(false);
            SetField(screen, "introBanner", group);
            SetField(screen, "introText", text);
        }

        /// <summary>
        /// The playing field: everything the goal row and the powers leave. A match-3 board grows extra
        /// rows to fill it; the memory grid picks the column count that gives the biggest cards; the
        /// catch shaft takes the full height.
        /// </summary>
        static void BuildBoardArea(RectTransform board, FinikGamesScreen screen, FinikGameArt art)
        {
            var fxRect = Rect("Fx", board);
            Stretch(fxRect);
            var fx = fxRect.gameObject.AddComponent<FinikGameFx>();
            fx.Configure(SpriteOrNull("ui_piece"), SpriteOrNull("ui_decor_star_small"), SpriteOrNull("hud_glow_round"),
                FinikUiKit.Font, FinikUiKit.OutlinedMaterial);
            SetField(screen, "boardFx", fx);

            SetField(screen, "memoryBoard", BuildBoard<FinikMemoryBoard>(board, "MemoryBoard", art, fx));
            SetField(screen, "match3Board", BuildBoard<FinikMatch3Board>(board, "Match3Board", art, fx));
            // The catch board clips its own shaft; drops never leave it.
            SetField(screen, "catchBoard", BuildBoard<FinikCatchBoard>(board, "CatchBoard", art, fx));
            // Last, so the effects draw over the pieces.
            fxRect.SetAsLastSibling();
        }

        /// <summary>The three powers on one lit shelf, each with its name under it.</summary>
        static RectTransform BuildDock(RectTransform content, FinikGamesScreen screen)
        {
            var dock = Img("Dock", content, "gameui_dock", sliced: true);
            Orient(dock.rectTransform,
                FinikRectLayout.At(BottomRight, BottomRight, Vector2.zero, new Vector2(SideWidth, DockHeight)),
                BottomBand(DockHeight));
            var row = dock.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(10, 10, 12, 8);
            row.spacing = 0;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            // Every slot gets a third of the shelf, so the three spread evenly on a phone and still
            // fit the narrow side column in landscape.
            row.childForceExpandWidth = row.childForceExpandHeight = true;

            var slots = new Object[FinikBoosterCatalog.ChargesPerLevel];
            for (int i = 0; i < slots.Length; i++) slots[i] = BuildBoosterSlot(dock.rectTransform, i);
            SetField(screen, "boosterSlots", slots);
            return dock.rectTransform;
        }

        static FinikBoosterSlotView BuildBoosterSlot(Transform row, int index)
        {
            var slot = Rect("Booster" + index, row);

            var ring = Rect("Ring", slot);
            Place(ring, TopCenter, TopCenter, new Vector2(0, -2), new Vector2(BoosterRing, BoosterRing));
            ring.gameObject.AddComponent<FinikPressFeedback>();
            // "Plate", not "Face": the charges sit over its rim on purpose.
            var face = Img("Plate", ring, "gameui_slot", raycast: true, preserveAspect: true);
            Stretch(face.rectTransform);

            var armed = Img("Armed", ring, "hud_glow_round", color: new Color(1f, 0.86f, 0.35f, 0.85f), preserveAspect: true);
            Stretch(armed.rectTransform, -28, -28, -28, -28);
            armed.gameObject.SetActive(false);

            // Fills the ring: the picture is what the player looks for, the ring is only its plate.
            var icon = Img("Icon", ring, "booster_hint", preserveAspect: true);
            Stretch(icon.rectTransform, 8, 8, 8, 8);
            var motion = icon.gameObject.AddComponent<FinikIdleMotion>();
            motion.Configure(2.2f, 5f, 0.04f, 2.2f, index);
            motion.enabled = false;

            // Charges sit on the ring's lower-right edge, the same size and spot on every slot.
            var badge = Img("Badge", ring, "gameui_badge", preserveAspect: true);
            Place(badge.rectTransform, TopRight, Center, new Vector2(-12, -12), new Vector2(46, 46));
            var count = Text("Count", badge.transform, "3", 26, Color.white, TextAlignmentOptions.Center, outlined: false);
            Stretch(count.rectTransform, 0, 2, 0, 0);

            var title = Text("Title", slot, "Подсказка", 26, new Color(0.9f, 0.93f, 1f), TextAlignmentOptions.Bottom, outlined: false);
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = new Vector2(1, 0);
            title.rectTransform.pivot = new Vector2(0.5f, 0);
            title.rectTransform.anchoredPosition = new Vector2(0, 2);
            title.rectTransform.sizeDelta = new Vector2(-8, 36);
            AutoSize(title, 16, 24);

            var view = slot.gameObject.AddComponent<FinikBoosterSlotView>();
            view.Configure(MakeButton(ring, face), icon, count, badge.gameObject, armed, motion, title);
            return view;
        }

        static T BuildBoard<T>(Transform parent, string name, FinikGameArt art, FinikGameFx fx) where T : FinikMiniGameBoard
        {
            var rect = Rect(name, parent);
            Stretch(rect);
            var board = rect.gameObject.AddComponent<T>();
            SetField(board, "art", art);
            SetField(board, "fx", fx);
            rect.gameObject.SetActive(false);
            return board;
        }

        // ------------------------------------------------------------------ result

        /// <summary>
        /// What the round gave, over a darkened screen, in the same kit as the hub and the other room
        /// menus: the light card with the paw, ink text, prizes on inner cards, the green way on and the
        /// blue way back. One hero — the game's picture on turning rays — and a garland over the edge
        /// for a win; a loss says why it was lost and what to try instead.
        /// </summary>
        static void BuildResult(Transform safe, FinikGamesScreen screen)
        {
            var panelRoot = PanelRoot(safe, "Result", out var panel);
            var view = panelRoot.gameObject.AddComponent<FinikGameResultView>();

            // Dark enough that the room behind stops competing with the card.
            var wash = Rect("Wash", panelRoot);
            Stretch(wash, -400, -400, -400, -400);
            var washImage = wash.gameObject.AddComponent<Image>();
            washImage.color = new Color(0.03f, 0.05f, 0.16f, 0.86f);
            washImage.raycastTarget = true;

            var column = Rect("Column", panelRoot);
            column.gameObject.AddComponent<FinikFitInside>().Configure(new Vector2(40f, 120f));
            Place(column, Center, Center, new Vector2(0, -20), new Vector2(ResultWidth, ResultHeight));
            PanelCard(column);

            var content = Rect("Content", column);
            Stretch(content, 40, 40, 40, 40);

            // The hero: the game's own picture on slowly turning rays.
            var hero = Rect("Hero", content);
            AnchorTop(hero, 0, 0, 0, 200);
            var rays = Img("Rays", hero, "game_rays", preserveAspect: true);
            Place(rays.rectTransform, Center, Center, Vector2.zero, new Vector2(420, 420));
            SetField(view, "rays", rays.rectTransform);
            var icon = Img("Icon", hero, "game_tile_match3", preserveAspect: true);
            Place(icon.rectTransform, Center, Center, Vector2.zero, new Vector2(190, 190));
            icon.gameObject.AddComponent<FinikIdleMotion>().Configure(3f, 5f, 0.015f, 1.8f, 0f);

            var title = Text("Title", content, "Уровень пройден!", 50, Ink, TextAlignmentOptions.Top, outlined: false);
            AnchorTop(title.rectTransform, 0, 0, -206, 62);
            AutoSize(title, 32, 50);

            var explanation = Text("Explanation", content, "…", 28, InkSoft, TextAlignmentOptions.Top, outlined: false);
            Wrap(explanation);
            AnchorTop(explanation.rectTransform, 24, 24, -270, 52);
            AutoSize(explanation, 20, 28);

            // The prizes: an inner card each, the amount big, the word for it under it.
            var shelf = Rect("Changes", content);
            AnchorTop(shelf, 0, 0, -330, 140);
            var row = shelf.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 24;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            var mood = RewardTile(shelf, "Mood", "icon_mood", "настроение", Good);
            var xp = RewardTile(shelf, "Xp", "icon_star", "опыт", Ink);

            var buttons = Rect("Buttons", content);
            buttons.anchorMin = new Vector2(0, 0);
            buttons.anchorMax = new Vector2(1, 0);
            buttons.pivot = new Vector2(0.5f, 0);
            buttons.anchoredPosition = Vector2.zero;
            buttons.sizeDelta = new Vector2(0, ButtonHeight + 8);
            var buttonRow = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            buttonRow.spacing = 20;
            buttonRow.childAlignment = TextAnchor.MiddleCenter;
            buttonRow.childControlWidth = buttonRow.childControlHeight = true;
            buttonRow.childForceExpandWidth = buttonRow.childForceExpandHeight = false;
            // The menus' own pair: the blue candy back, the green way on.
            var close = WideButton(buttons, "Close", "ui_btn_white", "К играм", 32, Ink, ButtonHeight, 260);
            var primary = WideButton(buttons, "Primary", "ui_btn_green", "Следующий уровень", 32, Color.white, ButtonHeight, 440);

            // One festive touch over the card's top edge, only on a win.
            var festive = Rect("Festive", column);
            Stretch(festive);
            Decor(festive, "game_garland", TopCenter, Center, new Vector2(0, -2), new Vector2(460, 124));
            SetField(view, "festive", festive.gameObject);

            var confettiLayer = Rect("Confetti", panelRoot);
            Stretch(confettiLayer);
            var confetti = confettiLayer.gameObject.AddComponent<FinikConfettiBurst>();
            SetField(confetti, "pieceSprite", SpriteOrNull("ui_piece"));

            view.Configure(panel, icon, title, explanation,
                mood.transform.parent.gameObject, mood, xp.transform.parent.gameObject, xp, shelf.gameObject,
                primary, primary.transform.Find("Label").GetComponent<TMP_Text>(),
                close, close.transform.Find("Label").GetComponent<TMP_Text>(), confetti);
            SetField(view, "column", column);
            SetField(view, "fullHeight", ResultHeight);
            SetField(view, "shelfHeight", ResultShelf);
            SetField(screen, "result", view);
            panelRoot.gameObject.SetActive(false);
        }

        /// <summary>One prize: icon and amount on an inner card, the word for it underneath. Returns the amount text.</summary>
        static TMP_Text RewardTile(Transform row, string id, string icon, string caption, Color color)
        {
            var tile = InnerCard(id, row);
            Size(tile, 230, 136);
            var rt = tile.rectTransform;
            var image = Img("Icon", rt, icon, preserveAspect: true);
            Place(image.rectTransform, TopLeft, Center, new Vector2(64, -54), new Vector2(64, 64));
            var value = Text("Value", rt, "+0", 50, color, TextAlignmentOptions.MidlineLeft, outlined: false);
            Place(value.rectTransform, TopLeft, LeftMiddle, new Vector2(104, -54), new Vector2(114, 64));
            value.textWrappingMode = TextWrappingModes.NoWrap;
            AutoSize(value, 30, 50);
            var word = Text("Caption", rt, caption, 24, InkSoft, TextAlignmentOptions.Midline, outlined: false);
            Place(word.rectTransform, BottomCenter, BottomCenter, new Vector2(0, 16), new Vector2(200, 34));
            return value;
        }
    }
}
