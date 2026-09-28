using Finik.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Quests
{
    /// <summary>
    /// The weekly streak strip on the quest board (web: tasks.tsx «СЕРИЯ НЕДЕЛИ»): progress towards the
    /// target, the bonus, and a claim button once the target is reached.
    /// </summary>
    public sealed class FinikWeeklyStreakView : MonoBehaviour
    {
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text progress;
        [SerializeField] FinikFillBar bar;
        [SerializeField] GameObject rewardRoot;
        [SerializeField] TMP_Text reward;
        [SerializeField] GameObject claimRoot;
        [SerializeField] Button claimButton;
        [SerializeField] TMP_Text claimLabel;
        [SerializeField] GameObject doneRoot;

        public Button ClaimButton => claimButton;

        public void Configure(TMP_Text titleText, TMP_Text progressText, FinikFillBar fill, GameObject rewardGroup, TMP_Text rewardText,
            GameObject claimGroup, Button claim, TMP_Text claimText, GameObject doneGroup)
        {
            title = titleText;
            progress = progressText;
            bar = fill;
            rewardRoot = rewardGroup;
            reward = rewardText;
            claimRoot = claimGroup;
            claimButton = claim;
            claimLabel = claimText;
            doneRoot = doneGroup;
        }

        void ApplyBarLayout()
        {
            if (!bar) return;
            var track = bar.GetComponent<RectTransform>();
            if (!track) return;

            track.offsetMin = new Vector2(track.offsetMin.x, 12f);
            track.offsetMax = new Vector2(track.offsetMax.x, 40f);

            var fill = track.Find("Fill") as RectTransform;
            if (fill)
            {
                fill.offsetMin = new Vector2(4f, 4f);
                fill.offsetMax = new Vector2(-4f, -4f);
            }
        }

        public void Show(FinikQuestTask weekly, FinikQuestStatus state, int claimedDaily, bool animate)
        {
            var kicker = transform.Find("Kicker");
            if (kicker) kicker.gameObject.SetActive(false);
            int target = Mathf.Max(1, weekly.TargetCount);
            int done = Mathf.Min(claimedDaily, target);
            if (title)
            {
                title.text = "Серия заданий";
                title.enableAutoSizing = false;
                title.fontSize = 34;
                title.rectTransform.offsetMax = new Vector2(title.rectTransform.offsetMax.x, -16f);
            }
            bool hasReward = weekly.Reward > 0;
            if (progress)
            {
                progress.text = FinikTypography.Fix(state switch
                {
                    FinikQuestStatus.Claimed => hasReward ? "Бонус уже получен." : "Серия выполнена.",
                    FinikQuestStatus.Completed => "Серия собрана!",
                    _ => hasReward ? $"{done} из {target} — и бонус твой." : $"{done} из {target} заданий выполнено."
                });
                // The bar is now taller, so keep this line higher instead of letting it sit on the track.
                progress.rectTransform.offsetMin = new Vector2(progress.rectTransform.offsetMin.x, -84f);
                progress.rectTransform.offsetMax = new Vector2(progress.rectTransform.offsetMax.x, -50f);
            }
            ApplyBarLayout();
            if (bar) bar.SetValue(done / (float)target, animate);
            if (reward) reward.text = $"+{weekly.Reward}";
            if (claimLabel) claimLabel.text = $"Забрать +{weekly.Reward}";
            if (rewardRoot) rewardRoot.SetActive(hasReward && (state == FinikQuestStatus.Available || state == FinikQuestStatus.Active));
            if (claimRoot) claimRoot.SetActive(hasReward && state == FinikQuestStatus.Completed);
            if (doneRoot) doneRoot.SetActive(state == FinikQuestStatus.Claimed || (!hasReward && state == FinikQuestStatus.Completed));
        }
    }
}
