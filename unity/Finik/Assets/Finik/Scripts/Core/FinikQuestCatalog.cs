using System;
using System.Collections.Generic;
using UnityEngine;

namespace Finik.Core
{
    /// <summary>How a quest choice is judged after the fact (web: ScenarioConsequence.tag).</summary>
    public enum FinikQuestTag
    {
        Good,
        Neutral,
        Risk
    }

    /// <summary>
    /// How a quest is played. Plain quests offer their choices as buttons; the others are small
    /// hands-on tasks (web: QuestExperience) that end in one of the quest's choices.
    /// </summary>
    public enum FinikQuestMechanicType
    {
        Choices,
        /// <summary>Tap every suspicious message in a chat, then check (web: RedFlagChat).</summary>
        RedFlags,
        /// <summary>Reveal what a mystery box really holds, then decide (web: MysteryOdds).</summary>
        Odds,
        /// <summary>Put an item into the «Нужно» or «Хочу» envelope (web: NeedOrWant).</summary>
        NeedOrWant
    }

    /// <summary>Skill changes a choice teaches. Carried over from the web data; the quest flow does not apply them yet.</summary>
    public readonly struct FinikSkillDeltas
    {
        public readonly int planning, mindfulSpending, savings, reserve, safety;

        public FinikSkillDeltas(int planning, int mindfulSpending, int savings, int reserve, int safety)
        {
            this.planning = planning;
            this.mindfulSpending = mindfulSpending;
            this.savings = savings;
            this.reserve = reserve;
            this.safety = safety;
        }
    }

    /// <summary>Budget envelope changes a choice implies in the weekly scenario. Carried over, not applied by quests.</summary>
    public readonly struct FinikBucketDeltas
    {
        public readonly int needs, wants, savings, reserve;

        public FinikBucketDeltas(int needs, int wants, int savings, int reserve)
        {
            this.needs = needs;
            this.wants = wants;
            this.savings = savings;
            this.reserve = reserve;
        }
    }

    public sealed class FinikQuestChoice
    {
        public string Id { get; }
        public string Label { get; }
        public FinikQuestTag Tag { get; }
        public string Explanation { get; }
        public FinikSkillDeltas Skills { get; }
        public FinikBucketDeltas Buckets { get; }
        public int Spend { get; }
        public int Save { get; }
        public int Withdraw { get; }

        public FinikQuestChoice(string id, string label, FinikQuestTag tag, string explanation, FinikSkillDeltas skills,
            FinikBucketDeltas buckets, int spend = 0, int save = 0, int withdraw = 0)
        {
            Id = id;
            Label = label;
            Tag = tag;
            Explanation = explanation;
            Skills = skills;
            Buckets = buckets;
            Spend = Math.Max(0, spend);
            Save = Math.Max(0, save);
            Withdraw = Math.Max(0, withdraw);
        }
    }

    public sealed class FinikQuestQuestion
    {
        public string Id { get; }
        public string Prompt { get; }
        public string Hint { get; }
        public FinikQuestChoice[] Choices { get; }

        public FinikQuestQuestion(string id, string prompt, string hint, FinikQuestChoice[] choices)
        {
            Id = id;
            Prompt = prompt;
            Hint = hint;
            Choices = choices ?? Array.Empty<FinikQuestChoice>();
        }

        public FinikQuestChoice Choice(string id)
        {
            foreach (var choice in Choices) if (choice.Id == id) return choice;
            return null;
        }
    }

    public sealed class FinikRedFlagMessage
    {
        public string Id { get; }
        public string Who { get; }
        public string Text { get; }
        public bool Suspicious { get; }

        public FinikRedFlagMessage(string id, string who, string text, bool suspicious)
        {
            Id = id;
            Who = who;
            Text = text;
            Suspicious = suspicious;
        }
    }

    /// <summary>
    /// Settings of a hands-on quest. Which fields matter depends on <see cref="Type"/>; the catalog
    /// checks that the ones the type needs are there and that the choice ids exist.
    /// </summary>
    public sealed class FinikQuestMechanic
    {
        public static readonly FinikQuestMechanic Plain = new() { Type = FinikQuestMechanicType.Choices };

