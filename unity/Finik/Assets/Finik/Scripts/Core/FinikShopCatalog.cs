using System;
using System.Collections.Generic;
using UnityEngine;

namespace Finik.Core
{
    /// <summary>The two shopping tabs. Same split as the budget envelopes and the onboarding lesson.</summary>
    public enum FinikShopCategory
    {
        Need,
        Want
    }

    /// <summary>One thing a need or mood gains from a purchase, as the card's chip shows it.</summary>
    public readonly struct FinikShopEffect
    {
        /// <summary>"сытость" / "настроение" — the word for the confirmation sentence.</summary>
        public readonly string label;
        /// <summary>Sprite for the card chip (icon_food / icon_mood).</summary>
        public readonly string icon;
        public readonly int amount;

        public FinikShopEffect(string label, string icon, int amount)
        {
            this.label = label;
            this.icon = icon;
            this.amount = amount;
        }

        public string Chip => $"+{amount}";
    }

    /// <summary>
    /// Something the child can buy for Finik. Food and care live in «Нужно», toys, clothes and
    /// trinkets in «Хочу»; the price comes out of the wallet and the effect goes into the pet's
    /// needs. An item with an <see cref="AccessoryId"/> is worn by the 3D Finik and is bought once.
    /// </summary>
    public sealed class FinikShopItem
    {
        public string Id { get; }
        public string Title { get; }
        /// <summary>The name in the accusative case, for «Купить кепку?»; the title when not given.</summary>
        public string Accusative { get; }
        /// <summary>One short line under the title: what it is for, never the price.</summary>
        public string Subtitle { get; }
        /// <summary>Small caps tag on the card: «Еда», «Уход», «Игрушки», «Одежда», «Украшения».</summary>
        public string Group { get; }
        public int Price { get; }
        public FinikShopCategory Category { get; }
        public FinikNeeds Deltas { get; }
        public int Xp { get; }
        /// <summary>What Finik says after the purchase, shown on the result card.</summary>
        public string Result { get; }
        /// <summary>Sprite name in Assets/Finik/UI/Art: shop_&lt;id&gt; unless the catalog names one.</summary>
        public string Icon { get; }
        /// <summary>FinikAccessoryCatalog id when the item is worn by the 3D Finik; empty otherwise.</summary>
        public string AccessoryId { get; }
        public IReadOnlyList<FinikShopEffect> Effects { get; }
        /// <summary>
        /// Set when the card is a signpost to another screen instead of something to buy ("food" opens
        /// the fridge). Choosing tonight's meal is a scene with a trade-off, not a price-list entry, so
        /// the shop points at that screen rather than selling a second, poorer version of it.
        /// </summary>
        public string Link { get; }

        public bool IsLink => !string.IsNullOrEmpty(Link);

        /// <summary>Accessories stay owned: bought once, then worn from the same card.</summary>
        public bool IsAccessory => !string.IsNullOrEmpty(AccessoryId);

        public string CategoryLabel => Category == FinikShopCategory.Need ? "Нужно" : "Хочу";

        public FinikShopItem(string id, string title, string subtitle, string group, int price, FinikShopCategory category,
            FinikNeeds deltas, int xp, string result, string icon, string accessoryId, string link = null, string accusative = null)
        {
            Id = id;
            Title = title;
            Accusative = string.IsNullOrWhiteSpace(accusative) ? title : accusative.Trim();
            Link = (link ?? string.Empty).Trim();
            Subtitle = subtitle;
            Group = group;
            Price = price;
            Category = category;
            Deltas = deltas;
            Xp = xp;
            Result = result;
            Icon = string.IsNullOrWhiteSpace(icon) ? "shop_" + id.Replace('-', '_') : icon.Trim();
            AccessoryId = (accessoryId ?? string.Empty).Trim();

            var effects = new List<FinikShopEffect>(2);
            if (deltas.food > 0) effects.Add(new FinikShopEffect("сытость", "icon_food", Mathf.RoundToInt(deltas.food)));
            if (deltas.mood > 0) effects.Add(new FinikShopEffect("настроение", "icon_mood", Mathf.RoundToInt(deltas.mood)));
            Effects = effects;
        }

