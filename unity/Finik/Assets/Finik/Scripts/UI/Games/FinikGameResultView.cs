using System.Collections;
using Finik.Core;
using Finik.UI.Onboarding;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Games
{
    /// <summary>
    /// What a finished level gave. A cleared level shows the mood it added — the whole point of the
    /// games — and the XP the first time it is cleared; a lost one says so kindly and offers another
    /// go. Nothing is ever taken away here: losing costs the child nothing but the round.
    ///
    /// A win is a moment: rays turn behind the picture and the rewards pop in one after the other,
    /// so the prize reads as a prize and not as two numbers under a paragraph.
    /// </summary>
    public sealed class FinikGameResultView : MonoBehaviour
    {
        public readonly struct Outcome
        {
            public readonly bool won;
            public readonly FinikMiniGame game;
            public readonly FinikMiniGameLevel level;
            public readonly int mood;
            public readonly int xp;
            /// <summary>A harder step opened, so the primary button offers it.</summary>
            public readonly bool hasNext;
            /// <summary>The pet was already at 100: the round still counts, but nothing was added.</summary>
            public readonly bool moodWasFull;
            /// <summary>Why a lost level was lost ("Закончились ходы"); the card's title on a loss.</summary>
            public readonly string lossReason;

            public Outcome(bool won, FinikMiniGame game, FinikMiniGameLevel level, int mood, int xp, bool hasNext, bool moodWasFull,
                string lossReason = null)
            {
                this.lossReason = lossReason;
                this.won = won;
                this.game = game;
                this.level = level;
                this.mood = mood;
                this.xp = xp;
                this.hasNext = hasNext;
                this.moodWasFull = moodWasFull;
            }
        }

        [SerializeField] FinikScreenPanel panel;
        [SerializeField] Image icon;
        [Tooltip("Cup beside the picture; only a cleared level gets one.")]
        [SerializeField] GameObject trophy;
        [Tooltip("Light rays behind the picture, turning slowly on a win.")]
        [SerializeField] RectTransform rays;
        [Tooltip("Garland and balloons: a win only.")]
        [SerializeField] GameObject festive;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text explanation;
        [Tooltip("Whole reward tile, hidden when the round gave no mood at all.")]
        [SerializeField] GameObject moodChip;
        [SerializeField] TMP_Text moodDelta;
        [SerializeField] GameObject xpChip;
        [SerializeField] TMP_Text xpDelta;
        [Tooltip("The shelf both reward tiles stand on; hidden when there is nothing to show.")]
        [SerializeField] GameObject rewards;
        [SerializeField] Button primaryButton;
        [SerializeField] TMP_Text primaryLabel;
        [SerializeField] Button closeButton;
        [SerializeField] TMP_Text closeLabel;
        [SerializeField] FinikConfettiBurst confetti;
        [SerializeField] float raysDegreesPerSecond = 18f;
        [Tooltip("The card, shortened by the prize shelf's height when there is no prize to show.")]
        [SerializeField] RectTransform column;
        [SerializeField] float fullHeight = 770f;
        [SerializeField] float shelfHeight = 170f;

        Coroutine pop;

        public FinikScreenPanel Panel => panel;
        public Button PrimaryButton => primaryButton;
        public Button CloseButton => closeButton;

        /// <summary>What the primary button does now: play the next step, or the same one again.</summary>
        public bool PrimaryIsNext { get; private set; }

        public void Configure(FinikScreenPanel resultPanel, Image resultIcon, TMP_Text resultTitle, TMP_Text resultExplanation,
            GameObject mood, TMP_Text moodValue, GameObject xp, TMP_Text xpValue, GameObject rewardShelf,
            Button primary, TMP_Text primaryText, Button close, TMP_Text closeText, FinikConfettiBurst burst)
        {
            panel = resultPanel;
            icon = resultIcon;
            title = resultTitle;
            explanation = resultExplanation;
            moodChip = mood;
            moodDelta = moodValue;
            xpChip = xp;
            xpDelta = xpValue;
            rewards = rewardShelf;
            primaryButton = primary;
            primaryLabel = primaryText;
            closeButton = close;
            closeLabel = closeText;
            confetti = burst;
        }

        public void Show(Outcome outcome, Sprite picture)
        {
            PrimaryIsNext = outcome.won && outcome.hasNext;

            if (icon)
            {
                if (picture) icon.sprite = picture;
                icon.gameObject.SetActive(icon.sprite);
            }
            if (trophy) trophy.SetActive(outcome.won);
            if (rays) rays.gameObject.SetActive(outcome.won);
            if (festive) festive.SetActive(outcome.won);
            if (title)
                title.text = outcome.won ? "Уровень пройден!"
                    : string.IsNullOrEmpty(outcome.lossReason) ? "Почти получилось!" : outcome.lossReason;
            // A lost round keeps the game's picture, but faded: the card should not look like a prize.
            if (icon) icon.color = outcome.won ? Color.white : new Color(0.78f, 0.82f, 0.95f, 0.85f);
            if (explanation) explanation.text = FinikTypography.Fix(Explain(outcome));

            bool showMood = outcome.won && outcome.mood > 0;
            if (moodChip) moodChip.SetActive(showMood);
            if (moodDelta && showMood) moodDelta.text = $"+{outcome.mood}";
            bool showXp = outcome.won && outcome.xp > 0;
            if (xpChip) xpChip.SetActive(showXp);
            if (xpDelta && showXp) xpDelta.text = $"+{outcome.xp}";
            bool anyPrize = showMood || showXp;
            if (rewards) rewards.SetActive(anyPrize);
            // No prize, no empty band where it would have been: the card closes up around the verdict.
            if (column) column.sizeDelta = new Vector2(column.sizeDelta.x, anyPrize ? fullHeight : fullHeight - shelfHeight);

            if (primaryLabel) primaryLabel.text = PrimaryIsNext ? "Следующий уровень" : outcome.won ? "Сыграть ещё" : "Попробовать снова";
            if (closeLabel) closeLabel.text = "К играм";

            if (panel) panel.Show();
            if (outcome.won && confetti) confetti.Burst(Vector2.zero);
            if (pop != null) StopCoroutine(pop);
            pop = isActiveAndEnabled ? StartCoroutine(PopRewards()) : null;
        }

        void Update()
        {
            if (rays && rays.gameObject.activeInHierarchy)
                rays.localRotation = Quaternion.Euler(0f, 0f, rays.localEulerAngles.z - raysDegreesPerSecond * Time.unscaledDeltaTime);
        }

        /// <summary>The tiles land one after the other, each with a little overshoot.</summary>
        IEnumerator PopRewards()
        {
            var tiles = new[] { moodChip, xpChip };
            foreach (var tile in tiles)
                if (tile) tile.transform.localScale = Vector3.zero;
            yield return new WaitForSecondsRealtime(0.25f);
            foreach (var tile in tiles)
            {
                if (!tile || !tile.activeSelf) continue;
                const float seconds = 0.35f;
                for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
                {
                    float s = FinikUiMotion.EaseOutBack(t / seconds, 2.2f);
                    tile.transform.localScale = new Vector3(s, s, 1f);
                    yield return null;
                }
                tile.transform.localScale = Vector3.one;
            }
            pop = null;
        }

        void OnDisable()
        {
            if (pop != null) StopCoroutine(pop);
            pop = null;
            foreach (var tile in new[] { moodChip, xpChip })
                if (tile) tile.transform.localScale = Vector3.one;
        }

        static string Explain(Outcome outcome)
        {
            if (!outcome.won)
            {
                if (outcome.game != null && outcome.game.Id == FinikMiniGameId.Catch)
                    return "Давай попробуем еще раз";
                string rule = outcome.game?.Rule;
                if (string.IsNullOrEmpty(rule)) return "Попробуй ещё раз!";
                return "Совет: " + char.ToLowerInvariant(rule[0]) + rule.Substring(1);
            }
            if (outcome.moodWasFull)
                return "Финик и так счастлив — но поиграть с тобой он рад всегда.";
            return "Финик стал веселее!";
        }
    }
}
