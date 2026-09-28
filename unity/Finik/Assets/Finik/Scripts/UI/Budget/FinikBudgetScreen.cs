using System;
using Finik.Core;
using Finik.Navigation;
using Finik.UI.Onboarding;
using Finik.UI.Quests;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Budget
{
    /// <summary>
    /// The plan step of the day: раскладываем монеты по «Нужно», «Хочу» и «Коплю».
    ///
    /// Two states. Before the plan is confirmed it is a planner: the day's coins at the top, three
    /// envelopes with −/+ controls, and a counter of what is still unplanned; the confirm button only
    /// works once every coin has a job. After that it becomes the day's plan/fact card: what was
    /// planned against what actually happened, with a neutral sentence and no grade.
    /// </summary>
    public sealed class FinikBudgetScreen : MonoBehaviour, IFinikScreen
    {
        /// <summary>The desk is where planning happens, like the fridge is for food.</summary>
        public const string DeskInteraction = "study-desk";

        [Header("Plan")]
        [SerializeField] FinikScreenPanel plan;
        [SerializeField] FinikCounterText incomeCounter;
        [SerializeField] TMP_Text unplannedLabel;
        [SerializeField] TMP_Text planHint;
        [SerializeField] TMP_Text[] envelopeAmounts = Array.Empty<TMP_Text>();
        [SerializeField] TMP_Text[] envelopeHints = Array.Empty<TMP_Text>();
        [SerializeField] Button[] minusButtons = Array.Empty<Button>();
        [SerializeField] Button[] plusButtons = Array.Empty<Button>();
        [SerializeField] Button confirmButton;
        [SerializeField] TMP_Text confirmLabel;
        [SerializeField] Button suggestButton;

        [Header("Plan vs fact")]
        [SerializeField] FinikScreenPanel fact;
        [SerializeField] TMP_Text[] factLabels = Array.Empty<TMP_Text>();
        [SerializeField] TMP_Text[] factValues = Array.Empty<TMP_Text>();
        [SerializeField] FinikFillBar[] factBars = Array.Empty<FinikFillBar>();
        [SerializeField] TMP_Text factConclusion;
        [SerializeField] Button factCloseButton;
        [SerializeField] Button endDayButton;

        [Header("Shared")]
        [SerializeField] Button closeButton;
        [SerializeField] GameObject hudRoot;
        [SerializeField] FinikMovementController movement;
        [SerializeField] FinikShowcaseCamera showcase;
        [SerializeField] Behaviour[] pauseWhileOpen = Array.Empty<Behaviour>();

        /// <summary>Coins one tap moves — the same grain the suggestion is drawn on.</summary>
        const int Step = FinikBudgetEngine.Step;

        static readonly FinikBudgetBucket[] Buckets = FinikBudgetEngine.Buckets;

        readonly int[] draft = new int[3];
        bool open;
        /// <summary>Said by the pet in the room once this screen closes, after the day made it grow.</summary>
        string growthMessageAfterClose;
        int income;

        public bool IsOpen => open;

        /// <summary>This menu takes the room over: the HUD goes away and Finik stands still.</summary>
        public bool HoldsRoom => true;

        void Awake()
        {
            for (int i = 0; i < minusButtons.Length; i++)
            {
                int index = i;
                if (minusButtons[i]) minusButtons[i].onClick.AddListener(() => Adjust(index, -Step));
            }
            for (int i = 0; i < plusButtons.Length; i++)
            {
                int index = i;
                if (plusButtons[i]) plusButtons[i].onClick.AddListener(() => Adjust(index, Step));
            }
            if (confirmButton) confirmButton.onClick.AddListener(Confirm);
            if (suggestButton) suggestButton.onClick.AddListener(UseSuggestion);
            if (closeButton) closeButton.onClick.AddListener(Close);
            if (factCloseButton) factCloseButton.onClick.AddListener(Close);
            if (endDayButton) endDayButton.onClick.AddListener(EndDay);
        }

        void OnEnable()
        {
            FinikScreens.Register(this);
            FinikGame.Changed += OnGameChanged;
        }

        void OnDisable()
        {
            FinikScreens.Unregister(this);
            FinikGame.Changed -= OnGameChanged;
        }

        void OnGameChanged()
        {
            if (!open) return;
            if (FinikGame.HasBudgetPlan) RenderFact();
            else RenderPlan();
        }

        public bool Open()
        {
            if (open) return true;
            if (!FinikProfileStore.TryLoad(out _)) return false;
            FinikGame.EnsureJourney();

            open = true;
            // One menu at a time: whatever else is up closes before this one takes the room.
            FinikScreens.CloseOthers(this);
            if (hudRoot) hudRoot.SetActive(false);
            foreach (var behaviour in pauseWhileOpen) if (behaviour) behaviour.enabled = false;
            if (movement) movement.BeginActivity(this);
            if (showcase) showcase.Engage();

            if (FinikGame.HasBudgetPlan) ShowFact();
            else ShowPlan();
            return true;
        }

        public void Close()
        {
            if (!open) return;
            open = false;
            if (plan) plan.Hide();
            if (fact) fact.Hide();
            // Another menu took over while this one was folding away: it already owns the HUD, the
            // camera and Finik, so hand nothing back — only let go of what belongs to this screen.
            // This menu's own hold on Finik goes whoever takes the room next; the new menu holds its own.
            if (movement) movement.EndActivity(this);
            string message = growthMessageAfterClose;
            growthMessageAfterClose = null;
            if (FinikScreens.RoomTakenOver(this)) return;
            foreach (var behaviour in pauseWhileOpen) if (behaviour) behaviour.enabled = true;
            if (showcase) showcase.Release();
            if (hudRoot) hudRoot.SetActive(true);
            if (!string.IsNullOrEmpty(message)) FinikPetPopup.ShowMessage(FinikTypography.Fix(message));
        }

        // ------------------------------------------------------------------ plan

        void ShowPlan()
        {
            var suggested = FinikGame.SuggestedBudget();
            income = suggested.income;
            draft[0] = suggested.needs;
            draft[1] = suggested.wants;
            draft[2] = suggested.savings;
            if (fact) fact.Hide();
            if (plan) plan.Show();
            RenderPlan();
        }

        void UseSuggestion()
        {
            var suggested = FinikGame.SuggestedBudget();
            income = suggested.income;
            draft[0] = suggested.needs;
            draft[1] = suggested.wants;
            draft[2] = suggested.savings;
            RenderPlan();
        }

        int Unplanned => income - draft[0] - draft[1] - draft[2];

        void Adjust(int index, int delta)
        {
            if (index < 0 || index >= draft.Length) return;
            // A bucket can only take coins that are still unplanned, and can never go below zero.
            int next = Mathf.Clamp(draft[index] + delta, 0, draft[index] + Mathf.Max(0, Unplanned));
            if (next == draft[index]) return;
            draft[index] = next;
            RenderPlan();
        }

        void RenderPlan()
        {
            if (incomeCounter) incomeCounter.SetValue(income, animate: false);

            for (int i = 0; i < Buckets.Length; i++)
            {
                if (i < envelopeAmounts.Length && envelopeAmounts[i]) envelopeAmounts[i].text = draft[i].ToString();
                if (i < minusButtons.Length && minusButtons[i]) minusButtons[i].interactable = draft[i] > 0;
                if (i < plusButtons.Length && plusButtons[i]) plusButtons[i].interactable = Unplanned > 0;
            }

            int left = Unplanned;
            if (unplannedLabel)
                unplannedLabel.text = left > 0 ? $"Без задачи: {left}" : "У каждой монеты есть задача";

            if (planHint)
                planHint.text = income <= 0
                    ? "Монет пока нет. Выполни квест — и возвращайся составить план."
                    : left > 0
                        ? "Разложи все монеты по трём конвертам: нужное, желания, копилка."
                        : "План готов. Дальше трать из конвертов, а вечером нажми «Завершить день».";

            bool canConfirm = income > 0 && left == 0;
            if (confirmButton) confirmButton.interactable = canConfirm;
            if (confirmLabel)
                confirmLabel.text = income <= 0 ? "Нет монет" : canConfirm ? "Так и сделаем" : $"Осталось {left}";
            if (suggestButton) suggestButton.interactable = income > 0;
        }

        void Confirm()
        {
            if (FinikGame.ConfirmBudget(draft[0], draft[1], draft[2]) != FinikBudgetFailure.None)
            {
                RenderPlan();
                return;
            }
            ShowFact();
        }

        // ------------------------------------------------------------------ plan vs fact

        void ShowFact()
        {
            if (plan) plan.Hide();
            if (fact) fact.Show();
            RenderFact();
        }

        void RenderFact()
        {
            var summary = FinikGame.BudgetSummary();
            for (int i = 0; i < Buckets.Length; i++)
            {
                var row = i < summary.rows.Count ? summary.rows[i] : default;
                if (i < factLabels.Length && factLabels[i]) factLabels[i].text = FinikFoodCatalog.BucketLabel(Buckets[i]);
                if (i < factValues.Length && factValues[i]) factValues[i].text = $"План {row.planned} / факт {row.actual}";
                if (i < factBars.Length && factBars[i])
                    factBars[i].SetValue(row.planned > 0 ? Mathf.Clamp01((float)row.actual / row.planned) : 0f, animate: false);
            }
            var review = FinikGame.TodayReview();
            if (factConclusion)
                factConclusion.text = review == null
                    ? summary.Conclusion + "\nКогда всё купишь и отложишь, нажми «Завершить день» — посчитаем очки роста."
                    : FinikDayReviewEngine.Describe(review);
            if (endDayButton) endDayButton.gameObject.SetActive(review == null);
        }

        /// <summary>
        /// «Завершить день»: today's plan against today's facts becomes growth points. The card then
        /// shows what counted, and the pet says so in the room if it grew.
        /// </summary>
        void EndDay()
        {
            int stageBefore = FinikGame.CharacterStage;
            if (FinikGame.CloseBudgetDay() == null) return;
            if (FinikGame.CharacterStage > stageBefore)
                growthMessageAfterClose = FinikQuestText.Resolve(
                    "*Имя персонажа* подрос! Ты покупал нужное, держался плана и откладывал в копилку.");
            RenderFact();
        }
    }
}
