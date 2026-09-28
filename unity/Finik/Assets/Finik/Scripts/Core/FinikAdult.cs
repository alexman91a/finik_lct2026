using System;
using System.Collections.Generic;
using System.Linq;

namespace Finik.Core
{
    /// <summary>A small addition task an adult solves instantly and a 7–11-year-old does not.</summary>
    public readonly struct FinikAdultChallenge
    {
        public readonly int left;
        public readonly int right;

        public FinikAdultChallenge(int left, int right)
        {
            this.left = left;
            this.right = right;
        }

        public string Prompt => $"Сколько будет {left} + {right}?";
        public int Answer => left + right;

        public bool Accepts(string input) =>
            int.TryParse((input ?? string.Empty).Trim(), out int value) && value == Answer;
    }

    /// <summary>One line of the parent-facing curriculum.</summary>
    public readonly struct FinikAdultTopic
    {
        public readonly string id;
        public readonly string title;
        public readonly string description;
        public readonly bool practised;

        public FinikAdultTopic(string id, string title, string description, bool practised)
        {
            this.id = id;
            this.title = title;
            this.description = description;
            this.practised = practised;
        }
    }

    /// <summary>Everything the adult section shows about a child, assembled once.</summary>
    public readonly struct FinikAdultReport
    {
        public readonly int questsDone;
        public readonly int questsToday;
        public readonly int daysFinished;
        public readonly int periodsFinished;
        public readonly IReadOnlyList<FinikAdultTopic> topics;
        public readonly FinikGoalProgress goal;
        public readonly bool hasGoal;
        public readonly FinikBudgetSummary day;

        public FinikAdultReport(int questsDone, int questsToday, int daysFinished, int periodsFinished,
            IReadOnlyList<FinikAdultTopic> topics, FinikGoalProgress goal, bool hasGoal, FinikBudgetSummary day)
        {
            this.questsDone = questsDone;
            this.questsToday = questsToday;
            this.daysFinished = daysFinished;
            this.periodsFinished = periodsFinished;
            this.topics = topics ?? Array.Empty<FinikAdultTopic>();
            this.goal = goal;
            this.hasGoal = hasGoal;
            this.day = day;
        }

        public int TopicsPractised
        {
            get { int n = 0; foreach (var topic in topics) if (topic.practised) n++; return n; }
        }
    }

    /// <summary>
    /// The adult section: a detailed, neutral picture of what the child has been doing.
    ///
    /// It reports and never grades. There are no marks, no "did badly", no comparison with other
    /// children — only what happened and what the child has been practising.
    /// </summary>
    public static class FinikAdult
    {
        /// <summary>How long the entry button must be held before the addition task appears.</summary>
        public const float HoldSeconds = 1.5f;

        public const string ParentNote =
            "Финик помогает ребёнку тренироваться распределять ограниченный бюджет, отличать необходимое от желаемого и копить на цель.";

        public const string CurrencyNote =
            "Все суммы — условные игровые монеты Финика. Они не имеют реальной стоимости и не обмениваются на деньги.";

        public const string NeutralNote =
            "Здесь нет оценок и сравнения с другими детьми — только описание того, что делал ребёнок.";

        /// <summary>The curriculum shown to the parent, whatever the child has reached so far.</summary>
        static readonly (string id, string title, string description)[] Curriculum =
        {
            ("budget", "Различает «нужно» и «хочу»",
                "Раскладывает ограниченные монеты по конвертам и видит разницу между обязательным и желаемым."),
            ("planning", "Учится планировать бюджет",
                "Сначала составляет план на день, а потом сверяет его с тем, что получилось."),
            ("savings", "Учится откладывать",
                "Регулярно переводит часть монет на выбранную цель и видит, как она приближается."),
            ("payments-purchases", "Разбирается с покупками и платежами",
                "Сравнивает цену и пользу, подтверждает покупку и видит остаток до и после."),
            ("safety", "Осторожен с подозрительными просьбами",
                "Учится не вводить данные и звать взрослого, когда предложение выглядит странно.")
        };

        /// <summary>Rows the parent-facing curriculum needs; the builder makes exactly this many.</summary>
        public static int TopicCount => Curriculum.Length;

        /// <summary>Deterministic for the same seed, so Demo Mode and acceptance runs repeat exactly.</summary>
        public static FinikAdultChallenge Challenge(int seed)
        {
            int normalized = Math.Abs(seed);
            return new FinikAdultChallenge(7 + normalized % 6, 4 + normalized / 6 % 6);
        }

        public static FinikAdultChallenge RandomChallenge() =>
            Challenge(UnityEngine.Random.Range(0, 36));

        public static FinikAdultReport Build()
        {
            var tasks = FinikGame.QuestTasks();
            int done = 0;
            var practised = new HashSet<string>(StringComparer.Ordinal);

            foreach (var task in tasks)
            {
                if (task.IsWeekly) continue;
                var status = FinikGame.QuestProgress(task.Id)?.status ?? FinikQuestStatus.Available;
                if (status != FinikQuestStatus.Completed && status != FinikQuestStatus.Claimed) continue;
                done++;
                if (task.Quest != null && !string.IsNullOrEmpty(task.Quest.Topic)) practised.Add(task.Quest.Topic);
            }

            // Confirming a plan is budget practice in itself, and a filled piggy bank is saving practice.
            var summary = FinikGame.BudgetSummary();
            if (summary.hasPlan)
            {
                practised.Add("budget");
                practised.Add("planning");
            }
            if (FinikGame.Savings > 0) practised.Add("savings");

            var topics = Curriculum
                .Select(entry => new FinikAdultTopic(entry.id, entry.title, entry.description, practised.Contains(entry.id)))
                .ToArray();

            return new FinikAdultReport(
                done,
                tasks.Count(t => !t.IsWeekly),
                FinishedDays(),
                CompletedPeriods(),
                topics,
                FinikGame.GoalProgress,
                FinikGame.HasSelectedGoal,
                summary);
        }

        static int CompletedPeriods()
        {
            var state = FinikGame.State;
            return Math.Clamp(state?.quests?.completedPeriods ?? 0, 0, 5);
        }

        /// <summary>Game days the child has already lived through, counted from the campaign start.</summary>
        static int FinishedDays()
        {
            var state = FinikGame.State;
            if (state?.quests == null || string.IsNullOrEmpty(state.quests.campaignStartDayKey)) return 0;
            return Math.Max(0, FinikQuestEngine.CampaignDay(state.quests, DateTime.Now) - 1);
        }

        /// <summary>Wipes the progress but keeps who the child is: same nickname and pet.</summary>
        public static void ResetProgress() => FinikGame.StartJourney();

        /// <summary>
        /// Removes the local profile entirely; the app returns to its first-launch state. The profile
        /// store is in the UI assembly, so the caller clears it and this only drops the game state.
        /// </summary>
        public static void DeleteGameState() => FinikGame.Clear();
    }
}
