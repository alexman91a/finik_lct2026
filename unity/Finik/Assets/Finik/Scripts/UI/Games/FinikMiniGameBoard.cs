using System;
using System.Collections.Generic;
using Finik.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Games
{
    /// <summary>
    /// Shared plumbing of the three mood games. A board owns its own rect — everything it draws is a
    /// child of it — is dormant until <see cref="Play"/>, and stops touching the field the moment it
    /// reports a result. It re-lays itself out whenever the field changes size, so turning the phone
    /// mid-game never scrambles a board.
    /// </summary>
    /// <summary>One of several goals on the goal card: its picture and how many are still to go.</summary>
    public readonly struct FinikGoalItem
    {
        public readonly string Icon;
        public readonly int Left;

        public FinikGoalItem(string icon, int left)
        {
            Icon = icon;
            Left = left;
        }

        public bool Done => Left <= 0;
    }

    public abstract class FinikMiniGameBoard : MonoBehaviour
    {
        [SerializeField] protected FinikGameArt art;
        [Tooltip("Rounded plate the pieces sit on; sized by the board to whatever it lays out.")]
        [SerializeField] string traySprite = "gameui_board";
        [Tooltip("Sparks, flashes and rising labels over the board.")]
        [SerializeField] protected FinikGameFx fx;

        /// <summary>True when the level was cleared, false when it was lost (out of moves, out of lives).</summary>
        public event Action<bool> Finished;
        /// <summary>Raised whenever one of the two counter lines changed.</summary>
        public event Action Changed;
        /// <summary>A super power actually fired, so the screen takes one charge off it.</summary>
        public event Action<string> BoosterSpent;
        /// <summary>A power that needs a target was armed (or disarmed, with null): the screen says so.</summary>
        public event Action<string> BoosterArmed;

        RectTransform fieldRect;
        Vector2 lastSize;
        Image tray;

        public bool Running { get; private set; }

        /// <summary>
        /// The area the board draws into: its own rect. Cached through Unity's own null check, not
        /// <c>??=</c> — a cached reference to a destroyed object is not C# null, and reading it throws.
        /// </summary>
        protected RectTransform Field
        {
            get
            {
                if (!fieldRect) fieldRect = (RectTransform)transform;
                return fieldRect;
            }
        }

        /// <summary>
        /// The two numbers the HUD shows, each as a picture and a bare value: a player reads
        /// "🧊 12/50" faster than "Кубики: 12 / 50", and the picture also says what to collect.
        /// </summary>
        public abstract string GoalIcon { get; }
        public abstract string GoalValue { get; }
        /// <summary>How far the goal is, 0..1, for the progress bar under the goal line.</summary>
        public abstract float GoalProgress { get; }
        /// <summary>
        /// A level with more than one goal lists them here, each with what is left of it, and the goal
        /// card shows the list instead of the one line. Empty for a single goal.
        /// </summary>
        public virtual IReadOnlyList<FinikGoalItem> GoalItems => Array.Empty<FinikGoalItem>();
        /// <summary>
        /// Where goal <c>i</c>'s picture sits on the goal card, so a board can fly pieces into it.
        /// Set by the screen; null means there is nowhere to fly to.
        /// </summary>
        public Func<int, RectTransform> GoalAnchor { get; set; }
        /// <summary>What is running out: moves, turns taken, lives left.</summary>
        public abstract string CounterIcon { get; }
        public abstract string CounterValue { get; }
        /// <summary>
        /// The word under the counter ("ходов", "жизни", "попыток"). A bare number next to a picture
        /// left the player guessing whether it counted moves, lives or mistakes.
        /// </summary>
        public abstract string CounterLabel { get; }
        /// <summary>True when the counter is about to run out: the HUD turns it red.</summary>
        public virtual bool CounterLow => false;
        /// <summary>
        /// Why a lost level was lost, in the child's words ("Закончились ходы"). The result card leads
        /// with it: "almost made it" alone left the player guessing what went wrong.
        /// </summary>
        public virtual string LossReason => "Не получилось";

        /// <summary>Starts <paramref name="level"/> on a cleared field. Ignores a level it was not written for.</summary>
        public void Play(FinikMiniGameLevel level)
        {
            StopGame();
            if (!Accepts(level))
            {
                Debug.LogWarning($"[FinikGames] {GetType().Name} cannot play {level?.GetType().Name ?? "null"}.");
                return;
            }
            gameObject.SetActive(true);
            lastSize = Field.rect.size;
            Running = true;
            Begin(level);
            Changed?.Invoke();
        }

        /// <summary>Ends the level without a result and empties the field. Safe to call at any time.</summary>
        public void StopGame()
        {
            Running = false;
            StopAllCoroutines();
            if (fx) fx.StopAll();
            ArmedBooster = null;
            Clear();
        }

        protected abstract bool Accepts(FinikMiniGameLevel level);
        protected abstract void Begin(FinikMiniGameLevel level);

        /// <summary>Removes everything the board put on the field and forgets its state.</summary>
        protected abstract void Clear();

        /// <summary>Places the pieces for the field's current size. Called on every size change.</summary>
        protected abstract void Relayout();

        protected void RaiseChanged() => Changed?.Invoke();

        /// <summary>
        /// Fires a super power. True means the board took it: a power that works at once has already
        /// run and reported its charge, one that needs a target is now armed and will report when it
        /// actually goes off. A board that does not know the power says false and nothing is spent.
        /// </summary>
        public virtual bool UseBooster(string boosterId) => false;

        /// <summary>The power waiting for the player to pick a piece, or null.</summary>
        public string ArmedBooster { get; private set; }

        protected void Arm(string boosterId)
        {
            ArmedBooster = boosterId;
            BoosterArmed?.Invoke(boosterId);
        }

        protected void Spend(string boosterId)
        {
            if (ArmedBooster == boosterId) Arm(null);
            BoosterSpent?.Invoke(boosterId);
            RaiseChanged();
        }

        /// <summary>Reports the level as cleared or lost. The first call wins; later ones are ignored.</summary>
        protected void Finish(bool won)
        {
            if (!Running) return;
            Running = false;
            Changed?.Invoke();
            Finished?.Invoke(won);
        }

        protected virtual void LateUpdate()
        {
            if (!Running) return;
            var size = Field.rect.size;
            if ((size - lastSize).sqrMagnitude < 0.5f) return;
            lastSize = size;
            Relayout();
        }

        /// <summary>True inside OnDisable, where Unity forbids moving children to another parent.</summary>
        bool disabling;

        void OnDisable()
        {
            disabling = true;
            try { StopGame(); }
            finally { disabling = false; }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Square cells that fill the field: as big as they can be while the whole grid still fits,
        /// with gaps of <paramref name="gapRatio"/> of a cell between and around them.
        /// </summary>
        /// <param name="verticalBias">Where the grid sits in the leftover height: 0 top, 0.5 centre, 1 bottom.</param>
        protected void Metrics(int columns, int rows, float gapRatio, out float cell, out float step, out Vector2 origin,
            float verticalBias = 0.5f)
        {
            var size = Field.rect.size;
            float unitX = size.x / (columns + gapRatio * (columns + 1));
            float unitY = size.y / (rows + gapRatio * (rows + 1));
            cell = Mathf.Max(1f, Mathf.Min(unitX, unitY));
            float gap = cell * gapRatio;
            step = cell + gap;
            // Cell (0,0) is the top-left one. A square grid never fills a phone screen, so the leftover
            // height is split by the bias: sitting a little above centre leaves the free space in one
            // piece at the bottom, next to the pet, instead of as two empty bands.
            float free = Mathf.Max(0f, size.y - ((rows - 1) * step + cell));
            float shift = free * (0.5f - Mathf.Clamp01(verticalBias));
            origin = new Vector2(-(columns - 1) * step / 2f, (rows - 1) * step / 2f + shift);
        }

        protected static Vector2 CellPosition(Vector2 origin, float step, int column, int row) =>
            origin + new Vector2(column * step, -row * step);

        /// <summary>
        /// Puts the board's plate behind everything, sized to what the board actually laid out plus a
        /// margin. Called from Relayout, so the plate follows the grid through every size change.
        /// </summary>
        protected void FitTray(Vector2 size, float padding, Vector2 center = default, Color? tint = null)
        {
            if (!tray)
            {
                // ClearField detaches the old plate at once, so whatever is found here is this round's.
                var existing = Field.Find("Tray");
                tray = existing ? existing.GetComponent<Image>() : NewImage("Tray", Field, traySprite, preserveAspect: false);
                tray.type = Image.Type.Sliced;
            }
            tray.color = tint ?? Color.white;
            tray.transform.SetAsFirstSibling();
            tray.rectTransform.sizeDelta = size + new Vector2(padding * 2f, padding * 2f);
            tray.rectTransform.anchoredPosition = center;
        }

        /// <summary>The size a <paramref name="columns"/> x <paramref name="rows"/> grid takes up.</summary>
        protected static Vector2 GridSize(int columns, int rows, float cell, float step) =>
            new((columns - 1) * step + cell, (rows - 1) * step + cell);

        /// <summary>Middle of the grid the <paramref name="origin"/> describes, so the tray lands on it.</summary>
        protected static Vector2 GridCenter(Vector2 origin, float step, int columns, int rows) =>
            new(origin.x + (columns - 1) * step / 2f, origin.y - (rows - 1) * step / 2f);

        /// <summary>A plain picture holder centred on the field, without a layout group in the way.</summary>
        protected RectTransform NewPiece(string name, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent ? parent : Field, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            return rect;
        }

        protected Image NewImage(string name, Transform parent, string sprite, bool raycast = false, bool preserveAspect = true)
        {
            var rect = NewPiece(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            if (art) art.Apply(image, sprite);
            image.preserveAspect = preserveAspect;
            image.raycastTarget = raycast;
            return image;
        }

        /// <summary>Stretches a child over its whole parent with the given inset.</summary>
        protected static void Fill(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        protected void ClearField()
        {
            tray = null;
            for (int i = Field.childCount - 1; i >= 0; i--)
            {
                var child = Field.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    // Destroy only takes effect at the end of the frame. A level restarted in the same
                    // frame would otherwise find last round's pieces — its plate above all — still
                    // here, adopt them, and lose them a moment later: so they are renamed out of the
                    // way and, where Unity allows it, taken off the field at once.
                    child.name = "_Cleared";
                    child.SetActive(false);
                    if (!disabling) child.transform.SetParent(null, false);
                    Destroy(child);
                }
                else DestroyImmediate(child);
            }
        }
    }
}
