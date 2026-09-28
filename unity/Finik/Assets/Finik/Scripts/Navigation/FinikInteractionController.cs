using System;
using System.Collections;
using System.Collections.Generic;
using Finik.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.Navigation
{
    [RequireComponent(typeof(FinikMovementController))]
    public sealed class FinikInteractionController : MonoBehaviour
    {
        [Serializable]
        sealed class InteractionResolution
        {
            public string interactionId;
            public string status;
        }

        static readonly string[,] RoomTargets =
        {
            { "Fridge", "fridge" },
            { "Sofa", "play-zone" },
            { "Backpack", "care-bag" },
            { "Desk", "study-desk" },
            { "Bed", "bed" },
            { "Shelves", "decor-shelf" },
            { "Curtains", "decor-window" }
        };

        /// <summary>
        /// In-Unity handlers for interaction targets (fridge, bed, вЂ¦). A handler returns true when it
        /// takes the interaction; Finik then waits in place until it calls <see cref="CompleteInteraction"/>.
        /// </summary>
        static readonly List<Func<FinikInteractionController, string, bool>> Handlers = new();

        public static void RegisterHandler(Func<FinikInteractionController, string, bool> handler)
        {
            if (handler != null && !Handlers.Contains(handler)) Handlers.Add(handler);
        }

        public static void UnregisterHandler(Func<FinikInteractionController, string, bool> handler) => Handlers.Remove(handler);

        FinikMovementController movement;
        FinikActivityController activity;
        FinikInteractionTarget pendingTarget;
        Vector3 pendingApproachPoint;
        bool waitingForUi;
        int petTapCount;
        float lastPetTapAt;

        void Awake()
        {
            movement = GetComponent<FinikMovementController>();
            activity = GetComponent<FinikActivityController>();
            ConfigurePetTarget();
            ConfigureRoomTargets();
        }

        void Update()
        {
            if (!pendingTarget || waitingForUi || movement.State != FinikState.Idle) return;
            if (PlanarDistance(transform.position, pendingApproachPoint) > pendingTarget.ArrivalRadius + 0.12f) return;
            CompleteArrival();
        }
        public bool RequestInteraction(FinikInteractionTarget target)
        {
            if (!target || string.IsNullOrWhiteSpace(target.InteractionId)) return false;
            if (waitingForUi) return true;
            if (string.Equals(target.InteractionId, "pet", StringComparison.Ordinal))
            {
                pendingTarget = null;
                if (Time.unscaledTime - lastPetTapAt > 10f) petTapCount = 0;
                lastPetTapAt = Time.unscaledTime;
                petTapCount++;
                // The first tap only triggers the pet reaction. Mood is rewarded from the
                // second consecutive tap so the child sees the reaction before receiving feedback.
                bool playful = petTapCount >= 2;
                if (activity && activity.TryPlayTapReaction(playful))
                {
                    if (playful)
                    {
                        int gained = FinikGame.TryRaiseMoodFromTap();
                        if (gained > 0) FinikPetPopup.ShowDelta("icon_mood", gained, "Стало веселее");
                        petTapCount = 0;
                    }
                    FinikAudioManager.Instance.PlayInteraction("pet");
                    FinikMobileBridge.SendPetTapped();
                }
                return true;
            }
            if (!target.TryGetApproachPoint(transform.position, out Vector3 approachPoint)) return false;

            pendingTarget = target;
            pendingApproachPoint = approachPoint;
            float distance = PlanarDistance(transform.position, approachPoint);
            if (distance <= target.ArrivalRadius)
            {
                CompleteArrival();
                return true;
            }

            if (movement.TrySetUserDestination(approachPoint)) return true;
            pendingTarget = null;
            return false;
        }

        IEnumerator ShowMoodGain(int gained)
        {
            var overlay = new GameObject("MoodGain", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            var canvas = overlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 3000;
            overlay.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            overlay.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
            var group = overlay.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            // Use the game's mood sprite instead of a Unicode emoji: the TMP font used on Android
            // does not contain colour emoji glyphs and rendered a missing-character square.
            var card = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(Shadow));
            var cardRect = card.GetComponent<RectTransform>();
            cardRect.SetParent(overlay.transform, false);
            cardRect.sizeDelta = new Vector2(236f, 92f);
            // Bottom-center pivot keeps the entire popup above the pet instead of covering its face.
            cardRect.pivot = new Vector2(.5f, 0f);
            var background = card.GetComponent<Image>();
            // A dedicated small light preset avoids squashing the large button artwork into an oval
            // on phones while keeping the warm quest-card look.
            var popupTexture = MakeRoundedPopupTexture(
                new Color(1f, .985f, .93f, .99f),
                new Color(.96f, .73f, .25f, 1f));
            var popupSprite = Sprite.Create(
                popupTexture, new Rect(0f, 0f, popupTexture.width, popupTexture.height),
                new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(12f, 12f, 12f, 12f));
            popupSprite.hideFlags = HideFlags.HideAndDontSave;
            background.sprite = popupSprite;
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = false;
            var shadow = card.GetComponent<Shadow>();
            shadow.effectColor = new Color(0.18f, 0.12f, 0.04f, 0.18f);
            shadow.effectDistance = new Vector2(0f, -5f);

            var moodIcon = new GameObject("MoodIcon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            moodIcon.transform.SetParent(card.transform, false);
            moodIcon.sprite = LoadedSprite("icon_mood");
            moodIcon.gameObject.SetActive(moodIcon.sprite);
            moodIcon.preserveAspect = true;
            moodIcon.raycastTarget = false;
            moodIcon.rectTransform.anchorMin = moodIcon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            moodIcon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            moodIcon.rectTransform.anchoredPosition = new Vector2(52f, 0f);
            moodIcon.rectTransform.sizeDelta = new Vector2(52f, 52f);

            var label = new GameObject("Value", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.transform.SetParent(card.transform, false);
            label.text = $"+{gained}";
            label.fontSize = 48;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.08f, 0.64f, 0.30f);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.offsetMin = new Vector2(82f, 5f);
            label.rectTransform.offsetMax = new Vector2(-16f, -5f);

            float elapsed = 0f;
            // Keep the mood gain visible long enough for a child to notice and read it.
            const float hold = 2.5f;
            const float fade = 0.75f;
            while (elapsed < hold + fade)
            {
                elapsed += Time.unscaledDeltaTime;
                var camera = Camera.main;
                if (camera)
                {
                    Vector3 screen = camera.WorldToScreenPoint(PopupAnchorWorld());
                    if (screen.z > 0f)
                    {
                        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)overlay.transform,
                            screen, null, out var local);
                        // Keep a small screen-space gap above the highest visible point of the model.
                        cardRect.anchoredPosition = local + Vector2.up * (18f + Mathf.Max(0f, elapsed - hold) * 70f);
                    }
                }
                group.alpha = elapsed <= hold ? 1f : 1f - (elapsed - hold) / fade;
                yield return null;
            }
            Destroy(overlay);
            Destroy(popupSprite);
            Destroy(popupTexture);
        }

        Vector3 PopupAnchorWorld()
        {
            // Use the actual visible model bounds so fox/cat/raccoon and all growth stages get
            // the popup above the head even though their heights differ substantially.
            var renderers = GetComponentsInChildren<Renderer>(false);
            bool found = false;
            Bounds combined = default;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!found)
                {
                    combined = renderer.bounds;
                    found = true;
                }
                else
                {
                    combined.Encapsulate(renderer.bounds);
                }
            }

            if (!found) return transform.position + Vector3.up * 2f;
            float gap = Mathf.Clamp(combined.size.y * .10f, .12f, .28f);
            return new Vector3(combined.center.x, combined.max.y + gap, combined.center.z);
        }

        static Sprite LoadedSprite(string spriteName)
        {
            if (string.IsNullOrWhiteSpace(spriteName)) return null;
            // Art and Generated contain duplicate logical names. Prefer the larger art sprite.
            Sprite best = null;
            float bestArea = -1f;
            var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
            for (int i = 0; i < sprites.Length; i++)
            {
                var candidate = sprites[i];
                if (!candidate || !string.Equals(candidate.name, spriteName, StringComparison.OrdinalIgnoreCase)) continue;
                float area = candidate.rect.width * candidate.rect.height;
                if (area <= bestArea) continue;
                best = candidate;
                bestArea = area;
            }
            return best;
        }

        static Texture2D MakeRoundedPopupTexture(Color fill, Color border)
        {
            const int width = 64;
            const int height = 40;
            const int radius = 12;
            const int borderWidth = 2;
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

        public void CancelPendingInteraction()
        {
            if (waitingForUi) return;
            pendingTarget = null;
        }
        public void ResolveInteraction(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            InteractionResolution resolution;
            try { resolution = JsonUtility.FromJson<InteractionResolution>(json); }
            catch (Exception exception)
            {
                Debug.LogWarning("FINIK_INTERACTION_RESOLVE_INVALID: " + exception.Message);
                return;
            }

            if (resolution != null) CompleteInteraction(resolution.interactionId);
        }

        /// <summary>Releases Finik after the UI for <paramref name="interactionId"/> is done.</summary>
        public void CompleteInteraction(string interactionId)
        {
            if (!waitingForUi || !pendingTarget) return;
            if (!string.Equals(interactionId, pendingTarget.InteractionId, StringComparison.Ordinal)) return;

            waitingForUi = false;
            pendingTarget = null;
            movement.EndActivity();
        }

        void ConfigurePetTarget()
        {
            EnsureTouchCollider(gameObject);
            var target = GetComponent<FinikInteractionTarget>();
            if (!target) target = gameObject.AddComponent<FinikInteractionTarget>();
            target.Configure("pet", 0.1f, 0.1f);
        }

        void ConfigureRoomTargets()
        {
            for (int i = 0; i < RoomTargets.GetLength(0); i++)
            {
                string objectName = RoomTargets[i, 0];
                string interactionId = RoomTargets[i, 1];
                var go = GameObject.Find(objectName);
                if (!go)
                {
                    Debug.LogWarning($"FINIK_INTERACTION_TARGET_MISSING: {objectName}");
                    continue;
                }

                EnsureTouchCollider(go);
                var target = go.GetComponent<FinikInteractionTarget>();
                if (!target) target = go.AddComponent<FinikInteractionTarget>();
                target.Configure(interactionId);
            }
        }

        static void EnsureTouchCollider(GameObject go)
        {
            if (go.GetComponentInChildren<Collider>()) return;
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            var box = go.AddComponent<BoxCollider>();
            box.center = go.transform.InverseTransformPoint(bounds.center);
            Vector3 scale = go.transform.lossyScale;
            box.size = new Vector3(
                bounds.size.x / Mathf.Max(0.001f, Mathf.Abs(scale.x)),
                bounds.size.y / Mathf.Max(0.001f, Mathf.Abs(scale.y)),
                bounds.size.z / Mathf.Max(0.001f, Mathf.Abs(scale.z)));
        }

        void CompleteArrival()
        {
            if (!pendingTarget) return;
            waitingForUi = true;
            movement.BeginActivity();
            FinikAudioManager.Instance.PlayInteraction(pendingTarget.InteractionId);
            string id = pendingTarget.InteractionId;
            FinikMobileBridge.SendObjectTapped(id);
            foreach (var handler in Handlers.ToArray())
                if (handler(this, id)) return;
            // Nobody on the Unity side took it and there is no mobile host to answer: don't leave Finik frozen.
            if (!FinikMobileBridge.HasHost) CompleteInteraction(id);
        }
        static float PlanarDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
