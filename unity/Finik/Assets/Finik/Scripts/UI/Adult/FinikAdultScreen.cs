using System;
using Finik.Core;
using Finik.Navigation;
using Finik.UI.Onboarding;
using Finik.UI.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Finik.UI.Adult
{
    /// <summary>
    /// «Для взрослых»: the detailed, neutral picture of the child's progress, reached from settings.
    ///
    /// The gate has two steps a child will not pass by accident: a button held for
    /// <see cref="FinikAdult.HoldSeconds"/>, then a small addition typed on the screen's own keypad.
    /// Inside there are no grades and no comparisons — only what happened, what the child is
    /// practising, a short note for the parent, the frame counter and the two destructive actions,
    /// each behind its own confirmation.
    /// </summary>
    public sealed class FinikAdultScreen : MonoBehaviour, IFinikScreen
    {
        const int MaxAnswerDigits = 3;

        [Header("Gate")]
        [SerializeField] FinikScreenPanel gate;
        [SerializeField] TMP_Text stepLabel;
        [Tooltip("Step one and step two: two dots, the current one lit.")]
        [SerializeField] Image[] stepDots = Array.Empty<Image>();
        [SerializeField] GameObject holdRoot;
        [SerializeField] FinikAdultHoldButton holdButton;
        [SerializeField] GameObject challengeRoot;
        [SerializeField] TMP_Text challengePrompt;
        [SerializeField] TMP_Text answerText;
        [Tooltip("Digits 0–9, index = digit.")]
        [SerializeField] Button[] digitButtons = Array.Empty<Button>();
        [SerializeField] Button eraseButton;
        [SerializeField] Button answerButton;
        [SerializeField] TMP_Text answerError;
        [SerializeField] Button gateBackButton;
        [SerializeField] Button gateCloseButton;

        [Header("Report")]
        [SerializeField] FinikScreenPanel report;
        [SerializeField] ScrollRect reportScroll;
        [Tooltip("Прогресс, Обучение, Управление: one chip per page, same order as tabPages.")]
        [SerializeField] FinikChoiceItem[] tabChips = Array.Empty<FinikChoiceItem>();
        [SerializeField] GameObject[] tabPages = Array.Empty<GameObject>();
        [SerializeField] TMP_Text questsValue;
        [SerializeField] TMP_Text daysValue;
        [SerializeField] TMP_Text topicsValue;
        [SerializeField] TMP_Text goalTitle;
        [SerializeField] TMP_Text goalAmounts;
        [SerializeField] TMP_Text goalPercent;
        [SerializeField] FinikFillBar goalBar;
        [Tooltip("One row per curriculum line; rows past the curriculum are hidden.")]
        [SerializeField] GameObject[] topicRows = Array.Empty<GameObject>();
        [SerializeField] TMP_Text[] topicTitles = Array.Empty<TMP_Text>();
        [SerializeField] TMP_Text[] topicDescriptions = Array.Empty<TMP_Text>();
        [Tooltip("The mark in front of each curriculum line: a tick once practised, a dot until then.")]
        [SerializeField] Image[] topicMarks = Array.Empty<Image>();
        [Tooltip("The green tick badge; it carries its own colours, so it is shown untinted.")]
        [SerializeField] Sprite topicPractisedIcon;
        [Tooltip("A plain white dot, tinted and shrunk into a quiet bullet.")]
        [SerializeField] Sprite topicPendingIcon;
        [SerializeField] Color topicPendingColor = new(0.36f, 0.41f, 0.62f, 1f);
        [SerializeField, Range(0.1f, 1f)] float topicPendingScale = 0.4f;
        [SerializeField] TMP_Text dayIncome;
        [Tooltip("Plan column: needs, wants, savings.")]
        [SerializeField] TMP_Text[] dayPlanned = Array.Empty<TMP_Text>();
        [Tooltip("Actual column: needs, wants, savings.")]
        [SerializeField] TMP_Text[] dayActual = Array.Empty<TMP_Text>();
        [SerializeField] TMP_Text dayConclusion;
        [SerializeField] TMP_Text parentNote;
        [SerializeField] FinikToggleSwitch frameCounterSwitch;
        [SerializeField] TMP_Text frameCounterNote;
        [SerializeField] Button resetButton;
        [SerializeField] Button deleteButton;
        [SerializeField] TMP_Text actionResult;
        [SerializeField] Button reportBackButton;
        [SerializeField] Button howToPlayButton;
        [SerializeField] Button reportDoneButton;
        [SerializeField] Button reportCloseButton;

        [Header("Confirm")]
        [SerializeField] FinikScreenPanel confirm;
        [SerializeField] TMP_Text confirmTitle;
        [SerializeField] TMP_Text confirmBody;
        [SerializeField] Button confirmYesButton;
        [SerializeField] TMP_Text confirmYesLabel;
        [SerializeField] Button confirmNoButton;

        [Header("Shared")]
        [SerializeField] FinikSettingsScreen settingsScreen;
        [SerializeField] GameObject hudRoot;
        [SerializeField] FinikMovementController movement;
        [SerializeField] FinikShowcaseCamera showcase;
        [SerializeField] Behaviour[] pauseWhileOpen = Array.Empty<Behaviour>();
        [SerializeField] Color stepDotOn = new(0.95f, 0.5f, 0.1f, 1f);
        [SerializeField] Color stepDotOff = new(0.36f, 0.41f, 0.62f, 0.35f);

        enum Pending { None, Reset, Delete }

        FinikAdultChallenge challenge;
        string answer = string.Empty;
        int tab;
        Pending pending;
        bool open;
        bool hooked;

        public bool IsOpen => open;

        /// <summary>This menu takes the room over: the HUD goes away and the pet stands still.</summary>
        public bool HoldsRoom => true;

        void Awake()
        {
            EnsureHowToPlayButton();
            Hook();
        }

        void OnEnable()
        {
            EnsureHowToPlayButton();
            Hook();
            FinikScreens.Register(this);
            FinikPerformance.Changed += RenderDevice;
        }

        void OnDisable()
        {
            FinikScreens.Unregister(this);
            FinikPerformance.Changed -= RenderDevice;
        }

        void OnDestroy()
        {
            FinikScreens.Unregister(this);
            if (holdButton) holdButton.Held -= OnHeld;
        }

        void EnsureHowToPlayButton()
        {
            if (howToPlayButton || !reportBackButton || !reportBackButton.transform.parent) return;
            var clone = Instantiate(reportBackButton.gameObject, reportBackButton.transform.parent, false);
            clone.name = "HowToPlay";
            clone.transform.SetSiblingIndex(Mathf.Min(reportBackButton.transform.GetSiblingIndex() + 1, clone.transform.parent.childCount - 1));
            var label = clone.GetComponentInChildren<TMP_Text>(true);
            if (label) label.text = "Как играть";
            howToPlayButton = clone.GetComponent<Button>();

            void Width(Button button, float value)
            {
                if (!button) return;
                var layout = button.GetComponent<LayoutElement>();
                if (!layout) layout = button.gameObject.AddComponent<LayoutElement>();
                layout.minWidth = value;
                layout.preferredWidth = value;
                layout.flexibleWidth = 0f;
            }

            Width(reportBackButton, 280f);
            Width(howToPlayButton, 320f);
            Width(reportDoneButton, 360f);
        }

        /// <summary>Guarded and repeated on enable: the editor runs without domain reload, see FinikChoiceItem.</summary>
        void Hook()
        {
            if (hooked) return;
            hooked = true;
            if (holdButton) holdButton.Held += OnHeld;
            for (int digit = 0; digit < digitButtons.Length; digit++)
            {
                int d = digit;
                if (digitButtons[digit]) digitButtons[digit].onClick.AddListener(() => Type(d));
            }
            if (eraseButton) eraseButton.onClick.AddListener(Erase);
            if (answerButton) answerButton.onClick.AddListener(SubmitAnswer);
            if (gateBackButton) gateBackButton.onClick.AddListener(BackToSettings);
            if (gateCloseButton) gateCloseButton.onClick.AddListener(Close);

            if (frameCounterSwitch) frameCounterSwitch.Toggled += value => FinikPerformance.ShowFrameCounter = value;
            if (resetButton) resetButton.onClick.AddListener(() => Ask(Pending.Reset));
            if (deleteButton) deleteButton.onClick.AddListener(() => Ask(Pending.Delete));
            if (reportBackButton) reportBackButton.onClick.AddListener(BackToSettings);
            if (howToPlayButton) howToPlayButton.onClick.AddListener(OpenHowToPlay);
            if (reportDoneButton) reportDoneButton.onClick.AddListener(Close);
            if (reportCloseButton) reportCloseButton.onClick.AddListener(Close);
            for (int i = 0; i < tabChips.Length; i++)
            {
                int page = i;
                if (tabChips[i]) tabChips[i].Clicked += _ => SelectTab(page);
            }

            if (confirmYesButton) confirmYesButton.onClick.AddListener(RunPending);
            if (confirmNoButton) confirmNoButton.onClick.AddListener(CancelPending);
        }

        /// <summary>The way in from settings: always through the gate, never straight to the report.</summary>
        public bool OpenGate()
        {
            if (!TakeRoom()) return false;
            ShowGate();
            return true;
        }

        /// <summary>Same as <see cref="OpenGate"/>; kept for callers that open any screen by Open().</summary>
        public bool Open() => OpenGate();

        public void Close()
        {
            if (!open) return;
            open = false;
            pending = Pending.None;
            if (gate) gate.Hide();
            if (report) report.Hide();
            if (confirm) confirm.Hide();
            // Another menu has already taken the HUD, the camera and the pet: leave all three to it.
            // This menu's own hold on Finik goes whoever takes the room next; the new menu holds its own.
            if (movement) movement.EndActivity(this);
            if (FinikScreens.RoomTakenOver(this)) return;
            foreach (var behaviour in pauseWhileOpen) if (behaviour) behaviour.enabled = true;
            if (showcase) showcase.Release();
            if (hudRoot) hudRoot.SetActive(true);
        }

        bool TakeRoom()
        {
            if (open) return true;
            if (!FinikProfileStore.TryLoad(out _)) return false;
            FinikGame.EnsureJourney();

            open = true;
            // One menu at a time: settings (or whatever else is up) closes as this one takes the room.
            FinikScreens.CloseOthers(this);
            pending = Pending.None;
            if (hudRoot) hudRoot.SetActive(false);
            foreach (var behaviour in pauseWhileOpen) if (behaviour) behaviour.enabled = false;
            if (movement) movement.BeginActivity(this);
            if (showcase) showcase.Engage();
            return true;
        }

        void BackToSettings()
        {
            if (settingsScreen && settingsScreen.Open()) return;
            Close();
        }

        void OpenHowToPlay()
        {
            var tutorial = FindFirstObjectByType<FinikOnboardingFlow>(FindObjectsInactive.Include);
            if (!tutorial) return;
            Close();
            tutorial.OpenTutorialAgain();
        }

        // ------------------------------------------------------------------ gate

        void ShowGate()
        {
            if (report) report.Hide();
            if (confirm) confirm.Hide();
            if (holdButton) holdButton.ResetProgress();
            if (gate) gate.Show();
            SetStep(1);
        }

        void SetStep(int step)
        {
            if (holdRoot) holdRoot.SetActive(step == 1);
            if (challengeRoot) challengeRoot.SetActive(step == 2);
            if (stepLabel) stepLabel.text = step == 1 ? "Шаг 1 из 2 — удержание" : "Шаг 2 из 2 — короткий пример";
            for (int i = 0; i < stepDots.Length; i++)
                if (stepDots[i]) stepDots[i].color = i < step ? stepDotOn : stepDotOff;
            if (step == 2) NewChallenge(error: null);
            // The two steps differ in height: the card follows at once, not a frame later.
            FinikFitColumn.RefreshUnder(gate);
        }

        void OnHeld() => SetStep(2);

        void NewChallenge(string error)
        {
            challenge = FinikAdult.RandomChallenge();
            answer = string.Empty;
            if (challengePrompt) challengePrompt.text = challenge.Prompt;
            if (answerError) answerError.text = Fix(error ?? string.Empty);
            RenderAnswer();
        }

        void Type(int digit)
        {
            if (answer.Length >= MaxAnswerDigits) return;
            // A leading zero is never part of the answer; it only makes the field look wrong.
            answer = answer == "0" ? digit.ToString() : answer + digit;
            if (answerError) answerError.text = string.Empty;
            RenderAnswer();
        }

        void Erase()
        {
            if (answer.Length > 0) answer = answer.Substring(0, answer.Length - 1);
            RenderAnswer();
        }

        void RenderAnswer()
        {
            if (answerText)
            {
                answerText.text = answer.Length > 0 ? answer : "?";
                answerText.alpha = answer.Length > 0 ? 1f : 0.35f;
            }
            if (answerButton) answerButton.interactable = answer.Length > 0;
            if (eraseButton) eraseButton.interactable = answer.Length > 0;
        }

        void SubmitAnswer()
        {
            if (answer.Length == 0) return;
            if (!challenge.Accepts(answer))
            {
                // A fresh task after every miss: guessing numbers one by one leads nowhere.
                NewChallenge("Ответ не подошёл. Вот новый пример.");
                return;
            }
            ShowReport();
        }

        // ------------------------------------------------------------------ report

        void ShowReport()
        {
            if (gate) gate.Hide();
            if (confirm) confirm.Hide();
            ShowResult(string.Empty);
            Render();
            if (report) report.Show();
            // Every visit starts on the progress, the page a parent opens the section for.
            SelectTab(0);
        }

        /// <summary>Shows one page of the report; the card resizes to it and starts from the top.</summary>
        void SelectTab(int index)
        {
            tab = Mathf.Clamp(index, 0, Mathf.Max(0, tabPages.Length - 1));
            for (int i = 0; i < tabPages.Length; i++)
                if (tabPages[i]) tabPages[i].SetActive(i == tab);
            for (int i = 0; i < tabChips.Length; i++)
                if (tabChips[i]) tabChips[i].SetSelected(i == tab);
            FinikFitColumn.RefreshUnder(report);
            if (tab == 2) EqualizeManageSections();
            if (reportScroll)
            {
                reportScroll.StopMovement();
                reportScroll.verticalNormalizedPosition = 1f;
            }
        }

        void EqualizeManageSections()
        {
            if (tabPages == null || tabPages.Length < 3 || !tabPages[2]) return;
            var root = tabPages[2].transform;
            RectTransform device = root.Find("Device") as RectTransform;
            RectTransform profile = root.Find("Profile") as RectTransform;
            if (!device || !profile)
            {
                foreach (var rect in root.GetComponentsInChildren<RectTransform>(true))
                {
                    if (!device && rect.name == "Device") device = rect;
                    else if (!profile && rect.name == "Profile") profile = rect;
                }
            }
            if (!device || !profile) return;

            Canvas.ForceUpdateCanvases();
            float height = Mathf.Max(LayoutUtility.GetPreferredHeight(device), LayoutUtility.GetPreferredHeight(profile));
            height = Mathf.Max(190f, height);
            foreach (var rect in new[] { device, profile })
            {
                var layout = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
                layout.minHeight = height;
                layout.preferredHeight = height;
            }
            if (root is RectTransform rootRect)
                LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);
        }

        void Render()
        {
            var data = FinikAdult.Build();

            if (questsValue) questsValue.text = $"{data.questsDone} из {data.questsToday}";
            if (daysValue) daysValue.text = data.daysFinished.ToString();
            if (topicsValue)
            {
                topicsValue.text = $"{data.periodsFinished} из 5";
                var caption = topicsValue.transform.parent ? topicsValue.transform.parent.Find("Caption")?.GetComponent<TMP_Text>() : null;
                if (caption) caption.text = "Периодов";
            }

            if (goalTitle) goalTitle.text = data.hasGoal ? data.goal.goal.Title : "Цель пока не выбрана";
            if (goalAmounts)
                goalAmounts.text = Fix(data.hasGoal
                    ? data.goal.Reached
                        ? $"Накоплено {data.goal.saved} из {data.goal.Target} — цель собрана."
                        : $"Накоплено {data.goal.saved} из {data.goal.Target}, осталось {data.goal.Left}."
                    : "Ребёнок ещё не выбрал, на что копит. Цель выбирается в копилке.");
            if (goalPercent)
            {
                goalPercent.gameObject.SetActive(data.hasGoal);
                goalPercent.text = $"{Mathf.RoundToInt(data.goal.Fraction * 100f)}%";
            }
            if (goalBar)
            {
                goalBar.gameObject.SetActive(data.hasGoal);
                goalBar.SetValue(data.hasGoal ? data.goal.Fraction : 0f, animate: false);
            }

            for (int i = 0; i < topicTitles.Length; i++)
            {
                bool has = i < data.topics.Count;
                if (i < topicRows.Length && topicRows[i]) topicRows[i].SetActive(has);
                if (!has) continue;
                var topic = data.topics[i];
                if (topicTitles[i]) topicTitles[i].text = Fix(topic.title);
                if (i < topicDescriptions.Length && topicDescriptions[i]) topicDescriptions[i].text = Fix(topic.description);
                if (i < topicMarks.Length && topicMarks[i])
                {
                    // A picture, not a character: the UI font carries no tick, and a missing glyph
                    // shows up as an empty box in front of the line.
                    topicMarks[i].sprite = topic.practised ? topicPractisedIcon : topicPendingIcon;
                    topicMarks[i].color = topic.practised ? Color.white : topicPendingColor;
                    topicMarks[i].rectTransform.localScale = Vector3.one * (topic.practised ? 1f : topicPendingScale);
                }
            }

            var day = data.day;
            if (dayIncome) dayIncome.text = day.income > 0 ? $"Получено за день: {day.income}" : "Монеты за день ещё не получены";
            var buckets = new[] { FinikBudgetBucket.Needs, FinikBudgetBucket.Wants, FinikBudgetBucket.Savings };
            for (int i = 0; i < buckets.Length; i++)
            {
                var row = Row(day, buckets[i]);
                if (i < dayPlanned.Length && dayPlanned[i]) dayPlanned[i].text = day.hasPlan ? row.planned.ToString() : "—";
                if (i < dayActual.Length && dayActual[i]) dayActual[i].text = row.actual.ToString();
            }
            if (dayConclusion) dayConclusion.text = Fix(day.Conclusion);

            if (parentNote) parentNote.text = $"{Fix(FinikAdult.ParentNote)}\n\n{Fix(FinikAdult.CurrencyNote)}\n\n{Fix(FinikAdult.NeutralNote)}";

            RenderDevice();
        }

        /// <summary>The frame counter: a developer overlay, so it lives here and not in the child's settings.</summary>
        void RenderDevice()
        {
            if (frameCounterSwitch)
            {
                frameCounterSwitch.SetIsOn(FinikPerformance.ShowFrameCounter, animate: open);
                // An overlay that was never built into the scene would switch on and show nothing.
                frameCounterSwitch.SetInteractable(FinikGraphy.Available);
            }
            if (frameCounterNote)
                frameCounterNote.text = Fix(FinikGraphy.Available
                    ? $"Кадры в секунду и график поверх игры. Сейчас игра рассчитана на {FinikPerformance.Target} кадров."
                    : "Счётчик не собран в сцену: выполните Finik / UI / Rebuild Frame Counter.");
        }

        /// <summary>The line under the profile buttons; hidden when empty so it leaves no gap.</summary>
        void ShowResult(string text)
        {
            if (!actionResult) return;
            actionResult.text = Fix(text);
            actionResult.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        static string Fix(string text) => FinikTypography.Fix(text);

        static FinikBudgetRow Row(FinikBudgetSummary summary, FinikBudgetBucket bucket)
        {
            foreach (var row in summary.rows) if (row.bucket == bucket) return row;
            return default;
        }

        // ------------------------------------------------------------------ destructive actions

        void Ask(Pending what)
        {
            pending = what;
            if (confirmTitle) confirmTitle.text = what == Pending.Reset ? "Сбросить прогресс?" : "Удалить профиль?";
            if (confirmBody)
                confirmBody.text = Fix(what == Pending.Reset
                    ? "Дни, задания, кошелёк, копилка и состояние питомца начнутся заново. Имя и питомец останутся. Это действие нельзя отменить."
                    : "Профиль, имя, прогресс, кошелёк и копилка будут удалены с этого устройства. Игра начнётся со знакомства. Это действие нельзя отменить.");
            if (confirmYesLabel) confirmYesLabel.text = what == Pending.Reset ? "Сбросить" : "Удалить";
            if (report) report.Hide();
            if (confirm) confirm.Show();
            FinikFitColumn.RefreshUnder(confirm);
        }

        void CancelPending()
        {
            pending = Pending.None;
            if (confirm) confirm.Hide();
            if (report) report.Show();
        }

        void RunPending()
        {
            var what = pending;
            pending = Pending.None;
            if (confirm) confirm.Hide();

            if (what == Pending.Reset)
            {
                FinikAdult.ResetProgress();
                RestartGame();
                return;
            }

            if (what == Pending.Delete)
            {
                FinikAdult.DeleteGameState();
                FinikProfileStore.Clear();
                RestartGame();
            }
        }

        static void RestartGame()
        {
            Time.timeScale = 1f;
            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
            else SceneManager.LoadScene(scene.name);
        }
    }
}
