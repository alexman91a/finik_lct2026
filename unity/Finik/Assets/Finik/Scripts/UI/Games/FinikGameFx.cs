using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Games
{
    /// <summary>
    /// The juice layer over a game board: sparks, expanding rings, rising labels and a shake. Everything
    /// is pooled uGUI graphics driven by one Update — no packages, no particle system, and nothing that
    /// allocates once a level is running, so it stays smooth on a phone.
    ///
    /// Positions are in the layer's own centred coordinates, which is exactly what a board's pieces use:
    /// the layer stretches over the same rect as the boards do, so a piece's anchoredPosition can be
    /// handed over as is.
    /// </summary>
    public sealed class FinikGameFx : MonoBehaviour
    {
        const int MaxSparks = 96;
        const int MaxRings = 8;
        const int MaxLabels = 6;

        sealed class Spark
        {
            public RectTransform rect;
            public Image image;
            public Vector2 velocity;
            public float spin;
            public float life;
            public float age;
            public float size;
            public Color color;
        }

        sealed class Ring
        {
            public RectTransform rect;
            public Image image;
            public float from;
            public float to;
            public float life;
            public float age;
            public Color color;
        }

        sealed class Label
        {
            public RectTransform rect;
            public TMP_Text text;
            public float life;
            public float age;
            public Vector2 origin;
        }

        [SerializeField] Sprite sparkSprite;
        [SerializeField] Sprite starSprite;
        [SerializeField] Sprite ringSprite;
        [SerializeField] TMP_FontAsset font;
        [SerializeField] Material fontMaterial;
        [Tooltip("Downward pull on a spark, in layer units per second squared.")]
        [SerializeField] float gravity = 2600f;

        readonly List<Spark> sparks = new();
        readonly List<Ring> rings = new();
        readonly List<Label> labels = new();
        Coroutine shake;

        public void Configure(Sprite spark, Sprite star, Sprite ring, TMP_FontAsset textFont, Material textMaterial)
        {
            sparkSprite = spark;
            starSprite = star;
            ringSprite = ring;
            font = textFont;
            fontMaterial = textMaterial;
        }

        // Not cached: `??=` misses a destroyed Unity object, and a script reload can leave one behind.
        RectTransform Self => (RectTransform)transform;

        /// <summary>Puts every live effect away at once — used when a level ends or is abandoned.</summary>
        public void StopAll()
        {
            foreach (var spark in sparks) Retire(spark.rect);
            foreach (var ring in rings) Retire(ring.rect);
            foreach (var label in labels) Retire(label.rect);
            sparks.Clear();
            rings.Clear();
            labels.Clear();
            if (shake != null) StopCoroutine(shake);
            shake = null;
        }

        void OnDisable() => StopAll();

        // ------------------------------------------------------------------ effects

        /// <summary>A puff of sparks thrown out of <paramref name="at"/>: what a cleared piece leaves behind.</summary>
        public void Burst(Vector2 at, Color color, float size, int count = 9)
        {
            for (int i = 0; i < count && sparks.Count < MaxSparks; i++)
            {
                var rect = Take("Spark");
                var image = rect.GetComponent<Image>();
                // Every third one is a star, the rest are little chips: a mixed puff reads richer.
                image.sprite = i % 3 == 0 && starSprite ? starSprite : sparkSprite;
                image.color = color;
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float speed = Random.Range(0.9f, 2.1f) * size * 5f;
                float scale = Random.Range(0.18f, 0.34f) * size;
                rect.anchoredPosition = at + Random.insideUnitCircle * (size * 0.2f);
                rect.sizeDelta = new Vector2(scale, scale);
                rect.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
                sparks.Add(new Spark
                {
                    rect = rect,
                    image = image,
                    velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed + Vector2.up * speed * 0.35f,
                    spin = Random.Range(-540f, 540f),
                    life = Random.Range(0.42f, 0.68f),
                    size = scale,
                    color = color
                });
            }
        }

        /// <summary>A ring of light that swells and fades — the flash under a burst.</summary>
        public void Flash(Vector2 at, float size, Color color)
        {
            if (rings.Count >= MaxRings || !ringSprite) return;
            var rect = Take("Flash");
            var image = rect.GetComponent<Image>();
            image.sprite = ringSprite;
            image.color = color;
            rect.anchoredPosition = at;
            rect.localRotation = Quaternion.identity;
            rings.Add(new Ring { rect = rect, image = image, from = size * 0.5f, to = size * 2.1f, life = 0.36f, color = color });
        }

        /// <summary>A short word that rises and fades over the board: "+1", "Супер!".</summary>
        public void Say(Vector2 at, string message, Color color, float size = 54f)
        {
            if (labels.Count >= MaxLabels || !font) return;
            var rect = Take("Label", withImage: false);
            var text = rect.GetComponent<TMP_Text>();
            if (!text)
            {
                var added = rect.gameObject.AddComponent<TextMeshProUGUI>();
                if (!added) return;
                added.font = font;
                if (fontMaterial) added.fontSharedMaterial = fontMaterial;
                added.alignment = TextAlignmentOptions.Center;
                added.textWrappingMode = TextWrappingModes.NoWrap;
                added.raycastTarget = false;
                text = added;
            }
            text.text = message;
            text.fontSize = size;
            text.color = color;
            rect.sizeDelta = new Vector2(520f, size * 1.6f);
            rect.anchoredPosition = at;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one * 0.6f;
            labels.Add(new Label { rect = rect, text = text, life = 0.9f, origin = at });
        }

        /// <summary>Knocks <paramref name="target"/> about for a moment: a big clear, or a miss.</summary>
        public void Shake(RectTransform target, float strength = 14f, float seconds = 0.28f)
        {
            if (!target) return;
            if (shake != null) StopCoroutine(shake);
            shake = StartCoroutine(ShakeRoutine(target, strength, seconds));
        }

        IEnumerator ShakeRoutine(RectTransform target, float strength, float seconds)
        {
            var start = target.anchoredPosition;
            for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
            {
                if (!target) yield break;
                float fade = 1f - t / seconds;
                target.anchoredPosition = start + new Vector2(
                    Mathf.Sin(t * 74f) * strength * fade,
                    Mathf.Sin(t * 53f) * strength * fade * 0.6f);
                yield return null;
            }
            if (target) target.anchoredPosition = start;
            shake = null;
        }

        // ------------------------------------------------------------------ simulation

        void Update()
        {
            float dt = Time.unscaledDeltaTime;

            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                var spark = sparks[i];
                spark.age += dt;
                if (spark.age >= spark.life || !spark.rect)
                {
                    Retire(spark.rect);
                    sparks.RemoveAt(i);
                    continue;
                }
                spark.velocity.y -= gravity * dt;
                spark.rect.anchoredPosition += spark.velocity * dt;
                spark.rect.localRotation *= Quaternion.Euler(0, 0, spark.spin * dt);
                float k = 1f - spark.age / spark.life;
                spark.image.color = new Color(spark.color.r, spark.color.g, spark.color.b, spark.color.a * k);
                float scale = spark.size * (0.6f + 0.4f * k);
                spark.rect.sizeDelta = new Vector2(scale, scale);
            }

            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var ring = rings[i];
                ring.age += dt;
                if (ring.age >= ring.life || !ring.rect)
                {
                    Retire(ring.rect);
                    rings.RemoveAt(i);
                    continue;
                }
                float k = ring.age / ring.life;
                float size = Mathf.LerpUnclamped(ring.from, ring.to, FinikUiMotion.EaseOutCubic(k));
                ring.rect.sizeDelta = new Vector2(size, size);
                ring.image.color = new Color(ring.color.r, ring.color.g, ring.color.b, ring.color.a * (1f - k));
            }

            for (int i = labels.Count - 1; i >= 0; i--)
            {
                var label = labels[i];
                label.age += dt;
                if (label.age >= label.life || !label.rect)
                {
                    Retire(label.rect);
                    labels.RemoveAt(i);
                    continue;
                }
                float k = label.age / label.life;
                label.rect.anchoredPosition = label.origin + Vector2.up * (FinikUiMotion.EaseOutCubic(k) * 110f);
                float pop = k < 0.25f ? Mathf.LerpUnclamped(0.6f, 1.1f, k / 0.25f) : Mathf.LerpUnclamped(1.1f, 1f, (k - 0.25f) / 0.75f);
                label.rect.localScale = Vector3.one * pop;
                var color = label.text.color;
                label.text.color = new Color(color.r, color.g, color.b, k < 0.65f ? 1f : 1f - (k - 0.65f) / 0.35f);
            }
        }

        // ------------------------------------------------------------------ pool

        readonly Stack<RectTransform> idle = new();

        RectTransform Take(string name, bool withImage = true)
        {
            RectTransform rect = null;
            // The pool only holds sprites. A label needs a TMP_Text, and putting one on a pooled
            // object that already carries an Image leaves two graphics on it, which is what threw.
            if (withImage)
                while (idle.Count > 0 && !rect) rect = idle.Pop();
            if (!rect)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.layer = gameObject.layer;
                rect = (RectTransform)go.transform;
                rect.SetParent(Self, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                if (withImage)
                {
                    var image = go.AddComponent<Image>();
                    image.raycastTarget = false;
                    image.preserveAspect = true;
                }
            }
            rect.localScale = Vector3.one;
            rect.gameObject.SetActive(true);
            return rect;
        }

        void Retire(RectTransform rect)
        {
            if (!rect) return;
            rect.gameObject.SetActive(false);
            // A label and a spark are different objects; keeping one pool would hand a spark a TMP_Text.
            // Both are tiny, so a retired object is simply dropped rather than sorted back.
            if (rect.GetComponent<Image>()) idle.Push(rect);
            else Destroy(rect.gameObject);
        }
    }
}
