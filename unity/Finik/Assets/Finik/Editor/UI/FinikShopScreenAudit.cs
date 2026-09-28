using System.Collections.Generic;
using Finik.Core;
using Finik.UI.Onboarding;
using Finik.UI.Shop;
using UnityEditor;
using UnityEngine;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Play Mode check of the shop at the current Game view resolution: both tabs with every item card
    /// in each state (affordable, short of coins, already owned), the confirmation, the "not enough
    /// coins" explanation for the widest gaps and the result card. Does not touch the saved progress —
    /// the wallet is faked per state and the cards are rendered directly.
    ///
    ///     Finik.Editor.UI.FinikShopScreenAudit.SetResolution(1080, 1920);  // then let a few frames pass
    ///     Finik.Editor.UI.FinikShopScreenAudit.Run();
    /// </summary>
    public static class FinikShopScreenAudit
    {
        static readonly (string a, string b)[] IntendedOverlaps =
        {
            // The coin sits on the edge of its price, and «Куплено» takes the price's corner.
            ("Coin", "Price"),
            ("Owned", "Price"),
            ("Owned", "Shortfall"),
            ("Owned", "Coin")
        };

        // "Plate" is the chip's own background, not content: it is a container, listed below.
        static readonly string[] ContentImages = { "Icon", "Coin", "Image", "Picture" };

        static readonly string[] Containers = { "Face", "Math", "Hero", "Plate" };

        [MenuItem("Finik/UI/Audit Shop Screen")]
        static void Menu() => Debug.Log(Run());

        public static string SetResolution(int width, int height) => FinikUiAudit.SetResolution(width, height);

        public static string EnsureProfile() => FinikUiAudit.EnsureProfile();

        public static string Capture(string path) => FinikUiAudit.Capture(path);

        public static string Run()
        {
            if (!EditorApplication.isPlaying) return "Enter Play Mode first.";
            var screen = Object.FindAnyObjectByType<FinikShopScreen>(FindObjectsInactive.Include);
            if (!screen) return "Screen_Shop is missing: run Finik/UI/Rebuild Shop Screen.";
            if (!screen.IsOpen && !screen.Open()) return "Shop did not open: finish onboarding first.";

            var audit = new FinikUiAudit(IntendedOverlaps, ContentImages, Containers);
            var main = (FinikScreenPanel)FinikUiAudit.Get(screen, "main");
            var confirm = (FinikScreenPanel)FinikUiAudit.Get(screen, "confirm");
            var shortPanel = (FinikScreenPanel)FinikUiAudit.Get(screen, "shortPanel");
            var result = (FinikScreenPanel)FinikUiAudit.Get(screen, "result");
            var cards = (FinikShopItemView[])FinikUiAudit.Get(screen, "items");

            var catalogProblems = new List<string>();
            var asset = Resources.Load<TextAsset>(FinikShopCatalog.ResourcePath);
            if (asset) catalogProblems.AddRange(FinikShopCatalog.Parse(asset.text, out _));

            // The shelf: each tab with a full wallet, an empty one and a half-way one, so every card is
            // seen priced, short and (for the accessories) owned.
            ShowOnly(main, confirm, shortPanel, result);
            foreach (var category in new[] { FinikShopCategory.Need, FinikShopCategory.Want })
            {
                var stock = FinikShopCatalog.ItemsIn(category);
                foreach (var (balance, owned, what) in new[] { (9999, false, "всё по карману"), (0, false, "пустой кошелёк"), (150, true, "часть куплена") })
                {
                    for (int i = 0; i < cards.Length; i++)
                    {
                        bool visible = i < stock.Count;
                        cards[i].gameObject.SetActive(visible);
                        if (!visible) continue;
                        var item = stock[i];
                        cards[i].Show(item, screen.IconFor(item.Icon), n => screen.IconFor(n), balance, owned, worn: false);
                    }
                    audit.Check(Column(screen, "Main"), $"{FinikShopCatalog.CategoryLabel(category)} / {what}");
                }
            }

            // The confirmation and the shortfall card, for the cheapest and the dearest thing on sale:
            // the longest names and the widest gap in coins are where the copy breaks if it ever does.
            foreach (var item in Edges())
            {
                FinikUiAudit.Set(screen, "pending", item);

                ShowOnly(confirm, main, shortPanel, result);
                FinikUiAudit.Call(screen, "ShowConfirm", new FinikPurchasePreview(item, item.Price + 40));
                audit.Check(Column(screen, "Confirm"), $"подтверждение / {item.Id}");

                ShowOnly(shortPanel, main, confirm, result);
                FinikUiAudit.Call(screen, "ShowShortfall", new FinikPurchasePreview(item, 0));
                audit.Check(Column(screen, "Short"), $"не хватает всего / {item.Id}");
                FinikUiAudit.Call(screen, "ShowShortfall", new FinikPurchasePreview(item, Mathf.Max(0, item.Price - 5)));
                audit.Check(Column(screen, "Short"), $"не хватает 5 / {item.Id}");

                // The result: a first purchase (with XP) and a repeat of the same thing (without).
                ShowOnly(result, main, confirm, shortPanel);
                FinikUiAudit.Call(screen, "ShowResult",
                    new FinikPurchaseResult(FinikPurchaseFailure.None, item, item.Price, item.Xp, item.Deltas, 40));
                audit.Check(Column(screen, "Result"), $"результат / {item.Id}");
                FinikUiAudit.Call(screen, "ShowResult",
                    new FinikPurchaseResult(FinikPurchaseFailure.None, item, item.Price, 0, item.Deltas, 0));
                audit.Check(Column(screen, "Result"), $"результат без опыта / {item.Id}");
            }

            ShowOnly(main, confirm, shortPanel, result);
            return audit.Report("Shop", Column(screen, "Main"), catalogProblems);
        }

        /// <summary>The cheapest, the dearest and the longest-named item: where the copy is tightest.</summary>
        static List<FinikShopItem> Edges()
        {
            FinikShopItem cheap = null, dear = null, longest = null;
            foreach (var item in FinikShopCatalog.Items)
            {
                if (cheap == null || item.Price < cheap.Price) cheap = item;
                if (dear == null || item.Price > dear.Price) dear = item;
                int length = item.Title.Length + item.Subtitle.Length;
                if (longest == null || length > longest.Title.Length + longest.Subtitle.Length) longest = item;
            }
            var list = new List<FinikShopItem>();
            foreach (var item in new[] { cheap, dear, longest })
                if (item != null && !list.Contains(item)) list.Add(item);
            return list;
        }

        static void ShowOnly(FinikScreenPanel visible, params FinikScreenPanel[] hidden)
        {
            foreach (var panel in hidden) if (panel) panel.Hide(instant: true);
            if (visible) visible.Show(instant: true);
        }

        static RectTransform Column(FinikShopScreen screen, string panelName)
        {
            var safe = screen.transform.Find("SafeArea");
            var panel = safe ? safe.Find(panelName) : null;
            return panel ? (RectTransform)panel.Find("Column") : null;
        }
    }
}
