using Finik.UI;
using UnityEditor;
using UnityEngine;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Play Mode check of the home HUD at the current Game view resolution. The HUD is always on
    /// screen, so it is checked with the numbers that stretch it most: a long nickname, a two-digit
    /// level, four-digit coins and savings, and every dock tab selected in turn. Nothing is saved —
    /// the state is pushed straight into the view and the live one is restored at the end.
    ///
    ///     Finik.Editor.UI.FinikHomeHudAudit.SetResolution(1080, 1920);  // then let a few frames pass
    ///     Finik.Editor.UI.FinikHomeHudAudit.Run();
    /// </summary>
    public static class FinikHomeHudAudit
    {
        static readonly (string a, string b)[] IntendedOverlaps =
        {
            // The HUD is a stack of badges: the coin sits on its pill, the level on the portrait ring,
            // and the "+" buttons hang over the edge of their counters by design.
            ("Coin", "Coins"),
            ("Coin", "Savings"),
            ("Add", "Coins"),
            ("Add", "Savings"),
            ("Level", "Avatar"),
            ("Level", "Xp"),
            ("Badge", "Mail"),
            ("Ring", "Avatar")
        };

        static readonly string[] ContentImages = { "Coin", "Avatar", "Icon" };

        static readonly string[] Containers = { "Pill", "Plate", "Face", "Card" };

        [MenuItem("Finik/UI/Audit Home HUD")]
        static void Menu() => Debug.Log(Run());

        public static string SetResolution(int width, int height) => FinikUiAudit.SetResolution(width, height);

        public static string EnsureProfile() => FinikUiAudit.EnsureProfile();

        public static string Run()
        {
            if (!EditorApplication.isPlaying) return "Enter Play Mode first.";
            // A room screen switches the HUD off while it is open, so look past active objects.
            var view = Object.FindAnyObjectByType<FinikHudView>(FindObjectsInactive.Include);
            if (!view) return "HUD_Home is missing: run Finik/UI/Rebuild Home HUD.";
            var hud = view.gameObject;
            bool wasActive = hud.activeSelf;
            hud.SetActive(true);

            var audit = new FinikUiAudit(IntendedOverlaps, ContentImages, Containers);
            var root = (RectTransform)hud.transform;
            var binder = hud.GetComponent<FinikHudBinder>();
            // The binder repaints a few times a second and would overwrite the faked states.
            if (binder) binder.enabled = false;

            var states = new (FinikHudState state, string what)[]
            {
                (Make("ФИНИК", 1, 0, 40, 0, 0, 1f, 1f), "начало игры"),
                (Make("Александра", 7, 120, 250, 1250, 860, 0.5f, 0.5f), "длинное имя и крупные суммы"),
                (Make("Ян", 12, 995, 1000, 9999, 9999, 0.04f, 0.04f), "предельные числа и пустые шкалы"),
                (Make("Ян", 12, 995, 1000, 9999, 9999, 0.04f, 0.04f, unreadMail: 99), "непрочитанная почта")
            };

            foreach (var (state, what) in states)
            {
                view.Apply(state, animate: false);
                foreach (FinikHudTab tab in System.Enum.GetValues(typeof(FinikHudTab)))
                {
                    view.SelectTab(tab, animate: false);
                    audit.Check(root, $"{what}, вкладка {tab}");
                }
            }

            view.SelectTab(FinikHudTab.Home, animate: false);
            if (binder) binder.enabled = true;

            // The report measures the canvas, so build it before the HUD goes back to sleep.
            string report = audit.Report("Home HUD", root, System.Array.Empty<string>());
            hud.SetActive(wasActive);
            return report;
        }

        static FinikHudState Make(string name, int level, int xp, int span, int coins, int savings,
            float food, float mood, int unreadMail = 0) => new()
        {
            playerName = name,
            level = level,
            xp = xp,
            xpToNextLevel = span,
            coins = coins,
            savings = savings,
            food = food,
            mood = mood,
            unreadMail = unreadMail
        };
    }
}
