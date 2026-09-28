using System.Linq;
using Finik.Core;
using Finik.UI.Onboarding;
using Finik.UI.Savings;
using UnityEditor;
using UnityEngine;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Play Mode check of the piggy bank at the current Game view resolution: the goal picker, the main
    /// card at every step of progress (empty, part way, one step short, reached) with each offered
    /// amount selected, and the take-back confirmation. The goal and the wallet are faked per state —
    /// the saved progress is restored at the end.
    ///
    ///     Finik.Editor.UI.FinikSavingsScreenAudit.SetResolution(1080, 1920);  // then let a few frames pass
    ///     Finik.Editor.UI.FinikSavingsScreenAudit.Run();
    /// </summary>
    public static class FinikSavingsScreenAudit
    {
        static readonly (string a, string b)[] IntendedOverlaps =
        {
            // The preview bar is drawn under the real one on purpose, and the coin sits on its pill.
            ("Preview", "Bar"),
            ("Coin", "Coins"),
            ("Coin", "Price")
        };

        static readonly string[] ContentImages = { "Icon", "Coin", "Picture" };

        static readonly string[] Containers = { "Face", "Hero", "Plate" };

        [MenuItem("Finik/UI/Audit Savings Screen")]
        static void Menu() => Debug.Log(Run());

        public static string SetResolution(int width, int height) => FinikUiAudit.SetResolution(width, height);

        public static string EnsureProfile() => FinikUiAudit.EnsureProfile();

        public static string Run()
        {
            if (!EditorApplication.isPlaying) return "Enter Play Mode first.";
            var screen = Object.FindAnyObjectByType<FinikSavingsScreen>(FindObjectsInactive.Include);
            if (!screen) return "Screen_Savings is missing: run Finik/UI/Rebuild Savings Screen.";
            if (!screen.IsOpen && !screen.Open()) return "Savings did not open: finish onboarding first.";

            var audit = new FinikUiAudit(IntendedOverlaps, ContentImages, Containers);
            var main = (FinikScreenPanel)FinikUiAudit.Get(screen, "main");
            var goals = (FinikScreenPanel)FinikUiAudit.Get(screen, "goals");
            var confirm = (FinikScreenPanel)FinikUiAudit.Get(screen, "confirm");
            var mainColumn = (RectTransform)main.transform.Find("Column");
            var goalsColumn = (RectTransform)goals.transform.Find("Column");
            var confirmColumn = (RectTransform)confirm.transform.Find("Column");

            // The picker: every goal in the catalog has a card, and the longest title must fit it.
            FinikUiAudit.Call(screen, "ShowGoals");
            goals.Show(instant: true);
            audit.Check(goalsColumn, "выбор цели");

            // The main card at the progress steps a child actually passes through.
            var priciest = FinikGoalCatalog.Goals.OrderByDescending(g => g.Price).First();
            FinikGame.SelectGoal(priciest.Id);
            goals.Hide(instant: true);
            confirm.Hide(instant: true);
            main.Show(instant: true);

            var steps = (int[])FinikUiAudit.Get(screen, "amountSteps");
            foreach (var (saved, what) in new[]
            {
                (0, "копилка пустая"),
                (priciest.Price / 2, "половина цели"),
                (priciest.Price - 1, "шаг до цели"),
                (priciest.Price, "цель собрана")
            })
            {
                Seed(saved);
                for (int i = 0; i < Mathf.Max(1, steps.Length); i++)
                {
                    FinikUiAudit.Set(screen, "step", i);
                    FinikUiAudit.Call(screen, "Render", false);
                    audit.Check(mainColumn, $"{what}, шаг {i + 1}");
                }
            }

            // The take-back confirmation, with the largest amount the pot allows.
            Seed(priciest.Price);
            FinikUiAudit.Set(screen, "step", Mathf.Max(0, steps.Length - 1));
            FinikUiAudit.Call(screen, "AskWithdraw");
            confirm.Show(instant: true);
            audit.Check(confirmColumn, "возврат из копилки");

            // Back to what the live state says.
            confirm.Hide(instant: true);
            FinikUiAudit.Call(screen, "ShowMain");
            FinikUiAudit.Call(screen, "Render", false);

            return audit.Report("Savings screen", mainColumn, FinikEconomyBalance.Validate());
        }

        /// <summary>Puts exactly <paramref name="saved"/> coins in the pot without touching the ledger's history.</summary>
        static void Seed(int saved)
        {
            var wallet = FinikGame.State?.wallet;
            if (wallet == null) return;
            wallet.savingsBalance = Mathf.Max(0, saved);
            // The card also reads the spendable balance, so keep a realistic amount next to the pot.
            wallet.balance = Mathf.Max(wallet.balance, 60);
        }
    }
}
