using Finik.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static Finik.Editor.UI.FinikUiKit;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Builds the in-Unity home HUD (profile with level, currencies, needs, settings, bottom dock, room
    /// hints) into the active scene. Re-running rebuilds the children of HUD_Home in place (the other
    /// screens keep their reference to it), so hand tweaks should be captured back into this builder
    /// (or into FinikOrientationLayout via its context menu) before rebuilding.
    /// </summary>
    public static class FinikHomeHudBuilder
    {
        const string RootName = "HUD_Home";
        const float Margin = 32f;
        // Portrait: profile (avatar + level), coins and savings share the top row; under the profile a
        // left column with the need rings; settings in the top-right corner under the savings pill.
        const float PortraitNeedsY = -Margin - 150 - 28;
        const float PortraitGaugeStep = GaugeSize + GaugeGap; // the percent label sits beside the ring
        [MenuItem("Finik/UI/Rebuild Home HUD")]
        public static void RebuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            if (!Prepare(out string message)) return message;

            var scene = EditorSceneManager.GetActiveScene();
            // In place: the food, quest and onboarding screens keep their reference to HUD_Home.
            var root = ReuseOrCreateCanvas(RootName, 20);
            GetOrAdd<FinikHudView>(root);
            GetOrAdd<FinikHudBinder>(root);
            var safe = Rect("SafeArea", root.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<FinikSafeArea>();

            var view = root.GetComponent<FinikHudView>();
            // The room hints go in first so every HUD widget draws over them: a bubble floating above
            // an object in the room must pass behind the need rings and the pills, not cover them.
            BuildRoomBubble(safe, "FridgeBubble", "fridge", FinikRoomBubble.Attention.Hunger, SpriteOrNull("icon_fridge") ? "icon_fridge" : "icon_food", new Color(1f, 0.78f, 0.3f, 0f));
            BuildRoomBubble(safe, "DeskBubble", "study-desk", FinikRoomBubble.Attention.OpenQuests, "icon_quest", new Color(0.72f, 0.55f, 1f, 0f));
            // The sofa: free games that lift the mood, so the hint pulses exactly when the mood sags.
            BuildRoomBubble(safe, "SofaBubble", "play-zone", FinikRoomBubble.Attention.LowMood,
                SpriteOrNull("icon_games") ? "icon_games" : "icon_mood", new Color(0.36f, 0.86f, 0.55f, 0f));
            // The profile card carries the level: star badge on the avatar, XP bar and count in the pill.
            BuildProfile(safe, out var profileButton, out var levelText, out var xpBar, out var xpText);
            // Top row (landscape): profile | need rings ... coins | gap | savings in the right corner. Each gap leaves room
            // for the next group's icon, which overhangs its pill by half its width.
            var coins = BuildCurrency(safe, "Coins", "icon_coin", 250, 0.15f,
                landscape: new Vector2(-Margin - SettingsReserve - 260 - CurrencyGap, -Margin - 37), portrait: new Vector2(466, -Margin - 37),
                out var coinCounter, out var addCoins, out var coinsButton);
            var savings = BuildCurrency(safe, "Savings", "icon_piggy_coin", 260, 0.2f,
                landscape: new Vector2(-Margin - SettingsReserve, -Margin - 37), portrait: new Vector2(788, -Margin - 37),
                out var savingsCounter, out var addSavings, out var savingsButton);
            BuildNeeds(safe, out var food, out var mood);
            // The need rings are indicators: a tap only shows their percentage. Feeding starts at the fridge
            // in the room (or its hint); there is no mail or side food button.
            // Settings sits in the top-right corner: last in the top row in landscape, under the savings
            // pill in portrait (the top row is full there). The inventory button is dropped for now.
            BuildRoundButton(safe, "Settings", "icon_settings", 0.3f,
                new Vector2(-Margin - 48, -Margin - 37 - 38), new Vector2(-Margin - 48, -Margin - 37 - 76 - 16 - 48),
                out var settingsButton, TopRight);
            var tabs = BuildDock(safe);
            BuildHomeCards(root, safe);

            view.Bind(null, levelText, xpBar, xpText, profileButton,
                coinCounter, savingsCounter, addCoins, addSavings,
                food, mood,
                null, null, null, settingsButton, null, tabs);
            // The counters are tap targets too, not just their «+» buttons.
            SetField(view, "coinsButton", coinsButton);
            SetField(view, "savingsButton", savingsButton);

            // Preview values so the edit-mode layout is representative.
            view.Apply(new FinikHudState
            {
                playerName = "FINIK", level = 5, xp = 120, xpToNextLevel = 200,
                coins = 600, savings = 1230, food = 0.7f, mood = 0.6f, unreadMail = 1
            }, animate: false);

            // Every button answers at least a 48 dp tap; the component is a no-op on the big ones.

            EnsureTapTargets(root);

            EnsureEventSystem();
            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;
            return $"Built {RootName} in {scene.path} with {Sprites.Count} sprites.";
        }

        // ------------------------------------------------------------------ sections

        /// <summary>Avatar with the level star on its corner and a pill with the XP bar and count (no name).</summary>
        static RectTransform BuildProfile(Transform parent, out Button button, out TMP_Text level, out FinikFillBar xpBar, out TMP_Text xpText)
        {
            var layout = FinikRectLayout.At(TopLeft, TopLeft, new Vector2(Margin, -Margin), new Vector2(360, 150));
            var (_, body) = Widget("Profile", parent, layout, layout, 0.05f);
            button = MakeButton(body, null);

            var pill = Img("LevelPill", body, "hud_pill_dark", sliced: true, raycast: true);
            Place(pill.rectTransform, LeftMiddle, LeftMiddle, new Vector2(96, 0), new Vector2(264, 92));
            button.targetGraphic = pill;
            // Inside the pill, in the free part right of the avatar ring: the count centred over the XP bar,
            // the pair centred on the pill's face (its thick bottom lip puts the face centre ~5 units up).
            const float xpLeft = 78f, xpWidth = 166f, faceCentre = 51f;
            xpText = Text("XpText", pill.transform, "120/200", 26, Color.white, TextAlignmentOptions.Midline);
            Place(xpText.rectTransform, BottomLeft, BottomLeft, new Vector2(xpLeft, faceCentre - 1), new Vector2(xpWidth, 36));
            // The art fill is already gold (tint white); the placeholder fill takes the gold tint.
            bool xpArt = System.IO.File.Exists("Assets/Finik/UI/Art/hud_xp_fill.png");
            xpBar = Bar("XpBar", pill.transform, xpArt ? "#FFFFFF" : "#FFC53A", 0f, "hud_xp_track", "hud_xp_fill", inset: 6f);
            Place((RectTransform)xpBar.transform, BottomLeft, BottomLeft, new Vector2(xpLeft, faceCentre - 32), new Vector2(xpWidth, 30));

            // Avatar inside a gold ring; the ring's hole is transparent, so the portrait shows through.
            var avatarCenter = new Vector2(75, 0);
            var avatar = Img("Avatar", body, "avatar_finik", raycast: true, preserveAspect: true);
            Place(avatar.rectTransform, LeftMiddle, Center, avatarCenter, new Vector2(136, 136));
            body.GetComponentInParent<FinikHudView>().BindAvatar(avatar);
            if (SpriteOrNull("hud_avatar_frame"))
            {
                var ring = Img("AvatarFrame", body, "hud_avatar_frame", preserveAspect: true);
                Place(ring.rectTransform, LeftMiddle, Center, avatarCenter, new Vector2(170, 170));
            }

            // Level badge pinned to the avatar's bottom-right corner, over the pill's edge; the number sits
            // in the shield's body, under the star on its rim.
            bool badgeArt = SpriteOrNull("hud_level_badge");
            var badge = Img("LevelBadge", body, badgeArt ? "hud_level_badge" : "icon_star", preserveAspect: true);
            Place(badge.rectTransform, LeftMiddle, Center, new Vector2(136, -42), badgeArt ? new Vector2(70, 76) : new Vector2(76, 76));
            badge.gameObject.AddComponent<FinikIdleMotion>().Configure(0f, 5f, 0f, 1.4f, 0f);
            level = Text("LevelNumber", badge.transform, "5", 32, Color.white, TextAlignmentOptions.Center);
            Stretch(level.rectTransform, 0, badgeArt ? 4 : 6, 0, badgeArt ? 18 : 0);
            return body;
        }

        static RectTransform BuildCurrency(Transform parent, string id, string icon, float width, float delay,
            Vector2 landscape, Vector2 portrait, out FinikCounterText counter, out Button add, out Button pillButton)
        {
            var size = new Vector2(width, 76);
            var (_, body) = Widget(id, parent,
                // Landscape: pinned to the right corner, so wide screens keep them there.
                FinikRectLayout.At(TopRight, TopRight, landscape, size),
                FinikRectLayout.At(TopLeft, TopLeft, portrait, size), delay);
            counter = CurrencyPill(body, id, icon, out var pillFace, rightInset: 68);
            pillButton = MakeButton(pillFace.rectTransform, pillFace);

            var plus = Img("AddButton", body, "hud_btn_plus", raycast: true);
            Place(plus.rectTransform, RightMiddle, RightMiddle, new Vector2(-8, 0), new Vector2(60, 60));
            add = MakeButton(plus.rectTransform, plus);
            plus.gameObject.AddComponent<FinikPressFeedback>();
            return body;
        }

        const float GaugeSize = 116f;
        // Between the coins and the savings pill: the piggy icon overhangs its pill on the left.
        const float CurrencyGap = 72f;
        // Room for the settings button right of the savings pill in the landscape top row.
        const float SettingsReserve = 96f + 24f;
        const float GaugeGap = 14f;
        const int NeedCount = 2;

        static void BuildNeeds(Transform parent, out FinikMeter food, out FinikMeter mood)
        {
            // Landscape: a row right of the profile card. Portrait: a column on the left edge, centred on
            // the side buttons' axis.
            var landscape = FinikRectLayout.At(TopLeft, TopLeft, new Vector2(Margin + 360 + 28, -Margin - (150 - GaugeSize) / 2f),
                new Vector2(GaugeSize * NeedCount + GaugeGap * (NeedCount - 1), GaugeSize));
            var portrait = FinikRectLayout.At(TopLeft, TopLeft, new Vector2(Margin + 62 - GaugeSize / 2f, PortraitNeedsY),
                new Vector2(GaugeSize, GaugeSize * NeedCount + (PortraitGaugeStep - GaugeSize) * (NeedCount - 1)));
            var (_, body) = Widget("Needs", parent, landscape, portrait, 0.1f);
            food = Gauge(body, 0, "Food", "icon_food", "#46E27A");
            mood = Gauge(body, 1, "Mood", "icon_mood", "#FFB12B");
        }

        static FinikMeter Gauge(Transform parent, int index, string id, string icon, string color)
        {
            var root = Rect(id, parent);
            root.gameObject.AddComponent<FinikOrientationLayout>().Configure(
                FinikRectLayout.At(LeftMiddle, LeftMiddle, new Vector2(index * (GaugeSize + GaugeGap), 0), new Vector2(GaugeSize, GaugeSize)),
                FinikRectLayout.At(TopCenter, TopCenter, new Vector2(0, -index * PortraitGaugeStep), new Vector2(GaugeSize, GaugeSize)));
            Stretch(Img("Disc", root, "hud_gauge_disc", raycast: true).rectTransform);
            Stretch(Img("Track", root, "hud_ring_track").rectTransform);
            var ring = Img("Ring", root, "hud_ring_fill", color: FinikSdfCanvas.Hex(color));
            Stretch(ring.rectTransform);
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.fillClockwise = true;

            var iconHolder = Rect("IconHolder", root);
            Place(iconHolder, Center, Center, Vector2.zero, new Vector2(70, 70));
            var punch = iconHolder.gameObject.AddComponent<FinikPressFeedback>();
            Stretch(Img("Icon", iconHolder, icon, preserveAspect: true).rectTransform);

            root.gameObject.AddComponent<FinikPressFeedback>();
            var percent = Text("Percent", root, "0%", 30, Color.white, TextAlignmentOptions.Center);
            // Under the ring in the landscape row, beside it in the portrait column.
            percent.gameObject.AddComponent<FinikOrientationLayout>().Configure(
                FinikRectLayout.At(BottomCenter, new Vector2(0.5f, 1f), new Vector2(0, -2), new Vector2(GaugeSize, 40)),
                FinikRectLayout.At(RightMiddle, LeftMiddle, new Vector2(6, 0), new Vector2(96, 40)));
            percent.alpha = 0f;
            var gauge = root.gameObject.AddComponent<FinikRingGauge>();
            gauge.Configure(ring, punch, 0.25f, percent);
            return gauge;
        }

        /// <param name="anchor">Screen corner the positions are measured from (top-left by default).</param>
        static RectTransform BuildRoundButton(Transform parent, string id, string icon, float delay, Vector2 landscape, Vector2 portrait, out Button button,
            Vector2? anchor = null)
        {
            var size = new Vector2(96, 96);
            var corner = anchor ?? TopLeft;
            var (_, body) = Widget(id, parent,
                FinikRectLayout.At(corner, Center, landscape, size),
                FinikRectLayout.At(corner, Center, portrait, size), delay);
            var bg = Img("Background", body, "hud_btn_round_light", raycast: true);
            Stretch(bg.rectTransform);
            button = MakeButton(body, bg);
            var iconImage = Img("Icon", body, icon, preserveAspect: true);
            Stretch(iconImage.rectTransform, 16, 18, 16, 14);
            return body;
        }

        // Four tabs on the 1080-wide portrait canvas: 4 x 190 + 3 x 18 = 814. There is no «Дом» tab —
        // the room is what the dock sits on, not a page of it, so closing a screen already goes home.
        const float TabWidth = 190f;
        const float TabSpacing = 18f;
        const float TabFaceHeight = 150f;
        const float TabLabelHeight = 44f;

        static FinikTabButton[] BuildDock(Transform parent)
        {
            var dock = Rect("Dock", parent);
            const int tabCount = 4;
            var size = new Vector2(TabWidth * tabCount + TabSpacing * (tabCount - 1), TabFaceHeight + TabLabelHeight);
            var layout = FinikRectLayout.At(BottomCenter, BottomCenter, new Vector2(0, 16), size);
            var portraitLayout = FinikRectLayout.At(BottomCenter, BottomCenter, new Vector2(0, 36), size);
            dock.gameObject.AddComponent<FinikOrientationLayout>().Configure(layout, portraitLayout);
            dock.gameObject.AddComponent<FinikHudObstacle>();
            var group = dock.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = TabSpacing;
            group.childAlignment = TextAnchor.MiddleCenter;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = group.childForceExpandHeight = true;

            return new[]
            {
                Tab(dock, FinikHudTab.Budget, "tab_budget", "Бюджет", "#E6F6FF", 0.36f),
                Tab(dock, FinikHudTab.Quests, "tab_quests", "Задания", "#FFE8E4", 0.42f),
                Tab(dock, FinikHudTab.Shopping, "tab_shopping", "Покупки", "#E8FFF0", 0.48f),
                Tab(dock, FinikHudTab.Savings, "tab_savings", "Копилка", "#F3EAFF", 0.54f)
            };
        }

        static FinikTabButton Tab(Transform dock, FinikHudTab tab, string face, string caption, string glowColor, float delay)
        {
            var slot = Rect("Tab_" + tab, dock);
            slot.gameObject.AddComponent<CanvasGroup>();
            slot.gameObject.AddComponent<FinikPopIn>().Configure(delay, 0.4f);

            var body = Rect("Body", slot);
            body.anchorMin = new Vector2(0, 1);
            body.anchorMax = new Vector2(1, 1);
            body.pivot = new Vector2(0.5f, 0.5f);
            body.sizeDelta = new Vector2(0, TabFaceHeight);
            body.anchoredPosition = new Vector2(0, -TabFaceHeight * 0.5f);
            var feedback = body.gameObject.AddComponent<FinikPressFeedback>();

            var glow = Img("SelectedGlow", body, "hud_glow", sliced: true, color: FinikSdfCanvas.Hex(glowColor, 0f));
            Stretch(glow.rectTransform, -6, 0, -6, -4);
            var faceImage = Img("Face", body, face, raycast: true, preserveAspect: true);
            Stretch(faceImage.rectTransform);
            var button = MakeButton(body, faceImage);
            var motion = faceImage.gameObject.AddComponent<FinikIdleMotion>();
            motion.Configure(4f, 0f, 0.015f, 2.4f, (int)tab);
            motion.enabled = false;

            var label = Text("Label", slot, caption, 30, Color.white, TextAlignmentOptions.Center);
            label.rectTransform.anchorMin = new Vector2(0, 0);
            label.rectTransform.anchorMax = new Vector2(1, 0);
            label.rectTransform.pivot = new Vector2(0.5f, 0);
            label.rectTransform.sizeDelta = new Vector2(0, TabLabelHeight);
            label.rectTransform.anchoredPosition = new Vector2(0, 6);

            var indicator = Img("Indicator", slot, "hud_bar_fill", sliced: true, color: FinikSdfCanvas.Hex("#FF9A2E", 0f));
            Place(indicator.rectTransform, BottomCenter, BottomCenter, new Vector2(0, -2), new Vector2(64, 10));
            indicator.pixelsPerUnitMultiplier *= 2.6f;

            var tabButton = slot.gameObject.AddComponent<FinikTabButton>();
            tabButton.Configure(tab, button, feedback, glow, motion, indicator, label);
            return tabButton;
        }

        // ------------------------------------------------------------------ goal and task of the day

        const float CardGap = 20f;
        const float GoalHeight = 170f;
        const float TaskHeight = 148f;
        // Landscape width of the goal and task cards (portrait works out its own from the canvas).
        // The task card is three columns — picture, text, action — and the reward chip rides the
        // title's line, so 132 + 210 + 214 + 24 of it is spoken for: at 620 the title had 40 units
        // left and was cut. The room and its floating hints still keep the right two thirds.
        const float CardsWidth = 860f;

        [InitializeOnLoadMethod]
        static void EnsureCurrentCardLayout()
        {
            EditorApplication.delayCall += TryUpgradeCardLayout;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += TryUpgradeCardLayout;
        }

        static void TryUpgradeCardLayout()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var hud = GameObject.Find("HUD_Home");
            if (!hud) return;
            var card = hud ? hud.transform.Find("SafeArea/TaskCard") as RectTransform : null;
            if (!card || Mathf.Abs(card.rect.height - TaskHeight) > 1f) Debug.Log(Build());
        }
        static readonly Color TaskInk = new(0.48f, 0.23f, 0.07f, 1f);

        /// <summary>
        /// Landscape: a left column under the profile card, the goal over the task of the day; the right
        /// side stays free for the fridge and desk hints that float over the room there. Portrait: the goal
        /// right of the need rings under the top row, the task above the dock. Mock data lives in FinikHomeCards.
        /// </summary>
        static void BuildHomeCards(GameObject root, Transform safe)
        {
            var cards = GetOrAdd<FinikHomeCards>(root);
            SetField(cards, "unknownGoalIcon", SpriteOrNull("goal_unknown"));
            float topRowBottom = -Margin - 150 - CardGap;
            // Portrait: between the need rings column and the settings button.
            float portraitGoalLeft = Margin + 62 + GaugeSize / 2f + 24;
            float portraitGoalWidth = 1080 - portraitGoalLeft - Margin - 96 - 20;

            var goal = BuildGoalCard(safe,
                FinikRectLayout.At(TopLeft, TopLeft, new Vector2(Margin, topRowBottom), new Vector2(CardsWidth, GoalHeight)),
                FinikRectLayout.At(TopLeft, TopLeft, new Vector2(portraitGoalLeft, PortraitNeedsY), new Vector2(portraitGoalWidth, GoalHeight)),
                out var goalIcon, out var goalTitle, out var goalBar, out var goalProgress, out var goalLeft);
            var task = BuildTaskCard(safe,
                FinikRectLayout.At(TopLeft, TopLeft, new Vector2(Margin, topRowBottom - GoalHeight - CardGap), new Vector2(CardsWidth, TaskHeight)),
                FinikRectLayout.At(BottomCenter, BottomCenter, new Vector2(0, 36 + TabFaceHeight + TabLabelHeight + 28), new Vector2(1080 - Margin * 2, TaskHeight)),
                out var taskTitle, out var taskDescription, out var taskReward);

            cards.Configure(goal, goalIcon, goalTitle, goalBar, goalProgress, goalLeft, task, taskTitle, taskDescription, taskReward);
            cards.SetGoal(new FinikGoalCardData
            {
                title = "Велосипед", icon = SpriteOrNull("goal_bike") ?? SpriteOrNull("quest_a_goal"), saved = 200, target = 500
            }, animate: false);
            cards.SetTask(new FinikDailyTaskData
            {
                title = "Задание дня", description = "Помоги Финику распланировать 50 монет", reward = 30
            });
        }

        static Button BuildGoalCard(Transform parent, FinikRectLayout landscape, FinikRectLayout portrait,
            out Image icon, out TMP_Text title, out FinikFillBar bar, out TMP_Text progress, out TMP_Text left)
        {
            var (_, body) = Widget("GoalCard", parent, landscape, portrait, 0.22f);
            var face = Img("Face", body, "ui_card", sliced: true, raycast: true);
            Stretch(face.rectTransform);
            face.pixelsPerUnitMultiplier *= 1.6f;
            var button = MakeButton(body, face);

            // Picture on a paler tile at the left.
            var tile = Img("IconTile", body, "ui_card", sliced: true, color: FinikSdfCanvas.Hex("#F4F9FF"));
            tile.pixelsPerUnitMultiplier *= 2.4f;
            Place(tile.rectTransform, LeftMiddle, LeftMiddle, new Vector2(16, 0), new Vector2(150, 136));
            icon = Img("Icon", tile.transform, "quest_a_goal", preserveAspect: true);
            Stretch(icon.rectTransform, 8, 8, 8, 8);
            icon.gameObject.AddComponent<FinikIdleMotion>().Configure(2f, 3f, 0.015f, 1.8f, 2f);

            const float textLeft = 186f, textRight = 64f;
            title = Text("Title", body, "Цель: Велосипед", 32, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            AutoSize(title, 24, 32);
            StretchRow(title.rectTransform, textLeft, textRight, -22, 42);

            bar = Bar("Bar", body, "#FFB12B", 0f);
            StretchRow((RectTransform)bar.transform, textLeft, textRight, -72, 32);

            progress = Text("Progress", body, "200 / 500", 26, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            StretchRow(progress.rectTransform, textLeft, textRight, -112, 34);
            left = Text("Left", body, "Осталось: 300", 24, InkSoft, TextAlignmentOptions.MidlineRight, outlined: false);
            StretchRow(left.rectTransform, textLeft, textRight, -112, 34);

            var chevron = Decor(body, "ui_decor_chevron", RightMiddle, Center, new Vector2(-34, 0), new Vector2(44, 44));
            if (chevron) chevron.gameObject.AddComponent<FinikIdleMotion>().Configure(4f, 0f, 0f, 1.2f, 0f);
            return button;
        }

        static Button BuildTaskCard(Transform parent, FinikRectLayout landscape, FinikRectLayout portrait,
            out TMP_Text title, out TMP_Text description, out TMP_Text reward)
        {
            var (_, body) = Widget("TaskCard", parent, landscape, portrait, 0.26f);
            // Warm glass with a gold rim, so it reads as "a quest", not as info.
            var face = Img("Face", body, "ui_card_task", sliced: true, raycast: true);
            Stretch(face.rectTransform);
            face.pixelsPerUnitMultiplier *= 1.6f;

            var icon = Img("Icon", body, "icon_quest", preserveAspect: true);
            Place(icon.rectTransform, LeftMiddle, LeftMiddle, new Vector2(12, 2), new Vector2(104, 104));
            icon.gameObject.AddComponent<FinikIdleMotion>().Configure(2f, 4f, 0.02f, 1.6f, 1f);
            Decor(body, "ui_spark_yellow", TopLeft, Center, new Vector2(112, -18), new Vector2(36, 36), 20f);

            // Picture, text and action columns. Stack the reward above the button so long quest
            // titles can use the full text column without running into the reward pill.
            const float textLeft = 132f;
            const float actionWidth = 214f;
            const float chipWidth = 144f;

            title = Text("Title", body, "Задание дня", 34, TaskInk, TextAlignmentOptions.MidlineLeft, outlined: false);
            AutoSize(title, 26, 34);
            StretchRow(title.rectTransform, textLeft, actionWidth + 18, -14, 42);

            var chip = Img("Reward", body, "ui_pill_gold", sliced: true);
            Place(chip.rectTransform, TopRight, TopRight, new Vector2(-42, -12), new Vector2(chipWidth, 48));
            reward = Text("Label", chip.transform, "+30", 24, TaskInk, TextAlignmentOptions.MidlineLeft, outlined: false);
            AutoSize(reward, 18, 24);
            Stretch(reward.rectTransform, 16, 2, 50, 0);
            var coin = Img("Coin", chip.transform, "icon_coin", preserveAspect: true);
            Place(coin.rectTransform, RightMiddle, RightMiddle, new Vector2(-6, 2), new Vector2(42, 42));

            description = Text("Description", body, "Помоги Финику распланировать 50 монет", 23, Ink, TextAlignmentOptions.TopLeft, outlined: false);
            Wrap(description);
            AutoSize(description, 17, 23);
            description.lineSpacing = 0;
            StretchRow(description.rectTransform, textLeft, actionWidth + 18, -58, 72);

            var start = Rect("Start", body);
            Place(start, BottomRight, BottomRight, new Vector2(-14, 12), new Vector2(actionWidth - 14, 64));
            var button = WideButton(start, "Button", "ui_btn_green", "Начать", 30, Color.white, 64, actionWidth - 14);
            Stretch((RectTransform)button.transform.parent);
            return button;
        }

        /// <summary>A row stretched between the left and right insets, its top <paramref name="top"/> below the parent's top.</summary>
        static void StretchRow(RectTransform rect, float left, float right, float top, float height)
        {
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.offsetMin = new Vector2(left, top - height);
            rect.offsetMax = new Vector2(-right, top);
        }

        // ------------------------------------------------------------------ widgets

        /// <summary>
        /// Root carries placement + pop-in (scale), Body carries press feedback (scale), so the two
        /// animations never overwrite each other.
        /// </summary>
        static (RectTransform root, RectTransform body) Widget(string name, Transform parent, FinikRectLayout landscape, FinikRectLayout portrait, float delay)
        {
            var root = Rect(name, parent);
            root.gameObject.AddComponent<FinikOrientationLayout>().Configure(landscape, portrait);
            root.gameObject.AddComponent<CanvasGroup>();
            root.gameObject.AddComponent<FinikPopIn>().Configure(delay);
            // Room hints slide around these instead of sitting on top of them.
            root.gameObject.AddComponent<FinikHudObstacle>();
            var body = Rect("Body", root);
            Stretch(body);
            body.gameObject.AddComponent<FinikPressFeedback>();
            return (root, body);
        }

        static FinikFillBar Bar(string name, Transform parent, string color, float lowThreshold,
            string trackSprite = "hud_bar_track", string fillSprite = "hud_bar_fill", float inset = 4f)
        {
            var track = Img(name, parent, trackSprite, sliced: true);
            var fill = Img("Fill", track.transform, fillSprite, sliced: true, color: FinikSdfCanvas.Hex(color));
            fill.rectTransform.pivot = new Vector2(0, 0.5f);
            fill.rectTransform.offsetMin = new Vector2(inset, inset);
            fill.rectTransform.offsetMax = new Vector2(-inset, -inset);
            var bar = track.gameObject.AddComponent<FinikFillBar>();
            SetField(bar, "fill", fill.rectTransform);
            SetField(bar, "fillImage", fill);
            SetField(bar, "lowThreshold", lowThreshold);
            return bar;
        }

        /// <summary>
        /// Round hint that follows a room object (fridge, study desk): the light round button with the
        /// object's icon and a pointer on the rim that FinikRoomBubble turns towards the object. Pivot in
        /// the centre. First children so every HUD control draws over them; the desk hint counts waiting quests.
        /// </summary>
        static void BuildRoomBubble(Transform safe, string name, string interactionId, FinikRoomBubble.Attention attention, string icon, Color glowColor)
        {
            var root = Rect(name, safe);
            root.SetAsFirstSibling();
            Place(root, Center, Center, Vector2.zero, new Vector2(150, 150));
            root.gameObject.AddComponent<CanvasGroup>();
            var body = Rect("Body", root);
            Stretch(body);
            var glow = Img("Glow", body, "hud_glow_round", color: glowColor);
            Stretch(glow.rectTransform, -60, -60, -60, -60);
            // Under the face, so the disc covers the pointer's base.
            var pointer = Img("Pointer", body, "hud_pointer", preserveAspect: true);
            Place(pointer.rectTransform, Center, Center, new Vector2(0, -70), new Vector2(60, 60));
            var face = Img("Face", body, "hud_btn_round_light", raycast: true, preserveAspect: true);
            Stretch(face.rectTransform);
            var iconImage = Img("Icon", body, icon, preserveAspect: true);
            Stretch(iconImage.rectTransform, 26, 28, 26, 24);
            body.gameObject.AddComponent<FinikPressFeedback>();
            var button = MakeButton(body, face);

            GameObject badge = null;
            TMP_Text count = null;
            if (attention == FinikRoomBubble.Attention.OpenQuests)
            {
                var badgeImage = Img("Badge", body, "hud_badge");
                Place(badgeImage.rectTransform, TopRight, Center, new Vector2(-20, -20), new Vector2(48, 48));
                count = Text("Count", badgeImage.transform, "4", 28, Color.white, TextAlignmentOptions.Center);
                Stretch(count.rectTransform, 0, 3, 0, 0);
                badge = badgeImage.gameObject;
            }
            root.gameObject.AddComponent<FinikRoomBubble>().Configure(interactionId, attention, button, body, glow, pointer.rectTransform, badge, count);
        }
    }
}
