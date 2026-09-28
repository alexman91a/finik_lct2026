using System.Collections.Generic;
using System.Linq;
using Finik.Core;
using Finik.Navigation;
using Finik.UI;
using Finik.UI.Adult;
using Finik.UI.Onboarding;
using Finik.UI.Settings;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static Finik.Editor.UI.FinikUiKit;
using static Finik.Editor.UI.FinikMenuPanels;
using Object = UnityEngine.Object;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Builds Screen_Adult: the two-step gate (hold, then an addition on the screen's own keypad), the
    /// parent-facing report and the confirmation card the two destructive actions go through.
    /// </summary>
    public static class FinikAdultScreenBuilder
    {
        const string RootName = "Screen_Adult";
        const float KeyWidth = 160f, KeyHeight = 92f, KeyGap = 14f;
        const float SwitchWidth = 196f, SwitchHeight = 84f;

        /// <summary>One row per curriculum line, so no empty slot is left in the layout.</summary>
        static int TopicRows => FinikAdult.TopicCount;

        [MenuItem("Finik/UI/Rebuild Adult Screen")]
        public static void RebuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            if (!Prepare(out string message)) return message;
            var scene = EditorSceneManager.GetActiveScene();

            var finik = GameObject.Find("Finik_Root");
            if (!finik) return "Finik_Root is missing from the scene.";
            var cameraGo = GameObject.Find("camera_gameplay");
            var showcase = cameraGo ? cameraGo.GetComponent<FinikShowcaseCamera>() : null;
            if (!showcase) return "FinikShowcaseCamera is missing: run Finik/UI/Rebuild Onboarding first.";
            var hud = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "HUD_Home");
            if (!hud) return "Build the home HUD first (Finik/UI/Rebuild Home HUD).";
            var settings = Object.FindAnyObjectByType<FinikSettingsScreen>(FindObjectsInactive.Include);
            if (!settings) return "Build the settings first (Finik/UI/Rebuild Settings Screen): the adult section is reached from there.";

            var root = ReuseOrCreateCanvas(RootName, 27);
            var screen = GetOrAdd<FinikAdultScreen>(root);
            var safe = Rect("SafeArea", root.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<FinikSafeArea>();

            BuildGate(safe, screen);
            BuildReport(safe, screen);
            BuildConfirm(safe, screen);

            SetField(screen, "settingsScreen", settings);
            SetField(settings, "adultScreen", screen);
            SetField(screen, "showcase", showcase);
            SetField(screen, "movement", finik.GetComponent<FinikMovementController>());
            SetField(screen, "hudRoot", hud);
            var paused = new List<Object>();
            foreach (var behaviour in new Behaviour[] { finik.GetComponent<FinikInputController>(), finik.GetComponent<FinikWanderController>() })
                if (behaviour) paused.Add(behaviour);
            SetField(screen, "pauseWhileOpen", paused.ToArray());

            // Every button answers at least a 48 dp tap; the component is a no-op on the big ones.
            EnsureTapTargets(root);
            EnsureEventSystem();
            EditorUtility.SetDirty(root);
            EditorUtility.SetDirty(settings);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"Built {RootName}: проверка (удержание + пример на клавиатуре), отчёт для взрослого и подтверждения.";
        }

        // ------------------------------------------------------------------ gate

        static void BuildGate(Transform safe, FinikAdultScreen screen)
        {
            var menu = FinikMenuPanels.Build(safe, "Gate", footer: true);
            SetField(screen, "gate", menu.panel);
            Header(menu, "game_lock", "ДЛЯ ВЗРОСЛЫХ", Accent, "Проверка взрослого");
            var content = menu.content;

            // Two dots and the step's name.
            var steps = Row(content, "Steps", 40, 10);
            var dots = new List<Object>();
            for (int i = 0; i < 2; i++)
            {
                var holder = Rect("Dot" + i, steps);
                Size(holder, 22, 22);
                var dot = Img("Image", holder, "ui_dot", color: Accent);
                Stretch(dot.rectTransform);
                dots.Add(dot);
            }
            var stepLabel = Text("Label", steps, "Шаг 1 из 2 — удержание", 26, Accent, TextAlignmentOptions.MidlineLeft, outlined: false);
            Size(stepLabel, flexibleWidth: 1);
            SetField(screen, "stepLabel", stepLabel);
            SetField(screen, "stepDots", dots.Cast<Image>().ToArray());

            BuildHold(content, screen);
            BuildChallenge(content, screen);

            var back = WideButton(menu.footer, "Back", "ui_btn_white", "Назад", 32, Color.white, FooterHeight, 360);
            SetField(screen, "gateBackButton", back);
            var close = CloseCorner(menu);
            SetField(screen, "gateCloseButton", close);
            Dismiss(menu.panel, close);
        }

        static void BuildHold(Transform content, FinikAdultScreen screen)
        {
            var holdRoot = Stack(content, "Hold", 14);
            // Only step one explains the section: step two needs the room for the keypad.
            Paragraph(holdRoot, "Intro",
                "Дальше — подробный прогресс ребёнка и управление профилем. Короткая проверка не даёт ребёнку зайти сюда случайно.",
                26, InkSoft);
            var holdSlot = Rect("ButtonSlot", holdRoot);
            Size(holdSlot, height: 112);
            var holdFace = Img("Button", holdSlot, "ui_btn_white", sliced: true, color: Color.white, raycast: true);
            Stretch(holdFace.rectTransform);
            holdFace.gameObject.AddComponent<FinikPressFeedback>();
            var fillClip = Rect("FillArea", holdFace.transform);
            Stretch(fillClip, 6, 6, 6, 6);
            fillClip.gameObject.AddComponent<RectMask2D>();
            var fill = Img("Fill", fillClip, "ui_btn_orange", sliced: true, color: Color.white);
            var holdLabel = Text("Label", holdFace.transform, "Нажмите и держите", 34, Color.white, TextAlignmentOptions.Center);
            AutoSize(holdLabel, 26, 34);
            Stretch(holdLabel.rectTransform, 24, 14, 24, 4);
            var hold = holdFace.gameObject.AddComponent<FinikAdultHoldButton>();
            SetField(hold, "fill", fill.rectTransform);
            SetField(hold, "seconds", FinikAdult.HoldSeconds);
            SetField(screen, "holdButton", hold);
            SetField(screen, "holdRoot", holdRoot.gameObject);

            Paragraph(holdRoot, "Hint", "Держите кнопку, пока полоска не заполнится, — около двух секунд.", 24, InkSoft, TextAlignmentOptions.Top);
        }

        static void BuildChallenge(Transform content, FinikAdultScreen screen)
        {
            var challengeRoot = Stack(content, "Challenge", 14);
            challengeRoot.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            SetField(screen, "challengeRoot", challengeRoot.gameObject);

            // The task and the answer on one line, so the keypad fits a landscape phone too.
            var task = Row(challengeRoot, "Task", 100, 24);
            var prompt = Text("Prompt", task, "Сколько будет 7 + 5?", 40, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            AutoSize(prompt, 30, 40);
            Size(prompt, flexibleWidth: 1);
            SetField(screen, "challengePrompt", prompt);
            var answerFace = Img("Answer", task, "ui_input", sliced: true, color: Color.white);
            Size(answerFace, 260, 100);
            var answer = Text("Value", answerFace.transform, "?", 52, Ink, TextAlignmentOptions.Center, outlined: false);
            Stretch(answer.rectTransform, 12, 6, 12, 0);
            SetField(screen, "answerText", answer);

            var error = Text("Error", challengeRoot, string.Empty, 24, Danger, TextAlignmentOptions.Center, outlined: false);
            Size(error, height: 34);
            SetField(screen, "answerError", error);

            // 1 2 3 4 5 / 6 7 8 9 0 / Стереть · Войти — the phone's own keyboard would cover half the card.
            var digits = new Object[10];
            foreach (var keys in new[] { new[] { 1, 2, 3, 4, 5 }, new[] { 6, 7, 8, 9, 0 } })
            {
                var row = KeyRow(challengeRoot, "Keys" + keys[0]);
                foreach (int digit in keys)
                    digits[digit] = Flexible(WideButton(row, "Key" + digit, "ui_btn_white", digit.ToString(), 44, Color.white, KeyHeight, KeyWidth));
            }
            var actions = KeyRow(challengeRoot, "Actions");
            var erase = Flexible(WideButton(actions, "Erase", "ui_btn_white", "Стереть", 30, Color.white, KeyHeight, KeyWidth));
            var enter = Flexible(WideButton(actions, "Enter", "ui_btn_green", "Войти", 32, Color.white, KeyHeight, KeyWidth));
            SetField(screen, "digitButtons", digits);
            SetField(screen, "eraseButton", erase);
            SetField(screen, "answerButton", enter);
        }

        static RectTransform KeyRow(Transform parent, string name)
        {
            var row = Row(parent, name, KeyHeight, KeyGap, TextAnchor.MiddleCenter);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            return row;
        }

        // ------------------------------------------------------------------ report

        /// <summary>
        /// The report in three tabs, so no page is a wall of cards: «Прогресс» (numbers, goal, the day),
        /// «Обучение» (the curriculum and how the game works), «Управление» (device and profile).
        /// </summary>
        static void BuildReport(Transform safe, FinikAdultScreen screen)
        {
            var menu = FinikMenuPanels.Build(safe, "Report", footer: true, wide: true, strip: true);
            SetField(screen, "report", menu.panel);
            SetField(screen, "reportScroll", menu.scroll);
            Header(menu, "game_lock", "ОТЧЁТ И НАСТРОЙКИ", Violet, "Для взрослых");

            var tabs = menu.strip.gameObject.AddComponent<HorizontalLayoutGroup>();
            tabs.spacing = 14;
            tabs.childAlignment = TextAnchor.MiddleCenter;
            tabs.childControlWidth = tabs.childControlHeight = true;
            tabs.childForceExpandWidth = tabs.childForceExpandHeight = true;
            var chips = new List<Object>();
            foreach (var (id, label) in new[] { ("Progress", "Прогресс"), ("Learning", "Обучение"), ("Manage", "Управление") })
                chips.Add(Chip(menu.strip, id, label));
            SetField(screen, "tabChips", chips.ToArray());

            var progress = Page(menu.content, "Progress");
            // Upright: numbers, the day, the goal. Wide: numbers and the goal on the left, the day on the right.
            BuildMetrics(progress, screen);
            BuildDay(progress, screen);
            BuildGoal(progress, screen);

            var learning = Page(menu.content, "Learning");
            BuildTopics(learning, screen);
            var about = Section(learning, "About", "КАК УСТРОЕН ФИНИК", Accent, "ui_card_task");
            SetField(screen, "parentNote", Paragraph(about, "Text", FinikAdult.ParentNote, 26, Ink));

            var manage = Page(menu.content, "Manage");
            var device = Section(manage, "Device", "УСТРОЙСТВО", Violet);
            var counter = SettingRow(device, "FrameCounter", "Счётчик кадров", "Кадры в секунду и график поверх игры.",
                SwitchWidth, SwitchHeight, out var counterNote);
            SetField(screen, "frameCounterSwitch", ToggleSwitch(counter, SwitchHeight));
            SetField(screen, "frameCounterNote", counterNote);
            BuildProfile(manage, screen);

            SetField(screen, "tabPages", new Object[] { progress.gameObject, learning.gameObject, manage.gameObject });
            learning.gameObject.SetActive(false);
            manage.gameObject.SetActive(false);

            var back = WideButton(menu.footer, "Back", "ui_btn_white", "Назад", 32, Color.white, FooterHeight, 280);
            var howTo = WideButton(menu.footer, "HowToPlay", "ui_btn_white", "Как играть", 30, Color.white, FooterHeight, 320);
            var done = WideButton(menu.footer, "Done", "ui_btn_green", "Готово", 34, Color.white, FooterHeight, 360, "ui_decor_paw_green");
            SetField(screen, "reportBackButton", back);
            SetField(screen, "howToPlayButton", howTo);
            SetField(screen, "reportDoneButton", done);
            // The report is its own panel, so it carries its own «×».
            SetField(screen, "reportCloseButton", CloseCorner(menu));
            Dismiss(menu.panel, done);
        }

        static void BuildMetrics(Transform content, FinikAdultScreen screen)
        {
            var metrics = Row(content, "Metrics", 150, 16, TextAnchor.MiddleCenter);
            var group = metrics.GetComponent<HorizontalLayoutGroup>();
            group.childForceExpandWidth = group.childForceExpandHeight = true;
            SetField(screen, "questsValue", Metric(metrics, "Quests", "Заданий сегодня"));
            SetField(screen, "daysValue", Metric(metrics, "Days", "Дней пройдено"));
            SetField(screen, "topicsValue", Metric(metrics, "Topics", "Периодов"));
        }

        static TMP_Text Metric(Transform parent, string name, string caption)
        {
            var card = Img(name, parent, "ui_card", sliced: true, color: Color.white);
            Size(card, flexibleWidth: 1);
            var value = Text("Value", card.transform, "0", 44, Ink, TextAlignmentOptions.Center, outlined: false);
            AutoSize(value, 30, 44);
            Place(value.rectTransform, TopCenter, TopCenter, new Vector2(0, -20), new Vector2(260, 56));
            var label = Text("Caption", card.transform, caption, 24, InkSoft, TextAlignmentOptions.Top, outlined: false);
            Wrap(label);
            AutoSize(label, 24, 24);
            Place(label.rectTransform, TopCenter, TopCenter, new Vector2(0, -80), new Vector2(260, 56));
            return value;
        }

        static void BuildGoal(Transform content, FinikAdultScreen screen)
        {
            var section = Section(content, "Goal", "ЦЕЛЬ В КОПИЛКЕ", Good, "ui_card_savings");
            var row = Row(section, "Row", -1, 20);
            var iconHolder = Rect("Icon", row);
            Size(iconHolder, 76, 76);
            Stretch(Img("Image", iconHolder, "icon_piggy_coin", preserveAspect: true).rectTransform);
            var copy = Stack(row, "Copy", 6);
            Size(copy, flexibleWidth: 1);

            var titleRow = Row(copy, "TitleRow", 44, 12);
            var title = Text("Title", titleRow, "Велосипед", 32, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            AutoSize(title, 26, 32);
            Size(title, flexibleWidth: 1);
            var percent = Text("Percent", titleRow, "0%", 32, Good, TextAlignmentOptions.MidlineRight, outlined: false);
            Size(percent, 110, 44);
            SetField(screen, "goalTitle", title);
            SetField(screen, "goalPercent", percent);

            SetField(screen, "goalAmounts", Paragraph(copy, "Amounts", "Накоплено 0 из 90, осталось 90.", 24, InkSoft));

            var bar = Bar("Bar", copy, "#28B77B");
            Size(bar, height: 28);
            SetField(screen, "goalBar", bar);
        }

        static void BuildTopics(Transform content, FinikAdultScreen screen)
        {
            var section = Section(content, "Topics", "ЧТО ИЗУЧАЕТ РЕБЁНОК", Violet);
            var rows = new List<Object>();
            var titles = new List<Object>();
            var descriptions = new List<Object>();
            var marks = new List<Object>();
            for (int i = 0; i < TopicRows; i++)
            {
                var row = Row(section, "Topic" + i, -1, 16, TextAnchor.UpperLeft);
                var markHolder = Rect("MarkSlot", row);
                Size(markHolder, 44, 44);
                // A picture, not a character: the UI font has no tick, and the screen showed an empty
                // box in front of every line the child had already practised.
                var mark = Img("Mark", markHolder, "ui_dot", preserveAspect: true, color: InkSoft);
                Stretch(mark.rectTransform);
                mark.rectTransform.localScale = Vector3.one * 0.4f;
                var copy = Stack(row, "Copy", 2);
                Size(copy, flexibleWidth: 1);
                var title = Text("Title", copy, "Различает «нужно» и «хочу»", 28, Ink, TextAlignmentOptions.TopLeft, outlined: false);
                Wrap(title);
                var description = Paragraph(copy, "Description", "Описание темы.", 24, InkSoft);
                rows.Add(row.gameObject);
                titles.Add(title);
                descriptions.Add(description);
                marks.Add(mark);
            }
            Paragraph(section, "Legend", "Зелёная отметка — тема уже встречалась в заданиях. Это не оценка ребёнка.", 24, InkSoft);

            SetField(screen, "topicRows", rows.ToArray());
            SetField(screen, "topicTitles", titles.ToArray());
            SetField(screen, "topicDescriptions", descriptions.ToArray());
            SetField(screen, "topicMarks", marks.ToArray());
            SetField(screen, "topicPractisedIcon", SpriteOrNull("ui_check"));
            SetField(screen, "topicPendingIcon", SpriteOrNull("ui_dot"));
        }

        static void BuildDay(Transform content, FinikAdultScreen screen)
        {
            var section = Section(content, "Day", "ИТОГ ДНЯ", Accent);
            var income = Text("Income", section, "Получено за день: 30", 28, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            Size(income, height: 40);
            SetField(screen, "dayIncome", income);

            // Plan against what happened, one line per envelope.
            TableRow(section, "Head", "", "План", "Факт", InkSoft, 24, out _, out _);
            var planned = new Object[3];
            var actual = new Object[3];
            var labels = new[] { ("Needs", "Необходимое"), ("Wants", "Желаемое"), ("Savings", "В копилку") };
            for (int i = 0; i < labels.Length; i++)
            {
                TableRow(section, labels[i].Item1, labels[i].Item2, "0", "0", Ink, 28, out var plan, out var fact);
                planned[i] = plan;
                actual[i] = fact;
            }
            SetField(screen, "dayPlanned", planned);
            SetField(screen, "dayActual", actual);
            SetField(screen, "dayConclusion", Paragraph(section, "Conclusion", "Удалось придерживаться плана.", 26, Ink));
        }

        static void TableRow(Transform section, string name, string label, string plan, string fact, Color color, float size,
            out TMP_Text planText, out TMP_Text factText)
        {
            var row = Row(section, name, size + 12, 12);
            var caption = Text("Label", row, label, size, color, TextAlignmentOptions.MidlineLeft, outlined: false);
            Size(caption, flexibleWidth: 1);
            planText = Text("Plan", row, plan, size, color, TextAlignmentOptions.MidlineRight, outlined: false);
            Size(planText, 150, size + 12);
            factText = Text("Fact", row, fact, size, color, TextAlignmentOptions.MidlineRight, outlined: false);
            Size(factText, 150, size + 12);
        }

        static void BuildProfile(Transform content, FinikAdultScreen screen)
        {
            var section = Section(content, "Profile", "УПРАВЛЕНИЕ ПРОФИЛЕМ", Danger);
            Paragraph(section, "Note", "Оба действия необратимы и затрагивают только это устройство.", 24, InkSoft);
            var result = Paragraph(section, "Result", string.Empty, 26, Good);
            result.gameObject.SetActive(false);
            SetField(screen, "actionResult", result);

            var buttons = Row(section, "Buttons", 96, 18, TextAnchor.MiddleCenter);
            var reset = Flexible(WideButton(buttons, "Reset", "ui_btn_white", "Сбросить прогресс", 28, Color.white, 96, 400));
            var delete = Flexible(WideButton(buttons, "Delete", "ui_btn_red", "Удалить профиль", 28, Color.white, 96, 400));
            SetField(screen, "resetButton", reset);
            SetField(screen, "deleteButton", delete);
        }

        static FinikFillBar Bar(string name, Transform parent, string color)
        {
            var track = Img(name, parent, "hud_bar_track", sliced: true);
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

        // ------------------------------------------------------------------ confirm

        static void BuildConfirm(Transform safe, FinikAdultScreen screen)
        {
            var menu = FinikMenuPanels.Build(safe, "Confirm", footer: true);
            SetField(screen, "confirm", menu.panel);
            var title = Header(menu, "game_lock", "ПОДТВЕРЖДЕНИЕ", Danger, "Сбросить прогресс?");
            SetField(screen, "confirmTitle", title);

            var body = Paragraph(menu.content, "Body",
                "Дни, задания, кошелёк, копилка и состояние питомца начнутся заново. Это действие нельзя отменить.",
                28, Ink);
            SetField(screen, "confirmBody", body);

            var no = WideButton(menu.footer, "No", "ui_btn_white", "Отмена", 32, Color.white, FooterHeight, 340);
            var yes = WideButton(menu.footer, "Yes", "ui_btn_red", "Сбросить", 32, Color.white, FooterHeight, 380);
            SetField(screen, "confirmNoButton", no);
            SetField(screen, "confirmYesButton", yes);
            SetField(screen, "confirmYesLabel", yes.transform.Find("Label").GetComponent<TMP_Text>());
            // A tap beside the card is the same answer as «Отмена»: nothing destructive happens.
            Dismiss(menu.panel, no);
        }
    }
}
