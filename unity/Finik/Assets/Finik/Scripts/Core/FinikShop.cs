using System;
using System.Collections.Generic;

namespace Finik.Core
{
    /// <summary>What the shop remembers between sessions: everything bought at least once.</summary>
    [Serializable]
    public sealed class FinikShopState
    {
        /// <summary>
        /// Ids of items bought at least once. Accessories use it to stay owned (bought once, then
        /// worn again for free); repeatable items use it so the first purchase is the one that teaches.
        /// </summary>
        public List<string> owned = new();

        public bool Owns(string itemId) =>
            !string.IsNullOrEmpty(itemId) && owned != null && owned.Contains(itemId);
    }

    public enum FinikPurchaseFailure
    {
        None,
        NoJourney,
        UnknownItem,
        InsufficientFunds,
        AlreadyOwned
    }

    /// <summary>
    /// What a purchase would cost and whether it fits, worked out before anything is charged. The
    /// confirmation card is built from this, and so is the "not enough" explanation.
    /// </summary>
    public readonly struct FinikPurchasePreview
    {
        public readonly FinikShopItem item;
        public readonly int balanceBefore;
        public readonly int balanceAfter;
        public readonly bool affordable;
        /// <summary>How many coins are missing; 0 when the wallet covers the price.</summary>
        public readonly int deficit;
        public readonly bool owned;

        public FinikPurchasePreview(FinikShopItem item, int balance)
        {
            this.item = item;
            int price = item?.Price ?? 0;
            balanceBefore = Math.Max(0, balance);
            affordable = item != null && price > 0 && balanceBefore >= price;
            balanceAfter = affordable ? balanceBefore - price : balanceBefore;
            deficit = item == null ? 0 : Math.Max(0, price - balanceBefore);
            owned = false;
        }

        FinikPurchasePreview(FinikShopItem item, int balanceBefore, int balanceAfter, bool affordable, int deficit, bool owned)
        {
            this.item = item;
            this.balanceBefore = balanceBefore;
            this.balanceAfter = balanceAfter;
            this.affordable = affordable;
            this.deficit = deficit;
            this.owned = owned;
        }

        public FinikPurchasePreview AsOwned() =>
            new(item, balanceBefore, balanceAfter, affordable, deficit, true);

        /// <summary>«Не хватает 40 монет» — the headline of the shortfall card.</summary>
        public string DeficitTitle => deficit > 0 ? $"Не хватает {deficit} {FinikCoins.Word(deficit)}" : "Монет хватает";
    }

    /// <summary>The outcome of a confirmed purchase: what was paid and what Finik actually gained.</summary>
    public readonly struct FinikPurchaseResult
    {
        public readonly FinikPurchaseFailure failure;
        public readonly FinikShopItem item;
        public readonly int coinsSpent;
        public readonly int xpGained;
        /// <summary>What the needs really gained after clamping at 100, not what the card promised.</summary>
        public readonly FinikNeeds gained;
        public readonly int balanceAfter;
        public readonly int deficit;

        public FinikPurchaseResult(FinikPurchaseFailure failure, FinikShopItem item = null, int coinsSpent = 0,
            int xpGained = 0, FinikNeeds gained = default, int balanceAfter = 0, int deficit = 0)
        {
            this.failure = failure;
            this.item = item;
            this.coinsSpent = coinsSpent;
            this.xpGained = xpGained;
            this.gained = gained;
            this.balanceAfter = balanceAfter;
            this.deficit = deficit;
        }

        public bool Ok => failure == FinikPurchaseFailure.None;
    }

    /// <summary>монета / монеты / монет — the shop, the piggy bank and the HUD all count coins.</summary>
    public static class FinikCoins
    {
        public static string Word(int count)
        {
            int n = Math.Abs(count) % 100;
            if (n is >= 11 and <= 14) return "монет";
            return (n % 10) switch
            {
                1 => "монета",
                2 or 3 or 4 => "монеты",
                _ => "монет"
            };
        }

        /// <summary>«40 монет» for sentences that need the number and the word together.</summary>
        public static string Amount(int count) => $"{count} {Word(count)}";
    }
}
