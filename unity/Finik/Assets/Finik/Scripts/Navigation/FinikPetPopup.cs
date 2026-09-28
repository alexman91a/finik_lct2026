using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.Navigation
{
    /// <summary>
    /// Short non-blocking messages that float above the visible pet.
    /// Used both for the pet's own phrases and for food/mood deltas.
    /// </summary>
    public sealed class FinikPetPopup : MonoBehaviour
    {
        readonly struct Entry
        {
            public readonly string icon;
            public readonly string title;
            public readonly string caption;
            public readonly bool phrase;

            public Entry(string icon, string title, string caption, bool phrase)
            {
                this.icon = icon;
                this.title = title;
                this.caption = caption;
                this.phrase = phrase;
            }
        }

        readonly Queue<Entry> queue = new();
        Coroutine routine;

        public static bool ShowMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            var host = Host();
            if (!host) return false;
            host.Enqueue(new Entry(null, message, null, true));
            return true;
        }

        public static void ShowDelta(string icon, int delta, string caption)
        {
            if (delta == 0) return;
            string sign = delta > 0 ? "+" : "–";
            Host()?.Enqueue(new Entry(icon, sign + Mathf.Abs(delta), caption, false));
        }

        static FinikPetPopup Host()
        {
            var interaction = FindFirstObjectByType<FinikInteractionController>(FindObjectsInactive.Include);
            if (!interaction || !interaction.gameObject.activeInHierarchy) return null;
            var host = interaction.GetComponent<FinikPetPopup>();
            return host ? host : interaction.gameObject.AddComponent<FinikPetPopup>();
        }

        void Enqueue(Entry entry)
        {
            // New state is more useful than a long backlog of stale notifications.
            while (queue.Count >= 3) queue.Dequeue();
            queue.Enqueue(entry);
            if (routine == null) routine = StartCoroutine(Process());
        }

        IEnumerator Process()
        {
            while (queue.Count > 0)
                yield return Present(queue.Dequeue());
            routine = null;
        }

        IEnumerator Present(Entry entry)
        {
            var overlay = new GameObject("PetPopup", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            var canvas = overlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 3000;

            var scaler = overlay.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = .5f;

            var group = overlay.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var card = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(Shadow));
            var cardRect = card.GetComponent<RectTransform>();
            cardRect.SetParent(overlay.transform, false);
            cardRect.sizeDelta = entry.phrase ? new Vector2(560f, 116f) : new Vector2(370f, 122f);
            cardRect.pivot = new Vector2(.5f, 0f);

            var background = card.GetComponent<Image>();
            var texture = MakeRoundedTexture(
                new Color(1f, .985f, .93f, .99f),
                new Color(.96f, .73f, .25f, 1f));
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(12f, 12f, 12f, 12f));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            background.sprite = sprite;
            background.type = Image.Type.Sliced;
            background.raycastTarget = false;

            var shadow = card.GetComponent<Shadow>();
            shadow.effectColor = new Color(.18f, .12f, .04f, .18f);
            shadow.effectDistance = new Vector2(0f, -5f);

            if (!entry.phrase)
            {
                var icon = new GameObject("StateIcon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                icon.transform.SetParent(card.transform, false);
                icon.sprite = LoadedSprite(entry.icon);
                icon.gameObject.SetActive(icon.sprite);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, .5f);
                icon.rectTransform.pivot = new Vector2(.5f, .5f);
                icon.rectTransform.anchoredPosition = new Vector2(54f, 0f);
                icon.rectTransform.sizeDelta = new Vector2(56f, 56f);
            }

            var title = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            title.transform.SetParent(card.transform, false);
            title.text = entry.title;
            title.fontStyle = FontStyles.Bold;
            title.raycastTarget = false;
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = Vector2.one;

            if (entry.phrase)
            {
                title.fontSize = 34f;
                title.enableAutoSizing = true;
                title.fontSizeMin = 28f;
                title.fontSizeMax = 34f;
                title.textWrappingMode = TextWrappingModes.Normal;
                title.alignment = TextAlignmentOptions.Center;
                title.color = new Color(.12f, .15f, .36f);
                title.rectTransform.offsetMin = new Vector2(24f, 10f);
                title.rectTransform.offsetMax = new Vector2(-24f, -10f);
            }
            else
            {
                title.fontSize = 46f;
                title.textWrappingMode = TextWrappingModes.NoWrap;
                title.alignment = TextAlignmentOptions.BottomLeft;
                title.color = entry.title.StartsWith("+", StringComparison.Ordinal)
                    ? new Color(.08f, .64f, .30f)
                    : new Color(.86f, .24f, .28f);
                title.rectTransform.offsetMin = new Vector2(94f, 57f);
                title.rectTransform.offsetMax = new Vector2(-14f, -6f);

                var caption = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
                caption.transform.SetParent(card.transform, false);
                caption.text = entry.caption ?? string.Empty;
                caption.fontSize = 24f;
                caption.color = new Color(.27f, .31f, .50f);
                caption.alignment = TextAlignmentOptions.TopLeft;
                caption.textWrappingMode = TextWrappingModes.Normal;
                caption.raycastTarget = false;
                caption.rectTransform.anchorMin = Vector2.zero;
                caption.rectTransform.anchorMax = Vector2.one;
                caption.rectTransform.offsetMin = new Vector2(94f, 8f);
                caption.rectTransform.offsetMax = new Vector2(-14f, -61f);
            }

            float elapsed = 0f;
            float hold = entry.phrase ? 3.0f : 2.0f;
            const float fade = .65f;
            var overlayRect = (RectTransform)overlay.transform;

            while (elapsed < hold + fade)
            {
                elapsed += Time.unscaledDeltaTime;
                var camera = Camera.main;
                if (camera)
                {
                    Vector3 screen = camera.WorldToScreenPoint(AnchorWorld());
                    if (screen.z > 0f)
                    {
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(overlayRect, screen, null, out var local);
                        float halfWidth = cardRect.sizeDelta.x * .5f;
                        local.x = Mathf.Clamp(local.x,
                            overlayRect.rect.xMin + halfWidth + 18f,
                            overlayRect.rect.xMax - halfWidth - 18f);
                        local.y = Mathf.Min(local.y, overlayRect.rect.yMax - cardRect.sizeDelta.y - 18f);
                        cardRect.anchoredPosition = local + Vector2.up * (18f + Mathf.Max(0f, elapsed - hold) * 70f);
                    }
                }

                group.alpha = elapsed <= hold ? 1f : 1f - (elapsed - hold) / fade;
                yield return null;
            }

            Destroy(overlay);
            Destroy(sprite);
            Destroy(texture);
        }

        Vector3 AnchorWorld()
        {
            var renderers = GetComponentsInChildren<Renderer>(false);
            bool found = false;
            Bounds combined = default;

            foreach (var renderer in renderers)
            {
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!found)
                {
                    combined = renderer.bounds;
                    found = true;
                }
                else combined.Encapsulate(renderer.bounds);
            }

            if (!found) return transform.position + Vector3.up * 2f;
            float gap = Mathf.Clamp(combined.size.y * .10f, .12f, .28f);
            return new Vector3(combined.center.x, combined.max.y + gap, combined.center.z);
        }

        static Sprite LoadedSprite(string spriteName)
        {
            if (string.IsNullOrWhiteSpace(spriteName)) return null;
            Sprite best = null;
            float bestArea = -1f;

            foreach (var candidate in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (!candidate || !string.Equals(candidate.name, spriteName, StringComparison.OrdinalIgnoreCase)) continue;
                float area = candidate.rect.width * candidate.rect.height;
                if (area <= bestArea) continue;
                best = candidate;
                bestArea = area;
            }
            return best;
        }

        static Texture2D MakeRoundedTexture(Color fill, Color border)
        {
            const int width = 64, height = 40, radius = 12, borderWidth = 2;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float dx = Mathf.Max(Mathf.Max(radius - x - .5f, x + .5f - (width - radius)), 0f);
                float dy = Mathf.Max(Mathf.Max(radius - y - .5f, y + .5f - (height - radius)), 0f);
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                if (distance > radius)
                {
                    pixels[y * width + x] = Color.clear;
                    continue;
                }

                bool edge = x < borderWidth || y < borderWidth ||
                            x >= width - borderWidth || y >= height - borderWidth ||
                            distance > radius - borderWidth;
                pixels[y * width + x] = edge ? border : fill;
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