        public FinikQuestMechanicType Type { get; internal set; }
        /// <summary>Small caps line above the task (odds, need-or-want).</summary>
        public string Kicker { get; internal set; }
        /// <summary>Heading of the task (red flags).</summary>
        public string Title { get; internal set; }
        /// <summary>Question or instruction under the heading.</summary>
        public string Prompt { get; internal set; }
        public string Hint { get; internal set; }
        /// <summary>Main button: "Проверить" for red flags, "Показать шансы" for odds.</summary>
        public string Action { get; internal set; }
        /// <summary>Shown after a wrong attempt; the child tries again.</summary>
        public string Retry { get; internal set; }
        /// <summary>Choice the quest ends in when the task is solved.</summary>
        public string SuccessChoiceId { get; internal set; }
        /// <summary>Choice for the risky way out (odds only).</summary>
        public string RiskChoiceId { get; internal set; }
        public string SuccessLabel { get; internal set; }
        public string RiskLabel { get; internal set; }
        /// <summary>Explanation revealed with the odds.</summary>
        public string Summary { get; internal set; }
        public int Cells { get; internal set; }
        public int Rare { get; internal set; }
        public string Item { get; internal set; }
        public int Price { get; internal set; }
        /// <summary>The right envelope for the item (need-or-want): Needs or Wants.</summary>
        public FinikBudgetBucket Answer { get; internal set; }
        public IReadOnlyList<FinikRedFlagMessage> Messages { get; internal set; } = Array.Empty<FinikRedFlagMessage>();

        public int SuspiciousCount
        {
            get
            {
                int count = 0;
                foreach (var message in Messages) if (message.Suspicious) count++;
                return count;
            }
        }
    }

    public sealed class FinikQuest
    {
        public string Id { get; }
        public int Day { get; }
        public int Order { get; }
        public string Title { get; }
        public string Description { get; }
        public string Category { get; }
        public int Difficulty { get; }
        public string CompetencyId { get; }
        public string Topic { get; }
        public string LearningOutcome { get; }
        public FinikQuestQuestion[] Questions { get; }
        public IReadOnlyList<FinikQuestChoice> Choices => Questions.Length > 0 ? Questions[0].Choices : Array.Empty<FinikQuestChoice>();
        public FinikQuestMechanic Mechanic { get; }
        public string Icon => "quest_" + Id.Replace('-', '_');
        public bool IsInteractive => Mechanic.Type != FinikQuestMechanicType.Choices;

        public FinikQuest(string id, int day, int order, string title, string description, string category, int difficulty,
            string competencyId, string topic, string learningOutcome, FinikQuestQuestion[] questions, FinikQuestMechanic mechanic)
        {
            Id = id;
            Day = Math.Max(1, day);
            Order = Math.Max(1, order);
            Title = title;
            Description = description;
            Category = category;
            Difficulty = difficulty;
            CompetencyId = competencyId;
            Topic = topic;
            LearningOutcome = learningOutcome;
            Questions = questions ?? Array.Empty<FinikQuestQuestion>();
            Mechanic = mechanic ?? FinikQuestMechanic.Plain;
        }

        public FinikQuestQuestion Question(int index) =>
            Questions.Length == 0 ? null : Questions[Math.Clamp(index, 0, Questions.Length - 1)];

        public FinikQuestChoice Choice(string choiceId)
        {
            foreach (var question in Questions)
                foreach (var choice in question.Choices)
                    if (choice.Id == choiceId) return choice;
            return null;
        }
    }

    /// <summary>Weekly streak task: claim this many daily quests in a week for a bonus.</summary>
    public sealed class FinikWeeklyQuestRule
    {
        public string Title { get; }
        public string Description { get; }
        public int Target { get; }
        public int Reward { get; }
        public int Difficulty { get; }

        public FinikWeeklyQuestRule(string title, string description, int target, int reward, int difficulty)
        {
            Title = title;
            Description = description;
            Target = target;
            Reward = reward;
            Difficulty = difficulty;
        }
    }

