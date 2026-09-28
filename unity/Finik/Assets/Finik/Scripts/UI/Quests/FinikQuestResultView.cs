using Finik.Core;
using Finik.UI.Onboarding;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Quests
{
    /// <summary>
    /// After an answer (web: task.tsx result and reward panels): the verdict («Хороший ход», «Запомни»,
    /// «Вот где подвох»), the answer and why, the reward that is already in the wallet, the weekly
    /// streak, and Finik's comment.
    /// </summary>
    public sealed class FinikQuestResultView : MonoBehaviour
    {
        [SerializeField] FinikScreenPanel panel;
        [SerializeField] Image verdictBackground;
        [SerializeField] TMP_Text verdict;
        [SerializeField] Image picture;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text answer;
        [SerializeField] TMP_Text explanation;
        [SerializeField] GameObject rewardStrip;
        [SerializeField] TMP_Text coinsDelta;
        [SerializeField] GameObject xpChip;
        [SerializeField] TMP_Text xpDelta;
        [SerializeField] TMP_Text streak;
        [SerializeField] TMP_Text petLine;
        [SerializeField] Image petAvatar;
        [SerializeField] Button moreButton;
        [SerializeField] Button closeButton;
        [SerializeField] FinikConfettiBurst confetti;

        [SerializeField] Color goodColor = new(0.18f, 0.74f, 0.35f, 1f);
        [SerializeField] Color neutralColor = new(0.25f, 0.62f, 1f, 1f);
        [SerializeField] Color riskColor = new(0.93f, 0.33f, 0.45f, 1f);
        [SerializeField] Color gainColor = new(0.13f, 0.62f, 0.3f, 1f);
        [SerializeField] Color flatColor = new(0.36f, 0.41f, 0.62f, 1f);

        public FinikScreenPanel Panel => panel;
        public Button MoreButton => moreButton;
        public Button CloseButton => closeButton;

        public void Show(FinikQuestOutcome outcome, int weeklyClaimed, FinikQuestTask weekly, Sprite icon, Sprite avatar, bool goToShop = false)
        {
            var tag = outcome.choice?.Tag ?? FinikQuestTag.Neutral;
            ApplyVerdict(tag);
            if (picture && icon) picture.sprite = icon;
            if (title) title.text = FinikTypography.Fix(FinikQuestText.Resolve(outcome.task?.Title));
            if (answer) answer.text = FinikTypography.Fix(outcome.choice != null ? $"Твой ответ: «{FinikQuestText.Resolve(outcome.choice.Label)}»" : string.Empty);
            if (explanation) explanation.text = FinikTypography.Fix(FinikQuestText.Resolve(outcome.choice?.Explanation));

            bool rewarded = outcome.coins > 0 || outcome.xp > 0;
            if (rewardStrip) rewardStrip.SetActive(rewarded);
            if (coinsDelta) coinsDelta.gameObject.SetActive(outcome.coins > 0);
            SetDelta(coinsDelta, outcome.coins);
            if (xpChip) xpChip.SetActive(outcome.xp > 0);
            SetDelta(xpDelta, outcome.xp);

            if (streak)
            {
                string line = null;
                if (!rewarded && outcome.task != null && !outcome.task.IsWeekly && outcome.task.Reward > 0)
                    line = $"Награда +{outcome.task.Reward} — за сценарий без ошибок. Попробуй следующий!";
                else if (weekly != null)
                {
                    int target = Mathf.Max(1, weekly.TargetCount);
                    line = outcome.weeklyUnlocked
                        ? $"Серия заданий собрана! Бонус +{weekly.Reward} ждёт на доске квестов."
                        : weeklyClaimed > 1 && weeklyClaimed < target ? $"Серия заданий: {weeklyClaimed} из {target}." : null;
                }
                streak.text = FinikTypography.Fix(line ?? string.Empty);
                streak.gameObject.SetActive(!string.IsNullOrEmpty(line));
            }

            if (petLine) petLine.text = FinikTypography.Fix($"«{PetComment(tag)}»");
            if (petAvatar && avatar) petAvatar.sprite = avatar;
            ApplyNavigationButtonLayout();
            SetMoreLabel(goToShop ? "К покупкам" : "К заданиям");
            panel.Show();
            if (confetti && (tag == FinikQuestTag.Good || outcome.weeklyUnlocked)) confetti.Burst(Vector2.zero);
        }

        public void ShowStep(FinikQuestTask task, FinikQuestChoice choice, int questionNumber, int totalQuestions, Sprite icon, Sprite avatar)
        {
            var tag = choice?.Tag ?? FinikQuestTag.Neutral;
            ApplyVerdict(tag);
            if (picture && icon) picture.sprite = icon;
            if (title) title.text = FinikTypography.Fix($"{FinikQuestText.Resolve(task?.Title)} · {questionNumber}/{totalQuestions}");
            if (answer) answer.text = FinikTypography.Fix(choice != null ? $"Твой ответ: «{FinikQuestText.Resolve(choice.Label)}»" : string.Empty);
            if (explanation) explanation.text = FinikTypography.Fix(FinikQuestText.Resolve(choice?.Explanation));
            bool good = tag == FinikQuestTag.Good;
            bool hasCoinReward = (task?.Reward ?? 0) > 0;
            if (rewardStrip) rewardStrip.SetActive(hasCoinReward);
            if (coinsDelta)
            {
                coinsDelta.gameObject.SetActive(hasCoinReward);
                if (hasCoinReward) coinsDelta.text = $"+{task.Reward} в финале";
            }
            if (xpChip) xpChip.SetActive(false);
            if (streak) streak.gameObject.SetActive(false);
            if (petLine) petLine.text = FinikTypography.Fix(good
                ? hasCoinReward ? "«Верно! Ещё немного — и заберём награду.»" : "«Верно! Отлично разобрались — идём дальше.»"
                : "«Подвох нашли. Запомним и идём дальше!»");
            if (petAvatar && avatar) petAvatar.sprite = avatar;
            ApplyNavigationButtonLayout();
            SetMoreLabel("Дальше");
            panel.Show();
            if (confetti && tag == FinikQuestTag.Good) confetti.Burst(Vector2.zero);
        }

        void ApplyVerdict(FinikQuestTag tag)
        {
            if (verdictBackground) verdictBackground.color = tag switch
            {
                FinikQuestTag.Good => goodColor,
                FinikQuestTag.Risk => riskColor,
                _ => neutralColor
            };
            if (verdict) verdict.text = tag switch
            {
                FinikQuestTag.Good => "Хороший ход",
                FinikQuestTag.Risk => "Вот где подвох",
                _ => "Запомни"
            };
        }

        void ApplyNavigationButtonLayout()
        {
            if (!moreButton || !closeButton) return;

            // Existing scenes may still contain the old layout. Fix it at runtime as well as in
            // the builder: «В комнату» = blue/left, primary continuation = green/right.
            var moreFace = moreButton.targetGraphic as Image;
            var closeFace = closeButton.targetGraphic as Image;
            bool oldColors = closeFace && closeFace.sprite && closeFace.sprite.name.Contains("green")
                && moreFace && moreFace.sprite && !moreFace.sprite.name.Contains("green");
            if (oldColors)
            {
                var sprite = moreFace.sprite;
                moreFace.sprite = closeFace.sprite;
                closeFace.sprite = sprite;

                // Keep the decorative sticker/sparkles with the primary green button.
                for (int i = closeButton.transform.childCount - 1; i >= 0; i--)
                {
                    var child = closeButton.transform.GetChild(i);
                    if (child.name.StartsWith("Decor_")) child.SetParent(moreButton.transform, false);
                }
            }

            var closeRoot = closeButton.transform.parent;
            var moreRoot = moreButton.transform.parent;
            if (closeRoot && moreRoot && closeRoot.parent == moreRoot.parent)
            {
                closeRoot.SetSiblingIndex(0);
                moreRoot.SetSiblingIndex(1);
            }
        }

        void SetMoreLabel(string value)
        {
            if (!moreButton) return;
            var label = moreButton.GetComponentInChildren<TMP_Text>(true);
            if (label) label.text = FinikTypography.Fix(value);
        }

        public void SetRoomExitVisible(bool visible)
        {
            if (closeButton) closeButton.transform.parent.gameObject.SetActive(visible);
        }

        void SetDelta(TMP_Text text, int value)
        {
            if (!text) return;
            text.text = value > 0 ? $"+{value}" : "0";
            text.color = value > 0 ? gainColor : flatColor;
        }

        static string PetComment(FinikQuestTag tag) => tag switch
        {
            FinikQuestTag.Good => "Супер! Всё правильно — идём дальше.",
            FinikQuestTag.Risk => "Подвох заметили. В следующем задании попробуем ещё раз!",
            _ => "Запомним этот ход на будущее."
        };
    }
}
