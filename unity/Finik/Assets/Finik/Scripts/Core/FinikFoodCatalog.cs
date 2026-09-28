using System;
using System.Collections.Generic;
using UnityEngine;

namespace Finik.Core
{
    public enum FinikBudgetBucket
    {
        Needs,
        Wants,
        Savings,
        Reserve
    }

    /// <summary>How the choice is judged after the fact. Never shown before the player picks.</summary>
    public enum FinikFoodVibe
    {
        Smart,
        Balanced,
        Treat,
        Trap
    }

    public readonly struct FinikFoodCharge
    {
        public readonly FinikBudgetBucket bucket;
        public readonly int amount;

        public FinikFoodCharge(FinikBudgetBucket bucket, int amount)
        {
            this.bucket = bucket;
            this.amount = amount;
        }
    }

    public sealed class FinikFoodPlan
    {
        public string Id { get; }
        public string Title { get; }
        /// <summary>Short trade-off line; the price is shown separately (the web app inlined it with an emoji).</summary>
        public string Subtitle { get; }
        public IReadOnlyList<FinikFoodCharge> Charges { get; }
        public FinikNeeds Deltas { get; }
        public FinikFoodVibe Vibe { get; }
        public int Xp { get; }
        public string Result { get; }
        public int TotalCost { get; }
        /// <summary>Sprite name in Assets/Finik/UI/Art (falls back to a generic plate): food_&lt;id&gt; unless the catalog names one.</summary>
        public string Icon { get; }

        public FinikFoodPlan(string id, string title, string subtitle, FinikFoodCharge[] charges, FinikNeeds deltas, FinikFoodVibe vibe, int xp, string result, string icon = null)
        {
            Id = id;
            Icon = string.IsNullOrWhiteSpace(icon) ? "food_" + id.Replace('-', '_') : icon.Trim();
            Title = title;
            Subtitle = subtitle;
            Charges = charges;
            Deltas = deltas;
            Vibe = vibe;
            Xp = xp;
            Result = result;
            int total = 0;
            foreach (var charge in charges) total += charge.amount;
            TotalCost = total;
        }
    }

    public sealed class FinikFoodScene
    {
        public string Id { get; }
        public string Title { get; }
        public string Setup { get; }
        public string Hint { get; }
        public IReadOnlyList<string> OptionIds { get; }
        public string Icon => "scene_" + Id.Replace('-', '_');

        public FinikFoodScene(string id, string title, string setup, string hint, params string[] optionIds)
        {
            Id = id;
            Title = title;
            Setup = setup;
            Hint = hint;
            OptionIds = optionIds;
        }
    }

    /// <summary>
    /// Food plans and life scenes, loaded from Resources/Data/food_catalog.json so the copy and the
    /// numbers can be edited without touching code. Loaded lazily on first use and validated: broken
    /// references or values are reported with the item id and the item is skipped.
    /// </summary>
    public static class FinikFoodCatalog
    {
        public const string DefaultSceneId = "evening-home";
        public const string ResourcePath = "Data/food_catalog";
        /// <summary>Longest copy that still fits the option card on the narrowest layout.</summary>
        public const int MaxTitleLength = 22;
        public const int MaxSubtitleLength = 24;
        /// <summary>Scene name on one line beside the coin pill.</summary>
        public const int MaxSceneTitleLength = 18;
        public const int OptionsPerScene = 4;

#pragma warning disable 0649 // assigned by JsonUtility
        [Serializable]
        sealed class PlanData
        {
            public string id, title, subtitle, vibe, result, icon;
            public int needs, wants, food, mood, xp;
        }

        [Serializable]
        sealed class SceneData
        {
            public string id, title, setup, hint;
            public string[] options;
        }

        [Serializable]
        sealed class CatalogData
        {
            public int version;
            public PlanData[] plans;
            public SceneData[] scenes;
        }
#pragma warning restore 0649

        static FinikFoodPlan[] planList;
        static FinikFoodScene[] sceneList;
        static Dictionary<string, FinikFoodPlan> plansById;
        static Dictionary<string, FinikFoodScene> scenesById;

        public static IReadOnlyList<FinikFoodPlan> Plans { get { EnsureLoaded(); return planList; } }
        public static IReadOnlyList<FinikFoodScene> Scenes { get { EnsureLoaded(); return sceneList; } }

        public static bool TryGetPlan(string id, out FinikFoodPlan plan)
        {
            EnsureLoaded();
            return plansById.TryGetValue(id ?? string.Empty, out plan);
        }

        public static bool TryGetScene(string id, out FinikFoodScene scene)
        {
            EnsureLoaded();
            return scenesById.TryGetValue(id ?? string.Empty, out scene);
        }

        /// <summary>One scene per local calendar day, rotating through the list (same as foodSceneForDate).</summary>
        public static FinikFoodScene SceneForDate(DateTime localNow)
        {
            EnsureLoaded();
            long day = (long)(localNow.Date - new DateTime(1970, 1, 1)).TotalDays;
            return sceneList[(int)(Math.Abs(day) % sceneList.Length)];
        }

        public static string BucketLabel(FinikBudgetBucket bucket) => bucket switch
        {
            FinikBudgetBucket.Needs => "Нужно",
            FinikBudgetBucket.Wants => "Хочу",
            FinikBudgetBucket.Savings => "Копилка",
            _ => "Резерв"
        };