    /// <summary>
    /// Financial quests for the 7–11 audience (port of the web scenario catalog and task rewards),
    /// loaded from Resources/Data/quest_catalog.json so analysts can edit copy and numbers without code.
    /// Loaded lazily and validated: a broken quest is reported with its id and left out.
    /// </summary>
    public static class FinikQuestCatalog
    {
        public const string ResourcePath = "Data/quest_catalog";
        /// <summary>Quest name on the board and in the quest header.</summary>
        public const int MaxTitleLength = 36;
        /// <summary>Choice buttons fit two lines.</summary>
        public const int MaxChoiceLength = 52;
        public const int MaxMessageLength = 48;

#pragma warning disable 0649 // assigned by JsonUtility
        [Serializable]
        sealed class SkillData
        {
            public int planning, mindfulSpending, savings, reserve, safety;
        }

        [Serializable]
        sealed class BucketData
        {
            public int needs, wants, savings, reserve;
        }

        [Serializable]
        sealed class ChoiceData
        {
            public string id, label, tag, explanation;
            public int spend, save, withdraw;
            public SkillData skills;
            public BucketData buckets;
        }

        [Serializable]
        sealed class QuestionData
        {
            public string id, prompt, hint;
            public ChoiceData[] choices;
        }

        [Serializable]
        sealed class MessageData
        {
            public string id, who, text;
            public bool suspicious;
        }

        [Serializable]
        sealed class MechanicData
        {
            public string type, kicker, title, prompt, hint, action, retry, success, risk, successLabel, riskLabel, summary, item, answer;
            public int cells, rare, price;
            public MessageData[] messages;
        }

        [Serializable]
        sealed class QuestData
        {
            public string id, title, description, category, competency, topic, outcome;
            public int day, order, difficulty;
            public ChoiceData[] choices;
            public QuestionData[] questions;
            public MechanicData mechanic;
        }

        [Serializable]
        sealed class WeeklyData
        {
            public string title, description;
            public int target, reward, difficulty;
        }

        [Serializable]
        sealed class CatalogData
        {
            public int version;
            public int dailyCount;
            public int[] dailyRewards;
            public WeeklyData weekly;
            public string[] eventQuests;
            public QuestData[] quests;
        }
#pragma warning restore 0649

        /// <summary>Everything the quest engine needs from the catalog.</summary>
        public sealed class Content
        {
            public FinikQuest[] quests;
            public int dailyCount;
            public int[] dailyRewards;
            public FinikWeeklyQuestRule weekly;
            /// <summary>Quest id of the day's pet event, by day ordinal modulo length (empty: none).</summary>
            public string[] eventQuests;
        }

        static Content content;
        static Dictionary<string, FinikQuest> byId;

        public static IReadOnlyList<FinikQuest> Quests => Loaded.quests;
        public static int DailyCount => Loaded.dailyCount;
        public static FinikWeeklyQuestRule Weekly => Loaded.weekly;
        public static IReadOnlyList<string> EventQuests => Loaded.eventQuests;

        /// <summary>Daily reward by difficulty (web: rewardForDifficulty).</summary>
        public static int DailyReward(int difficulty)
        {
            var rewards = Loaded.dailyRewards;
            return rewards[Math.Clamp(difficulty, 1, rewards.Length) - 1];
        }

        public static bool TryGet(string id, out FinikQuest quest)
        {
            _ = Loaded;
            return byId.TryGetValue(id ?? string.Empty, out quest);
        }

        /// <summary>Drops the loaded catalog; runs on every Play because domain reload is off.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reload()
        {
            content = null;
            byId = null;
        }

