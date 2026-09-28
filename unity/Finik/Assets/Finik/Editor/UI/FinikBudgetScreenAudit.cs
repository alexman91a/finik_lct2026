using Finik.Core;
using Finik.UI.Budget;
using Finik.UI.Onboarding;
using UnityEditor;
using UnityEngine;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Play Mode check of the budget screen at the current Game view resolution: the planner with every
    /// coin placed, with coins still unplanned and with an empty wallet, plus the plan/fact card.
    /// Nothing is saved — the draft is driven directly and the panels are rendered in place.
    ///
    ///     Finik.Editor.UI.FinikBudgetScreenAudit.SetResolution(1080, 1920);  // then let a few frames pass
    ///     Finik.Editor.UI.FinikBudgetScreenAudit.Run();
    /// </summary>
    public static class FinikBudgetScreenAudit
    {
        static readonly (string a, string b)[] IntendedOverlaps =
        {
            // The coin sits on the edge of the day's income.
            ("Coin", "Income")
        };

        static readonly string[] ContentImages = { "Icon", "Coin" };

        static readonly string[] Containers = { "Summary", "HintCard", "Conclusion" };

        [MenuItem("Finik/UI/Audit Budget Screen")]
        static void Menu() => Debug.Log(Run());

        public static string SetResolution(int width, int height) => FinikUiAudit.SetResolution(width, height);

        public static string EnsureProfile() => FinikUiAudit.EnsureProfile();

        public static string Run()
        {
            if (!EditorApplication.isPlaying) return "Enter Play Mode first.";
            var screen = Object.FindAnyObjectByType<FinikBudgetScreen>(FindObjectsInactive.Include);
            if (!screen) return "Screen_Budget is missing: run Finik/UI/Rebuild Budget Screen.";
            if (!screen.IsOpen && !screen.Open()) return "Budget did not open: finish onboarding first.";

            var audit = new FinikUiAudit(IntendedOverlaps, ContentImages, Containers);
            var plan = (FinikScreenPanel)FinikUiAudit.Get(screen, "plan");
            var fact = (FinikScreenPanel)FinikUiAudit.Get(screen, "fact");
            var planColumn = (RectTransform)plan.transform.Find("Column");
            var factColumn = (RectTransform)fact.transform.Find("Column");

            // The planner, across the states the child actually sees.
            fact.Hide(instant: true);
            plan.Show(instant: true);
            var draft = (int[])FinikUiAudit.Get(screen, "draft");

            foreach (var (income, needs, wants, savings, what) in new[]
            {
                (30, 15, 8, 7, "план разложен"),
                (30, 5, 0, 0, "монеты без задачи"),
                (0, 0, 0, 0, "пустой кошелёк"),
                (999, 333, 333, 333, "крупные суммы")
            })
            {
                FinikUiAudit.Set(screen, "income", income);
                draft[0] = needs;
                draft[1] = wants;
                draft[2] = savings;
                FinikUiAudit.Call(screen, "RenderPlan");
                audit.Check(planColumn, what);
            }

            // The plan/fact card as it reads at the end of a day.
            plan.Hide(instant: true);
            fact.Show(instant: true);
            FinikUiAudit.Call(screen, "RenderFact");
            audit.Check(factColumn, "план и факт");

            // Restore the screen to what the live state says.
            if (FinikGame.HasBudgetPlan)
            {
                FinikUiAudit.Call(screen, "RenderFact");
            }
            else
            {
                fact.Hide(instant: true);
                plan.Show(instant: true);
                FinikUiAudit.Call(screen, "ShowPlan");
            }

            return audit.Report("Budget screen", planColumn, FinikEconomyBalance.Validate());
        }
    }
}
