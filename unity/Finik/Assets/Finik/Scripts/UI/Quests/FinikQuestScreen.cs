using System;
using System.Collections;
using System.Collections.Generic;
using Finik.Core;
using Finik.Navigation;
using Finik.UI.Onboarding;
using Finik.UI.Shop;
using Finik.UI.Savings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Quests
{
    /// <summary>
    /// Financial quests (port of app/tasks.tsx and app/task.tsx). Opens when Finik walks to the study
    /// desk (or from «+» at the coins): a board with today's quests and the weekly streak; a quest
    /// shows a life situation and is answered with a choice or a small hands-on task; the answer is
    /// paid at once and explained on the result card.
    /// </summary>
    public sealed class FinikQuestScreen : MonoBehaviour, IFinikScreen
    {
        public const string DeskInteraction = "study-desk";

        enum Stage { Board, Quest, Result }

        [Serializable]
        public struct IconEntry
        {
            public string name;
            public Sprite sprite;
        }

        [Header("Board")]
        [SerializeField] FinikScreenPanel board;
        [SerializeField] FinikCounterText boardCoins;
        [SerializeField] FinikWeeklyStreakView weekly;
        [SerializeField] FinikQuestRowView[] rows = Array.Empty<FinikQuestRowView>();
        [SerializeField] TMP_Text boardEmpty;
        [SerializeField] Button boardClose;
        [SerializeField] FinikConfettiBurst boardConfetti;

        [Header("Quest")]
        [SerializeField] FinikScreenPanel quest;
        [SerializeField] Image questIcon;
        [SerializeField] TMP_Text questTag;
        [SerializeField] TMP_Text questTitle;
        [SerializeField] TMP_Text questDescription;
        [SerializeField] FinikCounterText questCoins;
        [Tooltip("One quiet line under the task: what to do, or why the attempt was not right yet.")]
        [SerializeField] TMP_Text questHint;
        [SerializeField] GameObject choicesRoot;
        [SerializeField] FinikQuestOptionView[] options = Array.Empty<FinikQuestOptionView>();
        [SerializeField] FinikRedFlagsView redFlags;
        [SerializeField] FinikOddsView odds;
        [SerializeField] FinikNeedOrWantView needOrWant;

        [Header("Quest buttons")]
        [SerializeField] Button backButton;
        [SerializeField] TMP_Text backLabel;
        [Tooltip("The catcher beside the quest card; it follows whatever the left button currently means.")]
        [SerializeField] FinikScrim questScrim;
        [Tooltip("Layout slot of the primary button; hidden as a whole so the row re-centres.")]
        [SerializeField] GameObject primaryRoot;
        [SerializeField] Button primaryButton;
        [SerializeField] TMP_Text primaryLabel;

        [SerializeField] FinikQuestResultView result;

        [Header("Colors")]
        [SerializeField] Color hintColor = new(0.36f, 0.41f, 0.62f, 1f);
        [SerializeField] Color warningColor = new(0.86f, 0.24f, 0.28f, 1f);

        [Header("Icons")]
        [SerializeField] IconEntry[] icons = Array.Empty<IconEntry>();
        [SerializeField] Sprite fallbackIcon;

        [Header("World")]
        [SerializeField] FinikShowcaseCamera showcase;
        [SerializeField] FinikMovementController movement;
        [SerializeField] FinikActivityController activity;
        [SerializeField] Behaviour[] pauseWhileOpen = Array.Empty<Behaviour>();
        [SerializeField] GameObject hudRoot;

        readonly Dictionary<string, Sprite> iconsByName = new(StringComparer.Ordinal);
        Stage stage;
        bool open;
        bool closing;
        FinikQuestTask current;
        FinikInteractionController pendingInteraction;
        Func<FinikInteractionController, string, bool> interactionHandler;
        // Primary button of the odds quest: before the reveal it opens the boxes, after it is the safe choice.
        bool oddsDecision;
        int currentQuestionIndex;
        bool resultHasNextQuestion;
        FinikShopCategory? pendingShopCategory;
        string abandonedQuestId;
        string abandonedQuestTitle;

        public bool IsOpen => open;

        /// <summary>This menu takes the room over: the HUD goes away and Finik stands still.</summary>
        public bool HoldsRoom => true;

        void Awake()
        {
            foreach (var entry in icons)
                if (!string.IsNullOrEmpty(entry.name) && entry.sprite) iconsByName[entry.name] = entry.sprite;
            foreach (var row in rows)
                if (row) row.Clicked += OnRowClicked;
            foreach (var option in options)
                if (option) option.Picked += view => Resolve(view.ChoiceId);
            if (weekly && weekly.ClaimButton) weekly.ClaimButton.onClick.AddListener(ClaimWeekly);
            if (redFlags) redFlags.Changed += OnFlagsChanged;
            if (needOrWant) needOrWant.Picked += OnEnvelopePicked;
            if (boardClose) boardClose.onClick.AddListener(Close);
            if (backButton) backButton.onClick.AddListener(OnBack);
            if (primaryButton) primaryButton.onClick.AddListener(OnPrimary);
            if (result)
            {
                if (result.MoreButton) result.MoreButton.onClick.AddListener(OnResultMore);
                if (result.CloseButton) result.CloseButton.onClick.AddListener(Close);
                if (result.Panel) result.Panel.Hide(instant: true);
            }
            if (quest) quest.Hide(instant: true);
            if (board) board.Hide(instant: true);
            interactionHandler = HandleInteraction;
            FinikInteractionController.RegisterHandler(interactionHandler);
            FinikQuestReminderController.Ensure(activity, movement);
        }

        void OnDestroy()
        {
            FinikScreens.Unregister(this);
            FinikInteractionController.UnregisterHandler(interactionHandler);
        }

        void OnEnable() => FinikScreens.Register(this);

        void OnDisable() => FinikScreens.Unregister(this);

        bool HandleInteraction(FinikInteractionController controller, string interactionId)
        {
            if (interactionId != DeskInteraction || open) return false;
            if (!FinikGame.HasSelectedGoal)
            {
                controller.CompleteInteraction(interactionId);
                var savings = FindFirstObjectByType<FinikSavingsScreen>(FindObjectsInactive.Include);
                if (savings) savings.Open();
                return true;
            }
            if (!Open()) return false;
            pendingInteraction = controller;
            return true;
        }

        /// <summary>Lets the desk Finik walked to know the visit is over, wherever the close ended.</summary>
        void HandBackInteraction()
        {
            if (!pendingInteraction) return;
            pendingInteraction.CompleteInteraction(DeskInteraction);
            pendingInteraction = null;
        }

        // ------------------------------------------------------------------ open / close

        /// <summary>Opens the quest board. False when there is no profile yet.</summary>
        public bool Open()
        {
            if (open) return true;
            if (!FinikProfileStore.TryLoad(out _)) return false;
            FinikGame.EnsureJourney();
            if (!FinikGame.HasSelectedGoal)
            {
                var savings = FindFirstObjectByType<FinikSavingsScreen>(FindObjectsInactive.Include);
                if (savings) savings.Open();
                return false;
            }

            open = true;
            closing = false;
            // Returning to Tasks is exactly what the reminder asked for.
            FinikQuestReminderController.Clear();
            abandonedQuestId = null;
            abandonedQuestTitle = null;
            // One menu at a time: whatever else is up closes before this one takes the room.
            FinikScreens.CloseOthers(this);
            pendingShopCategory = null;
            if (hudRoot) hudRoot.SetActive(false);
            foreach (var behaviour in pauseWhileOpen) if (behaviour) behaviour.enabled = false;
            if (movement) movement.BeginActivity(this);
            if (showcase) showcase.Engage();
            ShowBoard();
            FinikAudioManager.Instance.PlayAssistant(FinikAudioManager.VoiceQuestBoard, interruptCurrent: true);
            return true;
        }

        public void Close()
        {
            if (!open || closing) return;
            if (current?.Quest.Day == 1 && stage != Stage.Board && !pendingShopCategory.HasValue) return;
            if (stage == Stage.Board && DayOneSequenceActive()) return;
            ArmQuestReminderIfNeeded();
            closing = true;
            board.Hide();
            quest.Hide();
            if (result && result.Panel) result.Panel.Hide();
            StartCoroutine(CloseRoutine());
        }

        IEnumerator CloseRoutine()
        {
            // A menu takes a moment to close — the camera fly-back alone is about a second — and
            // another one can take over at any point during it. Whoever is open owns the HUD, the
            // camera and Finik, so this screen re-checks after every wait and hands back only what
            // is still its own.
            if (TakenOver()) yield break;

            bool released = false;
            if (showcase) showcase.Release(() => released = true);
            else released = true;
            while (!released) yield return null;
            if (TakenOver()) yield break;

            if (hudRoot) hudRoot.SetActive(true);
            HandBackInteraction();
            // Let the celebration clip finish before Finik may walk again.
            while (activity && activity.IsBusy) yield return null;
            if (TakenOver()) yield break;

            foreach (var behaviour in pauseWhileOpen) if (behaviour) behaviour.enabled = true;
            if (movement) movement.EndActivity(this);

            var shopCategory = pendingShopCategory;
            pendingShopCategory = null;
            open = false;
            closing = false;

            // The board's own hand-over to the shop: this screen is already closed, so the shop takes
            // the room cleanly rather than stacking on top of it.
            if (shopCategory.HasValue)
            {
                var shop = FindFirstObjectByType<FinikShopScreen>(FindObjectsInactive.Include);
                if (shop) shop.Open(shopCategory.Value);
            }
        }

        /// <summary>
        /// True once another menu holds the room. The close then stops where it is: this screen is no
        /// longer open, but the HUD, the camera and Finik stay exactly as the new menu set them.
        /// </summary>
        bool TakenOver()
        {
            if (!FinikScreens.RoomTakenOver(this)) return false;
            // The HUD and the camera belong to the new menu now; this menu's own hold on Finik does not.
            if (movement) movement.EndActivity(this);
            HandBackInteraction();
            pendingShopCategory = null;
            open = false;
            closing = false;
            return true;
        }

        // ------------------------------------------------------------------ board

        void ShowBoard()
        {
            if (closing) return;
            stage = Stage.Board;
            current = null;
            if (result && result.Panel) result.Panel.Hide();
            quest.Hide();
            RenderBoard(animate: false);
            board.Show();
        }

        void RenderBoard(bool animate)
        {
            var tasks = FinikGame.QuestTasks();
            if (boardClose) boardClose.gameObject.SetActive(!DayOneSequenceActive());
            if (boardCoins) boardCoins.SetValue(FinikGame.Balance, animate);
            if (weekly) weekly.gameObject.SetActive(false);
            int row = 0;
            foreach (var task in tasks)
            {
                if (task.IsWeekly)
                {
                    if (weekly)
                    {
                        weekly.gameObject.SetActive(true);
                        weekly.Show(task, StatusOf(task), FinikGame.WeeklyQuestsClaimed, animate);
                    }
                    continue;
                }
                if (row >= rows.Length) continue;
                rows[row].gameObject.SetActive(true);
                rows[row].Show(task, StatusOf(task), IconFor(task.Quest.Icon), !FinikGame.IsQuestUnlocked(task.Id));
                row++;
            }
            for (int i = row; i < rows.Length; i++) if (rows[i]) rows[i].gameObject.SetActive(false);
            if (boardEmpty) boardEmpty.gameObject.SetActive(row == 0);
        }

        void OnRowClicked(FinikQuestRowView row)
        {
            if (stage != Stage.Board || closing) return;
            var task = FindTask(row.TaskId);
            if (task == null) return;
            switch (StatusOf(task))
            {
                case FinikQuestStatus.Completed:
                    // Answered but not paid (e.g. the app closed in between): collect now (web: tasks.tsx openDaily).
                    FinikGame.ClaimQuest(task.Id);
                    RenderBoard(animate: true);
                    break;
                case FinikQuestStatus.Claimed:
                    break;
                default:
                    if (FinikGame.StartQuest(task.Id)) ShowQuest(task);
                    break;
            }
        }

        void ClaimWeekly()
        {
            if (stage != Stage.Board || closing) return;
            foreach (var task in FinikGame.QuestTasks())
            {
                if (!task.IsWeekly) continue;
                var outcome = FinikGame.ClaimQuest(task.Id);
                RenderBoard(animate: true);
                if (outcome.Ok)
                {
                    if (boardConfetti) boardConfetti.Burst(Vector2.zero);
                    if (activity) activity.PlayQuestDance();
                }
            }
        }

        // ------------------------------------------------------------------ quest

        void ShowQuest(FinikQuestTask task)
        {
            abandonedQuestId = null;
            abandonedQuestTitle = null;
            stage = Stage.Quest;
            current = task;
            int maxQuestion = Math.Max(0, task.Quest.Questions.Length - 1);
            currentQuestionIndex = Math.Clamp(FinikGame.QuestProgress(task.Id)?.questionIndex ?? 0, 0, maxQuestion);
            resultHasNextQuestion = false;
            oddsDecision = false;
            RenderCurrentQuestion();
            board.Hide();
            quest.Show();
        }

        void RenderCurrentQuestion()
        {
            if (current?.Quest == null) return;
            var q = current.Quest;
            var question = q.Question(currentQuestionIndex);
            if (questIcon) questIcon.sprite = IconFor(q.Icon);
            if (questTag)
            {
                string rewardPart = current.Reward > 0 ? $" · +{current.Reward}" : string.Empty;
                questTag.text = $"КВЕСТ · ВОПРОС {currentQuestionIndex + 1}/{q.Questions.Length}{rewardPart}";
            }
            if (questTitle) questTitle.text = FinikTypography.Fix(FinikQuestText.Resolve(q.Title));
            string body = currentQuestionIndex == 0
                ? $"{q.Description}\n\n{question?.Prompt}"
                : question?.Prompt ?? q.Description;
            if (questDescription) questDescription.text = FinikTypography.Fix(FinikQuestText.Resolve(body));
            if (questCoins) questCoins.SetValue(FinikGame.Balance, animate: false);
            if (q.Day <= 5)
                FinikAudioManager.Instance.PlayQuestVoice($"{q.Id}_q{currentQuestionIndex + 1}_prompt");

            var type = q.Mechanic.Type;
            if (choicesRoot) choicesRoot.SetActive(type == FinikQuestMechanicType.Choices);
            if (redFlags) redFlags.gameObject.SetActive(type == FinikQuestMechanicType.RedFlags);
            if (odds) odds.gameObject.SetActive(type == FinikQuestMechanicType.Odds);
            if (needOrWant) needOrWant.gameObject.SetActive(type == FinikQuestMechanicType.NeedOrWant);
            switch (type)
            {
                case FinikQuestMechanicType.Choices:
                    var choices = question?.Choices ?? Array.Empty<FinikQuestChoice>();
                    var displayChoices = (FinikQuestChoice[])choices.Clone();
                    if (displayChoices.Length == 2 && UnityEngine.Random.value < 0.5f)
                        (displayChoices[0], displayChoices[1]) = (displayChoices[1], displayChoices[0]);
                    for (int i = 0; i < options.Length; i++)
                    {
                        if (!options[i]) continue;
                        bool used = i < displayChoices.Length;
                        options[i].gameObject.SetActive(used);
                        if (used) options[i].Show(displayChoices[i], i);
                    }
                    break;
                case FinikQuestMechanicType.RedFlags:
                    redFlags.Show(q.Mechanic);
                    break;
                case FinikQuestMechanicType.Odds:
                    odds.Show(q.Mechanic);
                    break;
                case FinikQuestMechanicType.NeedOrWant:
                    needOrWant.Show(q.Mechanic);
                    break;
            }
            RenderQuestFooter();
            SetHint(null);
        }

        /// <summary>The footer depends on the task: back only, back + check, back + reveal, or the two odds decisions.</summary>
        void RenderQuestFooter()
        {
            var mechanic = current.Quest.Mechanic;
            bool lockedDayOne = current.Quest.Day == 1;
            string back = "Другие квесты";
            string primary = null;
            bool primaryEnabled = true;
            switch (mechanic.Type)
            {
                case FinikQuestMechanicType.RedFlags:
                    back = "Назад";
                    primary = mechanic.Action;
                    primaryEnabled = redFlags && redFlags.ReadyToCheck;
                    break;
                case FinikQuestMechanicType.Odds:
                    back = oddsDecision ? mechanic.RiskLabel : "Назад";
                    primary = oddsDecision ? mechanic.SuccessLabel : mechanic.Action;
                    break;
            }
            if (backLabel) backLabel.text = FinikTypography.Fix(back);
            if (backButton) backButton.gameObject.SetActive(!lockedDayOne || mechanic.Type == FinikQuestMechanicType.Odds && oddsDecision);
            // In the odds task the left button stops meaning «назад» once the boxes are open: it becomes the
            // risky answer. A tap beside the card must never spend the child's coins, so the catcher
            // goes dead for that one state instead of following the button.
            if (questScrim)
                questScrim.Action = lockedDayOne || mechanic.Type == FinikQuestMechanicType.Odds && oddsDecision ? null : backButton;
            var slot = primaryRoot ? primaryRoot : primaryButton ? primaryButton.gameObject : null;
            if (slot) slot.SetActive(primary != null);
            if (primaryLabel && primary != null) primaryLabel.text = FinikTypography.Fix(primary);
            if (primaryButton) primaryButton.interactable = primaryEnabled;
        }

        void SetHint(string warning)
        {
            if (!questHint || current?.Quest == null) return;
            var q = current.Quest;
            var question = q.Question(currentQuestionIndex);
            string normal = !string.IsNullOrEmpty(question?.Hint) ? question.Hint
                : !string.IsNullOrEmpty(q.Mechanic.Hint) ? q.Mechanic.Hint
                : q.IsInteractive ? "Тут надо что-то сделать."
                : "Выбери решение и посмотри, к чему оно приведёт.";
            string text = warning ?? normal;
            questHint.text = FinikTypography.Fix(FinikQuestText.Resolve(text));
            questHint.color = warning != null ? warningColor : hintColor;
        }

        void OnBack()
        {
            if (stage != Stage.Quest || closing) return;
            if (current.Quest.Mechanic.Type == FinikQuestMechanicType.Odds && oddsDecision)
            {
                // After the reveal the left button is the risky answer, not «back».
                Resolve(current.Quest.Mechanic.RiskChoiceId);
                return;
            }
            if (current.Quest.Day == 1) return;
            RememberAbandonedQuest(current);
            ShowBoard();
        }

        bool DayOneSequenceActive()
        {
            foreach (var task in FinikGame.QuestTasks())
            {
                if (task.IsWeekly || task.Quest?.Day != 1) continue;
                if (StatusOf(task) != FinikQuestStatus.Claimed) return true;
            }
            return false;
        }

        void OnPrimary()
        {
            if (stage != Stage.Quest || closing) return;
            var mechanic = current.Quest.Mechanic;
            switch (mechanic.Type)
            {
                case FinikQuestMechanicType.RedFlags:
                    if (!redFlags.ReadyToCheck) return;
                    if (redFlags.IsCorrect()) Resolve(mechanic.SuccessChoiceId);
                    else
                    {
                        SetHint(mechanic.Retry);
                        FinikAudioManager.Instance.PlayAssistant(FinikAudioManager.VoiceRetryAgain);
                    }
                    break;
                case FinikQuestMechanicType.Odds:
                    if (!oddsDecision)
                    {
                        odds.Reveal();
                        oddsDecision = true;
                        RenderQuestFooter();
                    }
                    else Resolve(mechanic.SuccessChoiceId);
                    break;
            }
        }

        void OnFlagsChanged()
        {
            if (stage != Stage.Quest) return;
            SetHint(null);
            RenderQuestFooter();
        }

        void OnEnvelopePicked(FinikBudgetBucket bucket)
        {
            if (stage != Stage.Quest || closing) return;
            var mechanic = current.Quest.Mechanic;
            if (bucket == mechanic.Answer) Resolve(mechanic.SuccessChoiceId);
            else
            {
                SetHint(mechanic.Retry);
                FinikAudioManager.Instance.PlayAssistant(FinikAudioManager.VoiceRetryAgain);
            }
        }

        // ------------------------------------------------------------------ result

        void Resolve(string choiceId)
        {
            if (stage != Stage.Quest || current?.Quest == null || closing) return;
            var q = current.Quest;
            var question = q.Question(currentQuestionIndex);
            var choice = question?.Choice(choiceId) ?? q.Choice(choiceId);
            if (choice == null)
            {
                ShowBoard();
                return;
            }

            if (choice.Save > 0 && !FinikGame.HasSelectedGoal)
            {
                SetHint("Сначала выбери цель в копилке.");
                return;
            }
            if (!FinikGame.ApplyQuestChoiceEffect(current.Id, currentQuestionIndex, choice))
            {
                SetHint("Для этого решения сейчас не хватает монет. Выбери другой вариант.");
                return;
            }

            bool hasNext = currentQuestionIndex + 1 < q.Questions.Length;
            stage = Stage.Result;
            quest.Hide();
            var avatar = hudRoot && hudRoot.TryGetComponent<FinikHudView>(out var hud) ? hud.AvatarSprite : null;
            if (hasNext)
            {
                if (!FinikGame.AdvanceQuestQuestion(current.Id, currentQuestionIndex + 1, choice.Id))
                {
                    ShowBoard();
                    return;
                }
                resultHasNextQuestion = true;
                if (result)
                {
                    result.ShowStep(current, choice, currentQuestionIndex + 1, q.Questions.Length, IconFor(q.Icon), avatar);
                    result.SetRoomExitVisible(q.Day != 1);
                    FinikAudioManager.Instance.PlayQuestVoice($"{q.Id}_q{currentQuestionIndex + 1}_{choice.Id}");
                }
                return;
            }

            resultHasNextQuestion = false;

            var outcome = FinikGame.ResolveQuest(current.Id, choiceId);
            if (!outcome.Ok)
            {
                ShowBoard();
                return;
            }

            // Purchases are part of the learning flow: after day 1 and after «Умная покупка» on day 2,
            // return straight to the shop instead of making the child navigate there manually.
            if (q.Id == "d1-first-purchases" || q.Id == "d2-smart-purchase")
                pendingShopCategory = FinikShopCategory.Need;

            if (result)
            {
                result.Show(outcome, FinikGame.WeeklyQuestsClaimed, null, IconFor(q.Icon), avatar,
                    pendingShopCategory.HasValue && q.Day == 1);
                result.SetRoomExitVisible(q.Day != 1);
                FinikAudioManager.Instance.PlayQuestVoice($"{q.Id}_q{currentQuestionIndex + 1}_{choice.Id}");
            }
        }

        void OnResultMore()
        {
            if (stage != Stage.Result || closing) return;
            if (!resultHasNextQuestion)
            {
                if (pendingShopCategory.HasValue) Close();
                else ShowBoard();
                return;
            }

            resultHasNextQuestion = false;
            if (result && result.Panel) result.Panel.Hide();
            currentQuestionIndex++;
            oddsDecision = false;
            stage = Stage.Quest;
            RenderCurrentQuestion();
            quest.Show();
        }

        void RememberAbandonedQuest(FinikQuestTask task)
        {
            if (task?.Quest == null) return;
            var progress = FinikGame.QuestProgress(task.Id);
            if (progress == null || progress.status != FinikQuestStatus.Active) return;
            abandonedQuestId = task.Id;
            abandonedQuestTitle = FinikQuestText.Resolve(task.Quest.Title);
        }

        void ArmQuestReminderIfNeeded()
        {
            if (current != null)
                RememberAbandonedQuest(current);

            if (string.IsNullOrWhiteSpace(abandonedQuestId)) return;
            var progress = FinikGame.QuestProgress(abandonedQuestId);
            if (progress == null || progress.status != FinikQuestStatus.Active)
            {
                abandonedQuestId = null;
                abandonedQuestTitle = null;
                return;
            }

            FinikQuestReminderController.Arm(abandonedQuestId, abandonedQuestTitle);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && open) ArmQuestReminderIfNeeded();
        }

        void OnApplicationQuit()
        {
            if (open) ArmQuestReminderIfNeeded();
        }

        // ------------------------------------------------------------------ helpers

        static FinikQuestStatus StatusOf(FinikQuestTask task) => FinikGame.QuestProgress(task.Id)?.status ?? FinikQuestStatus.Available;

        static FinikQuestTask FindTask(string taskId)
        {
            foreach (var task in FinikGame.QuestTasks())
                if (task.Id == taskId) return task;
            return null;
        }

        /// <summary>
        /// Fills the icon lookup the first time it is needed. Not left to Awake: the editor runs
        /// with domain and scene reload disabled, and a screen that was already in the scene can
        /// reach Play without its Awake — which showed every item on this screen as the fallback
        /// placeholder instead of its own picture.
        /// </summary>
        void EnsureIcons()
        {
            if (iconsByName.Count > 0 || icons.Length == 0) return;
            foreach (var entry in icons)
                if (!string.IsNullOrEmpty(entry.name) && entry.sprite) iconsByName[entry.name] = entry.sprite;
        }

        Sprite IconFor(string name)
        {
            EnsureIcons();
            return iconsByName.TryGetValue(name, out var sprite) ? sprite : fallbackIcon;
        }
    }
}