        static Content Loaded
        {
            get
            {
                if (content != null) return content;
                var asset = Resources.Load<TextAsset>(ResourcePath);
                if (!asset) throw new InvalidOperationException($"Quest catalog is missing: Resources/{ResourcePath}.json");
                var problems = Parse(asset.text, out var parsed);
                foreach (string problem in problems) Debug.LogError($"[FinikQuestCatalog] {problem}");
                if (parsed.quests.Length < parsed.dailyCount)
                    throw new InvalidOperationException($"Quest catalog has {parsed.quests.Length} valid quests, a day needs {parsed.dailyCount}; see the errors above.");
                byId = new Dictionary<string, FinikQuest>(StringComparer.Ordinal);
                foreach (var quest in parsed.quests) byId[quest.Id] = quest;
                content = parsed;
                return content;
            }
        }

        /// <summary>
        /// Parses and validates catalog JSON. Invalid quests are left out and described in the result;
        /// copy that is too long for its slot is reported but kept.
        /// </summary>
        public static List<string> Parse(string json, out Content result)
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
            data ??= new CatalogData();

            result = new Content
            {
                dailyCount = data.dailyCount > 0 ? data.dailyCount : 4,
                dailyRewards = data.dailyRewards is { Length: 3 } && Array.TrueForAll(data.dailyRewards, r => r > 0) ? data.dailyRewards : new[] { 80, 120, 160 },
                eventQuests = data.eventQuests ?? Array.Empty<string>()
            };
            if (data.dailyCount <= 0) problems.Add("dailyCount must be positive, using 4");
            if (!ReferenceEquals(result.dailyRewards, data.dailyRewards)) problems.Add("dailyRewards must be three positive numbers (difficulty 1..3), using 80/120/160");

            var weekly = data.weekly;
            if (weekly == null || string.IsNullOrWhiteSpace(weekly.title) || weekly.target <= 0 || weekly.reward < 0)
            {
                problems.Add("weekly needs a title, a positive target and a non-negative reward");
                result.weekly = new FinikWeeklyQuestRule("Не сбивай серию", "Закрой четыре квеста за неделю.", 4, 0, 1);
            }
            else
            {
                result.weekly = new FinikWeeklyQuestRule(weekly.title.Trim(), (weekly.description ?? string.Empty).Trim(), weekly.target, weekly.reward, Math.Clamp(weekly.difficulty, 1, 3));
                if (weekly.target > result.dailyCount * 7) problems.Add($"weekly target {weekly.target} cannot be reached with {result.dailyCount} quests a day");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var quests = new List<FinikQuest>();
            foreach (var item in data.quests ?? Array.Empty<QuestData>())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id)) { problems.Add("quest without id"); continue; }
                string where = $"quest '{item.id}'";
                if (!ids.Add(item.id)) { problems.Add($"{where}: duplicate id"); continue; }
                if (string.IsNullOrWhiteSpace(item.title) || string.IsNullOrWhiteSpace(item.description)) { problems.Add($"{where}: title and description are required"); continue; }
                if (item.difficulty < 1 || item.difficulty > 3) { problems.Add($"{where}: difficulty must be 1..3, got {item.difficulty}"); continue; }
                if (item.title.Trim().Length > MaxTitleLength) problems.Add($"{where}: title is {item.title.Trim().Length} characters, the header fits {MaxTitleLength}");

                var questions = ParseQuestions(item, where, problems);
                if (questions == null || questions.Length == 0) continue;
                var mechanic = ParseMechanic(item.mechanic, questions[0].Choices, where, problems, out bool mechanicOk);
                if (!mechanicOk) continue;

