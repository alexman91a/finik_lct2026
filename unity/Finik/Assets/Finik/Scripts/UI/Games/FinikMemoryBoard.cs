using System;
using System.Collections;
using System.Collections.Generic;
using Finik.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Games
{
    /// <summary>
    /// «Найди пару»: every picture is on exactly two cards. The whole board is shown face up for a
    /// couple of seconds first — the level is called "remember where things are", so the child is
    /// given something to remember — and then turns over. Two cards may be open at a time; a pair
    /// stays open, anything else turns back. There is nothing to lose here: the counter only tells
    /// the child how many mistakes it took.
    /// </summary>
    public sealed class FinikMemoryBoard : FinikMiniGameBoard
    {
        const float FlipSeconds = 0.16f;
        const float MismatchHold = 0.75f;
        const float GapRatio = 0.14f;

        sealed class Card
        {
            public RectTransform root;
            public Image back;
            public Image face;
            public Button button;
            public int picture;
            /// <summary>Place in the deal; the column and row follow from it and the current grid.</summary>
            public int slot;
            public bool faceUp;
            public bool matched;
            public Coroutine flip;
        }

        [Tooltip("Sprite on the hidden side of every card.")]
        [SerializeField] string backSprite = "game_card_back";
        [Tooltip("The same back drawn 3:4, for a tall phone where square cards would leave the board half empty.")]
        [SerializeField] string tallBackSprite = "game_card_back_tall";
        [Tooltip("Card-shaped plate the picture sits on while the card is open.")]
        [SerializeField] string faceSprite = "game_card_face";

        readonly List<Card> cards = new();
        FinikMemoryLevel level;
        Card first;
        int matchedPairs;
        /// <summary>Two cards that did not match. Nothing runs out here; the count is only for the child.</summary>
        int mistakes;
        bool busy;
        /// <summary>Columns of the grid as laid out now: picked per screen, not taken from the level.</summary>
        int columns;
        /// <summary>Height over width of the cards as laid out now: 1, or <see cref="TallAspect"/>.</summary>
        float aspect = 1f;

        const float TallAspect = 4f / 3f;
        /// <summary>
        /// Largest card width as a share of the board's shorter side. Eight cards on a tall phone came
        /// out as four giant tiles next to the small cards of the hard level; the cap keeps the three
        /// levels one family, and a capped grid takes the taller cards to fill the height instead.
        /// </summary>
        const float MaxCardShare = 0.3f;

        public override string GoalIcon => "game_tile_memory";
        public override string GoalValue => level == null ? string.Empty : $"{matchedPairs}/{level.Pairs}";
        public override float GoalProgress => level == null || level.Pairs == 0 ? 0f : matchedPairs / (float)level.Pairs;
        // Nothing runs out here, so the counter shows mistakes: "turns taken" meant nothing to a child,
        // and a zero next to it read like a limit already used up.
        public override string CounterIcon => "game_miss";
        public override string CounterValue => mistakes.ToString();
        public override string CounterLabel => FinikTypography.Plural(mistakes, "ошибка", "ошибки", "ошибок");

        protected override bool Accepts(FinikMiniGameLevel candidate) => candidate is FinikMemoryLevel;

        protected override void Begin(FinikMiniGameLevel candidate)
        {
            level = (FinikMemoryLevel)candidate;
            matchedPairs = 0;
            mistakes = 0;
            first = null;
            busy = true;

            var deck = Deal(level.Pairs);
            for (int i = 0; i < deck.Count; i++) cards.Add(Build(deck[i], i));
            Relayout();
            StartCoroutine(Preview());
        }

        protected override void Clear()
        {
            cards.Clear();
            first = null;
            busy = false;
            level = null;
            ClearField();
        }

        /// <summary>Every picture twice, shuffled. The pictures themselves are drawn from the front of the sheet.</summary>
        static List<int> Deal(int pairs)
        {
            var deck = new List<int>(pairs * 2);
            int available = FinikMiniGameCatalog.CardSprites.Length;
            var pictures = new List<int>(available);
            for (int i = 0; i < available; i++) pictures.Add(i);
            // Which pictures are in play is drawn too, so replaying a level is not the same board again.
            Shuffle(pictures);
            for (int i = 0; i < pairs; i++)
            {
                int picture = pictures[i % available];
                deck.Add(picture);
                deck.Add(picture);
            }
            Shuffle(deck);
            return deck;
        }

        static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        Card Build(int picture, int slot)
        {
            var root = NewPiece($"Card{slot}");
            var card = new Card { root = root, picture = picture, slot = slot, faceUp = true };

            var faceHolder = NewImage("Face", root, faceSprite, raycast: true, preserveAspect: false);
            faceHolder.type = Image.Type.Sliced;
            Fill(faceHolder.rectTransform);
            var pictureImage = NewImage("Picture", faceHolder.transform,
                FinikMiniGameCatalog.CardSprites[picture % FinikMiniGameCatalog.CardSprites.Length]);
            // The plate's rounded corners must stay visible around the picture.
            Fill(pictureImage.rectTransform);
            pictureImage.rectTransform.anchorMin = new Vector2(0.14f, 0.14f);
            pictureImage.rectTransform.anchorMax = new Vector2(0.86f, 0.86f);
            pictureImage.rectTransform.offsetMin = Vector2.zero;
            pictureImage.rectTransform.offsetMax = Vector2.zero;
            card.face = faceHolder;

            var back = NewImage("Back", root, backSprite, raycast: true);
            Fill(back.rectTransform);
            card.back = back;
            back.gameObject.SetActive(false);

            card.button = root.gameObject.AddComponent<Button>();
            card.button.transition = Selectable.Transition.None;
            card.button.targetGraphic = back;
            var navigation = card.button.navigation;
            navigation.mode = UnityEngine.UI.Navigation.Mode.None;
            card.button.navigation = navigation;
            card.button.onClick.AddListener(() => Tap(card));
            return card;
        }

        protected override void Relayout()
        {
            if (level == null) return;
            bool tallArt = art && art.Has(tallBackSprite);
            Pick(level.Cards, Field.rect.size, level.Columns, tallArt, out columns, out aspect, out float width);
            int rows = (level.Cards + columns - 1) / columns;
            float gap = width * GapRatio;
            float height = width * aspect;
            var grid = new Vector2(columns * width + (columns - 1) * gap, rows * height + (rows - 1) * gap);
            var topLeft = new Vector2(-grid.x / 2f + width / 2f, grid.y / 2f - height / 2f);

            // No plate: the cards carry thick frames of their own, and a box around the box was one
            // rectangle too many.
            string back = aspect > 1.01f ? tallBackSprite : backSprite;
            foreach (var card in cards)
            {
                card.root.sizeDelta = new Vector2(width, height);
                card.root.anchoredPosition = topLeft + new Vector2(card.slot % columns * (width + gap), -(card.slot / columns) * (height + gap));
                if (art) art.Apply(card.back, back);
            }
        }

        /// <summary>
        /// The grid that gives the biggest cards in <paramref name="area"/>: eight cards are a 4x2
        /// strip in landscape but a 2x4 block on a tall phone, where a fixed four columns left the cards
        /// small and half the screen empty. Only column counts that fill every row are considered, and
        /// with the tall back available each is tried with square and with 3:4 cards.
        /// </summary>
        static void Pick(int cards, Vector2 area, int fallback, bool tallArt, out int bestColumns, out float bestAspect, out float bestWidth)
        {
            bestColumns = Mathf.Max(1, fallback);
            bestAspect = 1f;
            bestWidth = 1f;
            if (area.x < 1f || area.y < 1f || cards < 2) return;
            float bestArea = 0f;
            foreach (float shape in tallArt ? new[] { 1f, TallAspect } : new[] { 1f })
            for (int candidate = 2; candidate <= cards; candidate++)
            {
                if (cards % candidate != 0) continue;
                int rows = cards / candidate;
                // Gaps are a share of the card's width, on both axes and around the grid.
                float width = Mathf.Min(area.x / (candidate + GapRatio * (candidate + 1)),
                    area.y / (rows * shape + GapRatio * (rows + 1)),
                    Mathf.Min(area.x, area.y) * MaxCardShare);
                float size = width * width * shape;
                // Ties go to square cards in the level's own shape, so a roomy screen keeps the designed grid.
                bool better = size > bestArea * 1.02f
                    || (size >= bestArea * 0.98f && candidate == fallback && shape <= bestAspect);
                if (!better) continue;
                bestArea = size;
                bestColumns = candidate;
                bestAspect = shape;
                bestWidth = Mathf.Max(1f, width);
            }
        }

        IEnumerator Preview()
        {
            yield return new WaitForSecondsRealtime(level.PreviewSeconds);
            foreach (var card in cards) SetFaceUp(card, false, animate: true);
            yield return new WaitForSecondsRealtime(FlipSeconds * 2f);
            busy = false;
        }

        // ------------------------------------------------------------------ super powers

        public override bool UseBooster(string boosterId)
        {
            if (!Running || busy || level == null) return false;
            switch (boosterId)
            {
                case FinikBoosterId.Peek:
                    StartCoroutine(Peek());
                    return true;
                case FinikBoosterId.Pair:
                    return FindPairForPlayer();
                case FinikBoosterId.Reshuffle:
                    StartCoroutine(ReshuffleHidden());
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Turns everything that is still face down up for a moment, then back.</summary>
        IEnumerator Peek()
        {
            busy = true;
            Spend(FinikBoosterId.Peek);
            var shown = new List<Card>();
            foreach (var card in cards)
            {
                if (card.matched || card.faceUp) continue;
                SetFaceUp(card, true, animate: true);
                shown.Add(card);
            }
            yield return new WaitForSecondsRealtime(1.4f);
            foreach (var card in shown) SetFaceUp(card, false, animate: true);
            yield return new WaitForSecondsRealtime(FlipSeconds * 2f);
            busy = false;
        }

        /// <summary>Opens one pair the player has not found yet. False when there is nothing left to give.</summary>
        bool FindPairForPlayer()
        {
            var byPicture = new Dictionary<int, Card>();
            foreach (var card in cards)
            {
                if (card.matched) continue;
                if (byPicture.TryGetValue(card.picture, out var other))
                {
                    // Whatever the player had half-opened goes back down first.
                    if (first != null)
                    {
                        SetFaceUp(first, false, animate: true);
                        first = null;
                    }
                    SetFaceUp(other, true, animate: true);
                    SetFaceUp(card, true, animate: true);
                    Spend(FinikBoosterId.Pair);
                    StartCoroutine(Matched(other, card));
                    return true;
                }
                byPicture[card.picture] = card;
            }
            return false;
        }

        /// <summary>Deals the still-hidden pictures again between the cards that are still face down.</summary>
        IEnumerator ReshuffleHidden()
        {
            busy = true;
            Spend(FinikBoosterId.Reshuffle);
            if (first != null)
            {
                SetFaceUp(first, false, animate: true);
                first = null;
            }
            yield return new WaitForSecondsRealtime(FlipSeconds * 2f);

            var hidden = new List<Card>();
            foreach (var card in cards)
                if (!card.matched) hidden.Add(card);
            var pictures = new List<int>(hidden.Count);
            foreach (var card in hidden) pictures.Add(card.picture);
            Shuffle(pictures);
            for (int i = 0; i < hidden.Count; i++)
            {
                hidden[i].picture = pictures[i];
                var picture = hidden[i].face.transform.Find("Picture");
                if (picture && picture.TryGetComponent<Image>(out var image) && art)
                    art.Apply(image, FinikMiniGameCatalog.CardSprites[pictures[i] % FinikMiniGameCatalog.CardSprites.Length]);
                if (fx) fx.Burst(hidden[i].root.anchoredPosition, new Color(0.55f, 0.75f, 1f), hidden[i].root.rect.width, 4);
            }
            yield return new WaitForSecondsRealtime(0.2f);
            busy = false;
        }

        void Tap(Card card)
        {
            if (!Running || busy || card.matched || card.faceUp) return;
            SetFaceUp(card, true, animate: true);
            if (first == null)
            {
                first = card;
                return;
            }

            var second = card;
            var opened = first;
            first = null;

            if (opened.picture == second.picture)
            {
                StartCoroutine(Matched(opened, second));
                return;
            }
            mistakes++;
            RaiseChanged();
            StartCoroutine(Mismatched(opened, second));
        }

        IEnumerator Matched(Card a, Card b)
        {
            busy = true;
            a.matched = b.matched = true;
            a.button.interactable = b.button.interactable = false;
            matchedPairs++;
            RaiseChanged();
            if (fx)
            {
                float size = a.root.rect.width;
                foreach (var card in new[] { a, b })
                {
                    fx.Flash(card.root.anchoredPosition, size * 1.4f, new Color(1f, 1f, 1f, 0.8f));
                    fx.Burst(card.root.anchoredPosition, new Color(1f, 0.78f, 0.24f), size, 7);
                }
                fx.Say((a.root.anchoredPosition + b.root.anchoredPosition) / 2f, "Пара!", new Color(0.13f, 0.66f, 0.33f), size * 0.55f);
            }
            yield return Pop(a, b);
            busy = false;
            if (matchedPairs >= level.Pairs) Finish(true);
        }

        IEnumerator Mismatched(Card a, Card b)
        {
            busy = true;
            yield return new WaitForSecondsRealtime(MismatchHold);
            SetFaceUp(a, false, animate: true);
            SetFaceUp(b, false, animate: true);
            yield return new WaitForSecondsRealtime(FlipSeconds * 2f);
            busy = false;
        }

        /// <summary>A matched pair swells once and settles slightly smaller, so found pairs read as done.</summary>
        static IEnumerator Pop(Card a, Card b)
        {
            const float up = 0.16f, down = 0.18f;
            for (float t = 0; t < up; t += Time.unscaledDeltaTime)
            {
                float s = Mathf.Lerp(1f, 1.12f, t / up);
                a.root.localScale = b.root.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            for (float t = 0; t < down; t += Time.unscaledDeltaTime)
            {
                float s = Mathf.Lerp(1.12f, 0.94f, t / down);
                a.root.localScale = b.root.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            a.root.localScale = b.root.localScale = new Vector3(0.94f, 0.94f, 1f);
            var faded = new Color(1f, 1f, 1f, 0.72f);
            a.face.color = b.face.color = faded;
        }

        void SetFaceUp(Card card, bool up, bool animate)
        {
            if (card.faceUp == up) return;
            card.faceUp = up;
            if (!animate || !isActiveAndEnabled)
            {
                ApplySide(card);
                return;
            }
            if (card.flip != null) StopCoroutine(card.flip);
            card.flip = StartCoroutine(Flip(card));
        }

        IEnumerator Flip(Card card)
        {
            // Half a turn squashes the card to nothing, the sides swap, the other half opens it again.
            for (float t = 0; t < FlipSeconds; t += Time.unscaledDeltaTime)
            {
                card.root.localScale = new Vector3(Mathf.Lerp(1f, 0f, t / FlipSeconds), 1f, 1f);
                yield return null;
            }
            card.root.localScale = new Vector3(0f, 1f, 1f);
            ApplySide(card);
            for (float t = 0; t < FlipSeconds; t += Time.unscaledDeltaTime)
            {
                card.root.localScale = new Vector3(Mathf.Lerp(0f, 1f, t / FlipSeconds), 1f, 1f);
                yield return null;
            }
            card.root.localScale = Vector3.one;
            card.flip = null;
        }

        static void ApplySide(Card card)
        {
            card.face.gameObject.SetActive(card.faceUp);
            card.back.gameObject.SetActive(!card.faceUp);
        }
    }
}