        /// <summary>«Сытость +26, настроение +4» for the confirmation card.</summary>
        public string EffectSentence()
        {
            if (Effects.Count == 0) return string.Empty;
            var parts = new string[Effects.Count];
            for (int i = 0; i < Effects.Count; i++)
                parts[i] = $"{Effects[i].label} +{Effects[i].amount}";
            string line = string.Join(", ", parts);
            return char.ToUpperInvariant(line[0]) + line[1..];
        }
    }

    /// <summary>
    /// The shop's stock, loaded from Resources/Data/shop_catalog.json so prices and copy can be
    /// tuned without touching code. Loaded lazily and validated: a broken item is reported with its
    /// id and left out rather than shown with a wrong price.
    /// </summary>
    public static class FinikShopCatalog
    {
        public const string ResourcePath = "Data/shop_catalog";
        /// <summary>Longest copy that still fits the item card on the narrowest layout.</summary>
        public const int MaxTitleLength = 20;
        public const int MaxSubtitleLength = 26;
        public const int MaxGroupLength = 12;

#pragma warning disable 0649 // assigned by JsonUtility
        [Serializable]
        sealed class ItemData
        {
            public string id, title, subtitle, group, category, result, icon, accessory, link, accusative;
            public int price, food, mood, xp;
        }

        [Serializable]
        sealed class CatalogData
        {
            public int version;
            public ItemData[] items;
        }
#pragma warning restore 0649

        static FinikShopItem[] itemList;
        static Dictionary<string, FinikShopItem> itemsById;

        public static IReadOnlyList<FinikShopItem> Items
        {
            get
            {
                EnsureLoaded();
                return itemList;
            }
        }

        public static bool TryGet(string id, out FinikShopItem item)
        {
            EnsureLoaded();
            return itemsById.TryGetValue(id ?? string.Empty, out item);
        }

        /// <summary>The tab's stock in catalog order.</summary>
        public static List<FinikShopItem> ItemsIn(FinikShopCategory category)
        {
            EnsureLoaded();
            var result = new List<FinikShopItem>();
            foreach (var item in itemList)
                if (item.Category == category) result.Add(item);
            return result;
        }

        public static string CategoryLabel(FinikShopCategory category) =>
            category == FinikShopCategory.Need ? "Нужно" : "Хочу";

        /// <summary>The cheapest item in the tab the wallet can still cover; null when nothing fits.</summary>
        public static FinikShopItem CheapestAffordable(FinikShopCategory category, int balance)
        {
            EnsureLoaded();
            FinikShopItem best = null;
            foreach (var item in itemList)
            {
                if (item.Category != category || item.IsLink || item.Price > balance) continue;
                if (best == null || item.Price < best.Price) best = item;
            }
            return best;
        }

        /// <summary>
        /// Drops the loaded catalog; the next access reads the JSON again. Runs on every Play because
        /// the project enters Play Mode without a domain reload, which would keep a stale catalog.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reload()
        {
            itemList = null;
            itemsById = null;
        }

        static void EnsureLoaded()
        {
            if (itemList != null) return;
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (!asset) throw new InvalidOperationException($"Shop catalog is missing: Resources/{ResourcePath}.json");
            var problems = Parse(asset.text, out var items);
            foreach (string problem in problems) Debug.LogError($"[FinikShopCatalog] {problem}");
            if (items.Length == 0) throw new InvalidOperationException("Shop catalog has no valid items, see the errors above.");
            itemsById = new Dictionary<string, FinikShopItem>(StringComparer.Ordinal);
            foreach (var item in items) itemsById[item.Id] = item;
            itemList = items;
        }

