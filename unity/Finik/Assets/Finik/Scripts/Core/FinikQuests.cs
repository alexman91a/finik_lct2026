using System;
using System.Collections.Generic;
using System.Globalization;

namespace Finik.Core
{
    /// <summary>available → active → completed → claimed; the reward is paid on the last step only.</summary>
    public enum FinikQuestStatus
    {
        Available,
        Active,
        Completed,
        Claimed
    }

    public enum FinikQuestCadence
    {
        Daily,
        Weekly
    }

    [Serializable]
    public sealed class FinikQuestProgress
    {
        public string taskId;
        public FinikQuestStatus status;
        public string choiceId;
        public int questionIndex;
        public int goodAnswers;
        public int riskAnswers;
        public bool rewardEligible;
        public long completedAtMs;
        public long claimedAtMs;
    }

    /// <summary>Persisted quest progress (web: taskStore). The day's tasks are rebuilt from the catalog and the date.</summary>
    [Serializable]
    public sealed class FinikQuestState
    {
        public string dayKey;
        public string weekKey;
        public string campaignStartDayKey;
        public int weeklyClaimedDaily;
        public int completedPeriods;
        public string lastCompletedPeriodDayKey;
        public List<FinikQuestProgress> progress = new();

        public FinikQuestProgress Find(string taskId)
        {
            foreach (var item in progress)
                if (item != null && item.taskId == taskId) return item;
            return null;
        }
    }

    /// <summary>One entry on the quest board: a daily quest or the weekly streak.</summary>
    public sealed class FinikQuestTask
    {
        public string Id { get; }
        public FinikQuestCadence Cadence { get; }
        /// <summary>The quest to play; null for the weekly streak.</summary>
        public FinikQuest Quest { get; }
        public string Title { get; }
        public string Description { get; }
        public int Difficulty { get; }
        public int Reward { get; }
        /// <summary>Daily quests to claim for the weekly streak; 0 for daily tasks.</summary>
        public int TargetCount { get; }

        public FinikQuestTask(string id, FinikQuestCadence cadence, FinikQuest quest, string title, string description, int difficulty, int reward, int targetCount)
        {
            Id = id;
            Cadence = cadence;
            Quest = quest;
            Title = title;
            Description = description;
            Difficulty = difficulty;
            Reward = reward;
            TargetCount = targetCount;
        }

        public bool IsWeekly => Cadence == FinikQuestCadence.Weekly;
    }

    /// <summary>
    /// Daily and weekly financial quests (port of src/domain/taskEngine.ts and the taskStore period
    /// logic). Pure functions over <see cref="FinikQuestState"/>; <see cref="FinikGame"/> owns the state,
    /// pays rewards and persists.
    /// </summary>
    public static class FinikQuestEngine
    {
        public static string DayKey(DateTime local) => local.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>Monday of the local week (web: weekKey).</summary>
        public static string WeekKey(DateTime local)
        {
            var date = local.Date;
            int weekday = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
            return DayKey(date.AddDays(1 - weekday));
        }

        /// <summary>Days since 1970-01-01 of the local calendar date (web: dayOrdinal).</summary>
        public static long DayOrdinal(DateTime local) => (long)(local.Date - new DateTime(1970, 1, 1)).TotalDays;

        public static int CampaignDay(FinikQuestState state, DateTime local)
        {
            if (state == null || string.IsNullOrEmpty(state.campaignStartDayKey)) return 1;
            if (!DateTime.TryParseExact(state.campaignStartDayKey, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var start)) return 1;
            return Math.Clamp((local.Date - start.Date).Days + 1, 1, 5);
        }

        public static string WeeklyTaskId(string weekKey) => $"weekly-{weekKey}-quest-streak";

        /// <summary>Wallet transaction id of a task reward: one payout per task, ever (web: taskRewardTransactionId).</summary>
        public static string RewardTransactionId(string taskId) => $"task-reward-{taskId}";

        public static string GrowthEventId(string taskId) => $"growth-task-{taskId}";

        /// <summary>Quest of the day's pet event, if it has one (web: competitionPetEventForDate → scenarioId).</summary>
        public static string EventQuestFor(DateTime local, IReadOnlyList<string> eventQuests)
        {
            if (eventQuests == null || eventQuests.Count == 0) return null;
            string id = eventQuests[(int)(Math.Abs(DayOrdinal(local)) % eventQuests.Count)];
            return string.IsNullOrEmpty(id) ? null : id;
        }