        /// <summary>
        /// Drops the loaded catalog; the next access reads the JSON again. Runs on every Play because the
        /// project enters Play Mode without a domain reload, which would otherwise keep a stale catalog.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reload()
        {
            planList = null;
            sceneList = null;
        }

        static void EnsureLoaded()
        {
            if (planList != null) return;
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (!asset) throw new InvalidOperationException($"Food catalog is missing: Resources/{ResourcePath}.json");
            var problems = Parse(asset.text, out var plans, out var scenes);
            foreach (string problem in problems) Debug.LogError($"[FinikFoodCatalog] {problem}");
            if (scenes.Length == 0) throw new InvalidOperationException("Food catalog has no valid scenes, see the errors above.");
            plansById = new Dictionary<string, FinikFoodPlan>(StringComparer.Ordinal);
            foreach (var plan in plans) plansById[plan.Id] = plan;
            scenesById = new Dictionary<string, FinikFoodScene>(StringComparer.Ordinal);
            foreach (var scene in scenes) scenesById[scene.Id] = scene;
            sceneList = scenes;
            planList = plans;
        }

        /// <summary>
        /// Parses and validates catalog JSON. Invalid items are left out and described in the result;
        /// copy that is too long for the card is reported but kept.
        /// </summary>
        public static List<string> Parse(string json, out FinikFoodPlan[] plans, out FinikFoodScene[] scenes)
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

            var planMap = new Dictionary<string, FinikFoodPlan>(StringComparer.Ordinal);
            var planOut = new List<FinikFoodPlan>();
            foreach (var item in data?.plans ?? Array.Empty<PlanData>())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id)) { problems.Add("plan without id"); continue; }
                string where = $"plan '{item.id}'";
                if (planMap.ContainsKey(item.id)) { problems.Add($"{where}: duplicate id"); continue; }
                if (string.IsNullOrWhiteSpace(item.title) || string.IsNullOrWhiteSpace(item.result)) { problems.Add($"{where}: title and result are required"); continue; }
                if (!TryParseVibe(item.vibe, out var vibe)) { problems.Add($"{where}: vibe must be smart, balanced, treat or trap, got '{item.vibe}'"); continue; }
                if (item.needs < 0 || item.wants < 0 || item.xp < 0) { problems.Add($"{where}: needs, wants and xp cannot be negative"); continue; }
                string subtitle = (item.subtitle ?? string.Empty).Trim();
                if (item.title.Trim().Length > MaxTitleLength) problems.Add($"{where}: title is {item.title.Trim().Length} characters, the card fits {MaxTitleLength}");
                if (subtitle.Length > MaxSubtitleLength) problems.Add($"{where}: subtitle is {subtitle.Length} characters, the card fits {MaxSubtitleLength}");

                var charges = new List<FinikFoodCharge>(2);
                if (item.needs > 0) charges.Add(new FinikFoodCharge(FinikBudgetBucket.Needs, item.needs));
                if (item.wants > 0) charges.Add(new FinikFoodCharge(FinikBudgetBucket.Wants, item.wants));
                var plan = new FinikFoodPlan(item.id, item.title.Trim(), subtitle, charges.ToArray(),
                    new FinikNeeds(item.food, item.mood), vibe, item.xp, item.result.Trim(), item.icon);
                planMap.Add(plan.Id, plan);
                planOut.Add(plan);
            }

            var sceneIds = new HashSet<string>(StringComparer.Ordinal);
            var sceneOut = new List<FinikFoodScene>();
            foreach (var item in data?.scenes ?? Array.Empty<SceneData>())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id)) { problems.Add("scene without id"); continue; }
                string where = $"scene '{item.id}'";
                if (!sceneIds.Add(item.id)) { problems.Add($"{where}: duplicate id"); continue; }
                if (string.IsNullOrWhiteSpace(item.title) || string.IsNullOrWhiteSpace(item.setup)) { problems.Add($"{where}: title and setup are required"); continue; }
                if (item.title.Trim().Length > MaxSceneTitleLength) problems.Add($"{where}: title is {item.title.Trim().Length} characters, the header fits {MaxSceneTitleLength}");
                var options = item.options ?? Array.Empty<string>();
                var missing = Array.FindAll(options, id => !planMap.ContainsKey(id ?? string.Empty));
                if (missing.Length > 0) { problems.Add($"{where}: unknown options {string.Join(", ", missing)}"); continue; }
                if (options.Length != OptionsPerScene) { problems.Add($"{where}: needs {OptionsPerScene} options, has {options.Length}"); continue; }
                // A child's balance never goes below zero, so every scene must be playable with an empty wallet.
                if (!Array.Exists(options, id => planMap[id].TotalCost == 0))
                    problems.Add($"{where}: has no free option (needs and wants 0); with an empty wallet the child could not eat");
                sceneOut.Add(new FinikFoodScene(item.id, item.title.Trim(), item.setup.Trim(), (item.hint ?? string.Empty).Trim(), options));
            }
            if (sceneOut.Count > 0 && !sceneIds.Contains(DefaultSceneId)) problems.Add($"default scene '{DefaultSceneId}' is missing");

            plans = planOut.ToArray();
            scenes = sceneOut.ToArray();
            return problems;
        }

        static bool TryParseVibe(string value, out FinikFoodVibe vibe) =>
            Enum.TryParse(value, ignoreCase: true, out vibe) && Enum.IsDefined(typeof(FinikFoodVibe), vibe);
    }
}
