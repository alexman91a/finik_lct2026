using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Onboarding
{
    /// <summary>
    /// Lightweight UI confetti: pooled Images with simple ballistic motion, spin and flutter.
    /// Lives on a full-screen RectTransform above the panels; never blocks raycasts.
    /// </summary>
    public sealed class FinikConfettiBurst : MonoBehaviour
    {
        [SerializeField] Sprite pieceSprite;
        [SerializeField] int count = 90;
        [SerializeField] float lifetime = 2.8f;
        [SerializeField] float gravity = -1600f;
        [SerializeField] Color[] palette =
        {
            new(1f, 0.78f, 0.2f), new(1f, 0.42f, 0.35f), new(0.35f, 0.72f, 1f),
            new(0.45f, 0.9f, 0.5f), new(0.78f, 0.55f, 1f), new(1f, 1f, 1f)
        };

        sealed class Piece
        {
            public RectTransform rect;
            public Image image;
            public Vector2 velocity;
            public float spin;
            public float age;
            public float flutter;
        }

        readonly List<Piece> pieces = new();

        public void Burst(Vector2 origin)
        {
            var area = (RectTransform)transform;
            for (int i = 0; i < count; i++)
            {
                var piece = Get(i);
                piece.rect.anchoredPosition = origin + Random.insideUnitCircle * 30f;
                float angle = Random.Range(40f, 140f) * Mathf.Deg2Rad;
                float speed = Random.Range(900f, 1900f);
                piece.velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
                piece.spin = Random.Range(-720f, 720f);
                piece.flutter = Random.Range(0f, 10f);
                piece.age = 0f;
                piece.rect.sizeDelta = new Vector2(Random.Range(14f, 24f), Random.Range(22f, 36f));
                piece.image.color = palette[Random.Range(0, palette.Length)];
                piece.rect.gameObject.SetActive(true);
            }
            for (int i = count; i < pieces.Count; i++) pieces[i].rect.gameObject.SetActive(false);
            area.SetAsLastSibling();
        }

        Piece Get(int index)
        {
            while (pieces.Count <= index)
            {
                var go = new GameObject("Confetti", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(transform, false);
                var image = go.GetComponent<Image>();
                image.sprite = pieceSprite;
                image.raycastTarget = false;
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
                pieces.Add(new Piece { rect = rect, image = image });
            }
            return pieces[index];
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            foreach (var p in pieces)
            {
                if (!p.rect.gameObject.activeSelf) continue;
                p.age += dt;
                if (p.age >= lifetime)
                {
                    p.rect.gameObject.SetActive(false);
                    continue;
                }
                p.velocity.y += gravity * dt;
                p.velocity *= Mathf.Exp(-1.6f * dt);
                float sway = Mathf.Sin((p.age + p.flutter) * 7f) * 120f;
                p.rect.anchoredPosition += (p.velocity + new Vector2(sway, 0f)) * dt;
                p.rect.localRotation = Quaternion.Euler(0f, 0f, p.spin * p.age);
                // Flip effect: squash the width like a paper piece turning over.
                float flip = Mathf.Abs(Mathf.Sin((p.age + p.flutter) * 9f));
                p.rect.localScale = new Vector3(0.25f + 0.75f * flip, 1f, 1f);
                var c = p.image.color;
                c.a = Mathf.Clamp01((lifetime - p.age) / 0.5f);
                p.image.color = c;
            }
        }
    }
}
