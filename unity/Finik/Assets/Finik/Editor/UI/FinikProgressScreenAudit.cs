using System.Linq;
using Finik.Core;
using Finik.UI;
using Finik.UI.Onboarding;
using UnityEditor;
using UnityEngine;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Play Mode check of the child's progress card: all three real pet stages, with/without
    /// a savings goal, a populated plan/fact summary, and every glossary card. Test state is restored.
    ///
    ///     Finik.Editor.UI.FinikProgressScreenAudit.SetResolution(1080, 1920);  // then let a few frames pass
    ///     Finik.Editor.UI.FinikProgressScreenAudit.Run();
    /// </summary>
    public static class FinikProgressScreenAudit
    {
        static readonly (string a, string b)[] IntendedOverlaps =
        {
            ("Coin", "Xp"),
            ("Icon", "Level")
        };

        static readonly string[] ContentImages = { "Icon", "Coin", "Picture" };

        static readonly string[] Containers = { "Face", "Hero", "Plate", "Card" };

        [MenuItem("Finik/UI/Audit Progress Screen")]
        static void Menu() => Debug.Log(Run());

        public static string SetResolution(int width, int height) => FinikUiAudit.SetResolution(width, height);

        public static string EnsureProfile() => FinikUiAudit.EnsureProfile();

        public static string Run()
        {
            if (!EditorApplication.isPlaying) return "Enter Play Mode first.";
            var screen = Object.FindAnyObjectByType<FinikProgressScreen>(FindObjectsInactive.Include);
            if (!screen) return "Screen_Progress is missing: run Finik/UI/Rebuild Progress Screen.";
            if (!FinikProfileStore.TryLoad(out _)) return "No profile: finish onboarding first.";

            var audit = new FinikUiAudit(IntendedOverlaps, ContentImages, Containers);
            var panel = (FinikScreenPanel)FinikUiAudit.Get(screen, "panel");
            var column = (RectTransform)panel.transform.Find("Column");

            var state = FinikGame.State;
            int savedStage = state?.petStage ?? 1;
            string savedGoal = state?.goalId;
            var savedPlan = state?.budget?.plan;
            var savedActuals = state?.budget?.actuals;

            if (state?.budget != null)
            {
                state.budget.plan = new FinikBudgetPlan
                {
                    dayKey = FinikGame.BudgetDayKey, income = 120, needs = 60, wants = 30, savings = 30
                };
                state.budget.actuals = new FinikBudgetActuals
                {
                    dayKey = FinikGame.BudgetDayKey, needs = 55, wants = 25, savings = 30
                };
            }

            screen.Open();
            panel.Show(instant: true);

            foreach (int stage in new[] { 1, 2, 3 })
            {
                if (state != null) state.petStage = stage;
                foreach (var (goalId, goalWhat) in new[] { (PriciestGoalId(), "с целью"), (string.Empty, "без цели") })
                {
                    if (state != null) state.goalId = goalId;
                    FinikUiAudit.Call(screen, "Refresh");
                    FinikUiAudit.Call(screen, "ShowProgressTab");
                    audit.Check(column, $"прогресс / стадия {stage}, {goalWhat}");
                }
            }

            FinikUiAudit.Call(screen, "ShowGlossaryTab");
            for (int i = 0; i < FinikGlossary.Count; i++)
            {
                FinikUiAudit.Set(screen, "glossaryIndex", i);
                FinikUiAudit.Call(screen, "RenderGlossary");
                audit.Check(column, $"словарик / {i + 1} из {FinikGlossary.Count}");
            }

            if (state != null)
            {
                state.petStage = savedStage;
                state.goalId = savedGoal;
                if (state.budget != null)
                {
                    state.budget.plan = savedPlan;
                    state.budget.actuals = savedActuals;
                }
            }
            FinikUiAudit.Call(screen, "Refresh");
            FinikUiAudit.Call(screen, "ShowProgressTab");
            screen.Close();

            return audit.Report("Progress screen", column, System.Array.Empty<string>());
        }

        static string PriciestGoalId() => FinikGoalCatalog.Goals.OrderByDescending(g => g.Price).First().Id;

    }
}
