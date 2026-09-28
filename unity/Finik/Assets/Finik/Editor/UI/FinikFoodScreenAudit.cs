using System;
using System.Linq;
using System.Reflection;
using Finik.Core;
using Finik.UI.Food;
using Finik.UI.Onboarding;
using UnityEditor;
using UnityEngine;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Play Mode check of the food screen at the current Game view resolution: walks every life scene
    /// through choosing, the preview of each option with a full and an empty wallet, a cooldown note and the
    /// result card of every plan, and reports text that overflows, gets cut, shrinks to its minimum
    /// size, collides with a neighbour or breaks lines badly (checks in <see cref="FinikUiAudit"/>).
    ///
    ///     Finik.Editor.UI.FinikFoodScreenAudit.SetResolution(1080, 1920);  // then let a few frames pass
    ///     Finik.Editor.UI.FinikFoodScreenAudit.Run();
    /// </summary>
    public static class FinikFoodScreenAudit
    {
        // Overlaps that are part of the design: the verdict tag is pinned onto the dish.
        static readonly (string a, string b)[] IntendedOverlaps = { ("Hero/Verdict", "Hero/Dish") };

        // Content that must not collide. Card faces, frames, pills and boxes are backgrounds.
        static readonly string[] ContentImages = { "Icon", "SceneIcon", "Coin", "Check", "Avatar", "Dish" };

        // Backgrounds a text must stay inside: the message box, an option card, the result strip, a tag.
        static readonly string[] Containers = { "Message", "Face", "Changes", "Verdict" };

        [MenuItem("Finik/UI/Audit Food Screen")]
        static void Menu() => Debug.Log(Run());

        public static string SetResolution(int width, int height) => FinikUiAudit.SetResolution(width, height);

        public static string EnsureProfile() => FinikUiAudit.EnsureProfile();

        public static string Capture(string path) => FinikUiAudit.Capture(path);

        /// <summary>Runs every scene and state and returns a report. Leaves the screen open.</summary>
        public static string Run()
        {
            if (!EditorApplication.isPlaying) return "Enter Play Mode first.";
            var screen = FindScreen(out string error);
            if (!screen) return error;

            var audit = new FinikUiAudit(IntendedOverlaps, ContentImages, Containers);
            var column = Column(screen, "Panel");
            var resultColumn = Column(screen, "Result");
            var result = (FinikFoodResultView)Get(screen, "result");
            var stageType = typeof(FinikFoodScreen).GetNestedType("Stage", BindingFlags.NonPublic);

            if (!screen.IsOpen && !screen.Open(FinikFoodCatalog.Scenes[0].Id)) return "Food screen did not open: finish onboarding first.";
            result.Panel.Hide(instant: true);
            ((FinikScreenPanel)Get(screen, "panel")).Show(instant: true);

            foreach (var scene in FinikFoodCatalog.Scenes)
            {
                Set(screen, "scene", scene);
                Call(screen, "RenderScene");

                SetStage(screen, stageType, "Choosing", null);
                audit.Check(column, $"{scene.Id} / выбор");

                foreach (string planId in scene.OptionIds)
                {
                    if (!FinikFoodCatalog.TryGetPlan(planId, out _)) continue;
                    Call(screen, "Select", planId);
                    audit.Check(column, $"{scene.Id} / превью {planId}");
                    // An empty wallet: the card says «не хватает», the button is disabled with the gap.
                    Set(screen, "balanceOverride", (int?)0);
                    Call(screen, "Render", (object)null);
                    audit.Check(column, $"{scene.Id} / нехватка {planId}");
                    Set(screen, "balanceOverride", null);
                }

                // The cooldown note replaces the footnote on the preview; use the longest plan title.
                var longest = scene.OptionIds.Select(id => FinikFoodCatalog.TryGetPlan(id, out var p) ? p : null)
                    .Where(p => p != null).OrderByDescending(p => p.Title.Length).First();
                Call(screen, "Select", longest.Id);
                Call(screen, "Render", "Питомец только что поел. Следующий выбор будет доступен через 59 сек.");
                audit.Check(column, $"{scene.Id} / кулдаун {longest.Id}");
            }

            foreach (var plan in FinikFoodCatalog.Plans)
            {
                result.Show(new FinikFoodResultView.Outcome(plan, plan.Result, plan.TotalCost, plan.Deltas, plan.Xp), null, null);
                audit.Check(resultColumn, $"результат {plan.Id}");
            }
            // Leave the screen open on the first scene: closing releases the camera asynchronously,
            // and a Present() right after would land in the middle of that.
            result.Panel.Hide(instant: true);
            Set(screen, "scene", FinikFoodCatalog.Scenes[0]);
            Call(screen, "RenderScene");
            SetStage(screen, stageType, "Choosing", null);

            var catalog = Resources.Load<TextAsset>(FinikFoodCatalog.ResourcePath);
            return audit.Report("Food screen", column, FinikFoodCatalog.Parse(catalog ? catalog.text : string.Empty, out _, out _));
        }

        /// <summary>Shows one state and leaves it on screen for a screenshot. Stage: choosing, preview, shortage, result.</summary>
        public static string Present(string sceneId, string stage, string planId = null)
        {
            if (!EditorApplication.isPlaying) return "Enter Play Mode first.";
            var screen = FindScreen(out string error);
            if (!screen) return error;
            if (!FinikFoodCatalog.TryGetScene(sceneId, out var scene)) return $"Unknown scene {sceneId}";
            FinikFoodCatalog.TryGetPlan(planId ?? scene.OptionIds[0], out var plan);
            if (!screen.IsOpen && !screen.Open(sceneId)) return "Food screen did not open: finish onboarding first.";

            var panel = (FinikScreenPanel)Get(screen, "panel");
            var result = (FinikFoodResultView)Get(screen, "result");
            var stageType = typeof(FinikFoodScreen).GetNestedType("Stage", BindingFlags.NonPublic);
            Set(screen, "balanceOverride", null);
            Set(screen, "scene", scene);
            Call(screen, "RenderScene");
            result.Panel.Hide(instant: true);
            panel.Show(instant: true);
            switch (stage)
            {
                case "preview":
                    Call(screen, "Select", plan.Id);
                    break;
                case "shortage":
                    Set(screen, "balanceOverride", (int?)0);
                    Call(screen, "RenderScene");
                    Call(screen, "Select", plan.Id);
                    break;
                case "result":
                    panel.Hide(instant: true);
                    var icons = (FinikFoodScreen.IconEntry[])Get(screen, "icons");
                    var sprite = icons.FirstOrDefault(e => e.name == plan.Icon).sprite;
                    result.Show(new FinikFoodResultView.Outcome(plan, plan.Result, plan.TotalCost, plan.Deltas, plan.Xp), sprite, null);
                    break;
                default:
                    SetStage(screen, stageType, "Choosing", null);
                    break;
            }
            return $"{sceneId} / {stage} {plan.Id} at {Screen.width}x{Screen.height}";
        }

        // ------------------------------------------------------------------ plumbing

        static FinikFoodScreen FindScreen(out string error)
        {
            var screen = UnityEngine.Object.FindAnyObjectByType<FinikFoodScreen>(FindObjectsInactive.Include);
            error = screen ? null : "Screen_Food is missing: run Finik/UI/Rebuild Food Screen.";
            return screen;
        }

        static RectTransform Column(FinikFoodScreen screen, string panelName) =>
            (RectTransform)screen.transform.Find($"SafeArea/{panelName}/Column");

        static void SetStage(FinikFoodScreen screen, Type stageType, string stage, FinikFoodPlan selected)
        {
            Set(screen, "selected", selected);
            Set(screen, "stage", Enum.Parse(stageType, stage));
            Call(screen, "Render", (object)null);
        }

        static object Get(object target, string field) => FinikUiAudit.Get(target, field);
        static void Set(object target, string field, object value) => FinikUiAudit.Set(target, field, value);
        static void Call(object target, string method, params object[] args) => FinikUiAudit.Call(target, method, args);
    }
}