        /// <summary>
        /// Parses and validates catalog JSON. Invalid items are left out and described in the result;
        /// copy that is too long for the card is reported but kept.
        /// </summary>
        public static List<string> Parse(string json, out FinikShopItem[] items)
        {
            var problems = new List<string>();
            CatalogData data = null;
            try
            {
                data = JsonUtility.FromJson<CatalogData>(json);
            }
            catch (ArgumentException e)
            {
                problems.Add($"not valid JSON: {e.Message}");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var accessories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var output = new List<FinikShopItem>();
            int needs = 0, wants = 0;

            foreach (var entry in data?.items ?? Array.Empty<ItemData>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.id)) { problems.Add("item without id"); continue; }
                string where = $"item '{entry.id}'";
                if (!ids.Add(entry.id)) { problems.Add($"{where}: duplicate id"); continue; }
                if (string.IsNullOrWhiteSpace(entry.title)) { problems.Add($"{where}: title is required"); continue; }
                if (!TryParseCategory(entry.category, out var category))
                { problems.Add($"{where}: category must be need or want, got '{entry.category}'"); continue; }

                string link = (entry.link ?? string.Empty).Trim();
                if (link.Length > 0)
                {
                    // A signpost card buys nothing, so a price or an effect on it would be a lie.
                    if (entry.price != 0 || entry.food != 0 || entry.mood != 0 || entry.xp != 0)
                    { problems.Add($"{where}: a link card opens '{link}' instead of buying, so price, food, mood and xp must be 0"); continue; }
                }
                else
                {
                    // A price of zero would make the confirmation meaningless: everything sold costs coins.
                    if (entry.price <= 0) { problems.Add($"{where}: price must be above zero"); continue; }
                    if (string.IsNullOrWhiteSpace(entry.result)) { problems.Add($"{where}: result is required"); continue; }
                    if (entry.food < 0 || entry.mood < 0)
                    { problems.Add($"{where}: food and mood are what the item gives, they cannot be negative"); continue; }
                    if (entry.food == 0 && entry.mood == 0)
                    { problems.Add($"{where}: gives nothing; every card promises at least one effect"); continue; }
                    if (entry.xp < 0) { problems.Add($"{where}: xp cannot be negative"); continue; }
                }

                string accessory = (entry.accessory ?? string.Empty).Trim();
                if (accessory.Length > 0 && !accessories.Add(accessory))
                { problems.Add($"{where}: accessory '{accessory}' is already sold by another item"); continue; }

                string title = entry.title.Trim();
                string subtitle = (entry.subtitle ?? string.Empty).Trim();
                string group = (entry.group ?? string.Empty).Trim();
                if (title.Length > MaxTitleLength) problems.Add($"{where}: title is {title.Length} characters, the card fits {MaxTitleLength}");
                if (subtitle.Length > MaxSubtitleLength) problems.Add($"{where}: subtitle is {subtitle.Length} characters, the card fits {MaxSubtitleLength}");
                if (group.Length > MaxGroupLength) problems.Add($"{where}: group is {group.Length} characters, the tag fits {MaxGroupLength}");

                if (category == FinikShopCategory.Need) needs++; else wants++;
                output.Add(new FinikShopItem(entry.id, title, subtitle, group, entry.price, category,
                    new FinikNeeds(entry.food, entry.mood), entry.xp, (entry.result ?? string.Empty).Trim(),
                    entry.icon, accessory, link, entry.accusative));
            }

            // Both tabs are always reachable from the dock: an empty one would open onto nothing.
            if (output.Count > 0 && needs == 0) problems.Add("no items in «Нужно»");
            if (output.Count > 0 && wants == 0) problems.Add("no items in «Хочу»");

            items = output.ToArray();
            return problems;
        }

        static bool TryParseCategory(string value, out FinikShopCategory category)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "need":
                case "needs":
                    category = FinikShopCategory.Need;
                    return true;
                case "want":
                case "wants":
                    category = FinikShopCategory.Want;
                    return true;
                default:
                    category = FinikShopCategory.Need;
                    return false;
            }
        }
    }
}
