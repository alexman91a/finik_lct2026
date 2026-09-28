using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI
{
    /// <summary>What the goal card on the home screen shows.</summary>
    [Serializable]
    public struct FinikGoalCardData
    {
        /// <summary>Catalog icon name, resolved against <c>goalIcons</c> when the screen has a set.</summary>
        public string iconName;
        public string title;
        public Sprite icon;
        public int saved;
        public int target;

        public int Left => Mathf.Max(0, target - saved);
        public float Progress => target > 0 ? Mathf.Clamp01((float)saved / target) : 0f;
    }

    /// <summary>What the "task of the day" card on the home screen shows.</summary>
    [Serializable]
    public struct FinikDailyTaskData
    {
        public string title;
        [TextArea] public string description;
        public int reward;
        public bool finished;
    }

    /// <summary>
    /// The two cards under the top row of the home HUD: the piggy bank goal (fed from FinikGame by
    /// FinikHudBinder) and the task of the day (mock data for now, set by the HUD builder). The buttons
    /// only raise events.
    /// </summary>
    public sealed class FinikHomeCards : MonoBehaviour
    {
        [Header("Goal")]
        [SerializeField] FinikGoalCardData goal = new() { title = "Велосипед", saved = 200, target = 500 };
        [SerializeField] Button goalButton;
        [SerializeField] Image goalIcon;
        [SerializeField] TMP_Text goalTitle;
        [SerializeField] FinikFillBar goalBar;
        [SerializeField] TMP_Text goalProgress;
        [SerializeField] TMP_Text goalLeft;
        [Tooltip("Colour the goal's name picks up.")]
        [SerializeField] Color goalAccent = new(0.184f, 0.482f, 0.941f, 1f);
        [Tooltip("Prefix the title with «Цель: ».")]
        [SerializeField] bool goalTitlePrefix = true;

        [System.Serializable]
        public struct GoalIcon
        {
            public string name;
            public Sprite sprite;
        }

        [Tooltip("Goal pictures by catalog name. Left empty the card keeps taking whatever " +
                 "sprite the binder hands over.")]
        [SerializeField] GoalIcon[] goalIcons = System.Array.Empty<GoalIcon>();
        [Tooltip("Shown on the goal card before a goal is picked, so the slot is never an empty box.")]
        [SerializeField] Sprite unknownGoalIcon;

        [Header("Task of the day")]
        [SerializeField] FinikDailyTaskData task = new() { title = "Задание дня", description = "Помоги Финику распланировать 50 монет", reward = 30 };
        [SerializeField] Button taskButton;
        Button taskCardButton;
        [SerializeField] TMP_Text taskTitle;
        [SerializeField] TMP_Text taskDescription;
        [SerializeField] TMP_Text taskReward;

        public event Action GoalClicked;
        public event Action TaskStarted;

        public void Configure(Button goalButton, Image goalIcon, TMP_Text goalTitle, FinikFillBar goalBar, TMP_Text goalProgress, TMP_Text goalLeft,
            Button taskButton, TMP_Text taskTitle, TMP_Text taskDescription, TMP_Text taskReward)
        {
            this.goalButton = goalButton;
            this.goalIcon = goalIcon;
            this.goalTitle = goalTitle;
            this.goalBar = goalBar;
            this.goalProgress = goalProgress;
            this.goalLeft = goalLeft;
            this.taskButton = taskButton;
            this.taskTitle = taskTitle;
            this.taskDescription = taskDescription;
            this.taskReward = taskReward;
        }

        void Awake()
        {
            if (goalButton) goalButton.onClick.AddListener(() => GoalClicked?.Invoke());
            if (taskButton) taskButton.onClick.AddListener(() => TaskStarted?.Invoke());
            EnsureTaskCardButton();
        }

        void EnsureTaskCardButton()
        {
            if (!taskButton || taskCardButton) return;

            Transform card = taskButton.transform;
            while (card && card != transform && card.name != "TaskCard") card = card.parent;
            if (!card || card == transform) return;

            var hitArea = card.GetComponent<Image>();
            if (!hitArea)
            {
                hitArea = card.gameObject.AddComponent<Image>();
                hitArea.color = Color.clear;
                hitArea.raycastTarget = true;
            }

            taskCardButton = card.GetComponent<Button>();
            if (!taskCardButton) taskCardButton = card.gameObject.AddComponent<Button>();
            taskCardButton.targetGraphic = hitArea;
            taskCardButton.transition = Selectable.Transition.None;
            taskCardButton.onClick.AddListener(() =>
            {
                if (!task.finished) TaskStarted?.Invoke();
            });
        }

        void OnEnable() => Refresh(animate: false);

        public FinikGoalCardData Goal => goal;
        public FinikDailyTaskData Task => task;

        public void SetGoal(FinikGoalCardData value, bool animate = true)
        {
            goal = value;
            RefreshGoal(animate);
        }

        public void SetTask(FinikDailyTaskData value)
        {
            task = value;
            RefreshTask();
        }

        public void Refresh(bool animate = true)
        {
            RefreshGoal(animate);
            RefreshTask();
        }

        void RefreshGoal(bool animate)
        {
            bool hasGoal = goal.target > 0;
            // A screen with its own set names the picture itself. The binder sources the sprite from
            // whichever savings screen it finds, and that one falls back to a piggy bank for goals it
            // does not know — which is how the bicycle turned into a money box.
            if (goalIcon)
            {
                // Without a goal the card still shows a picture — a question mark — rather than an
                // empty frame that reads as a missing asset.
                var picked = hasGoal ? Resolve(goal.iconName) ?? goal.icon : unknownGoalIcon;
                if (picked) goalIcon.sprite = picked;
                goalIcon.gameObject.SetActive(picked);
            }
            // The label is dark ink, the goal's name picks up the accent. Which accent, and whether
            // the line is prefixed at all, is the screen's call.
            if (goalTitle)
            {
                string tinted = $"<color=#{ColorUtility.ToHtmlStringRGB(goalAccent)}>{goal.title}</color>";
                goalTitle.text = hasGoal && goalTitlePrefix ? $"Цель: {tinted}" : tinted;
            }
            if (goalBar)
            {
                goalBar.gameObject.SetActive(hasGoal);
                if (hasGoal) goalBar.SetValue(goal.Progress, animate);
            }
            if (goalProgress) goalProgress.gameObject.SetActive(hasGoal);
            if (!hasGoal)
            {
                if (goalLeft) goalLeft.text = "Нажми, чтобы выбрать цель";
                return;
            }
            if (goalProgress) goalProgress.text = $"{goal.saved} / {goal.target}";
            if (goalLeft) goalLeft.text = goal.Left > 0 ? $"Осталось: <b>{goal.Left}</b>" : "Цель достигнута!";
        }

        Sprite Resolve(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var entry in goalIcons)
                if (entry.name == name) return entry.sprite;
            return null;
        }

        void RefreshTask()
        {
            bool hasReward = task.reward > 0;
            if (taskCardButton) taskCardButton.interactable = !task.finished;
            if (taskTitle) taskTitle.text = task.title;
            if (taskDescription) taskDescription.text = task.description;
            if (taskButton)
            {
                taskButton.gameObject.SetActive(!task.finished);
                taskButton.interactable = !task.finished;
                var label = taskButton.GetComponentInChildren<TMP_Text>(true);
                if (label) label.text = "Начать";
            }
            if (taskReward)
            {
                // Hide the whole reward pill, including the coin icon. Hiding only the label left
                // an empty gold slot with a coin after all daily tasks had been completed.
                var rewardRoot = taskReward.transform.parent;
                if (rewardRoot) rewardRoot.gameObject.SetActive(hasReward);
                else taskReward.gameObject.SetActive(hasReward);
                if (hasReward) taskReward.text = $"+{task.reward}";
            }
        }
    }
}
