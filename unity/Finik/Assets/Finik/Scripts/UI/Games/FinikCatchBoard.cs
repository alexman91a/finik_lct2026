using System.Collections;
using System.Collections.Generic;
using Finik.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Finik.UI.Games
{
    /// <summary>
    /// «Лови монетки»: the basket follows the finger along the bottom of the field. Coins are worth
    /// catching, impulse buys are not — catching one of those costs a life, exactly like letting a
    /// coin fall through. Everything is kept in field-relative coordinates (0..1), so the level
    /// survives a turn of the phone without the drops jumping.
    /// </summary>
    public sealed class FinikCatchBoard : FinikMiniGameBoard, IPointerDownHandler, IDragHandler
    {
        /// <summary>Height of the basket's middle as a share of the field; a drop is caught at its rim, above that.</summary>
        const float BasketY = 0.12f;
        /// <summary>The basket is the hero of this game: over a third of the shaft, not a small toy at its foot.</summary>
        const float BasketWidthShare = 0.35f;
        /// <summary>The basket picture is square and sits in a slot this much lower than it is wide.</summary>
        const float BasketAspect = 0.8f;
        const float DropSizeShare = 0.14f;
        /// <summary>
        /// How far a coin may hang off the rim and still go in, as a share of its size. Forgiving for small
        /// fingers, but a coin that only brushes a handle falls past instead of vanishing behind it.
        /// </summary>
        const float CatchOverhang = 0.3f;
        /// <summary>Shaft widths per second a drop caught off-centre rolls along the rim towards the mouth.</summary>
        const float RollSpeed = 1.3f;
        /// <summary>A sunk drop is a little smaller: it is further away, inside the basket.</summary>
        const float SunkScale = 0.8f;
        /// <summary>Seconds a new drop takes to pop in at the top of the shaft.</summary>
        const float PopInSeconds = 0.2f;
        const float DespawnBottom = -0.14f;
        /// <summary>Frame between the shaft and the plate around it.</summary>
        const float Rim = 12f;

        sealed class Drop
        {
            public RectTransform rect;
            public float x;
            public float y;
            public bool junk;
            public bool passed;
            public float age;
            /// <summary>Caught on the rim: it rolls into the mouth and sinks, and lands once it is out of sight.</summary>
            public bool caught;
            /// <summary>Offset from the middle of the mouth: the basket keeps moving, a caught drop goes with it.</summary>
            public float offset;
            /// <summary>All of it is over the mouth, so it is drawn inside the basket, behind the front rim.</summary>
            public bool inside;
            /// <summary>Where it started to sink: how far it has gone decides how small it has got.</summary>
            public float sinkFromY;
        }

        [SerializeField] string basketSprite = "game_basket";
        [SerializeField] string coinSprite = "icon_coin";
        [Tooltip("Sky the drops fall through. Without it the shaft was a flat dark box that looked unfinished.")]
        [SerializeField] string backdropSprite = "game_catch_bg";
        [Tooltip("Rounded shape the shaft is clipped to, so the sky follows the frame's corners.")]
        [SerializeField] string maskSprite = "gameui_mask";
        [SerializeField] string heartSprite = "game_heart";

        readonly List<Drop> drops = new();
        FinikCatchLevel level;
        /// <summary>Unscaled time the magnet and the slow-down run out at, and whether a shield is up.</summary>
        float magnetUntil;
        float slowUntil;
        bool shield;
        /// <summary>Holds the two halves of the basket picture and, between them, whatever is falling into it.</summary>
        RectTransform basket;
        /// <summary>Layer between the back and the front half: a drop in here is drawn inside the basket.</summary>
        RectTransform basketInside;
        /// <summary>Soft shadow on the grass under the basket: it says "this moves", not "this is scenery".</summary>
        RectTransform basketShadow;
        /// <summary>Everything that falls lives in here: a masked window, so nothing is drawn over the frame.</summary>
        RectTransform shaft;
        float basketX = 0.5f;
        float nextDropIn;
        int caught;
        int misses;

        public override string GoalIcon => "icon_coin";
        public override string GoalValue => level == null ? string.Empty : $"{caught}/{level.Target}";
        public override float GoalProgress => level == null || level.Target == 0 ? 0f : Mathf.Clamp01(caught / (float)level.Target);
        // A heart, not the shield: the shield is also one of the powers right under it.
        public override string CounterIcon => art && art.Has(heartSprite) ? heartSprite : "icon_mood";
        public override string CounterValue => level == null ? string.Empty : LivesLeft.ToString();
        public override string CounterLabel => FinikTypography.Plural(LivesLeft, "жизнь", "жизни", "жизней");
        public override bool CounterLow => level != null && LivesLeft <= 1;
        public override string LossReason => "Закончились жизни";

        int LivesLeft => level == null ? 0 : Mathf.Max(0, level.Lives - misses);

        public override bool UseBooster(string boosterId)
        {
            if (!Running || level == null) return false;
            switch (boosterId)
            {
                case FinikBoosterId.Magnet:
                    magnetUntil = Time.unscaledTime + 6f;
                    Spend(boosterId);
                    return true;
                case FinikBoosterId.Slow:
                    slowUntil = Time.unscaledTime + 6f;
                    Spend(boosterId);
                    return true;
                case FinikBoosterId.Shield:
                    // A second shield on top of a standing one would be a charge thrown away.
                    if (shield) return false;
                    shield = true;
                    Spend(boosterId);
                    return true;
                default:
                    return false;
            }
        }

        protected override bool Accepts(FinikMiniGameLevel candidate) => candidate is FinikCatchLevel;

        protected override void Begin(FinikMiniGameLevel candidate)
        {
            level = (FinikCatchLevel)candidate;
            caught = 0;
            misses = 0;
            basketX = 0.5f;
            nextDropIn = 0.45f;
            magnetUntil = 0f;
            slowUntil = 0f;
            shield = false;

            // Invisible catcher over the whole field: the drag handlers below live on this component,
            // and the event system walks up from whatever graphic the finger actually hit.
            var touch = NewPiece("Touch");
            Fill(touch);
            var catcher = touch.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;

            BuildShaft();
            var shadow = NewImage("BasketShadow", shaft, "hud_glow_round");
            shadow.color = new Color(0.05f, 0.12f, 0.02f, 0.45f);
            shadow.preserveAspect = false;
            basketShadow = shadow.rectTransform;
            BuildBasket();
            Relayout();
        }

        protected override void Clear()
        {
            drops.Clear();
            basket = null;
            basketInside = null;
            basketShadow = null;
            shaft = null;
            level = null;
            ClearField();
        }

        /// <summary>The masked window with the sky in it. Drops and the basket are its children.</summary>
        void BuildShaft()
        {
            shaft = NewPiece("Shaft");
            var shape = shaft.gameObject.AddComponent<Image>();
            if (art) art.Apply(shape, maskSprite);
            shape.type = Image.Type.Sliced;
            shape.raycastTarget = false;
            var mask = shaft.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            var sky = NewImage("Sky", shaft, backdropSprite, preserveAspect: false);
            if (art && art.Has(backdropSprite))
            {
                // Cover the shaft whatever its proportions: cropped, never stretched.
                var sprite = sky.sprite;
                var fitter = sky.gameObject.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = sprite ? sprite.rect.width / Mathf.Max(1f, sprite.rect.height) : 2f / 3f;
            }
            else
            {
                // No picture yet: a plain sky gradient is still better than the dark well.
                sky.sprite = null;
                sky.color = new Color(0.62f, 0.8f, 1f);
                Fill(sky.rectTransform);
            }
        }

        /// <summary>
        /// The basket is its picture cut along the front of the rim: the back half is drawn under the drops,
        /// the front half over the ones going in, so a coin drops into the basket instead of behind it.
        /// </summary>
        void BuildBasket()
        {
            basket = NewPiece("Basket", shaft);
            var sprite = art ? art.Get(basketSprite) : null;
            AddHalf("Back", sprite, FinikBasketLayer.Part.Back);
            basketInside = NewPiece("Inside", basket);
            AddHalf("Front", sprite, FinikBasketLayer.Part.Front);
        }

        void AddHalf(string name, Sprite sprite, FinikBasketLayer.Part part)
        {
            var piece = NewPiece(name, basket);
            Fill(piece);
            var half = piece.gameObject.AddComponent<FinikBasketLayer>();
            half.raycastTarget = false;
            half.Sprite = sprite;
            half.Half = part;
        }

        protected override void Relayout()
        {
            if (level == null) return;
            var size = Field.rect.size;
            var window = new Vector2(PlayWidth, size.y) - new Vector2(Rim * 2f, Rim * 2f);
            // The plate frames the shaft, not the whole field — on a wide screen the rest is margin.
            FitTray(window, Rim, Vector2.zero);
            if (shaft)
            {
                shaft.sizeDelta = window;
                shaft.anchoredPosition = Vector2.zero;
            }
            float basketWidth = PlayWidth * BasketWidthShare;
            if (basket)
            {
                // Square, like the picture: the cut along the rim is measured in its proportions.
                basket.sizeDelta = new Vector2(BasketSide, BasketSide);
                basket.anchoredPosition = Local(basketX, BasketY);
            }
            if (basketShadow) basketShadow.sizeDelta = new Vector2(basketWidth * 1.1f, basketWidth * 0.3f);
            PlaceShadow();
            float dropSize = PlayWidth * DropSizeShare;
            foreach (var drop in drops)
            {
                if (!drop.rect) continue;
                drop.rect.sizeDelta = new Vector2(dropSize, dropSize);
                Place(drop);
            }
        }

        /// <summary>
        /// The shaft things fall down: as wide as the field on a phone, but capped on a wide screen.
        /// Sizing everything off the full width made the coins as big as the basket in landscape.
        /// </summary>
        float PlayWidth => Mathf.Min(Field.rect.width, Field.rect.height * 0.8f);

        /// <summary>Shaft-relative (0..1 across, 0..1 up) to the field's own centred coordinates.</summary>
        Vector2 Local(float x, float y)
        {
            var size = Field.rect.size;
            return new Vector2((x - 0.5f) * PlayWidth, (y - 0.5f) * size.y);
        }

        float BasketSide => PlayWidth * BasketWidthShare * BasketAspect;

        /// <summary>A point of the basket picture (0..1 across, 0..1 up) in shaft-relative coordinates.</summary>
        float BasketPointX(float u) => basketX + (u - 0.5f) * BasketSide / Mathf.Max(1f, PlayWidth);
        float BasketPointY(float v) => BasketY + (v - 0.5f) * BasketSide / Mathf.Max(1f, Field.rect.height);

        /// <summary>Half a drop, in shaft heights.</summary>
        float DropHalfHeight => PlayWidth * DropSizeShare * 0.5f / Mathf.Max(1f, Field.rect.height);

        /// <summary>Puts a drop where its field position says, in the coordinates of the layer it lives in.</summary>
        void Place(Drop drop)
        {
            if (!drop.rect) return;
            var at = Local(drop.x, drop.y);
            // The layer inside the basket moves, bounces and shakes with it.
            if (basket && drop.rect.parent == basketInside) at -= basket.anchoredPosition;
            drop.rect.anchoredPosition = at;
        }

        /// <summary>Moves a drop from the open shaft to the inside of the basket, between its two halves.</summary>
        void PutInside(Drop drop)
        {
            if (drop.rect && basketInside) drop.rect.SetParent(basketInside, false);
        }

        // ------------------------------------------------------------------ input

        public void OnPointerDown(PointerEventData eventData) => MoveBasket(eventData);

        public void OnDrag(PointerEventData eventData) => MoveBasket(eventData);

        void MoveBasket(PointerEventData eventData)
        {
            if (!Running || !basket) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Field, eventData.position, eventData.pressEventCamera, out var local))
                return;
            float half = BasketWidthShare / 2f;
            basketX = Mathf.Clamp(local.x / Mathf.Max(1f, PlayWidth) + 0.5f, half, 1f - half);
            basket.anchoredPosition = Local(basketX, BasketY);
            PlaceShadow();
        }

        void PlaceShadow()
        {
            if (!basketShadow || !basket) return;
            basketShadow.anchoredPosition = basket.anchoredPosition - new Vector2(0f, basket.sizeDelta.y * 0.42f);
        }

        // ------------------------------------------------------------------ simulation

        void Update()
        {
            if (!Running || level == null) return;
            float dt = Time.unscaledDeltaTime;

            // The level speeds up as the goal gets closer, so the last coins are the ones worth catching.
            float pace = level.Target > 0 ? Mathf.Clamp01(caught / (float)level.Target) : 0f;
            float speed = Mathf.Lerp(level.StartSpeed, level.EndSpeed, pace);
            if (Time.unscaledTime < slowUntil) speed *= 0.5f;
            bool magnet = Time.unscaledTime < magnetUntil;

            nextDropIn -= dt;
            if (nextDropIn <= 0f)
            {
                Spawn();
                nextDropIn = Mathf.Lerp(level.StartInterval, level.EndInterval, pace);
            }

            // A drop is decided the moment its bottom reaches the front of the rim: over the basket it goes
            // in, anywhere else it falls past. Deciding any later showed coins half-hidden by the wall.
            float rimY = BasketPointY(FinikBasketLayer.FrontEdgeV);
            float dropHalf = DropHalfHeight;

            for (int i = drops.Count - 1; i >= 0; i--)
            {
                var drop = drops[i];
                drop.age += dt;
                if (drop.caught)
                {
                    if (!Sink(drop, speed, dt)) continue;
                    bool junk = drop.junk;
                    // Take it off the field first: landing can end the level, and the screen answers
                    // that by stopping the board, which empties the list under our feet.
                    Remove(i);
                    Land(junk);
                    if (!Running) return;
                    continue;
                }

                // A new drop swells into view inside the shaft instead of being sliced by its top edge.
                if (drop.rect && drop.age < PopInSeconds * 1.5f)
                {
                    float s = FinikUiMotion.EaseOutBack(Mathf.Clamp01(drop.age / PopInSeconds), 1.6f);
                    drop.rect.localScale = new Vector3(s, s, 1f);
                }
                drop.y -= speed * dt;
                // The magnet pulls the coins over the basket; junk keeps falling where it fell.
                if (magnet && !drop.junk) drop.x = Mathf.MoveTowards(drop.x, basketX, dt * 1.1f);

                if (!drop.passed && drop.y - dropHalf <= rimY)
                {
                    if (Overlaps(drop))
                    {
                        Catch(drop);
                        continue;
                    }
                    // Missed: from here on it falls past the basket, in front of it.
                    drop.passed = true;
                }
                Place(drop);
                if (drop.y >= DespawnBottom) continue;

                // Fell past the basket: a missed coin costs a life, a missed impulse buy is the point.
                bool wasCoin = !drop.junk;
                Remove(i);
                if (wasCoin && Miss()) return;
            }
        }

        /// <summary>The drop is over the rim of the basket, or hangs off it by less than <see cref="CatchOverhang"/>.</summary>
        bool Overlaps(Drop drop)
        {
            float rim = BasketSide * FinikBasketLayer.RimHalfWidth / Mathf.Max(1f, PlayWidth);
            float mouth = BasketPointX(FinikBasketLayer.MouthU);
            return Mathf.Abs(drop.x - mouth) <= rim + DropSizeShare * CatchOverhang;
        }

        /// <summary>The drop reached the rim over the basket: from now on it only goes into it.</summary>
        void Catch(Drop drop)
        {
            drop.caught = true;
            drop.offset = drop.x - BasketPointX(FinikBasketLayer.MouthU);
            // A long frame can carry it deep into the wall; it still goes in from there, never from below the end.
            drop.y = Mathf.Max(drop.y, SunkY);
            if (drop.rect) drop.rect.localScale = Vector3.one;
            Place(drop);
        }

        /// <summary>
        /// Moves a caught drop into the basket, following it as it moves. Caught off-centre, it first rolls
        /// along the rim in front of the basket until all of it is over the mouth — sinking straight away
        /// would slice it on the rim's side. Then it drops between the back and the front of the basket
        /// until the front edge has covered it. True once it is out of sight and can count.
        /// </summary>
        bool Sink(Drop drop, float speed, float dt)
        {
            float width = Mathf.Max(1f, PlayWidth);
            float fits = Mathf.Max(0f, FinikBasketLayer.MouthHalfWidth * BasketSide - PlayWidth * DropSizeShare * 0.5f) / width;
            drop.offset = Mathf.MoveTowards(drop.offset, 0f, RollSpeed * dt);
            if (!drop.inside && Mathf.Abs(drop.offset) <= fits)
            {
                drop.inside = true;
                drop.sinkFromY = drop.y;
                PutInside(drop);
            }
            float sunk = SunkY;
            if (drop.inside)
            {
                drop.y = Mathf.Max(drop.y - speed * dt, sunk);
                if (drop.rect)
                {
                    float s = Mathf.Lerp(1f, SunkScale, Mathf.InverseLerp(drop.sinkFromY, sunk, drop.y));
                    drop.rect.localScale = new Vector3(s, s, 1f);
                }
            }
            drop.x = BasketPointX(FinikBasketLayer.MouthU) + drop.offset;
            Place(drop);
            return drop.inside && drop.y <= sunk;
        }

        /// <summary>Where a sunk drop's middle is when its top has just gone under the front rim.</summary>
        float SunkY => BasketPointY(FinikBasketLayer.FrontEdgeV) - DropHalfHeight * SunkScale;

        void Land(bool junk)
        {
            if (junk)
            {
                Miss();
                return;
            }
            caught++;
            RaiseChanged();
            if (basket) StartCoroutine(Bounce(basket));
            if (fx)
            {
                var where = Local(basketX, BasketY + 0.04f);
                float size = PlayWidth * DropSizeShare;
                fx.Flash(where, size * 1.6f, new Color(1f, 0.95f, 0.6f, 0.8f));
                fx.Burst(where, new Color(1f, 0.82f, 0.25f), size, 8);
                fx.Say(where, "+1", new Color(0.13f, 0.66f, 0.33f), size * 0.5f);
            }
            if (caught >= level.Target) Finish(true);
        }

        /// <summary>Counts a miss and reports the loss when the lives run out. True when the level ended.</summary>
        bool Miss()
        {
            if (shield)
            {
                // The shield eats exactly one miss, and says so where the miss happened.
                shield = false;
                RaiseChanged();
                if (fx) fx.Say(Local(basketX, BasketY + 0.06f), "Щит!", new Color(0.29f, 0.62f, 1f),
                    PlayWidth * DropSizeShare * 0.45f);
                return false;
            }
            misses++;
            RaiseChanged();
            if (basket) StartCoroutine(Shake(basket));
            if (fx)
            {
                fx.Shake(Field, 16f);
                fx.Say(Local(basketX, BasketY + 0.06f), "Мимо", new Color(0.88f, 0.29f, 0.29f),
                    PlayWidth * DropSizeShare * 0.45f);
            }
            if (misses < level.Lives) return false;
            Finish(false);
            return true;
        }

        void Remove(int index)
        {
            var drop = drops[index];
            if (drop.rect) Destroy(drop.rect.gameObject);
            drops.RemoveAt(index);
        }

        void Spawn()
        {
            bool junk = Random.value < level.JunkShare;
            string sprite = junk
                ? FinikMiniGameCatalog.JunkSprites[Random.Range(0, FinikMiniGameCatalog.JunkSprites.Length)]
                : coinSprite;
            var image = NewImage(junk ? "Junk" : "Coin", shaft ? shaft : Field, sprite);
            float margin = BasketWidthShare / 2f;
            float size = PlayWidth * DropSizeShare;
            // Fully inside the shaft, just under its top edge.
            float top = 1f - (size * 0.6f) / Mathf.Max(1f, Field.rect.height);
            var drop = new Drop
            {
                rect = image.rectTransform,
                x = Random.Range(margin, 1f - margin),
                y = top,
                junk = junk
            };
            drop.rect.localScale = Vector3.zero;
            drop.rect.sizeDelta = new Vector2(size, size);
            drop.rect.anchoredPosition = Local(drop.x, drop.y);
            drops.Add(drop);
        }

        static IEnumerator Bounce(RectTransform rect)
        {
            const float seconds = 0.18f;
            for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
            {
                if (!rect) yield break;
                float s = 1f + 0.16f * Mathf.Sin(Mathf.PI * t / seconds);
                rect.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            if (rect) rect.localScale = Vector3.one;
        }

        static IEnumerator Shake(RectTransform rect)
        {
            const float seconds = 0.25f;
            var start = rect.localRotation;
            for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
            {
                if (!rect) yield break;
                float angle = 9f * Mathf.Sin(t / seconds * Mathf.PI * 4f) * (1f - t / seconds);
                rect.localRotation = Quaternion.Euler(0f, 0f, angle);
                yield return null;
            }
            if (rect) rect.localRotation = start;
        }
    }
}
