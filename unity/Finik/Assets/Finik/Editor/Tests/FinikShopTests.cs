using System.Linq;
using Finik.Accessories;
using Finik.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Finik.Editor.Tests
{
    /// <summary>The shop catalog and the purchase maths ported from src/domain/purchaseService.ts.</summary>
    public sealed class FinikShopTests
    {
        const string AccessoryCatalogPath = "Assets/Finik/Accessories/FinikAccessoryCatalog.asset";

        [Test]
        public void ShippedCatalogIsValid()
        {
            var asset = Resources.Load<TextAsset>(FinikShopCatalog.ResourcePath);
            Assert.That(asset, Is.Not.Null, "Resources/Data/shop_catalog.json is missing");
            var problems = FinikShopCatalog.Parse(asset.text, out var items);
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            Assert.That(items.Any(i => i.Category == FinikShopCategory.Need), "«Нужно» is empty");
            Assert.That(items.Any(i => i.Category == FinikShopCategory.Want), "«Хочу» is empty");
            // Every card for sale promises an effect; a signpost card sells nothing and promises nothing.
            Assert.That(items.Where(i => !i.IsLink).All(i => i.Effects.Count > 0));
            Assert.That(items.Where(i => i.IsLink).All(i => i.Price == 0 && i.Effects.Count == 0));
            // Food is not sold here: choosing dinner is the fridge screen's scene, not a price-list row.
            Assert.That(items.Any(i => i.Link == "food"), "the «Еда» signpost to the fridge is missing");
            foreach (var category in new[] { FinikShopCategory.Need, FinikShopCategory.Want })
                Assert.That(items.Where(i => i.Category == category && !i.IsLink).Min(i => i.Price), Is.LessThanOrEqualTo(120),
                    $"nothing cheap enough in {FinikShopCatalog.CategoryLabel(category)}");
        }

        [Test]
        public void EveryAccessoryOnSaleExistsInTheRig()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<FinikAccessoryCatalog>(AccessoryCatalogPath);
            Assert.That(catalog, Is.Not.Null, $"{AccessoryCatalogPath} is missing");
            foreach (var item in FinikShopCatalog.Items.Where(i => i.IsAccessory))
                Assert.That(catalog.Find(item.AccessoryId), Is.Not.Null,
                    $"item '{item.Id}' sells accessory '{item.AccessoryId}', which the rig does not have");
        }

        [Test]
        public void ASignpostCardCarriesNoPriceOrEffect()
        {
            const string json = @"{ ""version"": 1, ""items"": [
                { ""id"": ""meal"", ""title"": ""Еда"", ""category"": ""need"", ""price"": 0, ""food"": 0, ""mood"": 0, ""link"": ""food"" },
                { ""id"": ""priced-link"", ""title"": ""T"", ""category"": ""need"", ""price"": 40, ""food"": 0, ""mood"": 0, ""link"": ""food"" },
                { ""id"": ""toy"", ""title"": ""T"", ""category"": ""want"", ""price"": 10, ""food"": 0, ""mood"": 5, ""result"": ""R"" }
            ] }";
            var problems = FinikShopCatalog.Parse(json, out var items);
            Assert.That(items.Select(i => i.Id), Is.EqualTo(new[] { "meal", "toy" }));
            Assert.That(items[0].IsLink, Is.True);
            Assert.That(problems.Any(p => p.Contains("'priced-link'") && p.Contains("must be 0")));
        }

        [Test]
        public void AccusativeIsUsedForTheConfirmationAndFallsBackToTheTitle()
        {
            FinikShopCatalog.TryGet("cap", out var cap);
            Assert.That(cap.Accusative, Is.EqualTo("кепку"));
            var plain = new FinikShopItem("x", "Мячик", "", "Игрушки", 10, FinikShopCategory.Want,
                new FinikNeeds(0, 5), 0, "R", null, null);
            Assert.That(plain.Accusative, Is.EqualTo("Мячик"));
        }

        [Test]
        public void BrokenItemsAreReportedAndLeftOut()
        {
            const string json = @"{ ""version"": 1, ""items"": [
                { ""id"": ""ok"", ""title"": ""T"", ""category"": ""need"", ""price"": 10, ""food"": 5, ""mood"": 0, ""result"": ""R"" },
                { ""id"": ""want-ok"", ""title"": ""W"", ""category"": ""want"", ""price"": 20, ""food"": 0, ""mood"": 5, ""result"": ""R"" },
                { ""id"": ""free"", ""title"": ""T"", ""category"": ""need"", ""price"": 0, ""food"": 5, ""mood"": 0, ""result"": ""R"" },
                { ""id"": ""empty"", ""title"": ""T"", ""category"": ""need"", ""price"": 10, ""food"": 0, ""mood"": 0, ""result"": ""R"" },
                { ""id"": ""negative"", ""title"": ""T"", ""category"": ""want"", ""price"": 10, ""food"": -5, ""mood"": 0, ""result"": ""R"" },
                { ""id"": ""bad-category"", ""title"": ""T"", ""category"": ""maybe"", ""price"": 10, ""food"": 5, ""mood"": 0, ""result"": ""R"" },
                { ""id"": ""ok"", ""title"": ""dup"", ""category"": ""need"", ""price"": 10, ""food"": 5, ""mood"": 0, ""result"": ""R"" }
            ] }";
            var problems = FinikShopCatalog.Parse(json, out var items);
            Assert.That(items.Select(i => i.Id), Is.EqualTo(new[] { "ok", "want-ok" }));
            Assert.That(problems.Any(p => p.Contains("'free'") && p.Contains("price")));
            Assert.That(problems.Any(p => p.Contains("'empty'") && p.Contains("gives nothing")));
            Assert.That(problems.Any(p => p.Contains("'negative'")));
            Assert.That(problems.Any(p => p.Contains("'bad-category'") && p.Contains("category")));
            Assert.That(problems.Any(p => p.Contains("'ok'") && p.Contains("duplicate id")));
        }

        [Test]
        public void TwoItemsCannotSellTheSameAccessory()
        {
            const string json = @"{ ""version"": 1, ""items"": [
                { ""id"": ""cap"", ""title"": ""T"", ""category"": ""want"", ""price"": 10, ""food"": 0, ""mood"": 5, ""result"": ""R"", ""accessory"": ""gear_cap"" },
                { ""id"": ""cap-again"", ""title"": ""T"", ""category"": ""want"", ""price"": 20, ""food"": 0, ""mood"": 5, ""result"": ""R"", ""accessory"": ""gear_cap"" },
                { ""id"": ""food"", ""title"": ""T"", ""category"": ""need"", ""price"": 10, ""food"": 5, ""mood"": 0, ""result"": ""R"" }
            ] }";
            var problems = FinikShopCatalog.Parse(json, out var items);
            Assert.That(items.Select(i => i.Id), Is.EqualTo(new[] { "cap", "food" }));
            Assert.That(problems.Any(p => p.Contains("'cap-again'") && p.Contains("gear_cap")));
        }

        [Test]
        public void PreviewExplainsExactlyHowManyCoinsAreMissing()
        {
            var item = new FinikShopItem("lunch", "Полезный обед", "суп и второе", "Еда", 120,
                FinikShopCategory.Need, new FinikNeeds(26, 5), 6, "R", null, null);

            var rich = new FinikPurchasePreview(item, 300);
            Assert.That(rich.affordable, Is.True);
            Assert.That(rich.balanceAfter, Is.EqualTo(180));
            Assert.That(rich.deficit, Is.Zero);

            var poor = new FinikPurchasePreview(item, 80);
            Assert.That(poor.affordable, Is.False);
            // The wallet is never touched when it cannot cover the price.
            Assert.That(poor.balanceAfter, Is.EqualTo(80));
            Assert.That(poor.deficit, Is.EqualTo(40));
            Assert.That(poor.DeficitTitle, Is.EqualTo("Не хватает 40 монет"));

            var exact = new FinikPurchasePreview(item, 120);
            Assert.That(exact.affordable, Is.True);
            Assert.That(exact.balanceAfter, Is.Zero);
        }

        [Test]
        public void EffectSentenceReadsAsRussian()
        {
            var both = new FinikShopItem("lunch", "Обед", "", "Еда", 120, FinikShopCategory.Need,
                new FinikNeeds(26, 5), 6, "R", null, null);
            Assert.That(both.EffectSentence(), Is.EqualTo("Сытость +26, настроение +5"));

            var moodOnly = new FinikShopItem("ball", "Мячик", "", "Игрушки", 110, FinikShopCategory.Want,
                new FinikNeeds(0, 14), 3, "R", null, null);
            Assert.That(moodOnly.EffectSentence(), Is.EqualTo("Настроение +14"));
            Assert.That(moodOnly.Effects.Count, Is.EqualTo(1));
        }

        [Test]
        public void CoinsArePluralisedTheRussianWay()
        {
            Assert.That(FinikCoins.Word(1), Is.EqualTo("монета"));
            Assert.That(FinikCoins.Word(3), Is.EqualTo("монеты"));
            Assert.That(FinikCoins.Word(5), Is.EqualTo("монет"));
            Assert.That(FinikCoins.Word(11), Is.EqualTo("монет"));
            Assert.That(FinikCoins.Word(21), Is.EqualTo("монета"));
            Assert.That(FinikCoins.Word(112), Is.EqualTo("монет"));
            Assert.That(FinikCoins.Amount(40), Is.EqualTo("40 монет"));
        }

        [Test]
        public void CheapestAffordableSuggestsSomethingThatFits()
        {
            var cheap = FinikShopCatalog.CheapestAffordable(FinikShopCategory.Need, 1000);
            Assert.That(cheap, Is.Not.Null);
            // The free signpost card is not a suggestion: it sells nothing, so it can never be "cheapest".
            Assert.That(cheap.IsLink, Is.False);
            Assert.That(cheap.Price, Is.EqualTo(
                FinikShopCatalog.ItemsIn(FinikShopCategory.Need).Where(i => !i.IsLink).Min(i => i.Price)));
            // An empty wallet buys nothing, and the screen says so instead of suggesting a fantasy.
            Assert.That(FinikShopCatalog.CheapestAffordable(FinikShopCategory.Need, 0), Is.Null);
        }
    }
}