        static List<T> Rotate<T>(IReadOnlyList<T> items, long shift)
        {
            var result = new List<T>(items.Count);
            if (items.Count == 0) return result;
            int start = (int)(Math.Abs(shift) % items.Count);
            for (int i = 0; i < items.Count; i++) result.Add(items[(start + i) % items.Count]);
            return result;
        }

        /// <summary>
        /// The day's quests: the pet event's quest if any, one featured hands-on quest (rotating by day),
        /// then regular quests rotating three steps a day; any shortfall is filled from the full list.
        /// </summary>
        public static List<FinikQuest> PickDaily(IReadOnlyList<FinikQuest> quests, DateTime local, int count, string requestedId = null)
        {
            long ordinal = DayOrdinal(local);
            var interactive = new List<FinikQuest>();
            var regular = new List<FinikQuest>();
            foreach (var quest in quests) (quest.IsInteractive ? interactive : regular).Add(quest);

            var selected = new List<FinikQuest>(count);
            bool Taken(FinikQuest q) => selected.Exists(s => s.Id == q.Id);

            if (requestedId != null)
            {
                var requested = FindQuest(quests, requestedId);
                if (requested != null) selected.Add(requested);
            }

            var featured = Rotate(interactive, ordinal);
            if (featured.Count > 0 && !Taken(featured[0])) selected.Add(featured[0]);

            foreach (var quest in Rotate(regular, ordinal * 3 + 1))
            {
                if (selected.Count >= count) break;
                if (!Taken(quest)) selected.Add(quest);
            }

            if (selected.Count < count)
            {
                foreach (var quest in Rotate(quests, ordinal))
                {
                    if (selected.Count >= count) break;
                    if (!Taken(quest)) selected.Add(quest);
                }
            }

            if (selected.Count > count) selected.RemoveRange(count, selected.Count - count);
            return selected;
        }

        public const string FirstBudgetQuestId = "d1-first-budget";

        public static bool IsAnswered(FinikQuestProgress progress) => progress != null &&
            (progress.status == FinikQuestStatus.Completed || progress.status == FinikQuestStatus.Claimed);

        public static bool IsUnlocked(FinikQuestState state, IReadOnlyList<FinikQuestTask> tasks, string taskId)
        {
            bool previousAnswered = true;
            foreach (var task in tasks)
            {
                if (task.IsWeekly) continue;
                if (task.Id == taskId) return previousAnswered;
                previousAnswered &= IsAnswered(state?.Find(task.Id));
            }
            return false;
        }

        public static bool AllDailyAnswered(FinikQuestState state, IReadOnlyList<FinikQuestTask> tasks)
        {
            bool hasDaily = false;
            foreach (var task in tasks)
            {
                if (task.IsWeekly) continue;
                hasDaily = true;
                if (!IsAnswered(state?.Find(task.Id))) return false;
            }
            return hasDaily;
        }

        public const int FirstBudgetAutoSave = 20;

        /// <summary>The first budget lesson moves 20 of the gifted 30 coins into savings after question one, regardless of the answer.</summary>
        public static int AutoSaveAfterQuestion(FinikQuest quest, int questionIndex) =>
            quest != null && quest.Id == FirstBudgetQuestId && questionIndex == 0 ? FirstBudgetAutoSave : 0;

        /// <summary>The first quest uses the gifted 30 coins; later daily quests pay 10 coins.</summary>
        public static int RewardFor(FinikQuest quest)
        {
            if (quest == null) return 0;
            if (quest.Id == "d1-first-coins") return 0;
            return 10;
        }

