using System.Collections.Generic;
using System.Linq;
using Finik.Core;
using Finik.Navigation;
using Finik.UI;
using Finik.UI.Budget;
using Finik.UI.Onboarding;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static Finik.Editor.UI.FinikUiKit;
using Object = UnityEngine.Object;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Builds Screen_Budget: the plan step of the day and the plan/fact card that replaces it once the
    /// plan is confirmed. Same column and card style as the quest and savings screens.
    /// </summary>
    public static class FinikBudgetScreenBuilder
    {
        const string RootName = "Screen_Budget";
        const float Gap = 14f;
        const float PadX = 40f, PadY = 34f;
        const float HeaderHeight = 104f, ButtonsHeight = 112f;
        const float EnvelopeHeight = 150f, SummaryHeight = 70f, HintHeight = 92f;
        const float RowHeight = 118f, ConclusionHeight = 120f;
        const float ColumnWidth = 1010f;
        /// <summary>Stepper geometry: − amount + on the right edge of an envelope.</summary>
        const float StepSize = 72f, StepGap = 12f, StepPad = 22f, AmountWidth = 128f;

        static float PlanHeight => 2 * PadY + HeaderHeight + SummaryHeight + 3 * EnvelopeHeight + HintHeight + ButtonsHeight + 6 * Gap;
        static float FactHeight => 2 * PadY + HeaderHeight + 3 * RowHeight + ConclusionHeight + ButtonsHeight + 5 * Gap;

        static readonly Color Accent = new(0.95f, 0.5f, 0.1f, 1f);
        static readonly Color Violet = new(0.55f, 0.3f, 0.95f, 1f);

        /// <summary>Envelope faces, in the order of <see cref="FinikBudgetEngine.Buckets"/>.</summary>
        static readonly (string face, string tint, string icon, string hint)[] Envelopes =
        {
            ("ui_card_needs", "#FFF1E4", "icon_cart", "То, без чего день не получится"),
            ("ui_card_wants", "#F3ECFF", "icon_star", "Приятное, что можно и отложить"),
            ("ui_card_savings", "#E8F9EF", "icon_piggy_coin", "Шаг к цели в копилке")
        };

        [MenuItem("Finik/UI/Rebuild Budget Screen")]
        public static void RebuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            if (!Prepare(out string message)) return message;
            var scene = EditorSceneManager.GetActiveScene();

            var finik = GameObject.Find("Finik_Root");
            if (!finik) return "Finik_Root is missing from the scene.";
            var cameraGo = GameObject.Find("camera_gameplay");
            if (!cameraGo) return "camera_gameplay is missing from the scene.";
            var showcase = cameraGo.GetComponent<FinikShowcaseCamera>();
            if (!showcase) return "FinikShowcaseCamera is missing: run Finik/UI/Rebuild Onboarding first.";
            var hud = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "HUD_Home");
            if (!hud) return "Build the home HUD first (Finik/UI/Rebuild Home HUD).";

            // Same layer as the other room screens: above the HUD, below onboarding.
            var root = ReuseOrCreateCanvas(RootName, 25);
            var screen = GetOrAdd<FinikBudgetScreen>(root);
            var safe = Rect("SafeArea", root.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<FinikSafeArea>();

            BuildPlan(safe, screen);
            BuildFact(safe, screen);

            SetField(screen, "showcase", showcase);
            SetField(screen, "movement", finik.GetComponent<FinikMovementController>());
            SetField(screen, "hudRoot", hud);
            var paused = new List<Object>();
            foreach (var behaviour in new Behaviour[] { finik.GetComponent<FinikInputController>(), finik.GetComponent<FinikWanderController>() })
                if (behaviour) paused.Add(behaviour);
            SetField(screen, "pauseWhileOpen", paused.ToArray());

            var binder = hud.GetComponent<FinikHudBinder>();
            if (binder) SetField(binder, "budgetScreen", screen);

            // Every button answers at least a 48 dp tap; the component is a no-op on the big ones.
            EnsureTapTargets(root);

            EnsureEventSystem();
            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"Built {RootName}: план дня на «Нужно»/«Хочу»/«Коплю» и карточка план/факт.";
        }

        // ------------------------------------------------------------------ shared shell

        static RectTransform Panel(Transform safe, string name, float height, out FinikScreenPanel panel)
        {
            var panelRoot = Rect(name, safe);
            Stretch(panelRoot);
            panelRoot.gameObject.AddComponent<CanvasGroup>();
            panel = panelRoot.gameObject.AddComponent<FinikScreenPanel>();

            var column = Rect("Column", panelRoot);
            column.gameObject.AddComponent<FinikFitInside>();
            column.gameObject.AddComponent<FinikOrientationLayout>().Configure(
                FinikRectLayout.At(LeftMiddle, LeftMiddle, new Vector2(72, 0), new Vector2(ColumnWidth, height)),
                FinikRectLayout.At(BottomCenter, BottomCenter, new Vector2(0, 36), new Vector2(ColumnWidth, height)));
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

        static void Header(Transform content, string tagText, Color tagColor, string titleText, out TMP_Text title)
        {
            var row = Rect("Header", content);
            Size(row, height: HeaderHeight);
            var icon = Img("Icon", row, "icon_coin", preserveAspect: true);
            Place(icon.rectTransform, LeftMiddle, LeftMiddle, new Vector2(0, 0), new Vector2(84, 84));

            var tag = Text("Tag", row, tagText, 24, tagColor, TextAlignmentOptions.TopLeft, outlined: false);
            tag.characterSpacing = 6;
            Place(tag.rectTransform, TopLeft, TopLeft, new Vector2(104, -6), new Vector2(720, 32));

            title = Text("Title", row, titleText, 42, Ink, TextAlignmentOptions.BottomLeft);
            Wrap(title);
            AutoSize(title, 30, 42);
            title.rectTransform.anchorMin = new Vector2(0, 0);
            title.rectTransform.anchorMax = new Vector2(1, 1);
            title.rectTransform.offsetMin = new Vector2(104, 4);
            title.rectTransform.offsetMax = new Vector2(-8, -34);
        }

        /// <summary>Pill progress bar, same construction as the savings screen's.</summary>
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

        static void Buttons(Transform content, out RectTransform row)
        {
            row = Rect("Buttons", content);
            Size(row, height: ButtonsHeight);
            var group = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 18;
            group.childAlignment = TextAnchor.MiddleCenter;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = group.childForceExpandHeight = false;
        }

        // ------------------------------------------------------------------ plan

        static void BuildPlan(Transform safe, FinikBudgetScreen screen)
        {
            var content = Panel(safe, "Plan", PlanHeight, out var panel);
            SetField(screen, "plan", panel);
            Header(content, "ПЛАН НА ДЕНЬ", Accent, "Что сделаем с монетами?", out _);

            // The day's coins and what is still unplanned, side by side.
            var summary = Img("Summary", content, "ui_btn_white", sliced: true, color: FinikSdfCanvas.Hex("#EDF3FF"));
            Size(summary, height: SummaryHeight);
            var coinIcon = Img("Coin", summary.transform, "icon_coin", preserveAspect: true);
            Place(coinIcon.rectTransform, LeftMiddle, LeftMiddle, new Vector2(18, 0), new Vector2(46, 46));
            var income = Text("Income", summary.transform, "30", 34, Ink, TextAlignmentOptions.MidlineLeft);
            Place(income.rectTransform, LeftMiddle, LeftMiddle, new Vector2(74, 0), new Vector2(200, 48));
            SetField(screen, "incomeCounter", income.gameObject.AddComponent<FinikCounterText>());

            var unplanned = Text("Unplanned", summary.transform, "Без задачи: 30", 26, InkSoft, TextAlignmentOptions.MidlineRight, outlined: false);
            AutoSize(unplanned, 20, 26);
            Place(unplanned.rectTransform, RightMiddle, RightMiddle, new Vector2(-18, 0), new Vector2(560, 48));
            SetField(screen, "unplannedLabel", unplanned);

            var amounts = new List<Object>();
            var hints = new List<Object>();
            var minus = new List<Object>();
            var plus = new List<Object>();
            for (int i = 0; i < FinikBudgetEngine.Buckets.Length; i++)
            {
                BuildEnvelope(content, i, out var amount, out var hint, out var minusButton, out var plusButton);
                amounts.Add(amount);
                hints.Add(hint);
                minus.Add(minusButton);
                plus.Add(plusButton);
            }
            SetField(screen, "envelopeAmounts", amounts.Cast<TMP_Text>().ToArray());
            SetField(screen, "envelopeHints", hints.Cast<TMP_Text>().ToArray());
            SetField(screen, "minusButtons", minus.Cast<Button>().ToArray());
            SetField(screen, "plusButtons", plus.Cast<Button>().ToArray());

            var hintCard = Img("HintCard", content, "ui_bubble", sliced: true, color: FinikSdfCanvas.Hex("#FFF8DF"));
            hintCard.pixelsPerUnitMultiplier *= 1.5f;
            Size(hintCard, height: HintHeight);
            var planHint = Text("Hint", hintCard.transform, "Разложи все монеты по трём конвертам: нужное, желания, копилка.", 26, InkSoft, TextAlignmentOptions.MidlineLeft, outlined: false);
            Wrap(planHint);
            AutoSize(planHint, 20, 26);
            planHint.lineSpacing = -4;
            Stretch(planHint.rectTransform, 28, 10, 28, 6);
            SetField(screen, "planHint", planHint);

            Buttons(content, out var buttons);
            var suggest = WideButton(buttons, "Suggest", "ui_btn_white", "Подскажи", 30, Ink, ButtonsHeight, 320);
            var confirm = WideButton(buttons, "Confirm", "ui_btn_orange", "Так и сделаем", 30, Color.white, ButtonsHeight, 440, "ui_decor_star_orange");
            SetField(screen, "suggestButton", suggest);
            SetField(screen, "confirmButton", confirm);
            SetField(screen, "confirmLabel", confirm.transform.Find("Label").GetComponent<TMP_Text>());
            Dismiss(panel, BuildClose(content.parent, screen, "closeButton"));
        }

        static void BuildEnvelope(Transform content, int index, out TMP_Text amount, out TMP_Text hint, out Button minus, out Button plus)
        {
            var bucket = FinikBudgetEngine.Buckets[index];
            var config = Envelopes[index];
            var card = Img("Envelope" + index, content, config.face, sliced: true, color: Color.white);
            Size(card, height: EnvelopeHeight);

            var icon = Img("Icon", card.transform, config.icon, preserveAspect: true);
            Place(icon.rectTransform, TopLeft, TopLeft, new Vector2(20, -16), new Vector2(64, 64));

            var title = Text("Title", card.transform, FinikFoodCatalog.BucketLabel(bucket), 32, Ink, TextAlignmentOptions.TopLeft);
            Place(title.rectTransform, TopLeft, TopLeft, new Vector2(96, -14), new Vector2(420, 48));

            hint = Text("Hint", card.transform, config.hint, 24, InkSoft, TextAlignmentOptions.TopLeft, outlined: false);
            Wrap(hint);
            AutoSize(hint, 20, 24);
            Place(hint.rectTransform, TopLeft, TopLeft, new Vector2(96, -62), new Vector2(460, 56));

            // Stepper on the right, centred against the card: − number +. The amount is boxed to a
            // fixed width and auto-sizes inside it, so three digits never push the buttons apart.
            plus = StepButton(card.transform, "Plus", "+", -StepPad);
            amount = Text("Amount", card.transform, "0", 44, Ink, TextAlignmentOptions.Center);
            AutoSize(amount, 30, 44);
            amount.textWrappingMode = TextWrappingModes.NoWrap;
            Place(amount.rectTransform, RightMiddle, RightMiddle,
                new Vector2(-(StepPad + StepSize + StepGap), 0), new Vector2(AmountWidth, 60));
            minus = StepButton(card.transform, "Minus", "−",
                -(StepPad + StepSize + StepGap + AmountWidth + StepGap));
        }

        /// <summary>A round −/+ control, sized above the 48pt tap minimum on every device.</summary>
        static Button StepButton(Transform parent, string name, string glyph, float right)
        {
            var face = Img(name, parent, "ui_btn_white", sliced: true, color: Color.white, raycast: true);
            Place(face.rectTransform, RightMiddle, RightMiddle, new Vector2(right, 0), new Vector2(StepSize, StepSize));
            var label = Text("Label", face.transform, glyph, 40, Ink, TextAlignmentOptions.Center);
            Stretch(label.rectTransform, 2, 6, 2, 2);
            return MakeButton(face.rectTransform, face);
        }

        static Button BuildClose(Transform column, FinikBudgetScreen screen, string field)
        {
            var face = Img("Close", column, "ui_btn_white", sliced: true, color: Color.white, raycast: true);
            Place(face.rectTransform, TopRight, TopRight, new Vector2(-18, -18), new Vector2(76, 76));
            var label = Text("Label", face.transform, "×", 46, Ink, TextAlignmentOptions.Center);
            Stretch(label.rectTransform, 2, 8, 2, 0);
            var button = MakeButton(face.rectTransform, face);
            SetField(screen, field, button);
            return button;
        }

        // ------------------------------------------------------------------ plan vs fact

        static void BuildFact(Transform safe, FinikBudgetScreen screen)
        {
            var content = Panel(safe, "Fact", FactHeight, out var panel);
            SetField(screen, "fact", panel);
            Header(content, "ПЛАН И ФАКТ", Violet, "Что задумал и что вышло", out _);

            var labels = new List<Object>();
            var values = new List<Object>();
            var bars = new List<Object>();
            for (int i = 0; i < FinikBudgetEngine.Buckets.Length; i++)
            {
                var row = Img("Row" + i, content, Envelopes[i].face, sliced: true, color: Color.white);
                Size(row, height: RowHeight);

                var icon = Img("Icon", row.transform, Envelopes[i].icon, preserveAspect: true);
                Place(icon.rectTransform, LeftMiddle, LeftMiddle, new Vector2(20, 0), new Vector2(60, 60));

                var label = Text("Label", row.transform, FinikFoodCatalog.BucketLabel(FinikBudgetEngine.Buckets[i]), 30, Ink, TextAlignmentOptions.TopLeft);
                Place(label.rectTransform, TopLeft, TopLeft, new Vector2(96, -16), new Vector2(420, 38));

                var value = Text("Value", row.transform, "0 из 0", 28, Ink, TextAlignmentOptions.TopRight, outlined: false);
                AutoSize(value, 22, 28);
                Place(value.rectTransform, TopRight, TopRight, new Vector2(-24, -16), new Vector2(320, 38));

                var bar = Bar("Track", row.transform, i == 2 ? "#28B77B" : "#F58019");
                Place((RectTransform)bar.transform, BottomLeft, BottomLeft, new Vector2(96, 22), new Vector2(ColumnWidth - 2 * PadX - 140, 26));

                labels.Add(label);
                values.Add(value);
                bars.Add(bar);
            }
            SetField(screen, "factLabels", labels.Cast<TMP_Text>().ToArray());
            SetField(screen, "factValues", values.Cast<TMP_Text>().ToArray());
            SetField(screen, "factBars", bars.Cast<FinikFillBar>().ToArray());

            var conclusionCard = Img("Conclusion", content, "ui_bubble", sliced: true, color: FinikSdfCanvas.Hex("#EDF3FF"));
            conclusionCard.pixelsPerUnitMultiplier *= 1.5f;
            Size(conclusionCard, height: ConclusionHeight);
            var conclusion = Text("Text", conclusionCard.transform, "Удалось придерживаться плана.", 28, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            Wrap(conclusion);
            AutoSize(conclusion, 22, 28);
            Stretch(conclusion.rectTransform, 28, 12, 28, 8);
            SetField(screen, "factConclusion", conclusion);

            Buttons(content, out var buttons);
            var back = WideButton(buttons, "Back", "ui_btn_white", "В комнату", 30, Ink, ButtonsHeight, 340);
            SetField(screen, "factCloseButton", back);
            var endDay = WideButton(buttons, "EndDay", "ui_btn_green", "Завершить день", 30, Color.white, ButtonsHeight, 480, "ui_decor_paw_green");
            SetField(screen, "endDayButton", endDay);
            Dismiss(panel, back);
        }
    }
}