                quests.Add(new FinikQuest(item.id.Trim(), Math.Max(1, item.day), Math.Max(1, item.order), item.title.Trim(),
                    item.description.Trim(), (item.category ?? string.Empty).Trim(), item.difficulty,
                    (item.competency ?? string.Empty).Trim(), (item.topic ?? string.Empty).Trim(), (item.outcome ?? string.Empty).Trim(),
                    questions, mechanic));
            }

            for (int i = 0; i < result.eventQuests.Length; i++)
            {
                string id = result.eventQuests[i] = (result.eventQuests[i] ?? string.Empty).Trim();
                if (id.Length > 0 && !ids.Contains(id)) problems.Add($"eventQuests[{i}]: unknown quest '{id}'");
            }
            result.quests = quests.ToArray();
            return problems;
        }

        static FinikQuestQuestion[] ParseQuestions(QuestData item, string where, List<string> problems)
        {
            var source = item.questions ?? Array.Empty<QuestionData>();
            if (source.Length == 0)
            {
                var legacy = ParseChoices(item.choices, where, problems);
                return legacy == null ? null : new[] { new FinikQuestQuestion("q1", item.description.Trim(), string.Empty, legacy) };
            }

            var result = new FinikQuestQuestion[source.Length];
            var questionIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < source.Length; i++)
            {
                var q = source[i];
                if (q == null || string.IsNullOrWhiteSpace(q.id) || string.IsNullOrWhiteSpace(q.prompt) || !questionIds.Add(q.id))
                {
                    problems.Add($"{where}: every question needs a unique id and prompt");
                    return null;
                }
                var choices = ParseChoices(q.choices, $"{where} question '{q.id}'", problems);
                if (choices == null) return null;
                result[i] = new FinikQuestQuestion(q.id.Trim(), q.prompt.Trim(), (q.hint ?? string.Empty).Trim(), choices);
            }
            return result;
        }

        static FinikQuestChoice[] ParseChoices(ChoiceData[] source, string where, List<string> problems)
        {
            var list = source ?? Array.Empty<ChoiceData>();
            if (list.Length < 2) { problems.Add($"{where}: needs at least 2 choices, has {list.Length}"); return null; }
            var choiceIds = new HashSet<string>(StringComparer.Ordinal);
            var choices = new FinikQuestChoice[list.Length];
            for (int i = 0; i < list.Length; i++)
            {
                var c = list[i];
                if (c == null || string.IsNullOrWhiteSpace(c.id)) { problems.Add($"{where}: choice {i} has no id"); return null; }
                string at = $"{where} choice '{c.id}'";
                if (!choiceIds.Add(c.id)) { problems.Add($"{at}: duplicate id"); return null; }
                if (string.IsNullOrWhiteSpace(c.label) || string.IsNullOrWhiteSpace(c.explanation)) { problems.Add($"{at}: label and explanation are required"); return null; }
                if (!TryParseTag(c.tag, out var tag)) { problems.Add($"{at}: tag must be good, neutral or risk, got '{c.tag}'"); return null; }
                if (c.label.Trim().Length > MaxChoiceLength) problems.Add($"{at}: label is {c.label.Trim().Length} characters, the button fits {MaxChoiceLength}");
                var s = c.skills ?? new SkillData();
                var b = c.buckets ?? new BucketData();
                choices[i] = new FinikQuestChoice(c.id.Trim(), c.label.Trim(), tag, c.explanation.Trim(),
                    new FinikSkillDeltas(s.planning, s.mindfulSpending, s.savings, s.reserve, s.safety),
                    new FinikBucketDeltas(b.needs, b.wants, b.savings, b.reserve), c.spend, c.save, c.withdraw);
            }
            if (!Array.Exists(choices, c => c.Tag == FinikQuestTag.Good)) problems.Add($"{where}: no choice is tagged good");
            return choices;
        }

        static FinikQuestMechanic ParseMechanic(MechanicData m, FinikQuestChoice[] choices, string where, List<string> problems, out bool ok)
        {
            ok = true;
            // JsonUtility fills a missing nested object with an empty instance: no type means a plain quest.
            if (m == null || string.IsNullOrWhiteSpace(m.type)) return FinikQuestMechanic.Plain;
            string at = $"{where} mechanic '{m.type}'";
            bool valid = true;
            bool Has(string value, string field)
            {
                if (!string.IsNullOrWhiteSpace(value)) return true;
                problems.Add($"{at}: '{field}' is required");
                return valid = false;
            }
            bool Choice(string id, string field)
            {
                if (!Has(id, field)) return false;
                if (Array.Exists(choices, c => c.Id == id)) return true;
                problems.Add($"{at}: '{field}' names unknown choice '{id}'");
                return valid = false;
            }
            static string T(string value) => (value ?? string.Empty).Trim();

            FinikQuestMechanic result;
            switch (m.type.Trim().ToLowerInvariant())
            {
                case "red-flags":
                {
                    Has(m.title, "title");
                    Has(m.action, "action");
                    Has(m.retry, "retry");
                    Choice(m.success, "success");
                    var messages = new List<FinikRedFlagMessage>();
                    var messageIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var msg in m.messages ?? Array.Empty<MessageData>())
                    {
                        if (msg == null || string.IsNullOrWhiteSpace(msg.id) || string.IsNullOrWhiteSpace(msg.text) || !messageIds.Add(msg.id))
                        {
                            problems.Add($"{at}: every message needs a unique id and text");
                            valid = false;
                            continue;
                        }
                        if (msg.text.Trim().Length > MaxMessageLength) problems.Add($"{at}: message '{msg.id}' is {msg.text.Trim().Length} characters, the bubble fits {MaxMessageLength}");
                        messages.Add(new FinikRedFlagMessage(msg.id.Trim(), T(msg.who), msg.text.Trim(), msg.suspicious));
                    }
                    if (!messages.Exists(x => x.Suspicious) || !messages.Exists(x => !x.Suspicious))
                    {
                        problems.Add($"{at}: needs both suspicious and harmless messages");
                        valid = false;
                    }
                    result = new FinikQuestMechanic
                    {
                        Type = FinikQuestMechanicType.RedFlags, Title = T(m.title), Hint = T(m.hint), Action = T(m.action),
                        Retry = T(m.retry), SuccessChoiceId = T(m.success), Messages = messages
                    };
                    break;
                }
                case "odds":
                    Has(m.prompt, "prompt");
                    Has(m.action, "action");
                    Has(m.summary, "summary");
                    Has(m.successLabel, "successLabel");
                    Has(m.riskLabel, "riskLabel");
                    Choice(m.success, "success");
                    Choice(m.risk, "risk");
                    if (m.cells < 2 || m.cells > 12 || m.rare < 1 || m.rare >= m.cells)
                    {
                        problems.Add($"{at}: needs 2..12 cells and 1..cells-1 rare ones, got {m.cells} and {m.rare}");
                        valid = false;
                    }
                    result = new FinikQuestMechanic
                    {
                        Type = FinikQuestMechanicType.Odds, Kicker = T(m.kicker), Prompt = T(m.prompt), Action = T(m.action), Summary = T(m.summary),
                        SuccessChoiceId = T(m.success), SuccessLabel = T(m.successLabel), RiskChoiceId = T(m.risk), RiskLabel = T(m.riskLabel),
                        Cells = m.cells, Rare = m.rare
                    };
                    break;
                case "need-or-want":
                {
                    Has(m.item, "item");
                    Has(m.prompt, "prompt");
                    Has(m.retry, "retry");
                    Choice(m.success, "success");
                    string answer = T(m.answer).ToLowerInvariant();
                    if (answer != "needs" && answer != "wants")
                    {
                        problems.Add($"{at}: answer must be needs or wants, got '{m.answer}'");
                        valid = false;
                    }
                    if (m.price < 0) { problems.Add($"{at}: price cannot be negative"); valid = false; }
                    result = new FinikQuestMechanic
                    {
                        Type = FinikQuestMechanicType.NeedOrWant, Kicker = T(m.kicker), Item = T(m.item), Price = m.price, Prompt = T(m.prompt),
                        Retry = T(m.retry), SuccessChoiceId = T(m.success),
                        Answer = answer == "needs" ? FinikBudgetBucket.Needs : FinikBudgetBucket.Wants
                    };
                    break;
                }
                default:
                    problems.Add($"{at}: unknown type, expected red-flags, odds or need-or-want");
                    valid = false;
                    result = FinikQuestMechanic.Plain;
                    break;
            }
            ok = valid;
            return result;
        }

        static bool TryParseTag(string value, out FinikQuestTag tag) =>
            Enum.TryParse(value, ignoreCase: true, out tag) && Enum.IsDefined(typeof(FinikQuestTag), tag);
    }
}
