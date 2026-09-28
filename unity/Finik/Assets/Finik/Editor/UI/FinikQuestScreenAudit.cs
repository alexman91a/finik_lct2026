using System.Collections.Generic;
using System.Linq;
using Finik.Core;
using Finik.UI.Onboarding;
using Finik.UI.Quests;
using UnityEditor;
using UnityEngine;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Play Mode check of the quest screen at the current Game view resolution: the board with every
    /// quest name in every row state and the weekly strip in each state, every quest card with each
    /// step of its task (flags, wrong check, odds before and after the reveal, a wrong envelope) and
    /// the result card of every answer. Does not touch the saved progress: states are rendered directly.
    ///
    ///     Finik.Editor.UI.FinikQuestScreenAudit.SetResolution(1080, 1920);  // then let a few frames pass
    ///     Finik.Editor.UI.FinikQuestScreenAudit.Run();
    /// </summary>
    public static class FinikQuestScreenAudit
    {
        // The verdict tag is pinned onto the quest picture on the result card.
        static readonly (string a, string b)[] IntendedOverlaps = { ("Hero/Verdict", "Hero/Picture") };

        static readonly string[] ContentImages = { "Icon", "Coin", "Check", "Avatar", "Picture", "Flag", "Prize", "Badge", "Bar" };

        static readonly string[] Containers = { "Face", "Changes", "Verdict", "Weekly" };

        [MenuItem("Finik/UI/Audit Quest Screen")]
        static void Menu() => Debug.Log(Run());

        public static string SetResolution(int width, int height) => FinikUiAudit.SetResolution(width, height);

        public static string EnsureProfile() => FinikUiAudit.EnsureProfile();

        public static string Capture(string path) => FinikUiAudit.Capture(path);

        public static string Run()
        {
            if (!EditorApplication.isPlaying) return "Enter Play Mode first.";
            var screen = FindScreen(out string error);
            if (!screen) return error;
            if (!screen.IsOpen && !screen.Open()) return "Quest screen did not open: finish onboarding first.";

            var audit = new FinikUiAudit(IntendedOverlaps, ContentImages, Containers);
            var boardPanel = (FinikScreenPanel)Get(screen, "board");
            var questPanel = (FinikScreenPanel)Get(screen, "quest");
            var result = (FinikQuestResultView)Get(screen, "result");
            var boardColumn = Column(screen, "Board");
            var questColumn = Column(screen, "Quest");
            var resultColumn = Column(screen, "Result");
            var tasks = TasksFor(FinikQuestCatalog.Quests);
            var weeklyTask = WeeklyTask();

            // Board: every quest name in each row state, four at a time; the weekly strip in each state.
            ShowOnly(boardPanel, questPanel, result.Panel);
            var rows = (FinikQuestRowView[])Get(screen, "rows");
            var weekly = (FinikWeeklyStreakView)Get(screen, "weekly");
            var states = new[] { FinikQuestStatus.Available, FinikQuestStatus.Active, FinikQuestStatus.Completed, FinikQuestStatus.Claimed };
            for (int page = 0; page * rows.Length < tasks.Count; page++)
            {
                for (int i = 0; i < rows.Length; i++)
                {
                    var task = tasks[(page * rows.Length + i) % tasks.Count];
                    rows[i].gameObject.SetActive(true);
                    rows[i].Show(task, states[(page + i) % states.Length], (Sprite)Call(screen, "IconFor", task.Quest.Icon));
                }
                var weeklyState = states[page % states.Length];
                weekly.Show(weeklyTask, weeklyState, weeklyState == FinikQuestStatus.Available ? 2 : weeklyTask.TargetCount, animate: false);
                audit.Check(boardColumn, $"доска {page + 1}, серия {weeklyState}");
            }

            // Quest cards, each step of their task.
            foreach (var task in tasks)
            {
                var q = task.Quest;
                ShowQuest(screen, task, boardPanel, questPanel, result.Panel);
                audit.Check(questColumn, $"{q.Id} / начало");
                var mechanic = q.Mechanic;
                switch (mechanic.Type)
                {
                    case FinikQuestMechanicType.RedFlags:
                        var flags = (FinikRedFlagsView)Get(screen, "redFlags");
                        flags.Flag(mechanic.Messages.Where(m => m.Suspicious).Select(m => m.Id));
                        audit.Check(questColumn, $"{q.Id} / флаги отмечены");
                        flags.Flag(mechanic.Messages.Where(m => !m.Suspicious).Select(m => m.Id)
                            .Concat(mechanic.Messages.Where(m => m.Suspicious).Skip(1).Select(m => m.Id)));
                        Call(screen, "SetHint", mechanic.Retry);
                        audit.Check(questColumn, $"{q.Id} / ошибка");
                        break;
                    case FinikQuestMechanicType.Odds:
                        ((FinikOddsView)Get(screen, "odds")).Reveal(instant: true);
                        Set(screen, "oddsDecision", true);
                        Call(screen, "RenderQuestFooter");
                        audit.Check(questColumn, $"{q.Id} / шансы открыты");
                        break;
                    case FinikQuestMechanicType.NeedOrWant:
                        var envelopes = (FinikNeedOrWantView)Get(screen, "needOrWant");
                        envelopes.Select(mechanic.Answer == FinikBudgetBucket.Wants ? FinikBudgetBucket.Needs : FinikBudgetBucket.Wants);
                        Call(screen, "SetHint", mechanic.Retry);
                        audit.Check(questColumn, $"{q.Id} / не тот конверт");
                        break;
                }
            }

            // Result card for every answer; the weekly bonus line once.
            ShowOnly(null, boardPanel, questPanel);
            bool unlockedShown = false;
            foreach (var task in tasks)
            foreach (var choice in task.Quest.Choices)
            {
                bool unlocked = !unlockedShown && choice.Tag == FinikQuestTag.Risk;
                unlockedShown |= unlocked;
                var outcome = new FinikQuestOutcome(FinikQuestFailure.None, task, choice, task.Reward,
                    FinikGrowthEngine.TaskXp(task.Difficulty, false), unlocked);
                result.Show(outcome, unlocked ? weeklyTask.TargetCount : 2, weeklyTask, null, null);
                result.Panel.Show(instant: true);
                audit.Check(resultColumn, $"{task.Quest.Id} / итог {choice.Id}");
            }

            result.Panel.Hide(instant: true);
            Call(screen, "ShowBoard");
            boardPanel.Show(instant: true);

            var catalog = Resources.Load<TextAsset>(FinikQuestCatalog.ResourcePath);
            return audit.Report("Quest screen", boardColumn, FinikQuestCatalog.Parse(catalog ? catalog.text : string.Empty, out _));
        }

        /// <summary>
        /// Shows one state for a screenshot. Stage: board, quest, flags (red flags ticked), wrong (a wrong
        /// attempt), revealed (odds opened), result (with <paramref name="choiceId"/> or the first answer).
        /// </summary>
        public static string Present(string stage, string questId = null, string choiceId = null)
        {
            if (!EditorApplication.isPlaying) return "Enter Play Mode first.";
            var screen = FindScreen(out string error);
            if (!screen) return error;
            if (!screen.IsOpen && !screen.Open()) return "Quest screen did not open: finish onboarding first.";
            var boardPanel = (FinikScreenPanel)Get(screen, "board");
            var questPanel = (FinikScreenPanel)Get(screen, "quest");
            var result = (FinikQuestResultView)Get(screen, "result");

            if (stage == "board")
            {
                Call(screen, "ShowBoard");
                ShowOnly(boardPanel, questPanel, result.Panel);
                return $"board at {Screen.width}x{Screen.height}";
            }
            if (!FinikQuestCatalog.TryGet(questId ?? FinikQuestCatalog.Quests[0].Id, out var quest)) return $"Unknown quest {questId}";
            var task = TasksFor(new[] { quest })[0];
            var mechanic = quest.Mechanic;
            ShowQuest(screen, task, boardPanel, questPanel, result.Panel);
            switch (stage)
            {
                case "flags":
                    ((FinikRedFlagsView)Get(screen, "redFlags")).Flag(mechanic.Messages.Where(m => m.Suspicious).Select(m => m.Id));
                    break;
                case "wrong":
                    if (mechanic.Type == FinikQuestMechanicType.NeedOrWant)
                        ((FinikNeedOrWantView)Get(screen, "needOrWant")).Select(mechanic.Answer == FinikBudgetBucket.Wants ? FinikBudgetBucket.Needs : FinikBudgetBucket.Wants);
                    Call(screen, "SetHint", mechanic.Retry ?? "…");
                    break;
                case "revealed":
                    ((FinikOddsView)Get(screen, "odds")).Reveal(instant: true);
                    Set(screen, "oddsDecision", true);
                    Call(screen, "RenderQuestFooter");
                    break;
                case "result":
                    var choice = quest.Choice(choiceId ?? quest.Choices[0].Id) ?? quest.Choices[0];
                    var icons = (FinikQuestScreen.IconEntry[])Get(screen, "icons");
                    var sprite = icons.FirstOrDefault(e => e.name == quest.Icon).sprite;
                    ShowOnly(null, boardPanel, questPanel);
                    result.Show(new FinikQuestOutcome(FinikQuestFailure.None, task, choice, task.Reward, FinikGrowthEngine.TaskXp(task.Difficulty, false)),
                        2, WeeklyTask(), sprite, null);
                    result.Panel.Show(instant: true);
                    break;
            }
            return $"{quest.Id} / {stage} at {Screen.width}x{Screen.height}";
        }

        // ------------------------------------------------------------------ plumbing

        static List<FinikQuestTask> TasksFor(IEnumerable<FinikQuest> quests) => quests
            .Select(q => new FinikQuestTask($"audit-{q.Id}", FinikQuestCadence.Daily, q, q.Title, q.Description, q.Difficulty,
                FinikQuestCatalog.DailyReward(q.Difficulty), 0))
            .ToList();

        static FinikQuestTask WeeklyTask()
        {
            var rule = FinikQuestCatalog.Weekly;
            return new FinikQuestTask("audit-weekly", FinikQuestCadence.Weekly, null, rule.Title, rule.Description, rule.Difficulty, rule.Reward, rule.Target);
        }

        static void ShowQuest(FinikQuestScreen screen, FinikQuestTask task, FinikScreenPanel board, FinikScreenPanel quest, FinikScreenPanel result)
        {
            Set(screen, "stage", System.Enum.Parse(typeof(FinikQuestScreen).GetNestedType("Stage", System.Reflection.BindingFlags.NonPublic), "Quest"));
            Call(screen, "ShowQuest", task);
            // ShowQuest animates the panels; snap them so the layout is measured in place.
            ShowOnly(quest, board, result);
        }

        static void ShowOnly(FinikScreenPanel visible, params FinikScreenPanel[] hidden)
        {
            foreach (var panel in hidden) if (panel) panel.Hide(instant: true);
            if (visible) visible.Show(instant: true);
        }

        static FinikQuestScreen FindScreen(out string error)
        {
            var screen = Object.FindAnyObjectByType<FinikQuestScreen>(FindObjectsInactive.Include);
            error = screen ? null : "Screen_Quests is missing: run Finik/UI/Rebuild Quest Screen.";
            return screen;
        }

        static RectTransform Column(FinikQuestScreen screen, string panelName) =>
            (RectTransform)screen.transform.Find($"SafeArea/{panelName}/Column");

        static object Get(object target, string field) => FinikUiAudit.Get(target, field);
        static void Set(object target, string field, object value) => FinikUiAudit.Set(target, field, value);
        static object Call(object target, string method, params object[] args) => FinikUiAudit.Call(target, method, args);
    }
}
