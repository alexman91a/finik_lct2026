using System.Collections.Generic;
using System.Linq;
using Finik.Core;
using Finik.Navigation;
using Finik.UI;
using Finik.UI.Onboarding;
using Finik.UI.Quests;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static Finik.Editor.UI.FinikUiKit;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Builds the quest screen (Screen_Quests) over the room: the board with today's quests and the
    /// weekly streak, the quest card with its answer area (choices, red flags, odds, envelopes) and the
    /// result card. Same column as the food screen: beside Finik in landscape, under him in portrait.
    /// </summary>
    public static class FinikQuestScreenBuilder
    {
        const string RootName = "Screen_Quests";
        const float Gap = 14f;
        const float PadX = 40f, PadY = 34f;
        const float HeaderHeight = 104f, ButtonsHeight = 112f;
        const float WeeklyHeight = 132f, RowHeight = 96f, RowGap = 12f;
        // The description runs up to three lines at a size a 7-year-old can read, and the hint takes
        // two: a retry note after a wrong attempt is a full sentence. Both are sized with slack, so the
        // vertical layout never has to squeeze a block below its minimum and shrink the font with it.
        const float DescriptionHeight = 150f, BodyHeight = 410f, HintHeight = 84f;
        // Landscape keeps the same column width as portrait: the room leaves room for it beside Finik,
        // and a narrower column forced the quest description onto an extra line and shrank its font.
        const float LandscapeWidth = 1010f, PortraitWidth = 1010f;
        const int MaxRows = 4, MaxOptions = 3, MaxMessages = 4, MaxCells = 8;

        static float BoardHeight => 2 * PadY + HeaderHeight + WeeklyHeight + MaxRows * RowHeight + (MaxRows - 1) * RowGap + ButtonsHeight + 3 * Gap;
        static float QuestHeight => 2 * PadY + HeaderHeight + DescriptionHeight + BodyHeight + HintHeight + ButtonsHeight + 4 * Gap;
        const float ResultHeight = 900f;

        static readonly Color Accent = new(0.95f, 0.5f, 0.1f, 1f);
        static readonly Color Violet = new(0.55f, 0.3f, 0.95f, 1f);
        static readonly Color Good = new(0.13f, 0.62f, 0.3f, 1f);
        static readonly Color Flag = new(0.93f, 0.2f, 0.33f, 1f);

        [MenuItem("Finik/UI/Rebuild Quest Screen")]
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

            // Above the HUD (20), below onboarding (30); never open together with the food screen.
            var root = ReuseOrCreateCanvas(RootName, 25);
            var screen = GetOrAdd<FinikQuestScreen>(root);
            var safe = Rect("SafeArea", root.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<FinikSafeArea>();

            BuildBoard(safe, screen);
            BuildQuest(safe, screen);
            BuildResult(safe, screen);
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
            if (binder) SetField(binder, "questScreen", screen);

            // Every button answers at least a 48 dp tap; the component is a no-op on the big ones.
            EnsureTapTargets(root);

            EnsureEventSystem();
            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            int art = FinikQuestCatalog.Quests.Count(q => SpriteOrNull(q.Icon));
            return $"Built {RootName}: {FinikQuestCatalog.Quests.Count} quests, {art} with final art.";
        }

        // ------------------------------------------------------------------ shared

        /// <summary>Full-screen panel with the card column; returns the vertical content stack.</summary>
        static RectTransform Panel(Transform safe, string name, float height, out FinikScreenPanel panel)
        {
            var panelRoot = Rect(name, safe);
            Stretch(panelRoot);
            panelRoot.gameObject.AddComponent<CanvasGroup>();
            panel = panelRoot.gameObject.AddComponent<FinikScreenPanel>();

            var column = Rect("Column", panelRoot);
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
        static void Header(Transform content, string iconSprite, string tagText, Color tagColor, string titleText, bool coins,
            out Image icon, out TMP_Text tag, out TMP_Text title, out FinikCounterText counter)
        {
            var row = Rect("Header", content);
            Size(row, height: HeaderHeight);
            icon = Img("Icon", row, iconSprite, preserveAspect: true);
            Place(icon.rectTransform, LeftMiddle, LeftMiddle, Vector2.zero, new Vector2(104, 104));
            icon.gameObject.AddComponent<FinikIdleMotion>().Configure(3f, 3f, 0f, 1.8f, 0f);
            float right = coins ? 300 : 0;

            tag = Text("Tag", row, tagText, 24, tagColor, TextAlignmentOptions.BottomLeft, outlined: false);
            tag.rectTransform.anchorMin = new Vector2(0, 0.66f);
            tag.rectTransform.anchorMax = Vector2.one;
            tag.rectTransform.offsetMin = new Vector2(124, 0);
            tag.rectTransform.offsetMax = new Vector2(-right, -2);
            tag.characterSpacing = 4;

            title = Text("Title", row, titleText, 40, Ink, TextAlignmentOptions.TopLeft, outlined: false);
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = new Vector2(1, 0.66f);
            title.rectTransform.offsetMin = new Vector2(124, -4);
            title.rectTransform.offsetMax = new Vector2(-right, 0);
            // Quest names run up to two lines; the board title is one.
            Wrap(title);
            title.lineSpacing = -12;
            AutoSize(title, 26, coins ? 44 : 36);

            counter = null;
            if (!coins) return;
            var wallet = Rect("Coins", row);
            Place(wallet, RightMiddle, RightMiddle, new Vector2(0, 8), new Vector2(230, 76));
            var body = Rect("Body", wallet);
            Stretch(body);
            counter = CurrencyPill(body, "Coins", "icon_coin");
        }

        static void Buttons(Transform content, string name, out RectTransform row)
        {
            row = Rect(name, content);
            Size(row, height: ButtonsHeight);
            var group = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 18;
            group.childAlignment = TextAnchor.MiddleCenter;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = group.childForceExpandHeight = false;
        }

        /// <summary>A light rounded card that works as a tappable choice (FinikChoiceItem) with a selection frame.</summary>
        static FinikChoiceItem ChoiceCard(RectTransform slot, string id, Color frameColor, Color? faceColor, out RectTransform body, GameObject check)
        {
            body = Rect("Body", slot);
            Stretch(body);
            var press = body.gameObject.AddComponent<FinikPressFeedback>();
            var face = Img("Face", body, "ui_card", sliced: true, raycast: true, color: faceColor);
            Stretch(face.rectTransform);
            face.pixelsPerUnitMultiplier *= 1.6f;
            var button = MakeButton(body, face);
            var frame = Img("Selected", body, "ui_card_selected", sliced: true, color: new Color(frameColor.r, frameColor.g, frameColor.b, 0));
            Stretch(frame.rectTransform);
            frame.pixelsPerUnitMultiplier *= 1.6f;
            var choice = slot.gameObject.AddComponent<FinikChoiceItem>();
            choice.Configure(id, button, press, frame, check, null);
            SetField(choice, "selectedScale", 1f);
            return choice;
        }

        static RectTransform Stack(Transform parent, string name, float spacing, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var rect = Rect(name, parent);
            var stack = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.spacing = spacing;
            stack.childAlignment = alignment;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;
            return rect;
        }

        // ------------------------------------------------------------------ board

        static void BuildBoard(Transform safe, FinikQuestScreen screen)
        {
            var content = Panel(safe, "Board", BoardHeight, out var panel);
            SetField(screen, "board", panel);
            Header(content, "icon_quest", "КВЕСТЫ НА СЕГОДНЯ", Violet, "Выбирай приключение", coins: true, out _, out _, out var title, out var coins);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            SetField(screen, "boardCoins", coins);

            BuildWeekly(content, screen);

            var list = Stack(content, "Rows", RowGap, TextAnchor.UpperCenter);
            Size(list, height: MaxRows * RowHeight + (MaxRows - 1) * RowGap);
            var rows = new Object[MaxRows];
            for (int i = 0; i < MaxRows; i++) rows[i] = Row(list, i);
            SetField(screen, "rows", rows);

            var empty = Text("Empty", list, "На сегодня квестов нет. Загляни завтра!", 26, InkSoft, TextAlignmentOptions.Center, outlined: false);
            Size(empty, height: RowHeight);
            empty.gameObject.SetActive(false);
            SetField(screen, "boardEmpty", empty);

            Buttons(content, "Buttons", out var buttons);
            var close = WideButton(buttons, "Close", "ui_btn_green", "Вернуться в комнату", 34, Color.white, ButtonsHeight, 520, "ui_decor_paw_green");
            SetField(screen, "boardClose", close);
            Dismiss(panel, close);

            var confettiLayer = Rect("Confetti", panel.transform);
            Stretch(confettiLayer);
            var confetti = confettiLayer.gameObject.AddComponent<FinikConfettiBurst>();
            SetField(confetti, "pieceSprite", SpriteOrNull("ui_piece"));
            SetField(screen, "boardConfetti", confetti);
        }

        static void BuildWeekly(Transform content, FinikQuestScreen screen)
        {
            var strip = Img("Weekly", content, "ui_card", sliced: true, color: FinikSdfCanvas.Hex("#F1EBFF"));
            strip.pixelsPerUnitMultiplier *= 1.6f;
            Size(strip, height: WeeklyHeight);
            var view = strip.gameObject.AddComponent<FinikWeeklyStreakView>();
            var rt = strip.rectTransform;

            var icon = Img("Icon", rt, "icon_star", preserveAspect: true);
            Place(icon.rectTransform, LeftMiddle, LeftMiddle, new Vector2(18, 4), new Vector2(84, 84));
            icon.gameObject.AddComponent<FinikIdleMotion>().Configure(2f, 4f, 0.02f, 1.6f, 1f);

            // Texts between the star and the reward area; the bar runs under them.
            var kicker = Text("Kicker", rt, "СЕРИЯ НЕДЕЛИ", 20, Violet, TextAlignmentOptions.TopLeft, outlined: false);
            kicker.characterSpacing = 4;
            Place(kicker.rectTransform, TopLeft, TopLeft, new Vector2(120, -16), new Vector2(360, 26));
            var title = Text("Title", rt, "Не сбивай серию", 28, Ink, TextAlignmentOptions.TopLeft, outlined: false);
            AutoSize(title, 22, 28);
            title.rectTransform.anchorMin = new Vector2(0, 1);
            title.rectTransform.anchorMax = new Vector2(1, 1);
            title.rectTransform.pivot = TopLeft;
            title.rectTransform.offsetMin = new Vector2(120, -72);
            title.rectTransform.offsetMax = new Vector2(-270, -40);
            var progress = Text("Progress", rt, "2 из 4 — и бонус твой.", 21, InkSoft, TextAlignmentOptions.TopLeft, outlined: false);
            AutoSize(progress, 16, 21);
            progress.rectTransform.anchorMin = new Vector2(0, 1);
            progress.rectTransform.anchorMax = new Vector2(1, 1);
            progress.rectTransform.pivot = TopLeft;
            // Keep the status text clearly above the thicker progress bar.
            progress.rectTransform.offsetMin = new Vector2(120, -84);
            progress.rectTransform.offsetMax = new Vector2(-270, -50);

            // The bar gets its own line under the texts.
            var track = Img("Bar", rt, "hud_bar_track", sliced: true);
            track.rectTransform.anchorMin = new Vector2(0, 0);
            track.rectTransform.anchorMax = new Vector2(1, 0);
            track.rectTransform.pivot = new Vector2(0, 0);
            // Give the streak bar enough height for the pill sprite to keep its rounded caps,
            // and lift it slightly away from the bottom edge of the card.
            track.rectTransform.offsetMin = new Vector2(120, 12);
            track.rectTransform.offsetMax = new Vector2(-270, 40);
            var fill = Img("Fill", track.transform, "hud_bar_fill", sliced: true, color: FinikSdfCanvas.Hex("#8C5CF0"));
            fill.rectTransform.pivot = new Vector2(0, 0.5f);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.offsetMin = new Vector2(4, 4);
            fill.rectTransform.offsetMax = new Vector2(-4, -4);
            var bar = track.gameObject.AddComponent<FinikFillBar>();
            SetField(bar, "fill", fill.rectTransform);
            SetField(bar, "fillImage", fill);
            SetField(bar, "lowThreshold", 0f);

            // Right: the bonus while collecting, a claim button when reached, a check when taken.
            var reward = Rect("Reward", rt);
            Place(reward, RightMiddle, RightMiddle, new Vector2(-24, 0), new Vector2(220, 70));
            var coin = Img("Coin", reward, "icon_coin", preserveAspect: true);
            Place(coin.rectTransform, LeftMiddle, LeftMiddle, new Vector2(20, 0), new Vector2(58, 58));
            var amount = Text("Amount", reward, "+260", 36, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            Stretch(amount.rectTransform, 88, 0, 0, 0);

            var claimRoot = Rect("Claim", rt);
            Place(claimRoot, RightMiddle, RightMiddle, new Vector2(-18, 0), new Vector2(250, 92));
            var claim = WideButton(claimRoot, "Button", "ui_btn_orange", "Забрать +260", 28, Color.white, 92, 250, "ui_decor_star_orange");
            Stretch((RectTransform)claim.transform.parent);
            var claimLabel = claim.transform.Find("Label").GetComponent<TMP_Text>();

            var done = Rect("Done", rt);
            Place(done, RightMiddle, RightMiddle, new Vector2(-24, 0), new Vector2(220, 70));
            var check = Img("Check", done, "ui_check", preserveAspect: true);
            Place(check.rectTransform, LeftMiddle, LeftMiddle, new Vector2(10, 0), new Vector2(56, 56));
            var doneText = Text("Label", done, "получено", 26, Good, TextAlignmentOptions.MidlineLeft, outlined: false);
            Stretch(doneText.rectTransform, 76, 0, 0, 0);

            view.Configure(title, progress, bar, reward.gameObject, amount, claimRoot.gameObject, claim, claimLabel, done.gameObject);
            SetField(screen, "weekly", view);
        }

        static FinikQuestRowView Row(Transform list, int index)
        {
            var slot = Rect("Row" + index, list);
            Size(slot, height: RowHeight);
            var group = slot.gameObject.AddComponent<CanvasGroup>();
            var body = Rect("Body", slot);
            Stretch(body);
            body.gameObject.AddComponent<FinikPressFeedback>();
            var face = Img("Face", body, "ui_card", sliced: true, raycast: true);
            Stretch(face.rectTransform);
            face.pixelsPerUnitMultiplier *= 1.6f;
            var button = MakeButton(body, face);

            var icon = Img("Icon", body, "quest_placeholder", preserveAspect: true);
            Place(icon.rectTransform, LeftMiddle, LeftMiddle, new Vector2(12, 2), new Vector2(76, 76));

            var status = Text("Status", body, "ДОСТУПНО", 18, Violet, TextAlignmentOptions.BottomLeft, outlined: false);
            status.characterSpacing = 3;
            status.rectTransform.anchorMin = new Vector2(0, 0.56f);
            status.rectTransform.anchorMax = Vector2.one;
            status.rectTransform.offsetMin = new Vector2(104, 0);
            status.rectTransform.offsetMax = new Vector2(-200, -6);
            var title = Text("Title", body, "Перекус после школы", 28, Ink, TextAlignmentOptions.TopLeft, outlined: false);
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = new Vector2(1, 0.56f);
            title.rectTransform.offsetMin = new Vector2(104, 4);
            title.rectTransform.offsetMax = new Vector2(-200, 0);
            AutoSize(title, 20, 28);

            // Reward column: coin (or a check once played) and "+80" / "готово".
            var coin = Img("Coin", body, "icon_coin", preserveAspect: true);
            Place(coin.rectTransform, RightMiddle, Center, new Vector2(-160, 2), new Vector2(46, 46));
            var reward = Text("Reward", body, "+80", 30, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            Place(reward.rectTransform, RightMiddle, RightMiddle, new Vector2(-12, 2), new Vector2(122, 44));
            AutoSize(reward, 24, 30);
            var check = Img("Check", body, "ui_check", preserveAspect: true);
            Place(check.rectTransform, RightMiddle, Center, new Vector2(-160, 2), new Vector2(44, 44));
            check.gameObject.SetActive(false);

            var view = slot.gameObject.AddComponent<FinikQuestRowView>();
            view.Configure(button, group, icon, status, title, coin.gameObject, reward, check.gameObject);
            return view;
        }

        // ------------------------------------------------------------------ quest

        static void BuildQuest(Transform safe, FinikQuestScreen screen)
        {
            var content = Panel(safe, "Quest", QuestHeight, out var panel);
            SetField(screen, "quest", panel);
            Header(content, "quest_placeholder", "КВЕСТ · +80", Violet, "Перекус после школы", coins: false, out var icon, out var tag, out var title, out _);
            SetField(screen, "questIcon", icon);
            SetField(screen, "questTag", tag);
            SetField(screen, "questTitle", title);

            var description = Text("Description", content, "У тебя осталось 300 монет на «Хочу».", 32, Ink, TextAlignmentOptions.TopLeft, outlined: false);
            Wrap(description);
            AutoSize(description, 26, 32);
            Size(description, height: DescriptionHeight);
            SetField(screen, "questDescription", description);

            var body = Rect("Body", content);
            Size(body, height: BodyHeight);
            BuildChoices(body, screen);
            BuildRedFlags(body, screen);
            BuildOdds(body, screen);
            BuildNeedOrWant(body, screen);

            var hintCard = Img("HintCard", content, "ui_bubble", sliced: true, color: FinikSdfCanvas.Hex("#FFF8DF"));
            hintCard.pixelsPerUnitMultiplier *= 1.5f;
            Size(hintCard, height: HintHeight);
            var hint = Text("Hint", hintCard.transform, "Подсказка Финика: смотри, что важнее прямо сейчас.", 26, InkSoft, TextAlignmentOptions.MidlineLeft, outlined: false);
            Wrap(hint);
            AutoSize(hint, 20, 26);
            hint.lineSpacing = -4;
            Stretch(hint.rectTransform, 28, 10, 28, 6);
            var hintSpark = Img("Spark", hintCard.transform, "ui_spark_yellow", preserveAspect: true);
            Place(hintSpark.rectTransform, TopRight, TopRight, new Vector2(-10, -4), new Vector2(34, 34));
            SetField(screen, "questHint", hint);

            Buttons(content, "Buttons", out var buttons);
            var back = WideButton(buttons, "Back", "ui_btn_white", "Другие квесты", 30, Ink, ButtonsHeight, 340);
            var primary = WideButton(buttons, "Primary", "ui_btn_orange", "Проверить", 30, Color.white, ButtonsHeight, 420, "ui_decor_star_orange");
            SetField(screen, "backButton", back);
            SetField(screen, "backLabel", back.transform.Find("Label").GetComponent<TMP_Text>());
            SetField(screen, "questScrim", Dismiss(panel, back));
            SetField(screen, "primaryButton", primary);
            SetField(screen, "primaryRoot", primary.transform.parent.gameObject);
            SetField(screen, "primaryLabel", primary.transform.Find("Label").GetComponent<TMP_Text>());
        }

        static void BuildChoices(RectTransform body, FinikQuestScreen screen)
        {
            var root = Stack(body, "Choices", Gap, TextAnchor.UpperCenter);
            Stretch(root);
            var views = new Object[MaxOptions];
            for (int i = 0; i < MaxOptions; i++)
            {
                var slot = Rect("Option" + i, root);
                // Large, playful answer cards: soft colour, a big number bubble and a small sparkle.
                var element = slot.gameObject.AddComponent<LayoutElement>();
                element.minHeight = 124;
                element.preferredHeight = 160;
                element.flexibleHeight = 0;
                // All answer cards deliberately use the same neutral styling: colour must not reveal
                // which option is positive or negative before the child answers.
                var tint = FinikSdfCanvas.Hex("#F2ECFF");
                var bubble = Violet;
                var choice = ChoiceCard(slot, "option" + i, bubble, tint, out var card, null);
                var badge = Img("Badge", card, "ui_btn_white", sliced: true, color: bubble);
                Place(badge.rectTransform, LeftMiddle, LeftMiddle, new Vector2(20, 0), new Vector2(72, 72));
                var number = Text("Number", badge.transform, (i + 1).ToString(), 34, Color.white, TextAlignmentOptions.Center);
                Stretch(number.rectTransform, 2, 4, 2, 2);
                var label = Text("Label", card, "Оставить деньги по плану", 30, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
                Wrap(label);
                AutoSize(label, 24, 30);
                label.lineSpacing = -5;
                Stretch(label.rectTransform, 112, 12, 64, 12);
                // No coloured decoration on answer options: every slot must be visually equivalent.
                var sparkle = Img("Sparkle", card, "ui_spark_blue", preserveAspect: true);
                Place(sparkle.rectTransform, RightMiddle, RightMiddle, new Vector2(-14, 0), new Vector2(42, 42));
                sparkle.gameObject.SetActive(false);
                var view = slot.gameObject.AddComponent<FinikQuestOptionView>();
                view.Configure(choice, number, label);
                views[i] = view;
            }
            SetField(screen, "choicesRoot", root.gameObject);
            SetField(screen, "options", views);
        }

        static void BuildRedFlags(RectTransform body, FinikQuestScreen screen)
        {
            var root = Rect("RedFlags", body);
            Stretch(root);
            var view = root.gameObject.AddComponent<FinikRedFlagsView>();

            var title = Text("Title", root, "Поймай 3 ред флага", 30, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            title.rectTransform.anchorMin = new Vector2(0, 1);
            title.rectTransform.anchorMax = new Vector2(1, 1);
            title.rectTransform.pivot = TopLeft;
            title.rectTransform.offsetMin = new Vector2(0, -48);
            title.rectTransform.offsetMax = new Vector2(-130, 0);
            AutoSize(title, 22, 30);
            var pill = Rect("Counter", root);
            Place(pill, TopRight, TopRight, Vector2.zero, new Vector2(118, 48));
            Stretch(Img("Face", pill, "ui_btn_white", sliced: true, color: FinikSdfCanvas.Hex("#FFE3E8")).rectTransform);
            var counter = Text("Label", pill, "0/3", 28, Flag, TextAlignmentOptions.Midline, outlined: false);
            Stretch(counter.rectTransform, 8, 6, 8, 0);

            var grid = Rect("Messages", root);
            grid.anchorMin = Vector2.zero;
            grid.anchorMax = Vector2.one;
            grid.offsetMin = Vector2.zero;
            grid.offsetMax = new Vector2(0, -62);
            grid.gameObject.AddComponent<FinikFitGridLayout>().Configure(2, 158, 2, 158, new Vector2(Gap, Gap));

            var items = new FinikChoiceItem[MaxMessages];
            var senders = new TMP_Text[MaxMessages];
            var texts = new TMP_Text[MaxMessages];
            for (int i = 0; i < MaxMessages; i++)
            {
                var slot = Rect("Message" + i, grid);
                var choice = ChoiceCard(slot, "message" + i, Flag, null, out var card, null);
                // A flagged message gets a red flag in its top-right corner (the choice's check mark).
                var flag = Img("Flag", card, "quest_flag", preserveAspect: true);
                Place(flag.rectTransform, TopRight, Center, new Vector2(-30, -30), new Vector2(46, 46));
                flag.gameObject.SetActive(false);
                SetField(choice, "check", flag.gameObject);

                var who = Text("Who", card, "ЛИСТОВКА", 17, InkSoft, TextAlignmentOptions.TopLeft, outlined: false);
                who.characterSpacing = 3;
                Place(who.rectTransform, TopLeft, TopLeft, new Vector2(22, -16), new Vector2(300, 24));
                who.fontStyle = FontStyles.UpperCase;
                var text = Text("Text", card, "Сканируй QR и забери 500 игровых монет", 24, Ink, TextAlignmentOptions.TopLeft, outlined: false);
                Wrap(text);
                AutoSize(text, 18, 24);
                text.lineSpacing = -6;
                Stretch(text.rectTransform, 22, 16, 54, 46);
                items[i] = choice;
                senders[i] = who;
                texts[i] = text;
            }
            view.Configure(title, counter, items, senders, texts);
            SetField(screen, "redFlags", view);
            root.gameObject.SetActive(false);
        }

        static void BuildOdds(RectTransform body, FinikQuestScreen screen)
        {
            var root = Stack(body, "Odds", 12, TextAnchor.UpperCenter);
            Stretch(root);
            var view = root.gameObject.AddComponent<FinikOddsView>();

            var kicker = Text("Kicker", root, "ЧТО ТЫ ПОКУПАЕШЬ НА САМОМ ДЕЛЕ?", 20, Accent, TextAlignmentOptions.TopLeft, outlined: false);
            kicker.characterSpacing = 3;
            Size(kicker, height: 26);
            var prompt = Text("Prompt", root, "В рекламе только редкий приз. Посмотрим на всю коробку?", 26, Ink, TextAlignmentOptions.TopLeft, outlined: false);
            Wrap(prompt);
            AutoSize(prompt, 20, 26);
            Size(prompt, height: 100);

            var grid = Rect("Cells", root);
            Size(grid, height: 2 * 104 + Gap);
            grid.gameObject.AddComponent<FinikFitGridLayout>().Configure(4, 104, 4, 104, new Vector2(Gap, Gap));
            var cells = new GameObject[MaxCells];
            var faces = new Image[MaxCells];
            var marks = new TMP_Text[MaxCells];
            var prizes = new Image[MaxCells];
            for (int i = 0; i < MaxCells; i++)
            {
                var cell = Rect("Cell" + i, grid);
                var face = Img("Face", cell, "ui_card", sliced: true);
                Stretch(face.rectTransform);
                face.pixelsPerUnitMultiplier *= 1.6f;
                var mark = Text("Mark", cell, "?", 54, Violet, TextAlignmentOptions.Center, outlined: false);
                Stretch(mark.rectTransform, 0, 10, 0, 0);
                var prize = Img("Prize", cell, "quest_tag", preserveAspect: true);
                Place(prize.rectTransform, Center, Center, new Vector2(0, 4), new Vector2(76, 76));
                prize.gameObject.SetActive(false);
                cells[i] = cell.gameObject;
                faces[i] = face;
                marks[i] = mark;
                prizes[i] = prize;
            }
            // The rare prize is a crown once quest art is sliced in (web: 👑), the star until then.
            view.Configure(kicker, prompt, cells, faces, marks, prizes, SpriteOrNull("quest_crown") ?? SpriteOrNull("icon_star"), SpriteOrNull("quest_tag"));
            SetField(screen, "odds", view);
            root.gameObject.SetActive(false);
        }

        static void BuildNeedOrWant(RectTransform body, FinikQuestScreen screen)
        {
            var root = Stack(body, "NeedOrWant", 12, TextAnchor.UpperCenter);
            Stretch(root);
            var view = root.gameObject.AddComponent<FinikNeedOrWantView>();

            var kicker = Text("Kicker", root, "РАЗЛОЖИ ПО КОНВЕРТАМ", 20, Accent, TextAlignmentOptions.TopLeft, outlined: false);
            kicker.characterSpacing = 3;
            Size(kicker, height: 26);

            var itemCard = Rect("Item", root);
            Size(itemCard, height: 88);
            var itemFace = Img("Face", itemCard, "ui_card", sliced: true, color: FinikSdfCanvas.Hex("#FFF4E0"));
            itemFace.pixelsPerUnitMultiplier *= 1.6f;
            Stretch(itemFace.rectTransform);
            var item = Text("Title", itemCard, "Шапка для игрового питомца", 28, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            AutoSize(item, 20, 28);
            Stretch(item.rectTransform, 28, 6, 190, 0);
            var coin = Img("Coin", itemCard, "icon_coin", preserveAspect: true);
            Place(coin.rectTransform, RightMiddle, Center, new Vector2(-140, 2), new Vector2(50, 50));
            var price = Text("Price", itemCard, "160", 32, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            Place(price.rectTransform, RightMiddle, RightMiddle, new Vector2(-18, 2), new Vector2(96, 48));

            var prompt = Text("Prompt", root, "Куда это относится?", 28, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            AutoSize(prompt, 22, 28);
            Size(prompt, height: 38);

            var envelopes = Rect("Envelopes", root);
            Size(envelopes, height: 190);
            envelopes.gameObject.AddComponent<FinikFitGridLayout>().Configure(2, 190, 2, 190, new Vector2(Gap, Gap));
            var needs = Envelope(envelopes, "Needs", "ill_needs", "Нужно", "без этого никак", FinikSdfCanvas.Hex("#E6F0FF"));
            var wants = Envelope(envelopes, "Wants", "ill_wants", "Хочу", "приятно, но не обязательно", FinikSdfCanvas.Hex("#FFEAF3"));

            view.Configure(kicker, item, price, prompt, needs, wants);
            SetField(screen, "needOrWant", view);
            root.gameObject.SetActive(false);
        }

        static FinikChoiceItem Envelope(Transform grid, string id, string art, string title, string subtitle, Color tint)
        {
            var slot = Rect(id, grid);
            var choice = ChoiceCard(slot, id.ToLowerInvariant(), Accent, tint, out var card, null);
            var picture = Img("Picture", card, SpriteOrNull(art) ? art : "icon_star", preserveAspect: true);
            Place(picture.rectTransform, LeftMiddle, LeftMiddle, new Vector2(16, 0), new Vector2(120, 120));
            var name = Text("Title", card, title, 36, Ink, TextAlignmentOptions.BottomLeft, outlined: false);
            name.rectTransform.anchorMin = new Vector2(0, 0.5f);
            name.rectTransform.anchorMax = Vector2.one;
            name.rectTransform.offsetMin = new Vector2(148, 0);
            name.rectTransform.offsetMax = new Vector2(-16, -20);
            var hint = Text("Subtitle", card, FinikTypography.Fix(subtitle), 21, InkSoft, TextAlignmentOptions.TopLeft, outlined: false);
            Wrap(hint);
            AutoSize(hint, 16, 21);
            hint.rectTransform.anchorMin = Vector2.zero;
            hint.rectTransform.anchorMax = new Vector2(1, 0.5f);
            hint.rectTransform.offsetMin = new Vector2(148, 18);
            hint.rectTransform.offsetMax = new Vector2(-16, -4);
            return choice;
        }

        // ------------------------------------------------------------------ result

        static void BuildResult(Transform safe, FinikQuestScreen screen)
        {
            var content = Panel(safe, "Result", ResultHeight, out var panel);
            var view = panel.gameObject.AddComponent<FinikQuestResultView>();
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;

            // Hero: the quest picture with the verdict as a tag pinned to its bottom edge.
            var hero = Rect("Hero", content);
            Size(hero, height: 200);
            var picture = Img("Picture", hero, "quest_placeholder", preserveAspect: true);
            Place(picture.rectTransform, TopCenter, TopCenter, Vector2.zero, new Vector2(168, 168));
            picture.gameObject.AddComponent<FinikIdleMotion>().Configure(3f, 5f, 0.015f, 1.8f, 0f);
            HeroSparkles(hero, 84f);
            var pill = Img("Verdict", hero, "ui_btn_white", sliced: true);
            Place(pill.rectTransform, BottomCenter, BottomCenter, Vector2.zero, new Vector2(340, 58));
            var verdict = Text("Label", pill.transform, "Хороший ход", 28, Color.white, TextAlignmentOptions.Midline);
            Stretch(verdict.rectTransform, 18, 7, 18, 0);
            AutoSize(verdict, 20, 28);

            var title = Text("Title", content, "Перекус после школы", 40, Ink, TextAlignmentOptions.Top, outlined: false);
            AutoSize(title, 26, 40);
            Size(title, height: 50);

            var answer = Text("Answer", content, "Твой ответ: «Оставить деньги по плану»", 24, InkSoft, TextAlignmentOptions.Center, outlined: false);
            Wrap(answer);
            AutoSize(answer, 18, 24);
            answer.fontStyle = FontStyles.Italic;
            Size(answer, height: 62);

            var explanation = Text("Explanation", content, "…", 26, Ink, TextAlignmentOptions.Center, outlined: false);
            Wrap(explanation);
            AutoSize(explanation, 20, 26);
            explanation.lineSpacing = 4;
            Size(explanation, height: 104);

            var strip = Img("Changes", content, "ui_card", sliced: true, color: FinikSdfCanvas.Hex("#EEF4FF"));
            strip.pixelsPerUnitMultiplier *= 1.6f;
            Size(strip, height: 92);
            var items = Rect("Items", strip.transform);
            Stretch(items, 12, 10, 12, 10);
            var row = items.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = true;
            var coins = Change(items, "Coins", "icon_coin");
            var xp = Change(items, "Xp", "icon_star");

            var streak = Text("Streak", content, "Серия недели: 1 из 4.", 23, Violet, TextAlignmentOptions.Center, outlined: false);
            Wrap(streak);
            AutoSize(streak, 18, 23);
            Size(streak, height: 34);

            var petRow = Rect("Pet", content);
            Size(petRow, height: 72);
            var avatar = Img("Avatar", petRow, "avatar_finik", preserveAspect: true);
            Place(avatar.rectTransform, LeftMiddle, LeftMiddle, new Vector2(20, 0), new Vector2(72, 72));
            var line = Text("Line", petRow, "«…»", 25, InkSoft, TextAlignmentOptions.MidlineLeft, outlined: false);
            Wrap(line);
            AutoSize(line, 18, 25);
            line.fontStyle = FontStyles.Italic;
            Stretch(line.rectTransform, 108, 0, 20, 0);

            Buttons(content, "Buttons", out var buttons);
            // Result navigation: room is the secondary blue action on the left;
            // continuing the quest is the primary green action on the right.
            var close = WideButton(buttons, "Close", "ui_btn_white", "В комнату", 32, Color.white, ButtonsHeight, 360);
            var more = WideButton(buttons, "More", "ui_btn_green", "Ещё квест", 32, Color.white, ButtonsHeight, 320, "ui_decor_chevron");

            var confettiLayer = Rect("Confetti", panel.transform);
            Stretch(confettiLayer);
            var confetti = confettiLayer.gameObject.AddComponent<FinikConfettiBurst>();
            SetField(confetti, "pieceSprite", SpriteOrNull("ui_piece"));

            SetField(view, "panel", panel);
            SetField(view, "verdictBackground", pill);
            SetField(view, "verdict", verdict);
            SetField(view, "picture", picture);
            SetField(view, "title", title);
            SetField(view, "answer", answer);
            SetField(view, "explanation", explanation);
            SetField(view, "rewardStrip", strip.gameObject);
            SetField(view, "coinsDelta", coins);
            SetField(view, "xpChip", xp.transform.parent.gameObject);
            SetField(view, "xpDelta", xp);
            SetField(view, "streak", streak);
            SetField(view, "petLine", line);
            SetField(view, "petAvatar", avatar);
            SetField(view, "moreButton", more);
            SetField(view, "closeButton", close);
            SetField(view, "confetti", confetti);
            SetField(screen, "result", view);
            Dismiss(panel, close);
        }

        /// <summary>One "what changed" item: HUD icon and its delta, centred in its cell.</summary>
        static TMP_Text Change(Transform row, string id, string icon)
        {
            var item = Rect(id, row);
            var group = item.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 8;
            group.childAlignment = TextAnchor.MiddleCenter;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = group.childForceExpandHeight = false;
            var image = Img("Icon", item, icon, preserveAspect: true);
            Size(image, 56, 56);
            var value = Text("Value", item, "+0", 34, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            value.textWrappingMode = TextWrappingModes.NoWrap;
            AutoSize(value, 20, 34);
            var valueLayout = value.gameObject.AddComponent<LayoutElement>();
            valueLayout.preferredWidth = 190;
            valueLayout.minWidth = 90;
            return value;
        }

        static void WireIcons(FinikQuestScreen screen)
        {
            var so = new SerializedObject(screen);
            var icons = so.FindProperty("icons");
            icons.arraySize = 0;
            foreach (var quest in FinikQuestCatalog.Quests)
            {
                var sprite = SpriteOrNull(quest.Icon);
                if (!sprite) continue;
                int i = icons.arraySize++;
                var element = icons.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("name").stringValue = quest.Icon;
                element.FindPropertyRelative("sprite").objectReferenceValue = sprite;
            }
            so.FindProperty("fallbackIcon").objectReferenceValue = SpriteOrNull("quest_placeholder");
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
