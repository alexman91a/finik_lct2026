using Finik.Core;
using Finik.UI.Onboarding;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI
{
    /// <summary>
    /// «Мой прогресс»: the child's real pet stage, finished tasks, savings goal and latest
    /// plan/fact result, plus a short financial glossary. Opened from the HUD portrait and shown over
    /// the running room, so it takes its turn like the other menus without stopping the world.
    /// </summary>
    public sealed class FinikProgressScreen : MonoBehaviour, IFinikScreen
    {
        [SerializeField] FinikScreenPanel panel;
        [SerializeField] Button closeButton;
        [SerializeField] Button doneButton;
        [SerializeField] TMP_Text profileTitle;
        [SerializeField] Button progressTabButton;
        [SerializeField] Button glossaryTabButton;
        [SerializeField] GameObject progressContent;
        [SerializeField] GameObject glossaryContent;
        [SerializeField] Image stageIcon;
        [SerializeField] TMP_Text stageTitle;
        [SerializeField] TMP_Text stageReason;
        [SerializeField] TMP_Text tasksText;
        [SerializeField] TMP_Text goalText;
        [SerializeField] TMP_Text periodTitle;
        [SerializeField] TMP_Text periodText;
        [SerializeField] TMP_Text glossaryIndexText;
        [SerializeField] TMP_Text glossaryTerm;
        [SerializeField] TMP_Text glossaryDefinition;
        [SerializeField] TMP_Text glossaryExample;
        [SerializeField] Button glossaryPrevButton;
        [SerializeField] Button glossaryNextButton;

        bool open;
        int glossaryIndex;

        public bool IsOpen => open;

        /// <summary>A card over the running room: the HUD stays up and Finik carries on behind it.</summary>
        public bool HoldsRoom => false;

        void Awake()
        {
            if (!profileTitle)
            {
                foreach (var text in GetComponentsInChildren<TMP_Text>(true))
                    if (text && text.name == "Title") { profileTitle = text; break; }
            }
            if (!stageIcon && panel)
                stageIcon = panel.transform.Find("Column/Content/ProgressContent/Stage/Icon")?.GetComponent<Image>();
            if (closeButton) closeButton.onClick.AddListener(Close);
            if (doneButton) doneButton.onClick.AddListener(Close);
            if (progressTabButton) progressTabButton.onClick.AddListener(ShowProgressTab);
            if (glossaryTabButton) glossaryTabButton.onClick.AddListener(ShowGlossaryTab);
            if (glossaryPrevButton) glossaryPrevButton.onClick.AddListener(() => MoveGlossary(-1));
            if (glossaryNextButton) glossaryNextButton.onClick.AddListener(() => MoveGlossary(1));
        }

        void OnEnable()
        {
            FinikScreens.Register(this);
            FinikGame.Changed += Refresh;
        }

        void OnDisable()
        {
            FinikScreens.Unregister(this);
            FinikGame.Changed -= Refresh;
        }

        public void Open()
        {
            open = true;
            // One menu at a time: whatever else is up closes before this card goes over the room.
            FinikScreens.CloseOthers(this);
            Refresh();
            ShowProgressTab();
            if (panel)
            {
                panel.Show();
                FitStageCard();
            }
            else gameObject.SetActive(true);
        }

        public void Close()
        {
            open = false;
            if (panel) panel.Hide();
            else gameObject.SetActive(false);
        }

        void Refresh()
        {
            string petName = "Питомец";
            if (FinikProfileStore.TryLoad(out var profile) && !string.IsNullOrWhiteSpace(profile.petName))
                petName = profile.petName.Trim();
            if (profileTitle) profileTitle.text = $"Прогресс: {petName}";
            if (stageIcon)
            {
                var hud = FindFirstObjectByType<FinikHudView>(FindObjectsInactive.Include);
                if (hud && hud.AvatarSprite) stageIcon.sprite = hud.AvatarSprite;
            }

            int stage = FinikGame.CharacterStage;
            if (stageTitle) stageTitle.text = stage switch
            {
                1 => "Стадия 1 · Малыш",
                2 => "Стадия 2 · Подрос",
                _ => "Стадия 3 · Взрослый"
            };
            if (stageReason)
            {
                stageReason.text = FinikTypography.Fix(FinikGame.GrowthReason(petName));
                FitStageCard();
            }

            int done = 0, total = 0;
            foreach (var task in FinikGame.QuestTasks())
            {
                if (task.IsWeekly) continue;
                total++;
                if ((FinikGame.QuestProgress(task.Id)?.status ?? FinikQuestStatus.Available) == FinikQuestStatus.Claimed) done++;
            }

            int allTime = 0;
            var events = FinikGame.State?.growth?.events;
            if (events != null)
                foreach (var entry in events)
                    if (entry != null && entry.source == FinikGrowthSource.Task) allTime++;
            if (tasksText) tasksText.text = $"Сегодня: {done} из {total} заданий\nВсего завершено: {allTime}";

            if (goalText)
                goalText.text = FinikGame.HasSelectedGoal
                    ? $"Цель «{FinikGame.Goal.Title}»\nНакоплено {FinikGame.Savings} из {FinikGame.GoalProgress.Target} монет"
                    : "Цель накопления пока не выбрана";

            RenderPeriod();
            RenderGlossary();
        }

        void FitStageCard()
        {
            if (!stageReason) return;

            var reasonRect = stageReason.rectTransform;
            float width = reasonRect.rect.width > 8f ? reasonRect.rect.width : 520f;
            float preferredHeight = Mathf.Ceil(stageReason.GetPreferredValues(stageReason.text, width, 0f).y + 8f);
            float reasonHeight = Mathf.Max(64f, preferredHeight);
            reasonRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, reasonHeight);

            var stageLayout = stageReason.transform.parent ? stageReason.transform.parent.GetComponent<LayoutElement>() : null;
            if (stageLayout)
            {
                float cardHeight = Mathf.Max(154f, 88f + reasonHeight);
                stageLayout.minHeight = cardHeight;
                stageLayout.preferredHeight = cardHeight;
            }

            if (progressContent && progressContent.transform is RectTransform progressRect)
                LayoutRebuilder.ForceRebuildLayoutImmediate(progressRect);
        }

        void RenderPeriod()
        {
            var summary = FinikGame.LatestBudgetSummary();
            string dayKey = FinikGame.LatestBudgetDayKey;
            if (periodTitle) periodTitle.text = dayKey == FinikGame.BudgetDayKey ? "ИТОГ СЕГОДНЯ" : "ПОСЛЕДНИЙ ИТОГ";

            if (!summary.hasPlan || summary.rows.Count < 3)
            {
                if (periodText) periodText.text = "Сначала составь бюджет. Здесь появится сравнение плана и того, что получилось на самом деле.";
                return;
            }

            if (periodText)
                periodText.text =
                    $"{summary.rows[0].Label}: план {summary.rows[0].planned} · факт {summary.rows[0].actual}\n" +
                    $"{summary.rows[1].Label}: план {summary.rows[1].planned} · факт {summary.rows[1].actual}\n" +
                    $"{summary.rows[2].Label}: план {summary.rows[2].planned} · факт {summary.rows[2].actual}\n" +
                    summary.Conclusion;
        }

        void ShowProgressTab() => SetTab(glossary: false);
        void ShowGlossaryTab() => SetTab(glossary: true);

        void SetTab(bool glossary)
        {
            if (progressContent) progressContent.SetActive(!glossary);
            if (glossaryContent) glossaryContent.SetActive(glossary);
            StyleTab(progressTabButton, !glossary);
            StyleTab(glossaryTabButton, glossary);
            if (glossary) RenderGlossary();
        }

        static void StyleTab(Button button, bool selected)
        {
            if (!button) return;
            if (button.targetGraphic is Image image)
                image.color = selected ? new Color(1f, 0.49f, 0.12f, 1f) : Color.white;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label) label.color = selected ? Color.white : new Color(0.08f, 0.10f, 0.22f, 1f);
        }

        void MoveGlossary(int delta)
        {
            glossaryIndex = Mathf.Clamp(glossaryIndex + delta, 0, FinikGlossary.Count - 1);
            RenderGlossary();
        }

        void RenderGlossary()
        {
            if (FinikGlossary.Count <= 0) return;
            glossaryIndex = Mathf.Clamp(glossaryIndex, 0, FinikGlossary.Count - 1);
            var entry = FinikGlossary.At(glossaryIndex);
            if (glossaryIndexText) glossaryIndexText.text = $"{glossaryIndex + 1} из {FinikGlossary.Count}";
            if (glossaryTerm) glossaryTerm.text = FinikTypography.Fix(entry.Term);
            if (glossaryDefinition) glossaryDefinition.text = FinikTypography.Fix(entry.Definition);
            if (glossaryExample) glossaryExample.text = FinikTypography.Fix($"Например: {entry.Example}");
            if (glossaryPrevButton) glossaryPrevButton.interactable = glossaryIndex > 0;
            if (glossaryNextButton) glossaryNextButton.interactable = glossaryIndex < FinikGlossary.Count - 1;
        }
    }
}