        /// <summary>The three scenarios for the current campaign day.</summary>
        public static List<FinikQuestTask> BuildTasks(DateTime local, int campaignDay = 1)
        {
            string dayKey = DayKey(local);
            int day = Math.Clamp(campaignDay, 1, 5);
            var selected = new List<FinikQuest>();
            foreach (var quest in FinikQuestCatalog.Quests)
                if (quest.Day == day) selected.Add(quest);
            selected.Sort((a, b) => a.Order.CompareTo(b.Order));

            var tasks = new List<FinikQuestTask>(FinikQuestCatalog.DailyCount + 1);
            for (int i = 0; i < selected.Count && i < FinikQuestCatalog.DailyCount; i++)
            {
                var quest = selected[i];
                tasks.Add(new FinikQuestTask($"daily-{dayKey}-{quest.Id}", FinikQuestCadence.Daily, quest, quest.Title, quest.Description,
                    quest.Difficulty, RewardFor(quest), 0));
            }

            // The streak remains as progress/motivation even when it has no coin reward.
            // It carries its own id per week, so Sync keeps its progress when the day rolls over.
            var rule = FinikQuestCatalog.Weekly;
            if (rule != null && rule.Target > 0)
                tasks.Add(new FinikQuestTask(WeeklyTaskId(WeekKey(local)), FinikQuestCadence.Weekly, null,
                    rule.Title, rule.Description, rule.Difficulty, rule.Reward, rule.Target));

            return tasks;
        }

        /// <summary>
        /// Moves the state to the period of <paramref name="local"/> (web: taskStore.syncPeriod): a new day
        /// brings a fresh set of daily quests, a new week resets the streak. True when anything changed.
        /// </summary>
        public static bool Sync(FinikQuestState state, IReadOnlyList<FinikQuestTask> tasks, DateTime local, long nowMs)
        {
            string dayKey = DayKey(local);
            string weekKey = WeekKey(local);
            bool sameWeek = state.weekKey == weekKey;
            bool sameDay = sameWeek && state.dayKey == dayKey;

            if (sameDay && AllTracked(state, tasks)) return false;

            var progress = new List<FinikQuestProgress>(tasks.Count);
            foreach (var task in tasks)
            {
                // Same day: keep what was done today (the set can differ only after a catalog edit).
                // Same week: the streak task carries over. Otherwise everything starts fresh.
                var old = sameDay || (sameWeek && task.IsWeekly) ? state.Find(task.Id) : null;
                progress.Add(old ?? new FinikQuestProgress { taskId = task.Id, status = FinikQuestStatus.Available });
            }

            int claimed = sameWeek ? state.weeklyClaimedDaily : 0;
            foreach (var task in tasks)
            {
                if (!task.IsWeekly) continue;
                var weekly = progress.Find(p => p.taskId == task.Id);
                if (weekly.status == FinikQuestStatus.Available && claimed >= task.TargetCount)
                {
                    weekly.completedAtMs = nowMs;
                    if (task.Reward > 0)
                    {
                        weekly.status = FinikQuestStatus.Completed;
                    }
                    else
                    {
                        weekly.status = FinikQuestStatus.Claimed;
                        weekly.claimedAtMs = nowMs;
                    }
                }
            }

            state.dayKey = dayKey;
            state.weekKey = weekKey;
            state.weeklyClaimedDaily = claimed;
            state.progress = progress;
            return true;
        }

        static bool AllTracked(FinikQuestState state, IReadOnlyList<FinikQuestTask> tasks)
        {
            if (state.progress == null || state.progress.Count != tasks.Count) return false;
            foreach (var task in tasks)
                if (state.Find(task.Id) == null) return false;
            return true;
        }

        public static FinikQuestState Normalize(FinikQuestState state)
        {
            state ??= new FinikQuestState();
            state.progress ??= new List<FinikQuestProgress>();
            state.progress.RemoveAll(p => p == null || string.IsNullOrEmpty(p.taskId));
            state.weeklyClaimedDaily = Math.Max(0, state.weeklyClaimedDaily);
            foreach (var item in state.progress)
            {
                item.questionIndex = Math.Max(0, item.questionIndex);
                item.goodAnswers = Math.Max(0, item.goodAnswers);
                item.riskAnswers = Math.Max(0, item.riskAnswers);
            }
            return state;
        }

        public static string StatusLabel(FinikQuestStatus status) => status switch
        {
            FinikQuestStatus.Active => "В процессе",
            FinikQuestStatus.Completed => "Награда готова",
            FinikQuestStatus.Claimed => "Выполнено",
            _ => "Доступно"
        };

        static FinikQuest FindQuest(IReadOnlyList<FinikQuest> quests, string id)
        {
            foreach (var quest in quests)
                if (quest.Id == id) return quest;
            return null;
        }
    }
}
