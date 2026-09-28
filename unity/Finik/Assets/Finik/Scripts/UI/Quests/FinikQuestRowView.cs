using System;
using Finik.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Quests
{
    /// <summary>
    /// One daily quest on the board (web: QuestTile): picture, status and name on the left, the reward
    /// on the right. A played quest reads «готово» with a check and can no longer be opened.
    /// </summary>
    public sealed class FinikQuestRowView : MonoBehaviour
    {
        [SerializeField] Button button;
        [SerializeField] CanvasGroup group;
        [SerializeField] Image icon;
        [SerializeField] TMP_Text status;
        [SerializeField] TMP_Text title;
        [SerializeField] GameObject coin;
        [SerializeField] TMP_Text reward;
        [SerializeField] GameObject check;
        [SerializeField] Color statusColor = new(0.55f, 0.3f, 0.95f, 1f);
        [SerializeField] Color readyColor = new(0.95f, 0.5f, 0.1f, 1f);
        [SerializeField] Color doneColor = new(0.13f, 0.62f, 0.3f, 1f);
        [SerializeField] Color rewardColor = new(0.12f, 0.15f, 0.36f, 1f);
        [SerializeField, Range(0f, 1f)] float doneAlpha = 0.72f;

        public string TaskId { get; private set; }
        public event Action<FinikQuestRowView> Clicked;

        public void Configure(Button rowButton, CanvasGroup rowGroup, Image picture, TMP_Text statusText, TMP_Text titleText, GameObject coinIcon, TMP_Text rewardText, GameObject doneCheck)
        {
            button = rowButton;
            group = rowGroup;
            icon = picture;
            status = statusText;
            title = titleText;
            coin = coinIcon;
            reward = rewardText;
            check = doneCheck;
        }

        void Awake()
        {
            if (button) button.onClick.AddListener(() => Clicked?.Invoke(this));
        }

        public void Show(FinikQuestTask task, FinikQuestStatus state, Sprite picture, bool locked = false)
        {
            TaskId = task.Id;
            if (icon) icon.sprite = picture;
            if (title) title.text = FinikTypography.Fix(FinikQuestText.Resolve(task.Title));
            bool claimed = state == FinikQuestStatus.Claimed;
            bool hasCoinReward = task.Reward > 0;
            if (status)
            {
                status.text = locked ? "ПОСЛЕ ПРЕДЫДУЩЕГО" : state == FinikQuestStatus.Completed
                    ? hasCoinReward ? "НАГРАДА ЖДЁТ" : "ГОТОВО"
                    : FinikQuestEngine.StatusLabel(state).ToUpperInvariant();
                status.color = locked ? Color.gray : claimed ? doneColor : state == FinikQuestStatus.Completed ? readyColor : statusColor;
            }
            if (reward)
            {
                reward.gameObject.SetActive(hasCoinReward || claimed);
                reward.text = claimed ? "готово" : $"+{task.Reward}";
                reward.color = claimed ? doneColor : rewardColor;
            }
            if (coin) coin.SetActive(hasCoinReward && !claimed);
            if (check) check.SetActive(claimed);
            if (button)
            {
                button.interactable = !claimed && !locked;
                button.transition = Selectable.Transition.ColorTint;
                var colors = button.colors;
                colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                button.colors = colors;
            }
            if (group) group.alpha = locked ? 0.45f : claimed ? doneAlpha : 1f;
        }
    }
}
