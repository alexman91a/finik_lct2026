using System.Linq;
using Finik.UI;
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
    /// Builds «Мой прогресс» over the room: real pet stage, finished tasks, savings goal,
    /// latest plan/fact summary and a ten-card child glossary. Opened by the portrait on the home HUD.
    /// The two tabs share one card, so the child never leaves the familiar progress screen.
    /// </summary>
    public static class FinikProgressScreenBuilder
    {
        const string RootName = "Screen_Progress";
        const float Gap = 14f;
        const float PadX = 40f, PadY = 34f;
        const float HeaderHeight = 104f, TabsHeight = 78f, StageHeight = 154f;
        const float TasksHeight = 108f, GoalHeight = 100f, PeriodHeight = 224f, ButtonsHeight = 104f;
        const float GlossaryCardHeight = 480f, GlossaryNavHeight = 92f;
        const float LandscapeWidth = 860f, PortraitWidth = 1010f;

        static float ProgressHeight => StageHeight + TasksHeight + GoalHeight + PeriodHeight + 3 * Gap;
        static float GlossaryHeight => GlossaryCardHeight + GlossaryNavHeight + Gap;
        static float PanelHeight => 2 * PadY + HeaderHeight + TabsHeight + Mathf.Max(ProgressHeight, GlossaryHeight)
            + ButtonsHeight + 3 * Gap;

        static readonly Color Gold = new(0.85f, 0.6f, 0.13f, 1f);

        /// <summary>
        /// Builds the screen once after a compile when the scene has a HUD but no progress screen yet,
        /// so a fresh checkout does not leave the portrait button leading nowhere.
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

        [MenuItem("Finik/UI/Rebuild Progress Screen")]
        public static void RebuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            if (!Prepare(out string message)) return message;
            var scene = EditorSceneManager.GetActiveScene();
            var hud = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "HUD_Home");
            if (!hud) return "Build the home HUD first (Finik/UI/Rebuild Home HUD).";

            // Same layer as the other room screens: they never open together.
            var root = ReuseOrCreateCanvas(RootName, 25);
            var screen = GetOrAdd<FinikProgressScreen>(root);
            var safe = Rect("SafeArea", root.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<FinikSafeArea>();

            var content = Panel(safe, "Main", PanelHeight, out var panel, out var column);
            SetField(screen, "panel", panel);
            SetField(screen, "profileTitle", Header(content, "icon_star", "ПРОГРЕСС", Gold, "Прогресс питомца"));
            var close = CloseButton(column);
            SetField(screen, "closeButton", close);
            Dismiss(panel, close);

            BuildTabs(content, screen);

            var progress = Section(content, "ProgressContent", ProgressHeight);
            SetField(screen, "progressContent", progress.gameObject);
            BuildStage(progress, screen);
            SetField(screen, "tasksText", InfoCard(progress, "Tasks", "Сегодня: 0 из 3 заданий\nВсего завершено: 0", TasksHeight));
            SetField(screen, "goalText", InfoCard(progress, "Goal", "Цель накопления пока не выбрана", GoalHeight));
            BuildPeriod(progress, screen);

            var glossary = Section(content, "GlossaryContent", GlossaryHeight);
            SetField(screen, "glossaryContent", glossary.gameObject);
            BuildGlossary(glossary, screen);
            glossary.gameObject.SetActive(false);

            var buttons = Rect("Buttons", content);
            Size(buttons, height: ButtonsHeight);
            var row = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            SetField(screen, "doneButton", WideButton(buttons, "Done", "ui_btn_green", "Понятно", 34, Color.white, ButtonsHeight, 420, "ui_decor_paw_green"));

            var binder = hud.GetComponent<FinikHudBinder>();
            if (binder) SetField(binder, "progressScreen", screen);

            // Every button answers at least a 48 dp tap; the component is a no-op on the big ones.
            EnsureTapTargets(root);

            EnsureEventSystem();
            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"Built {RootName}: pet stage, tasks, goal, latest plan/fact and 10-term glossary.";
        }

        // ------------------------------------------------------------------ blocks

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

        /// <summary>Picture, small caps tag and title — the same header the other room screens wear.</summary>
        static TMP_Text Header(Transform content, string iconSprite, string tagText, Color tagColor, string titleText)
        {
            var row = Rect("Header", content);
            Size(row, height: HeaderHeight);
            var icon = Img("Icon", row, iconSprite, preserveAspect: true);
            Place(icon.rectTransform, LeftMiddle, LeftMiddle, Vector2.zero, new Vector2(104, 104));
            icon.gameObject.AddComponent<FinikIdleMotion>().Configure(3f, 3f, 0f, 1.8f, 0f);

            var tag = Text("Tag", row, tagText, 24, tagColor, TextAlignmentOptions.BottomLeft, outlined: false);
            tag.rectTransform.anchorMin = new Vector2(0, 0.66f);
            tag.rectTransform.anchorMax = Vector2.one;
            tag.rectTransform.offsetMin = new Vector2(124, 0);
            tag.rectTransform.offsetMax = new Vector2(0, -2);
            tag.characterSpacing = 4;

            var title = Text("Title", row, titleText, 40, Ink, TextAlignmentOptions.TopLeft, outlined: false);
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = new Vector2(1, 0.66f);
            title.rectTransform.offsetMin = new Vector2(124, -4);
            title.rectTransform.offsetMax = Vector2.zero;
            AutoSize(title, 26, 40);
            return title;
        }

        /// <summary>Round close button on the card's top-right corner.</summary>
        static Button CloseButton(RectTransform column)
        {
            var closeRoot = Rect("Close", column);
            Place(closeRoot, TopRight, Center, new Vector2(-18, -18), new Vector2(84, 84));
            closeRoot.gameObject.AddComponent<FinikPressFeedback>();
            var face = Img("Face", closeRoot, "hud_btn_close", raycast: true, preserveAspect: true);
            Stretch(face.rectTransform);
            return MakeButton(closeRoot, face);
        }

        static RectTransform Section(Transform parent, string name, float height)
        {
            var section = Rect(name, parent);
            Size(section, height: height);
            var layout = section.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = Gap;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return section;
        }

        static void BuildTabs(Transform content, FinikProgressScreen screen)
        {
            var tabs = Rect("Tabs", content);
            Size(tabs, height: TabsHeight);
            var row = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 18f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;

            var progress = WideButton(tabs, "Progress", "ui_btn_white", "Прогресс", 28, Ink, TabsHeight, 310);
            var glossary = WideButton(tabs, "Glossary", "ui_btn_white", "Словарик", 28, Ink, TabsHeight, 310);
            SetField(screen, "progressTabButton", progress);
            SetField(screen, "glossaryTabButton", glossary);
        }

        static void BuildStage(Transform content, FinikProgressScreen screen)
        {
            var card = Img("Stage", content, "ui_card_savings", sliced: true);
            Size(card, height: StageHeight);
            var rt = card.rectTransform;

            var avatar = Img("Icon", rt, "avatar_finik", preserveAspect: true);
            Place(avatar.rectTransform, LeftMiddle, LeftMiddle, new Vector2(22, 0), new Vector2(118, 118));
            SetField(screen, "stageIcon", avatar);

            var title = Text("Title", rt, "Стадия 1 · Малыш", 30, Ink, TextAlignmentOptions.BottomLeft, outlined: false);
            AnchorTop(title.rectTransform, 164, 28, -24, 40);
            AutoSize(title, 24, 30);

            var reason = Text("Reason", rt, "Заверши все задания 2-го дня — и откроется следующая стадия.",
                22, InkSoft, TextAlignmentOptions.TopLeft, outlined: false);
            Wrap(reason);
            AnchorTop(reason.rectTransform, 164, 28, -70, 64);
            SetField(screen, "stageTitle", title);
            SetField(screen, "stageReason", reason);
        }

        static void BuildPeriod(Transform content, FinikProgressScreen screen)
        {
            var card = Img("Period", content, "ui_input", sliced: true);
            Size(card, height: PeriodHeight);
            var rt = card.rectTransform;

            var title = Text("Title", rt, "ИТОГ СЕГОДНЯ", 22, Gold, TextAlignmentOptions.TopLeft, outlined: false);
            title.characterSpacing = 4;
            AnchorTop(title.rectTransform, 28, 28, -18, 34);

            var text = Text("Text", rt,
                "Сначала составь бюджет. Здесь появится сравнение плана и того, что получилось на самом деле.",
                23, Ink, TextAlignmentOptions.TopLeft, outlined: false);
            Wrap(text);
            AnchorTop(text.rectTransform, 28, 28, -58, 146);
            SetField(screen, "periodTitle", title);
            SetField(screen, "periodText", text);
        }

        static void BuildGlossary(Transform content, FinikProgressScreen screen)
        {
            var card = Img("GlossaryCard", content, "ui_card_selected", sliced: true);
            card.color = new Color(1f, 1f, 1f, 0.82f);
            Size(card, height: GlossaryCardHeight);
            var rt = card.rectTransform;

            var index = Text("Index", rt, "1 из 10", 22, InkSoft, TextAlignmentOptions.TopRight, outlined: false);
            AnchorTop(index.rectTransform, 30, 30, -24, 32);

            var term = Text("Term", rt, "Бюджет", 42, Ink, TextAlignmentOptions.TopLeft, outlined: false);
            AnchorTop(term.rectTransform, 36, 160, -62, 58);
            AutoSize(term, 32, 42);

            var definition = Text("Definition", rt, "План, как распределить свои деньги.", 28, Ink,
                TextAlignmentOptions.TopLeft, outlined: false);
            Wrap(definition);
            AnchorTop(definition.rectTransform, 36, 36, -136, 118);

            var exampleCard = Img("Example", rt, "ui_bubble", sliced: true);
            Place(exampleCard.rectTransform, BottomCenter, BottomCenter, new Vector2(0, 34),
                new Vector2(LandscapeWidth - 2 * PadX - 56, 158));
            var example = Text("Text", exampleCard.transform, "Например: часть монет — на важное, часть — в копилку.",
                23, InkSoft, TextAlignmentOptions.TopLeft, outlined: false);
            Wrap(example);
            Stretch(example.rectTransform, 28, 24, 28, 20);

            SetField(screen, "glossaryIndexText", index);
            SetField(screen, "glossaryTerm", term);
            SetField(screen, "glossaryDefinition", definition);
            SetField(screen, "glossaryExample", example);

            var nav = Rect("GlossaryNav", content);
            Size(nav, height: GlossaryNavHeight);
            var row = nav.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 20f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            SetField(screen, "glossaryPrevButton", WideButton(nav, "Prev", "ui_btn_white", "Назад", 28, Ink, GlossaryNavHeight, 250));
            SetField(screen, "glossaryNextButton", WideButton(nav, "Next", "ui_btn_orange", "Дальше", 28, Color.white, GlossaryNavHeight, 250));
        }

        /// <summary>A quiet card with one line of text: what is done today, what the goal is.</summary>
        static TMP_Text InfoCard(Transform content, string name, string value, float height)
        {
            var card = Img(name, content, "ui_card_selected", sliced: true);
            card.color = new Color(1f, 1f, 1f, 0.75f);
            Size(card, height: height);
            var text = Text("Text", card.rectTransform, value, 24, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            Wrap(text);
            Stretch(text.rectTransform, 28, 12, 28, 12);
            return text;
        }

        // ------------------------------------------------------------------ small helpers

        static void AnchorTop(RectTransform rect, float left, float right, float top, float height)
        {
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.offsetMin = new Vector2(left, top - height);
            rect.offsetMax = new Vector2(-right, top);
        }
    }
}
