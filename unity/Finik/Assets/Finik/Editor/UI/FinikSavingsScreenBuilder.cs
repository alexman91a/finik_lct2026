using System.Collections.Generic;
using System.Linq;
using Finik.Core;
using Finik.Navigation;
using Finik.UI;
using Finik.UI.Onboarding;
using Finik.UI.Savings;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static Finik.Editor.UI.FinikUiKit;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Builds the piggy bank screen (Screen_Savings) over the room: the goal with its progress and the
    /// "put aside" action, the goal picker and the take-back confirmation. Same column as the food and
    /// quest screens: beside Finik in landscape, under him in portrait.
    /// </summary>
    public static class FinikSavingsScreenBuilder
    {
        const string RootName = "Screen_Savings";
        const float Gap = 14f;
        const float PadX = 40f, PadY = 34f;
        const float HeaderHeight = 104f, ButtonsHeight = 112f, SecondaryHeight = 88f;
        const float HeroHeight = 300f, AmountsHeight = 130f, HintHeight = 64f;
        const float GoalCardHeight = 250f, GoalsHintHeight = 60f;
        const float ConfirmBodyHeight = 124f, ConfirmBarHeight = 112f;
        const float LandscapeWidth = 860f, PortraitWidth = 1010f;

        /// <summary>Coins one chip puts aside. Small first steps: saving must feel reachable every day.</summary>
        static readonly int[] AmountSteps = { 5, 10, 20 };

        static float MainHeight => 2 * PadY + HeaderHeight + HeroHeight + AmountsHeight + HintHeight + ButtonsHeight + SecondaryHeight + 5 * Gap;
        static float GoalsHeight => 2 * PadY + HeaderHeight + GoalsHintHeight + 2 * GoalCardHeight + 16 + ButtonsHeight + 3 * Gap;
        static float ConfirmHeight => 2 * PadY + HeaderHeight + ConfirmBodyHeight + ConfirmBarHeight + ButtonsHeight + 3 * Gap;

        static readonly Color Pink = new(0.9f, 0.3f, 0.52f, 1f);
        static readonly Color Warn = new(0.86f, 0.3f, 0.2f, 1f);

        [MenuItem("Finik/UI/Rebuild Savings Screen")]
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

            // Same layer as the food and quest screens: they never open together.
            var root = ReuseOrCreateCanvas(RootName, 25);
            var screen = GetOrAdd<FinikSavingsScreen>(root);
            var safe = Rect("SafeArea", root.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<FinikSafeArea>();

            BuildMain(safe, screen);
            BuildGoals(safe, screen);
            BuildConfirm(safe, screen);
            WireIcons(screen);

            SetField(screen, "showcase", showcase);
            SetField(screen, "movement", finik.GetComponent<FinikMovementController>());
            SetField(screen, "activity", finik.GetComponent<FinikActivityController>());
            SetField(screen, "hudRoot", hud);
            var paused = new List<Object>();
            foreach (var behaviour in new Behaviour[] { finik.GetComponent<FinikInputController>(), finik.GetComponent<FinikWanderController>() })
                if (behaviour) paused.Add(behaviour);
            SetField(screen, "pauseWhileOpen", paused.ToArray());

            var binder = hud.GetComponent<FinikHudBinder>();
            if (binder) SetField(binder, "savingsScreen", screen);

            // Every button answers at least a 48 dp tap; the component is a no-op on the big ones.
            EnsureTapTargets(root);

            EnsureEventSystem();
            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            int art = FinikGoalCatalog.Goals.Count(g => SpriteOrNull(g.Icon));
            return $"Built {RootName}: {FinikGoalCatalog.Goals.Count} goals, {art} with final art.";
        }

        // ------------------------------------------------------------------ shared

        /// <summary>Full-screen panel with the card column; returns the vertical content stack and the column.</summary>
        static RectTransform Panel(Transform safe, string name, float height, out FinikScreenPanel panel, out RectTransform column)
        {
            var panelRoot = Rect(name, safe);
            Stretch(panelRoot);
            panelRoot.gameObject.AddComponent<CanvasGroup>();
            panel = panelRoot.gameObject.AddComponent<FinikScreenPanel>();

            column = Rect("Column", panelRoot);
            column.gameObject.AddComponent<FinikFitInside>();
            column.gameObject.AddComponent<FinikOrientationLayout>().Configure(
                FinikRectLayout.At(LeftMiddle, LeftMiddle, new Vector2(72, 0), new Vector2(LandscapeWidth, height)),
                FinikRectLayout.At(BottomCenter, BottomCenter, new Vector2(0, 36), new Vector2(PortraitWidth, height)));
            PanelCard(column);
            var content = Rect("Content", column);
            Stretch(content, PadX, PadY, PadX, PadY);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = Gap;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panelRoot.gameObject.SetActive(false);
            return content;
        }

        /// <summary>Picture, small caps tag and title; the coin pill on the right when <paramref name="coins"/>.</summary>
        static TMP_Text Header(Transform content, string iconSprite, string tagText, Color tagColor, string titleText, bool coins, out FinikCounterText counter)
        {
            var row = Rect("Header", content);
            Size(row, height: HeaderHeight);
            var icon = Img("Icon", row, iconSprite, preserveAspect: true);
            Place(icon.rectTransform, LeftMiddle, LeftMiddle, Vector2.zero, new Vector2(104, 104));
            icon.gameObject.AddComponent<FinikIdleMotion>().Configure(3f, 3f, 0f, 1.8f, 0f);
            // The coin icon overhangs its pill on the left: keep the title clear of both.
            float right = coins ? 300 : 0;

            var tag = Text("Tag", row, tagText, 24, tagColor, TextAlignmentOptions.BottomLeft, outlined: false);
            tag.rectTransform.anchorMin = new Vector2(0, 0.66f);
            tag.rectTransform.anchorMax = Vector2.one;
            tag.rectTransform.offsetMin = new Vector2(124, 0);
            tag.rectTransform.offsetMax = new Vector2(-right, -2);
            tag.characterSpacing = 4;

            var title = Text("Title", row, titleText, 40, Ink, TextAlignmentOptions.TopLeft, outlined: false);
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = new Vector2(1, 0.66f);
            title.rectTransform.offsetMin = new Vector2(124, -4);
            title.rectTransform.offsetMax = new Vector2(-right, 0);
            AutoSize(title, 26, 40);

            counter = null;
            if (!coins) return title;
            var wallet = Rect("Coins", row);
            Place(wallet, RightMiddle, RightMiddle, new Vector2(0, 8), new Vector2(230, 76));
            var body = Rect("Body", wallet);
            Stretch(body);
            counter = CurrencyPill(body, "Coins", "icon_coin");
            return title;
        }

        static RectTransform Row(Transform content, string name, float height, float spacing = 18)
        {
            var row = Rect(name, content);
            Size(row, height: height);
            var group = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.childAlignment = TextAnchor.MiddleCenter;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = group.childForceExpandHeight = false;
            return row;
        }

        static TMP_Text LabelOf(Button button) => button.transform.Find("Label").GetComponent<TMP_Text>();

        /// <summary>Pill progress bar; <paramref name="bareTrack"/> hides the track so the bar can sit over another one.</summary>
        static FinikFillBar Bar(string name, Transform parent, string color, bool bareTrack = false)
        {
            var track = Img(name, parent, "hud_bar_track", sliced: true, color: bareTrack ? new Color(1, 1, 1, 0) : null);
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

        /// <summary>A preview bar (pale) with the real bar drawn over it, stretched across <paramref name="parent"/>'s row.</summary>
        static (FinikFillBar under, FinikFillBar over) DoubleBar(RectTransform parent, string underColor, string overColor)
        {
            var under = Bar("Preview", parent, underColor);
            Stretch((RectTransform)under.transform);
            var over = Bar("Bar", parent, overColor, bareTrack: true);
            Stretch((RectTransform)over.transform);
            return (under, over);
        }

        static void AnchorTop(RectTransform rect, float left, float right, float top, float height)
        {
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.offsetMin = new Vector2(left, top - height);
            rect.offsetMax = new Vector2(-right, top);
        }

        // ------------------------------------------------------------------ main

        static void BuildMain(Transform safe, FinikSavingsScreen screen)
        {
            var content = Panel(safe, "Main", MainHeight, out var panel, out var column);
            SetField(screen, "main", panel);
            // The wallet sits in the header: what can go into the piggy bank is always in sight.
            var title = Header(content, "icon_piggy_coin", "КОПИЛКА", Pink, "Коплю на велосипед", coins: true, out var walletCoins);
            SetField(screen, "title", title);
            SetField(screen, "walletCoins", walletCoins);

            // Round close button on the card's top-right corner (the header has no room for the wallet and it).
            var closeRoot = Rect("Close", column);
            Place(closeRoot, TopRight, Center, new Vector2(-18, -18), new Vector2(84, 84));
            closeRoot.gameObject.AddComponent<FinikPressFeedback>();
            var closeFace = Img("Face", closeRoot, "hud_btn_close", raycast: true, preserveAspect: true);
            Stretch(closeFace.rectTransform);
            var close = MakeButton(closeRoot, closeFace);
            SetField(screen, "closeButton", close);
            Dismiss(panel, close);

            BuildHero(content, screen);

            // «Сколько отложить?» and the chips under it.
            var amounts = Rect("Amounts", content);
            Size(amounts, height: AmountsHeight);
            SetField(screen, "amountsRoot", amounts.gameObject);
            var caption = Text("Caption", amounts, "СКОЛЬКО ОТЛОЖИТЬ?", 22, InkSoft, TextAlignmentOptions.BottomLeft, outlined: false);
            caption.characterSpacing = 4;
            AnchorTop(caption.rectTransform, 4, 4, 0, 34);

            var chips = Rect("Chips", amounts);
            AnchorTop(chips, 0, 0, -44, 86);
            var chipRow = chips.gameObject.AddComponent<HorizontalLayoutGroup>();
            chipRow.spacing = 18;
            chipRow.childAlignment = TextAnchor.MiddleLeft;
            chipRow.childControlWidth = chipRow.childControlHeight = true;
            chipRow.childForceExpandWidth = true;
            chipRow.childForceExpandHeight = true;
            var items = new Object[AmountSteps.Length];
            var labels = new Object[AmountSteps.Length];
            for (int i = 0; i < AmountSteps.Length; i++)
            {
                items[i] = AmountChip(chips, i, AmountSteps[i], out var label);
                labels[i] = label;
            }
            SetField(screen, "amounts", items);
            SetField(screen, "amountLabels", labels);
            // The chips and the amounts the screen offers come from one list, so a rebuild can never
            // leave a "+10" chip that puts 50 aside.
            SetField(screen, "amountSteps", (int[])AmountSteps.Clone());

            var hint = Text("Hint", content, "Отложишь 10 — останется 50. Ещё 5 раз по 10, и велосипед у тебя!", 25, InkSoft, TextAlignmentOptions.Center, outlined: false);
            Wrap(hint);
            AutoSize(hint, MinimumReadableFontSize, MinimumReadableFontSize);
            Size(hint, height: HintHeight);
            SetField(screen, "hint", hint);

            var buttons = Row(content, "Buttons", ButtonsHeight);
            var save = WideButton(buttons, "Save", "ui_btn_green", "Положить 10 в копилку", 34, Color.white, ButtonsHeight, 620, "ui_decor_paw_green");
            SetField(screen, "saveButton", save);
            SetField(screen, "saveLabel", LabelOf(save));

            var secondary = Row(content, "Secondary", SecondaryHeight);
            var change = WideButton(secondary, "ChangeGoal", "ui_btn_white", "Другая цель", 28, Ink, SecondaryHeight, 330);
            SetField(screen, "changeGoalButton", change);
            var withdraw = WideButton(secondary, "Withdraw", "ui_btn_white", "Взять обратно", 28, Ink, SecondaryHeight, 330);
            SetField(screen, "withdrawRoot", withdraw.transform.parent.gameObject);
            SetField(screen, "withdrawButton", withdraw);

            var confettiLayer = Rect("Confetti", panel.transform);
            Stretch(confettiLayer);
            var confetti = confettiLayer.gameObject.AddComponent<FinikConfettiBurst>();
            SetField(confetti, "pieceSprite", SpriteOrNull("ui_piece"));
            SetField(screen, "confetti", confetti);
        }

        /// <summary>The goal: big picture on the left; saved / price, the bar with its preview and what is left on the right.</summary>
        static void BuildHero(Transform content, FinikSavingsScreen screen)
        {
            var hero = Img("Hero", content, "ui_card_savings", sliced: true);
            hero.pixelsPerUnitMultiplier *= 1.6f;
            Size(hero, height: HeroHeight);
            var rt = hero.rectTransform;

            var holder = Rect("Picture", rt);
            Place(holder, LeftMiddle, Center, new Vector2(150, 4), new Vector2(250, 250));
            var punch = holder.gameObject.AddComponent<FinikPressFeedback>();
            var picture = Img("Image", holder, "goal_bike", preserveAspect: true);
            Stretch(picture.rectTransform);
            picture.gameObject.AddComponent<FinikIdleMotion>().Configure(2f, 4f, 0.02f, 2f, 3f);
            Decor(rt, "ui_spark_yellow", TopLeft, Center, new Vector2(40, -40), new Vector2(54, 54), -20f);
            Decor(rt, "ui_decor_star_small", BottomLeft, Center, new Vector2(262, 44), new Vector2(40, 40), 14f);
            SetField(screen, "goalImage", picture);
            SetField(screen, "goalPunch", punch);

            const float left = 296f, right = 32f;
            var kicker = Text("Kicker", rt, "СЕЙЧАС В КОПИЛКЕ", 22, InkSoft, TextAlignmentOptions.MidlineLeft, outlined: false);
            kicker.characterSpacing = 4;
            AnchorTop(kicker.rectTransform, left, right, -30, 32);

            // «200 / 500»: the rolling saved amount right-aligned against the price.
            var count = Rect("Count", rt);
            AnchorTop(count, left, right, -70, 80);
            var saved = Text("Saved", count, "200", 64, Ink, TextAlignmentOptions.BottomRight, outlined: false);
            saved.fontStyle = FontStyles.Bold;
            // 64pt does not fit the row's 80px, and a longer goal price would overflow further.
            AutoSize(saved, 52, 64);
            saved.rectTransform.anchorMin = Vector2.zero;
            saved.rectTransform.anchorMax = new Vector2(0, 1);
            saved.rectTransform.pivot = new Vector2(0, 0.5f);
            saved.rectTransform.offsetMin = Vector2.zero;
            saved.rectTransform.offsetMax = new Vector2(190, 0);
            var counter = saved.gameObject.AddComponent<FinikCounterText>();
            SetField(counter, "punchOnGain", saved.gameObject.AddComponent<FinikPressFeedback>());
            var target = Text("Target", count, "/ 500", 42, InkSoft, TextAlignmentOptions.BottomLeft, outlined: false);
            target.rectTransform.anchorMin = Vector2.zero;
            target.rectTransform.anchorMax = Vector2.one;
            target.rectTransform.offsetMin = new Vector2(202, 0);
            target.rectTransform.offsetMax = Vector2.zero;
            SetField(screen, "savedCounter", counter);
            SetField(screen, "targetLabel", target);

            var bars = Rect("Bars", rt);
            AnchorTop(bars, left, right, -166, 46);
            var (preview, bar) = DoubleBar(bars, "#B8EFC6", "#2EBE5A");
            SetField(screen, "previewBar", preview);
            SetField(screen, "bar", bar);

            var leftLabel = Text("Left", rt, "Осталось 300", 32, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            AnchorTop(leftLabel.rectTransform, left, right, -224, 44);
            SetField(screen, "leftLabel", leftLabel);
        }

        /// <summary>«+50» chip: the blue candy (ui_btn_white), orange when chosen; white outlined caption on both.</summary>
        static FinikChoiceItem AmountChip(Transform row, int index, int amount, out TMP_Text label)
        {
            var slot = Rect("Amount" + index, row);
            var body = Rect("Body", slot);
            Stretch(body);
            var press = body.gameObject.AddComponent<FinikPressFeedback>();
            var face = Img("Face", body, "ui_btn_white", sliced: true, raycast: true);
            Stretch(face.rectTransform);
            var button = MakeButton(body, face);
            var chosen = Img("Chosen", body, "ui_btn_orange", sliced: true, color: new Color(1, 1, 1, 0));
            Stretch(chosen.rectTransform);

            var coin = Img("Coin", body, "icon_coin", preserveAspect: true);
            Place(coin.rectTransform, LeftMiddle, Center, new Vector2(52, 4), new Vector2(54, 54));
            label = Text("Label", body, $"+{amount}", 38, Color.white, TextAlignmentOptions.Midline);
            Stretch(label.rectTransform, 84, 10, 16, 0);

            var choice = slot.gameObject.AddComponent<FinikChoiceItem>();
            choice.Configure(amount.ToString(), button, press, chosen, null, label);
            SetField(choice, "selectedScale", 1.04f);
            return choice;
        }

        // ------------------------------------------------------------------ goal picker

        static void BuildGoals(Transform safe, FinikSavingsScreen screen)
        {
            var content = Panel(safe, "Goals", GoalsHeight, out var panel, out _);
            SetField(screen, "goals", panel);
            Header(content, "icon_piggy_coin", "ЦЕЛИ", Pink, "На что будем копить?", coins: false, out _);

            var hint = Text("Hint", content, "Монеты в копилке останутся — поменяется только цель.", 25, InkSoft, TextAlignmentOptions.MidlineLeft, outlined: false);
            Wrap(hint);
            AutoSize(hint, MinimumReadableFontSize, MinimumReadableFontSize);
            Size(hint, height: GoalsHintHeight);

            var grid = Rect("Grid", content);
            Size(grid, height: 2 * GoalCardHeight + 16);
            var rows = grid.gameObject.AddComponent<VerticalLayoutGroup>();
            rows.spacing = 16;
            rows.childControlWidth = rows.childControlHeight = true;
            rows.childForceExpandWidth = rows.childForceExpandHeight = true;

            int count = FinikGoalCatalog.Goals.Count;
            var cards = new List<Object>();
            var images = new List<Object>();
            var titles = new List<Object>();
            var prices = new List<Object>();
            RectTransform row = null;
            for (int i = 0; i < count; i++)
            {
                if (i % 2 == 0)
                {
                    row = Rect("Row" + i / 2, grid);
                    var group = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                    group.spacing = 16;
                    group.childControlWidth = group.childControlHeight = true;
                    group.childForceExpandWidth = group.childForceExpandHeight = true;
                }
                var goal = FinikGoalCatalog.Goals[i];
                cards.Add(GoalCard(row, goal, out var image, out var title, out var price));
                images.Add(image);
                titles.Add(title);
                prices.Add(price);
            }
            SetField(screen, "goalCards", cards.ToArray());
            SetField(screen, "goalCardImages", images.ToArray());
            SetField(screen, "goalCardTitles", titles.ToArray());
            SetField(screen, "goalCardPrices", prices.ToArray());

            var buttons = Row(content, "Buttons", ButtonsHeight);
            var back = WideButton(buttons, "Back", "ui_btn_white", "Назад", 32, Ink, ButtonsHeight, 320);
            SetField(screen, "goalsBack", back);
            Dismiss(panel, back);
        }

        static FinikChoiceItem GoalCard(Transform row, FinikGoal goal, out Image image, out TMP_Text title, out TMP_Text price)
        {
            var slot = Rect("Goal_" + goal.Id, row);
            var body = Rect("Body", slot);
            Stretch(body);
            var press = body.gameObject.AddComponent<FinikPressFeedback>();
            var face = Img("Face", body, "ui_card", sliced: true, raycast: true);
            Stretch(face.rectTransform);
            face.pixelsPerUnitMultiplier *= 1.6f;
            var button = MakeButton(body, face);
            var frame = Img("Selected", body, "ui_card_selected", sliced: true, color: new Color(1, 1, 1, 0));
            Stretch(frame.rectTransform);
            frame.pixelsPerUnitMultiplier *= 1.6f;

            image = Img("Image", body, goal.Icon, preserveAspect: true);
            Place(image.rectTransform, TopCenter, TopCenter, new Vector2(0, -14), new Vector2(150, 150));
            title = Text("Title", body, goal.Title, 32, Ink, TextAlignmentOptions.Midline, outlined: false);
            AutoSize(title, 24, 32);
            AnchorTop(title.rectTransform, 16, 16, -166, 38);
            price = Text("Price", body, $"{goal.Price} {FinikSavingsScreen.Coins(goal.Price)}", 24, InkSoft, TextAlignmentOptions.Midline, outlined: false);
            AnchorTop(price.rectTransform, 16, 16, -204, 32);

            var check = Img("Check", body, "ui_check", preserveAspect: true);
            Place(check.rectTransform, TopRight, Center, new Vector2(-30, -30), new Vector2(48, 48));
            check.gameObject.SetActive(false);

            var choice = slot.gameObject.AddComponent<FinikChoiceItem>();
            choice.Configure(goal.Id, button, press, frame, check.gameObject, null);
            SetField(choice, "selectedScale", 1f);
            return choice;
        }

        // ------------------------------------------------------------------ take back

        static void BuildConfirm(Transform safe, FinikSavingsScreen screen)
        {
            var content = Panel(safe, "Confirm", ConfirmHeight, out var panel, out _);
            SetField(screen, "confirm", panel);
            var title = Header(content, "icon_piggy_coin", "ВЗЯТЬ ИЗ КОПИЛКИ", Warn, "Взять 50 монет обратно?", coins: false, out _);
            SetField(screen, "confirmTitle", title);

            var body = Text("Body", content, "Монеты вернутся в кошелёк, а копилка похудеет: останется накопить 350 вместо 300.", 27, Ink, TextAlignmentOptions.TopLeft, outlined: false);
            Wrap(body);
            AutoSize(body, 20, 27);
            Size(body, height: ConfirmBodyHeight);
            SetField(screen, "confirmBody", body);

            // Before (pale red) and after (green) on one bar: the lost part stays visible.
            var block = Img("Progress", content, "ui_card", sliced: true, color: FinikSdfCanvas.Hex("#FFF1EE"));
            block.pixelsPerUnitMultiplier *= 1.6f;
            Size(block, height: ConfirmBarHeight);
            var progress = Text("Label", block.rectTransform, "Было 200, станет 150 из 500", 28, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            AutoSize(progress, 20, 28);
            AnchorTop(progress.rectTransform, 28, 28, -12, 40);
            SetField(screen, "confirmProgress", progress);
            var bars = Rect("Bars", block.rectTransform);
            AnchorTop(bars, 28, 28, -56, 40);
            var (before, after) = DoubleBar(bars, "#FFB3A8", "#2EBE5A");
            SetField(screen, "confirmBefore", before);
            SetField(screen, "confirmAfter", after);

            var buttons = Row(content, "Buttons", ButtonsHeight);
            var keep = WideButton(buttons, "Keep", "ui_btn_green", "Оставить в копилке", 32, Color.white, ButtonsHeight, 440, "ui_decor_paw_green");
            SetField(screen, "keepButton", keep);
            var take = WideButton(buttons, "Take", "ui_btn_white", "Взять 50", 30, Ink, ButtonsHeight, 260);
            SetField(screen, "takeButton", take);
            SetField(screen, "takeLabel", LabelOf(take));
            // A tap beside the card keeps the coins where they are, like «Оставить в копилке».
            Dismiss(panel, keep);
        }

        // ------------------------------------------------------------------ icons

        static void WireIcons(FinikSavingsScreen screen)
        {
            var so = new SerializedObject(screen);
            var icons = so.FindProperty("icons");
            icons.arraySize = 0;
            foreach (var goal in FinikGoalCatalog.Goals)
            {
                var sprite = SpriteOrNull(goal.Icon);
                if (!sprite) continue;
                int i = icons.arraySize++;
                var element = icons.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("name").stringValue = goal.Icon;
                element.FindPropertyRelative("sprite").objectReferenceValue = sprite;
            }
            so.FindProperty("fallbackIcon").objectReferenceValue = SpriteOrNull("quest_a_goal");
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
