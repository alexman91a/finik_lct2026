using System;
using System.Collections;
using System.Collections.Generic;
using Finik.Core;
using Finik.Navigation;
using Finik.UI.Onboarding;
using Finik.UI.Quests;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Savings
{
    /// <summary>
    /// The piggy bank: one financial goal in the middle («Коплю на велосипед», its picture, 200 / 500,
    /// the bar and what is left), putting coins aside as the main action, switching to another goal, and
    /// taking coins back behind a confirmation that shows how the progress drops. The bar previews the
    /// chosen amount before it is saved, so "put aside now → closer to the goal" is visible up front.
    /// </summary>
    public sealed class FinikSavingsScreen : MonoBehaviour, IFinikScreen
    {
        [Serializable]
        public struct IconEntry
        {
            public string name;
            public Sprite sprite;
        }

        [Header("Main")]
        [SerializeField] FinikScreenPanel main;
        [SerializeField] FinikCounterText walletCoins;
        [SerializeField] Image goalImage;
        [SerializeField] FinikPressFeedback goalPunch;
        [SerializeField] TMP_Text title;
        [SerializeField] FinikCounterText savedCounter;
        [SerializeField] TMP_Text targetLabel;
        [SerializeField] FinikFillBar bar;
        [Tooltip("Pale bar under the main one: where the progress lands after saving the chosen amount.")]
        [SerializeField] FinikFillBar previewBar;
        [SerializeField] TMP_Text leftLabel;
        [SerializeField] TMP_Text hint;
        [SerializeField] GameObject amountsRoot;
        [SerializeField] FinikChoiceItem[] amounts = Array.Empty<FinikChoiceItem>();
        [SerializeField] TMP_Text[] amountLabels = Array.Empty<TMP_Text>();
        [SerializeField] Button saveButton;
        [SerializeField] TMP_Text saveLabel;
        [Tooltip("Face of the save button while there is nothing to save. Empty leaves it green (V1).")]
        [SerializeField] Image saveFace;
        [SerializeField] Sprite disabledFace;
        [SerializeField] Color disabledLabelColor = Color.white;
        [SerializeField] Button changeGoalButton;
        [SerializeField] GameObject withdrawRoot;
        [SerializeField] Button withdrawButton;
        [SerializeField] Button closeButton;
        [SerializeField] FinikConfettiBurst confetti;

        [Header("Goal picker")]
        [SerializeField] FinikScreenPanel goals;
        [SerializeField] FinikChoiceItem[] goalCards = Array.Empty<FinikChoiceItem>();
        [SerializeField] Image[] goalCardImages = Array.Empty<Image>();
        [SerializeField] TMP_Text[] goalCardTitles = Array.Empty<TMP_Text>();
        [SerializeField] TMP_Text[] goalCardPrices = Array.Empty<TMP_Text>();
        [SerializeField] Button goalsBack;

        [Header("Take back")]
        [SerializeField] FinikScreenPanel confirm;
        [SerializeField] TMP_Text confirmTitle;
        [SerializeField] TMP_Text confirmBody;
        [SerializeField] FinikFillBar confirmBefore;
        [SerializeField] FinikFillBar confirmAfter;
        [SerializeField] TMP_Text confirmProgress;
        [SerializeField] Button keepButton;
        [SerializeField] Button takeButton;
        [SerializeField] TMP_Text takeLabel;

        FinikScreenPanel reward;
        Image rewardGoalImage;
        TMP_Text rewardTag;
        TMP_Text rewardTitle;
        TMP_Text rewardBody;
        Button rewardBuyButton;
        TMP_Text rewardBuyLabel;
        FinikConfettiBurst rewardConfetti;
        Coroutine rewardFinishRoutine;
        string rewardMessageAfterClose;

        [Header("Rules")]
        [Tooltip("Whether the child may take coins back out of the piggy bank.")]
        [SerializeField] bool allowWithdraw = true;
        [Tooltip("Amounts offered as chips, in order. Kept in sync by FinikSavingsScreenBuilder.")]
        [SerializeField] int[] amountSteps = { 5, 10, 20 };
        [SerializeField] int defaultStep = 1;

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
        readonly List<int> offered = new();
        int step;
        bool open;
        bool closing;
        bool compactLayoutKnown;
        bool compactLayoutValue;
        bool openQuestsAfterClose;
        bool goalPickerHeightsCaptured;
        float goalPickerLandscapeHeight;
        float goalPickerPortraitHeight;

        public bool IsOpen => open;

        /// <summary>This menu takes the room over: the HUD goes away and Finik stands still.</summary>
        public bool HoldsRoom => true;

        void Awake()
        {
            EnsureRewardPanel();
            foreach (var entry in icons)
                if (!string.IsNullOrEmpty(entry.name) && entry.sprite) iconsByName[entry.name] = entry.sprite;
            step = Mathf.Clamp(defaultStep, 0, Math.Max(0, amountSteps.Length - 1));
            for (int i = 0; i < amounts.Length; i++)
            {
                int index = i;
                if (amounts[i]) amounts[i].Clicked += _ => PickAmount(index);
            }
            for (int i = 0; i < goalCards.Length; i++)
            {
                int index = i;
                if (goalCards[i]) goalCards[i].Clicked += _ => PickGoal(index);
            }
            if (saveButton) saveButton.onClick.AddListener(Save);
            if (changeGoalButton) changeGoalButton.onClick.AddListener(ShowGoals);
            if (withdrawButton) withdrawButton.onClick.AddListener(AskWithdraw);
            if (closeButton) closeButton.onClick.AddListener(Close);
            if (goalsBack) goalsBack.onClick.AddListener(BackFromGoals);
            if (keepButton) keepButton.onClick.AddListener(ShowMain);
            if (takeButton) takeButton.onClick.AddListener(Withdraw);
            if (rewardBuyButton) rewardBuyButton.onClick.AddListener(BuyGoalReward);
            if (main) main.Hide(instant: true);
            if (goals) goals.Hide(instant: true);
            if (confirm) confirm.Hide(instant: true);
            if (reward) reward.Hide(instant: true);
            ApplyCompactLayout(force: true);
        }

        void Update()
        {
            if (open) ApplyCompactLayout(force: false);
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

        void ApplyCompactLayout(bool force)
        {
            bool compact = Mathf.Min(Screen.width, Screen.height) <= 1000;
            if (!force && compactLayoutKnown && compactLayoutValue == compact) return;
            compactLayoutKnown = true;
            compactLayoutValue = compact;
            if (!hint) return;

            var content = hint.transform.parent as RectTransform;
            if (!content) return;
            SetLayoutHeight(hint.transform, compact ? 88f : 64f);
            SetLayoutHeight(content.Find("Hero"), compact ? 280f : 300f);
            SetLayoutHeight(content.Find("Amounts"), compact ? 126f : 130f);

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        }

        static void SetLayoutHeight(Transform target, float height)
        {
            if (!target) return;
            var layout = target.GetComponent<LayoutElement>();
            if (!layout) return;
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleHeight = 0f;
        }

        // Coins can arrive while the screen is open (e.g. a debug menu); keep the numbers honest.
        void OnGameChanged()
        {
            if (open && !closing) Render(animate: true);
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

        public Sprite IconFor(FinikGoal goal)
        {
            EnsureIcons();
            return goal != null && iconsByName.TryGetValue(goal.Icon, out var sprite) ? sprite : fallbackIcon;
        }

        // ------------------------------------------------------------------ open / close

        /// <summary>Opens the piggy bank. False when there is no profile yet.</summary>
        public bool Open()
        {
            if (open) return true;
            if (!FinikProfileStore.TryLoad(out _)) return false;
            FinikGame.EnsureJourney();

            open = true;
            closing = false;
            ApplyCompactLayout(force: false);
            // One menu at a time: whatever else is up closes before this one takes the room.
            FinikScreens.CloseOthers(this);
            if (hudRoot) hudRoot.SetActive(false);
            foreach (var behaviour in pauseWhileOpen) if (behaviour) behaviour.enabled = false;
            if (movement) movement.BeginActivity(this);
            if (showcase) showcase.Engage();
            step = Mathf.Clamp(defaultStep, 0, Math.Max(0, amountSteps.Length - 1));
            Render(animate: false);
            if (FinikGame.CanPurchaseCurrentGoal) ShowReward();
            else if (FinikGame.HasSelectedGoal) ShowMain();
            else ShowGoals();
            return true;
        }

        public void Close()
        {
            if (!open || closing) return;
            closing = true;
            main.Hide();
            goals.Hide();
            confirm.Hide();
            if (reward) reward.Hide();
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
            while (activity && activity.IsBusy) yield return null;
            if (TakenOver()) yield break;

            foreach (var behaviour in pauseWhileOpen) if (behaviour) behaviour.enabled = true;
            if (movement) movement.EndActivity(this);
            open = false;
            closing = false;

            if (!string.IsNullOrEmpty(rewardMessageAfterClose))
            {
                FinikPetPopup.ShowMessage(rewardMessageAfterClose);
                rewardMessageAfterClose = null;
            }

            if (openQuestsAfterClose)
            {
                openQuestsAfterClose = false;
                var quests = FindFirstObjectByType<FinikQuestScreen>(FindObjectsInactive.Include);
                if (quests && quests.Open())
                    FinikPetPopup.ShowMessage($"Выполняй задания, чтобы купить {FinikGame.Goal.SavingFor}");
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
            openQuestsAfterClose = false;
            open = false;
            closing = false;
            return true;
        }

        void ShowMain()
        {
            if (closing) return;
            if (!FinikGame.HasSelectedGoal)
            {
                ShowGoals();
                return;
            }
            goals.Hide();
            confirm.Hide();
            if (reward) reward.Hide();
            main.Show();
        }

        void BackFromGoals()
        {
            if (FinikGame.HasSelectedGoal) ShowMain();
            else Close();
        }

        // ------------------------------------------------------------------ main

        int SelectedAmount => offered.Count == 0 ? 0 : offered[Mathf.Clamp(step, 0, offered.Count - 1)];

        void Render(bool animate)
        {
            var progress = FinikGame.GoalProgress;
            var goal = progress.goal;
            int balance = FinikGame.Balance;

            bool purchased = FinikGame.CurrentGoalPurchased;
            if (goalImage) goalImage.sprite = IconFor(goal);
            if (walletCoins) walletCoins.SetValue(balance, animate);
            if (title) title.text = purchased ? $"{goal.Title} уже в комнате!" : progress.Reached ? $"Накоплено на {goal.SavingFor}!" : $"Коплю на {goal.SavingFor}";
            if (savedCounter) savedCounter.SetValue(progress.saved, animate);
            if (targetLabel) targetLabel.text = $"/ {progress.Target}";
            if (bar) bar.SetValue(progress.Fraction, animate);
            if (leftLabel) leftLabel.text = purchased ? "Цель куплена!" : progress.Reached ? "Цель накоплена!" : $"Осталось <b>{progress.Left}</b>";

            // Amounts on offer: the steps, never past what is left of the goal, without duplicates.
            offered.Clear();
            foreach (int s in amountSteps)
            {
                int amount = Math.Min(s, progress.Left);
                if (amount > 0 && !offered.Contains(amount)) offered.Add(amount);
            }
            step = Mathf.Clamp(step, 0, Math.Max(0, offered.Count - 1));
            for (int i = 0; i < amounts.Length; i++)
            {
                bool shown = i < offered.Count;
                if (!amounts[i]) continue;
                amounts[i].gameObject.SetActive(shown);
                if (!shown) continue;
                amounts[i].SetSelected(i == step);
                if (i < amountLabels.Length && amountLabels[i]) amountLabels[i].text = $"+{offered[i]}";
            }
            if (amountsRoot) amountsRoot.SetActive(!progress.Reached && !purchased);

            int selected = SelectedAmount;
            bool affordable = selected > 0 && selected <= balance;
            if (previewBar) previewBar.SetValue(progress.Reached ? progress.Fraction : progress.After(affordable ? selected : 0).Fraction, animate);
            bool canSave = !purchased && !progress.Reached && affordable;
            if (saveButton) saveButton.interactable = canSave;
            if (saveLabel)
                saveLabel.text = purchased ? "Цель куплена" : progress.Reached ? "Цель накоплена" : affordable ? $"Положить {selected} в копилку" : "Не хватает монет";
            // A green button that does nothing is a lie: when there is nothing to put aside the
            // button drops to the quiet face, the same way the food screen's does.
            SetSaveLook(canSave);
            if (hint) hint.text = purchased
                ? "Награда уже стоит в комнате. Выбери новую цель!"
                : FinikTypography.Fix(HintFor(progress, selected, balance));

            if (withdrawRoot) withdrawRoot.SetActive(allowWithdraw && progress.saved > 0);
        }

        Sprite enabledFace;
        Color enabledLabelColor;
        bool lookCaptured;

        void SetSaveLook(bool enabled)
        {
            if (!saveFace || !disabledFace || !saveLabel) return;
            if (!lookCaptured)
            {
                enabledFace = saveFace.sprite;
                enabledLabelColor = saveLabel.color;
                lookCaptured = true;
            }
            saveFace.sprite = enabled ? enabledFace : disabledFace;
            saveLabel.color = enabled ? enabledLabelColor : disabledLabelColor;
        }

        static string HintFor(FinikGoalProgress progress, int amount, int balance)
        {
            if (progress.Reached) return "Ура! Можно выбрать новую цель, а монеты останутся в копилке.";
            if (amount <= 0) return string.Empty;
            if (amount > balance)
                return balance > 0
                    ? $"В кошельке {balance} {Coins(balance)}. Выбери сумму поменьше или заработай на заданиях."
                    : "Кошелёк пуст. Монеты можно заработать на заданиях.";
            int after = progress.Left - amount;
            if (after <= 0) return $"Этого хватит: {progress.goal.SavingFor} будет у тебя!";
            int times = (after + amount - 1) / amount;
            return $"Отложишь {amount} — останется {after}. Ещё {times} {Times(times)} по {amount}, и {progress.goal.SavingFor} у тебя!";
        }

        void PickAmount(int index)
        {
            if (index >= offered.Count) return;
            step = index;
            Render(animate: true);
        }

        void Save()
        {
            int amount = SelectedAmount;
            bool wasReached = FinikGame.GoalProgress.Reached;
            if (FinikGame.SaveToGoal(amount) != FinikSavingsFailure.None)
            {
                Render(animate: true);
                return;
            }
            // Render already ran from FinikGame.Changed; celebrate on top of it.
            if (goalPunch) goalPunch.Punch();
            if (!wasReached && FinikGame.GoalProgress.Reached) ShowReward();
        }

        void EnsureRewardPanel()
        {
            if (reward || !confirm) return;

            reward = Instantiate(confirm, confirm.transform.parent);
            reward.name = "Reward";
            reward.Hide(instant: true);

            // The reward image is taller than Confirm's progress bar. Grow the card in both
            // orientations so the purchase button remains inside its raycastable area.
            var rewardColumn = FindDeep(reward.transform, "Column");
            var orientation = rewardColumn ? rewardColumn.GetComponent<FinikOrientationLayout>() : null;
            if (orientation) orientation.AddHeight(138f);

            var header = FindDeep(reward.transform, "Header");
            rewardTag = FindDeep(header, "Tag")?.GetComponent<TMP_Text>();
            rewardTitle = FindDeep(header, "Title")?.GetComponent<TMP_Text>();
            var headerIcon = FindDeep(header, "Icon");
            if (headerIcon) headerIcon.gameObject.SetActive(false);

            rewardBody = FindDeep(reward.transform, "Body")?.GetComponent<TMP_Text>();
            if (rewardBody)
            {
                rewardBody.alignment = TextAlignmentOptions.Center;
                rewardBody.fontSize = 30f;
            }

            var progressBlock = FindDeep(reward.transform, "Progress");
            if (progressBlock)
            {
                foreach (Transform child in progressBlock) child.gameObject.SetActive(false);
                var layout = progressBlock.GetComponent<LayoutElement>();
                if (layout)
                {
                    layout.minHeight = 250f;
                    layout.preferredHeight = 250f;
                }

                var iconGo = new GameObject("RewardGoalIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                iconGo.transform.SetParent(progressBlock, false);
                rewardGoalImage = iconGo.GetComponent<Image>();
                rewardGoalImage.preserveAspect = true;
                rewardGoalImage.raycastTarget = false;
                var iconRect = (RectTransform)iconGo.transform;
                iconRect.anchorMin = iconRect.anchorMax = new Vector2(.5f, .5f);
                iconRect.pivot = new Vector2(.5f, .5f);
                iconRect.sizeDelta = new Vector2(220f, 220f);
                iconRect.anchoredPosition = Vector2.zero;
            }

            var buttons = FindDeep(reward.transform, "Buttons");
            var keep = FindDeep(buttons, "Keep");
            if (keep) keep.gameObject.SetActive(false);

            var buy = FindDeep(buttons, "Take");
            rewardBuyButton = buy ? buy.GetComponentInChildren<Button>(true) : null;
            rewardBuyLabel = FindDeep(buy, "Label")?.GetComponent<TMP_Text>();
            if (rewardBuyButton)
            {
                var layout = rewardBuyButton.GetComponent<LayoutElement>();
                if (layout)
                {
                    layout.minWidth = 520f;
                    layout.preferredWidth = 520f;
                }
            }

            if (confetti)
            {
                var copy = Instantiate(confetti.gameObject, reward.transform);
                copy.name = "RewardConfetti";
                var rect = copy.transform as RectTransform;
                if (rect)
                {
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.zero;
                }
                rewardConfetti = copy.GetComponent<FinikConfettiBurst>();
            }
        }

        static Transform FindDeep(Transform root, string objectName)
        {
            if (!root) return null;
            if (root.name == objectName) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), objectName);
                if (found) return found;
            }
            return null;
        }

        void ShowReward()
        {
            EnsureRewardPanel();
            if (!reward || !FinikGame.CanPurchaseCurrentGoal) return;

            var goal = FinikGame.Goal;
            main.Hide();
            goals.Hide();
            confirm.Hide();

            if (rewardGoalImage) rewardGoalImage.sprite = IconFor(goal);
            if (rewardTag) rewardTag.text = "ЦЕЛЬ ДОСТИГНУТА";
            if (rewardTitle) rewardTitle.text = "Ура! Мы собрали деньги!";
            if (rewardBody)
                rewardBody.text = $"Можем купить <b>{goal.Title}</b>. После покупки награда появится в комнате!";
            if (rewardBuyLabel) rewardBuyLabel.text = $"КУПИТЬ ЗА {goal.Price}";
            if (rewardBuyButton) rewardBuyButton.interactable = true;

            reward.Show();
            FinikAudioManager.Instance.PlayAssistant(FinikAudioManager.VoiceGoalReachedBuy, interruptCurrent: true);
        }

        void BuyGoalReward()
        {
            var goal = FinikGame.Goal;
            if (!FinikGame.CanPurchaseCurrentGoal || !FinikGame.PurchaseCurrentGoal()) return;

            if (rewardBuyButton) rewardBuyButton.interactable = false;
            if (rewardBuyLabel) rewardBuyLabel.text = "КУПЛЕНО!";
            if (rewardConfetti) rewardConfetti.Burst(Vector2.zero);
            else if (confetti) confetti.Burst(Vector2.zero);
            if (goalPunch) goalPunch.Punch();

            FinikAudioManager.Instance.PlayAssistant(FinikAudioManager.VoiceGoalBought, interruptCurrent: true);
            rewardMessageAfterClose = $"{goal.Title}: награда теперь в комнате!";

            if (rewardFinishRoutine != null) StopCoroutine(rewardFinishRoutine);
            rewardFinishRoutine = StartCoroutine(FinishRewardRoutine());
        }

        IEnumerator FinishRewardRoutine()
        {
            yield return new WaitForSecondsRealtime(1.25f);
            rewardFinishRoutine = null;
            Close();
        }

        // ------------------------------------------------------------------ goal picker

        void ShowGoals()
        {
            if (closing) return;
            var list = FinikGoalCatalog.Goals;
            if (AllGoalsPurchased(list))
            {
                ShowAllGoalsPurchased();
                return;
            }

            SetGoalPickerCompact(false);
            SetGoalPickerHeader("ЦЕЛИ", "На что будем копить?", "Монеты в копилке останутся — поменяется только цель.");
            var current = FinikGame.HasSelectedGoal ? FinikGame.Goal : null;
            if (goalsBack) goalsBack.gameObject.SetActive(current != null);
            for (int i = 0; i < goalCards.Length; i++)
            {
                bool shown = i < list.Count;
                if (!goalCards[i]) continue;
                goalCards[i].gameObject.SetActive(shown);
                if (!shown) continue;
                var goal = list[i];
                if (i < goalCardImages.Length && goalCardImages[i]) goalCardImages[i].sprite = IconFor(goal);
                if (i < goalCardTitles.Length && goalCardTitles[i]) goalCardTitles[i].text = goal.Title;
                bool purchased = FinikGame.GoalPurchased(goal.Id);
                if (i < goalCardPrices.Length && goalCardPrices[i])
                    goalCardPrices[i].text = purchased ? "Куплено" : $"{goal.Price} {Coins(goal.Price)}";
                goalCards[i].SetSelected(goal == current);
                goalCards[i].SetCompleted(purchased);
                // A purchased reward remains visible in the catalogue, but cannot be selected or bought again.
                if (goalCards[i].Button) goalCards[i].Button.interactable = !purchased;
            }
            main.Hide();
            confirm.Hide();
            if (reward) reward.Hide();
            goals.Show();
            FinikAudioManager.Instance.PlayAssistant(FinikAudioManager.VoiceGoalPicker, interruptCurrent: true);
        }

        static bool AllGoalsPurchased(IReadOnlyList<FinikGoal> list)
        {
            if (list == null || list.Count == 0) return false;
            for (int i = 0; i < list.Count; i++)
                if (!FinikGame.GoalPurchased(list[i].Id)) return false;
            return true;
        }

        void ShowAllGoalsPurchased()
        {
            SetGoalPickerCompact(true);
            SetGoalPickerHeader("ВСЕ ЦЕЛИ", "Ура! Все цели достигнуты!", "Все награды уже стоят в комнате. Отличная работа!");
            for (int i = 0; i < goalCards.Length; i++)
                if (goalCards[i]) goalCards[i].gameObject.SetActive(false);

            var grid = FindDeep(goals.transform, "Grid");
            if (grid) grid.gameObject.SetActive(false);
            if (goalsBack)
            {
                goalsBack.gameObject.SetActive(true);
                var label = FindDeep(goalsBack.transform, "Label")?.GetComponent<TMP_Text>();
                if (label) label.text = "В комнату";
            }

            main.Hide();
            confirm.Hide();
            if (reward) reward.Hide();
            goals.Show();
        }

        void SetGoalPickerCompact(bool compact)
        {
            var column = FindDeep(goals.transform, "Column");
            var layout = column ? column.GetComponent<FinikOrientationLayout>() : null;
            if (!layout) return;
            if (!goalPickerHeightsCaptured)
            {
                layout.GetHeights(out goalPickerLandscapeHeight, out goalPickerPortraitHeight);
                goalPickerHeightsCaptured = true;
            }
            layout.SetHeights(compact ? 400f : goalPickerLandscapeHeight, compact ? 400f : goalPickerPortraitHeight);
        }

        void SetGoalPickerHeader(string tag, string titleText, string hintText)
        {
            var header = FindDeep(goals.transform, "Header");
            var tagLabel = FindDeep(header, "Tag")?.GetComponent<TMP_Text>();
            var titleLabel = FindDeep(header, "Title")?.GetComponent<TMP_Text>();
            var hintLabel = FindDeep(goals.transform, "Hint")?.GetComponent<TMP_Text>();
            if (tagLabel) tagLabel.text = tag;
            if (titleLabel) titleLabel.text = titleText;
            if (hintLabel) hintLabel.text = hintText;

            var grid = FindDeep(goals.transform, "Grid");
            if (grid) grid.gameObject.SetActive(true);
            if (goalsBack)
            {
                var label = FindDeep(goalsBack.transform, "Label")?.GetComponent<TMP_Text>();
                if (label) label.text = "Назад";
            }
        }

        void PickGoal(int index)
        {
            var list = FinikGoalCatalog.Goals;
            if (index >= list.Count) return;

            bool firstGoal = !FinikGame.HasSelectedGoal;
            if (!FinikGame.SelectGoal(list[index].Id)) return;

            for (int i = 0; i < goalCards.Length && i < list.Count; i++)
                if (goalCards[i]) goalCards[i].SetSelected(i == index);
            Render(animate: false);

            if (firstGoal)
            {
                // Do not expose the piggy-bank deposit screen yet: the initial 30 coins belong to
                // the first lesson. Hand the player straight to the quest board instead.
                openQuestsAfterClose = true;
                Close();
            }
            else
            {
                ShowMain();
            }
        }

        // ------------------------------------------------------------------ take back

        // The chosen step, not clamped to what is left of the goal: taking back is about the pot.
        int WithdrawAmount => amountSteps.Length == 0 ? 0 : Math.Min(amountSteps[Mathf.Clamp(step, 0, amountSteps.Length - 1)], FinikGame.Savings);

        void AskWithdraw()
        {
            if (closing || !allowWithdraw) return;
            var progress = FinikGame.GoalProgress;
            int amount = WithdrawAmount;
            if (amount <= 0) return;
            var after = progress.After(-amount);
            if (confirmTitle) confirmTitle.text = $"Взять {amount} {Coins(amount)} обратно?";
            if (confirmBody)
                confirmBody.text = FinikTypography.Fix($"Монеты вернутся в кошелёк, а копилка похудеет: останется накопить <b>{after.Left}</b> вместо {progress.Left}. " +
                                   "Цель отодвинется, но её всегда можно догнать.");
            if (confirmBefore) confirmBefore.SetValue(progress.Fraction, animate: false);
            if (confirmAfter) confirmAfter.SetValue(after.Fraction, animate: false);
            // No arrow: the UI font has no glyph for one, and it came out as an empty box.
            if (confirmProgress) confirmProgress.text = $"Было {progress.saved}, станет <b>{after.saved}</b> из {progress.Target}";
            if (takeLabel) takeLabel.text = $"Взять {amount}";
            main.Hide();
            goals.Hide();
            if (reward) reward.Hide();
            confirm.Show();
        }

        void Withdraw()
        {
            int amount = WithdrawAmount;
            if (amount > 0) FinikGame.WithdrawSavings(amount);
            Render(animate: true);
            ShowMain();
        }

        // ------------------------------------------------------------------ words

        /// <summary>монета / монеты / монет.</summary>
        public static string Coins(int n) => Plural(n, "монета", "монеты", "монет");

        static string Times(int n) => Plural(n, "раз", "раза", "раз");

        static string Plural(int n, string one, string few, string many)
        {
            n = Math.Abs(n) % 100;
            if (n is >= 11 and <= 14) return many;
            return (n % 10) switch
            {
                1 => one,
                2 or 3 or 4 => few,
                _ => many
            };
        }
    }
}
