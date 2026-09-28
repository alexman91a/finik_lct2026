using System;
using System.Collections;
using System.Collections.Generic;
using Finik.Core;
using Finik.Navigation;
using Finik.UI.Onboarding;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Games
{
    /// <summary>
    /// «Поиграть с Фиником»: the three free games that lift his mood. Opens when Finik walks to the
    /// sofa or to his backpack — both are places to play, so both lead here.
    ///
    /// The screen has three faces over the same card: the hub with the games and their difficulty
    /// steps, the board of the level being played, and the result. Mood is only ever paid out on a
    /// cleared level, and only through <see cref="FinikGame.CompleteMiniGameLevel"/>, so leaving a
    /// level halfway gives nothing — and costs nothing either.
    /// </summary>
    public sealed class FinikGamesScreen : MonoBehaviour, IFinikScreen
    {
        /// <summary>The sofa. Tapping it is the shortest way into the games.</summary>
        public const string SofaInteraction = "play-zone";
        /// <summary>The backpack: the same games, so a child finds them wherever they look.</summary>
        public const string BackpackInteraction = "care-bag";

        enum Stage { Hub, Playing, Result }

        [Header("Hub")]
        [SerializeField] FinikScreenPanel hubPanel;
        [SerializeField] TMP_Text hubTitle;
        [SerializeField] TMP_Text hubSubtitle;
        [SerializeField] TMP_Text moodValue;
        [SerializeField] FinikFillBar moodBar;
        [SerializeField] FinikGameCardView[] cards = Array.Empty<FinikGameCardView>();
        [Tooltip("The way back to the room; a tap beside the sheet does the same.")]
        [SerializeField] Button hubCloseButton;

        [Header("Playing")]
        [SerializeField] FinikScreenPanel playPanel;
        [SerializeField] TMP_Text playTitle;
        [Tooltip("Difficulty line under the title: a status, not a button.")]
        [SerializeField] TMP_Text playDifficulty;
        [Tooltip("One pip per difficulty step, lit up to the current one.")]
        [SerializeField] Image[] difficultyPips = Array.Empty<Image>();
        [Tooltip("What clearing the level is worth, in the header's reward chip.")]
        [SerializeField] TMP_Text playReward;
        [Tooltip("Goal card over the board: what to do, how far it is, and a bar that fills.")]
        [SerializeField] Image goalIcon;
        [SerializeField] TMP_Text goalText;
        [SerializeField] TMP_Text goalValue;
        [SerializeField] FinikFillBar goalBar;
        [Tooltip("Several goals at once: a row of pictures with what is left of each, in place of the goal line.")]
        [SerializeField] GameObject goalItemsRow;
        [SerializeField] Image[] goalItemIcons = Array.Empty<Image>();
        [SerializeField] TMP_Text[] goalItemValues = Array.Empty<TMP_Text>();
        [Tooltip("Tick over a goal that is done, in place of its number.")]
        [SerializeField] GameObject[] goalItemChecks = Array.Empty<GameObject>();
        [Tooltip("The resource that runs out: moves, turns, lives, with the word that says which.")]
        [SerializeField] Image counterIcon;
        [SerializeField] TMP_Text counterValue;
        [SerializeField] TMP_Text counterLabel;
        [Tooltip("How to play, laid over the goal card for the first seconds of a level.")]
        [SerializeField] CanvasGroup introBanner;
        [SerializeField] TMP_Text introText;
        [Tooltip("Cross in the header: leaves the level for the hub.")]
        [SerializeField] Button playCloseButton;
        [SerializeField] FinikMemoryBoard memoryBoard;
        [SerializeField] FinikMatch3Board match3Board;
        [SerializeField] FinikCatchBoard catchBoard;

        [Header("Boosters")]
        [SerializeField] FinikBoosterSlotView[] boosterSlots = Array.Empty<FinikBoosterSlotView>();

        [Header("Result")]
        [SerializeField] FinikGameResultView result;

        [Header("Art")]
        [SerializeField] FinikGameArt art;
        [Tooltip("Effects layer over the board; an armed power speaks through it.")]
        [SerializeField] FinikGameFx boardFx;

        [Header("World")]
        [SerializeField] FinikShowcaseCamera showcase;
        [SerializeField] FinikMovementController movement;
        [SerializeField] FinikActivityController activity;
        [SerializeField] Behaviour[] pauseWhileOpen = Array.Empty<Behaviour>();
        [SerializeField] GameObject hudRoot;

        /// <summary>Charges left in this level, by booster id. Every level starts with a full set.</summary>
        readonly Dictionary<string, int> charges = new(StringComparer.Ordinal);
        Stage stage;
        bool open;
        bool closing;
        FinikMiniGame game;
        FinikMiniGameLevel level;
        FinikMiniGameBoard board;
        Coroutine intro;
        Coroutine closeRoutine;
        FinikInteractionController pendingInteraction;
        string pendingInteractionId;
        Func<FinikInteractionController, string, bool> interactionHandler;

        public bool IsOpen => open;

        /// <summary>This menu takes the room over: the HUD goes away and Finik stands still.</summary>
        public bool HoldsRoom => true;

        void Awake()
        {
            foreach (var card in cards)
                if (card) card.LevelPicked += StartLevel;

            foreach (var candidate in Boards())
            {
                if (!candidate) continue;
                candidate.Changed += RenderCounters;
                candidate.Finished += OnFinished;
                candidate.BoosterSpent += OnBoosterSpent;
                candidate.BoosterArmed += OnBoosterArmed;
                candidate.gameObject.SetActive(false);
            }
            foreach (var slot in boosterSlots)
                if (slot) slot.Clicked += OnBoosterClicked;

            if (hubCloseButton) hubCloseButton.onClick.AddListener(Close);
            if (playCloseButton) playCloseButton.onClick.AddListener(BackToHub);
            if (result)
            {
                if (result.PrimaryButton) result.PrimaryButton.onClick.AddListener(OnResultPrimary);
                if (result.CloseButton) result.CloseButton.onClick.AddListener(BackToHub);
                if (result.Panel) result.Panel.Hide(instant: true);
            }
            if (playPanel) playPanel.Hide(instant: true);
            if (hubPanel) hubPanel.Hide(instant: true);

            interactionHandler = HandleInteraction;
            FinikInteractionController.RegisterHandler(interactionHandler);
        }

        void OnDestroy()
        {
            FinikScreens.Unregister(this);
            FinikInteractionController.UnregisterHandler(interactionHandler);
        }

        void OnEnable() => FinikScreens.Register(this);

        void OnDisable() => FinikScreens.Unregister(this);

        /// <summary>
        /// The three boards as a list. NonSerialized and length-checked on purpose: the editor's hot
        /// reload serializes private fields too and brings a null array back as an empty one, which a
        /// plain <c>??=</c> would keep for ever — no board would ever be stopped or listened to.
        /// </summary>
        [NonSerialized] FinikMiniGameBoard[] boards;

        FinikMiniGameBoard[] Boards()
        {
            if (boards == null || boards.Length != 3) boards = new FinikMiniGameBoard[] { memoryBoard, match3Board, catchBoard };
            return boards;
        }

        bool HandleInteraction(FinikInteractionController controller, string interactionId)
        {
            if (open) return false;
            if (interactionId != SofaInteraction && interactionId != BackpackInteraction) return false;
            if (!Open()) return false;
            pendingInteraction = controller;
            pendingInteractionId = interactionId;
            return true;
        }

        /// <summary>Lets the sofa (or the backpack) Finik walked to know the visit is over.</summary>
        void HandBackInteraction()
        {
            if (!pendingInteraction) return;
            pendingInteraction.CompleteInteraction(pendingInteractionId);
            pendingInteraction = null;
            pendingInteractionId = null;
        }

        // ------------------------------------------------------------------ open / close

        public bool Open()
        {
            if (open && !closing) return true;
            if (closing)
            {
                // Asked back while still closing (the camera is on its way back to the room): cancel
                // the close and take the room again, instead of answering "already open" with nothing
                // on screen.
                if (closeRoutine != null) StopCoroutine(closeRoutine);
                closeRoutine = null;
                closing = false;
                if (hudRoot) hudRoot.SetActive(false);
                if (showcase) showcase.Engage();
                ShowHub();
                return true;
            }
            if (!FinikProfileStore.TryLoad(out _)) return false;
            FinikGame.EnsureJourney();

            open = true;
            closing = false;
            // One menu at a time: whatever else is up closes before this one takes the room.
            FinikScreens.CloseOthers(this);
            if (hudRoot) hudRoot.SetActive(false);
            foreach (var behaviour in pauseWhileOpen) if (behaviour) behaviour.enabled = false;
            if (movement) movement.BeginActivity(this);
            if (showcase) showcase.Engage();

            ShowHub();
            return true;
        }

        public void Close()
        {
            if (!open || closing) return;
            closing = true;
            StopBoard();
            if (hubPanel) hubPanel.Hide();
            if (playPanel) playPanel.Hide();
            if (result && result.Panel) result.Panel.Hide();
            closeRoutine = StartCoroutine(CloseRoutine());
        }

        IEnumerator CloseRoutine()
        {
            // A menu takes a moment to close — the camera fly-back alone is about a second — and
            // another one can take over at any point during it. This screen re-checks after every
            // wait and hands back only what is still its own.
            if (TakenOver()) yield break;

            bool released = false;
            if (showcase) showcase.Release(() => released = true);
            else released = true;
            while (!released) yield return null;
            if (TakenOver()) yield break;

            if (hudRoot) hudRoot.SetActive(true);
            HandBackInteraction();
            while (activity && activity.IsBusy) yield return null;
            if (TakenOver()) yield break;

            foreach (var behaviour in pauseWhileOpen) if (behaviour) behaviour.enabled = true;
            if (movement) movement.EndActivity(this);
            open = false;
            closing = false;
            closeRoutine = null;
        }

        bool TakenOver()
        {
            if (!FinikScreens.RoomTakenOver(this)) return false;
            // The HUD and the camera belong to the new menu now; this menu's own hold on Finik does not.
            if (movement) movement.EndActivity(this);
            StopBoard();
            HandBackInteraction();
            open = false;
            closing = false;
            closeRoutine = null;
            return true;
        }

        // ------------------------------------------------------------------ flow

        void ShowHub()
        {
            stage = Stage.Hub;
            StopBoard();
            if (playPanel) playPanel.Hide();
            if (result && result.Panel) result.Panel.Hide();
            RenderHub();
            if (hubPanel) hubPanel.Show();
        }

        void BackToHub()
        {
            if (!open || closing) return;
            ShowHub();
        }

        void StartLevel(string gameId, int number)
        {
            if (!open || closing) return;
            if (!FinikMiniGameCatalog.TryGet(gameId, out var picked)) return;
            var definition = FinikMiniGameCatalog.Level(picked, number);
            if (definition == null) return;
            // The hub already greys a shut step out; this is the safety net for a stale card.
            if (!FinikGame.MiniGameUnlocked(gameId, number)) return;

            game = picked;
            level = definition;
            stage = Stage.Playing;

            StopBoard();
            board = BoardFor(gameId);
            if (!board)
            {
                Debug.LogWarning($"[FinikGames] No board wired for «{gameId}»: run Finik/UI/Rebuild Games Screen.");
                return;
            }
            // Belt and braces: the boards share one area and draw in sibling order, so one left switched
            // on would simply cover the new one — the old level's pieces over the new level's HUD.
            foreach (var candidate in Boards())
                if (candidate) candidate.gameObject.SetActive(candidate == board);

            // Every level hands out a fresh set of powers: they are a way out of a stuck board, not
            // something to hoard between levels.
            charges.Clear();
            foreach (var booster in FinikBoosterCatalog.For(gameId))
                charges[booster.Id] = FinikBoosterCatalog.ChargesPerLevel;

            if (hubPanel) hubPanel.Hide();
            if (result && result.Panel) result.Panel.Hide();
            if (playPanel) playPanel.Show();
            RenderPlayHeader();
            board.GoalAnchor = GoalAnchor;
            board.Play(level);
            RenderCounters();
            RenderBoosters();
            ShowIntro(game.Rule);
        }

        FinikMiniGameBoard BoardFor(string gameId) => gameId switch
        {
            FinikMiniGameId.Memory => memoryBoard,
            FinikMiniGameId.Match3 => match3Board,
            FinikMiniGameId.Catch => catchBoard,
            _ => null
        };

        void StopBoard()
        {
            HideIntro();
            foreach (var candidate in Boards())
            {
                if (!candidate) continue;
                candidate.StopGame();
                candidate.gameObject.SetActive(false);
            }
            board = null;
        }

        void OnFinished(bool won)
        {
            if (!open || stage != Stage.Playing || level == null) return;

            var reward = won
                ? FinikGame.CompleteMiniGameLevel(game.Id, level.Number)
                : default;
            if (won && !reward.Ok)
                Debug.LogWarning($"[FinikGames] «{game.Id}» level {level.Number} was cleared but not rewarded: {reward.failure}.");

            // Read before the board is stopped: stopping forgets the level.
            string lossReason = board ? board.LossReason : null;
            stage = Stage.Result;
            StopBoard();
            if (playPanel) playPanel.Hide();
            if (!result) return;

            bool hasNext = won && game.HasLevel(level.Number + 1);
            result.Show(new FinikGameResultView.Outcome(won, game, level, reward.mood, reward.xp, hasNext,
                moodWasFull: won && reward.Ok && reward.mood <= 0, lossReason), art ? art.Get(game.Icon) : null);
        }

        void OnResultPrimary()
        {
            if (!open || game == null || level == null) return;
            int number = result && result.PrimaryIsNext ? level.Number + 1 : level.Number;
            StartLevel(game.Id, number);
        }

        // ------------------------------------------------------------------ rendering

        void RenderHub()
        {
            if (hubTitle) hubTitle.text = "Поиграем с Фиником?";
            if (hubSubtitle) hubSubtitle.text = FinikTypography.Fix("Играй и поднимай Финику настроение");

            var needs = FinikGame.NeedsNow;
            int mood = Mathf.RoundToInt(needs.mood);
            if (moodValue) moodValue.text = $"{mood}<size=70%> / 100</size>";
            if (moodBar) moodBar.SetValue(Mathf.Clamp01(needs.mood / 100f), animate: hubPanel && hubPanel.IsVisible);

            for (int i = 0; i < cards.Length; i++)
            {
                var card = cards[i];
                if (!card) continue;
                bool used = i < FinikMiniGameCatalog.Games.Count;
                card.gameObject.SetActive(used);
                if (!used) continue;
                var definition = FinikMiniGameCatalog.Games[i];
                int best = FinikGame.MiniGameBestLevel(definition.Id);
                card.Show(definition, art ? art.Get(definition.Icon) : null, best);
                // A generated level takes a moment to build and test: do it now, while the child reads the hub.
                if (definition.Level(best + 1) is FinikMatch3Level upcoming) upcoming.Prewarm();
            }
        }

        /// <summary>Difficulty colours: the step reads at a glance, and "hard" finally looks hard.</summary>
        public static Color DifficultyColor(int number) => number switch
        {
            1 => new Color(0.55f, 0.9f, 0.5f),
            2 => new Color(1f, 0.78f, 0.35f),
            _ => new Color(1f, 0.52f, 0.45f)
        };

        static readonly Color PipOff = new(1f, 1f, 1f, 0.22f);
        static readonly Color CounterNormal = Color.white;
        static readonly Color CounterAlarm = new(1f, 0.45f, 0.4f);

        void RenderPlayHeader()
        {
            if (playTitle) playTitle.text = FinikTypography.Fix(game.Title);
            var accent = DifficultyColor(level.Tier);
            if (playDifficulty)
            {
                playDifficulty.text = level.Heading;
                playDifficulty.color = accent;
            }
            for (int i = 0; i < difficultyPips.Length; i++)
                if (difficultyPips[i]) difficultyPips[i].color = i < level.Tier ? accent : PipOff;
            if (playReward) playReward.text = $"+{level.Mood}";
            if (goalText) goalText.text = FinikTypography.Fix(level.Goal);
            if (goalIcon && art) goalIcon.sprite = art.Get(board ? board.GoalIcon : game.Icon);
            if (counterIcon && art && board) counterIcon.sprite = art.Get(board.CounterIcon);
            if (goalBar) goalBar.SetValue(0f, animate: false);
        }

        void RenderCounters()
        {
            if (goalValue) goalValue.text = board ? board.GoalValue : string.Empty;
            if (goalBar && board) goalBar.SetValue(board.GoalProgress, animate: true);
            if (counterValue)
            {
                counterValue.text = board ? board.CounterValue : string.Empty;
                counterValue.color = board && board.CounterLow ? CounterAlarm : CounterNormal;
            }
            if (counterLabel) counterLabel.text = board ? board.CounterLabel : string.Empty;
            RenderGoalItems();
        }

        /// <summary>Left inset of the goal line and bar beside the game's picture, and without it.</summary>
        [SerializeField] float goalInsetWithIcon = 112f;
        [SerializeField] float goalInsetWithoutIcon = 26f;
        [Tooltip("Right inset of the bar while the value text stands beside it.")]
        [SerializeField] float goalBarRightWithValue = 134f;

        void ShiftLeft(RectTransform rect, bool wide)
        {
            if (rect) rect.offsetMin = new Vector2(wide ? goalInsetWithoutIcon : goalInsetWithIcon, rect.offsetMin.y);
        }

        RectTransform GoalAnchor(int goal) =>
            goal >= 0 && goal < goalItemIcons.Length && goalItemIcons[goal] ? goalItemIcons[goal].rectTransform : null;

        /// <summary>
        /// A board that lists its goals gets them as a row of pictures, each with what is left of it;
        /// any other board keeps its one goal sentence.
        /// </summary>
        void RenderGoalItems()
        {
            var items = board ? board.GoalItems : null;
            bool several = items != null && items.Count > 0 && goalItemsRow;
            if (goalItemsRow) goalItemsRow.SetActive(several);
            if (goalText) goalText.gameObject.SetActive(!several);
            // The goal pictures say it all: the game's own picture on the left would only repeat one of
            // them, so it steps aside and the pictures and the bar take its room.
            if (goalIcon) goalIcon.gameObject.SetActive(!several);
            ShiftLeft(goalItemsRow ? goalItemsRow.transform as RectTransform : null, several);
            if (goalBar && goalBar.transform is RectTransform bar)
            {
                ShiftLeft(bar, several);
                // No "12/20" beside it any more: the counts live on the pictures, the bar takes the room.
                bar.offsetMax = new Vector2(several ? -goalInsetWithoutIcon : -goalBarRightWithValue, bar.offsetMax.y);
            }
            if (goalValue) goalValue.gameObject.SetActive(!several);
            if (!several) return;
            for (int i = 0; i < goalItemIcons.Length; i++)
            {
                bool used = i < items.Count;
                var icon = goalItemIcons[i];
                if (icon)
                {
                    // The icon's parent is the item: hiding it hides the number and the tick with it.
                    var root = icon.transform.parent ? icon.transform.parent.gameObject : icon.gameObject;
                    root.SetActive(used);
                    if (used && art) icon.sprite = art.Get(items[i].Icon);
                }
                if (!used) continue;
                bool done = items[i].Done;
                if (i < goalItemValues.Length && goalItemValues[i])
                {
                    goalItemValues[i].text = items[i].Left.ToString();
                    goalItemValues[i].gameObject.SetActive(!done);
                }
                if (i < goalItemChecks.Length && goalItemChecks[i]) goalItemChecks[i].SetActive(done);
            }
        }

        // ------------------------------------------------------------------ how to play

        void ShowIntro(string rule)
        {
            HideIntro();
            if (!introBanner || string.IsNullOrEmpty(rule)) return;
            if (introText) introText.text = FinikTypography.Fix(rule);
            intro = StartCoroutine(IntroRoutine());
        }

        void HideIntro()
        {
            if (intro != null) StopCoroutine(intro);
            intro = null;
            if (!introBanner) return;
            introBanner.alpha = 0f;
            introBanner.gameObject.SetActive(false);
        }

        /// <summary>The rule sits over the goal card for a few seconds, then the goal takes its place.</summary>
        IEnumerator IntroRoutine()
        {
            const float fadeIn = 0.2f, hold = 2.6f, fadeOut = 0.35f;
            introBanner.gameObject.SetActive(true);
            for (float t = 0f; t < fadeIn; t += Time.unscaledDeltaTime)
            {
                introBanner.alpha = t / fadeIn;
                yield return null;
            }
            introBanner.alpha = 1f;
            yield return new WaitForSecondsRealtime(hold);
            for (float t = 0f; t < fadeOut; t += Time.unscaledDeltaTime)
            {
                introBanner.alpha = 1f - t / fadeOut;
                yield return null;
            }
            introBanner.alpha = 0f;
            introBanner.gameObject.SetActive(false);
            intro = null;
        }

        // ------------------------------------------------------------------ super powers

        void OnBoosterClicked(string boosterId)
        {
            if (!open || stage != Stage.Playing || !board) return;
            if (!charges.TryGetValue(boosterId, out int left) || left <= 0) return;
            if (!board.UseBooster(boosterId)) return;
            // The charge is taken off when the power actually fires, which for an aimed one happens
            // on the next tap; the board says so through BoosterSpent.
            RenderBoosters();
        }

        void OnBoosterSpent(string boosterId)
        {
            if (charges.TryGetValue(boosterId, out int left)) charges[boosterId] = Mathf.Max(0, left - 1);
            RenderBoosters();
        }

        void OnBoosterArmed(string boosterId)
        {
            RenderBoosters();
            // The board has no room for a standing hint line, so an armed power says its piece over
            // the field and gets out of the way again.
            var booster = FindBooster(boosterId);
            if (booster != null) boardFx?.Say(Vector2.zero, booster.Hint, new Color(1f, 0.86f, 0.35f), 44f);
        }



        FinikBooster FindBooster(string boosterId)
        {
            if (string.IsNullOrEmpty(boosterId) || game == null) return null;
            foreach (var booster in FinikBoosterCatalog.For(game.Id))
                if (booster.Id == boosterId) return booster;
            return null;
        }

        void RenderBoosters()
        {
            var set = game != null ? FinikBoosterCatalog.For(game.Id) : Array.Empty<FinikBooster>();
            string armed = board ? board.ArmedBooster : null;
            for (int i = 0; i < boosterSlots.Length; i++)
            {
                var slot = boosterSlots[i];
                if (!slot) continue;
                var booster = i < set.Count ? set[i] : null;
                int left = booster != null && charges.TryGetValue(booster.Id, out int value) ? value : 0;
                slot.Show(booster, booster != null && art ? art.Get(booster.Icon) : null, left, booster != null && booster.Id == armed);
            }
        }
    }
}
