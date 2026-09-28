using System.Collections.Generic;
using Finik.Core;
using Finik.Navigation;
using Finik.UI.Onboarding;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI
{
    /// <summary>
    /// Round floating hint above a room object (the fridge, the study desk), so a child sees where
    /// food and quests come from. A small pointer on its rim always aims at the object, also when the
    /// object is off screen and the hint waits at the edge. It bobs gently and pulses with a glow while
    /// the object wants attention (Finik is hungry, quests are waiting). Tapping it does exactly what
    /// tapping the object does: Finik walks over and the object's screen opens.
    /// Hints never cover each other or a HUD widget: each takes the nearest free spot, and hides when
    /// there is none. Lives on the HUD canvas, so it hides
    /// together with the HUD during full-screen flows and onboarding.
    /// </summary>
    public sealed class FinikRoomBubble : MonoBehaviour
    {
        public enum Attention
        {
            /// <summary>Pulses while Finik's food is low.</summary>
            Hunger,
            /// <summary>Pulses and shows a count while quests or rewards wait on the board.</summary>
            OpenQuests,
            /// <summary>Pulses while Finik's mood is low — the games behind it are free.</summary>
            LowMood
        }

        [SerializeField] string interactionId = "fridge";
        [SerializeField] Attention attention = Attention.Hunger;
        [SerializeField] Button button;
        [Tooltip("Moves for the idle bob and the attention pulse; the root holds the placement.")]
        [SerializeField] RectTransform body;
        [SerializeField] Image glow;
        [Tooltip("Pointer on the rim, drawn pointing down; turned to aim at the object.")]
        [SerializeField] RectTransform pointer;
        [Tooltip("Optional counter badge (OpenQuests): hidden when nothing waits.")]
        [SerializeField] GameObject badge;
        [SerializeField] TMP_Text badgeText;
        [Tooltip("World-space lift above the top of the object's bounds.")]
        [SerializeField] float lift = 0.08f;
        [Tooltip("Canvas distance from the object's top to the hint's centre: radius plus the pointer.")]
        [SerializeField] float hover = 110f;
        [Tooltip("Distance of the pointer from the hint's centre (canvas units).")]
        [SerializeField] float pointerDistance = 70f;
        [Tooltip("Food level (0..1) under which a Hunger bubble pulses.")]
        [SerializeField, Range(0f, 1f)] float hungryBelow = 0.4f;
        [Tooltip("Mood (0..1) under which a LowMood bubble pulses.")]
        [SerializeField, Range(0f, 1f)] float sadBelow = 0.55f;
        [SerializeField] float bobHeight = 10f;
        [SerializeField] float bobSpeed = 2.2f;
        [Tooltip("Distance from the screen edge when the object is off screen: the hint waits at the edge on its side.")]
        [SerializeField] float edgeMargin = 90f;
        [Tooltip("The whole hint stays clear of the top HUD rows and the bottom dock (canvas units).")]
        [SerializeField] float landscapeTopReserve = 170f;
        [SerializeField] float portraitTopReserve = 330f;
        [SerializeField] float bottomReserve = 300f;
        [Tooltip("Minimum gap between two hints (canvas units).")]
        [SerializeField] float separationGap = 16f;
        [Tooltip("How fast a hint slides to a new free spot (1/s). The hint follows the camera instantly; only the offset from its desired spot eases.")]
        [SerializeField, Min(1f)] float stackEasing = 12f;
        [Tooltip("How often the attention state is re-read (seconds).")]
        [SerializeField, Min(0.1f)] float attentionRefresh = 0.5f;

        RectTransform rect;
        RectTransform parentRect;
        CanvasGroup group;
        FinikInteractionTarget target;
        Renderer[] targetRenderers;
        float nextLookup;
        float nextAttention;
        bool wantsAttention;
        float phase;
        // This frame: the object's point on the canvas (may lie off screen), where the hint wants to be,
        // the vertical range it may use, and whether it is shown at all.
        Vector2 aim;
        Vector2 desired;
        // Eased shift away from the desired spot while stacked with another hint.
        Vector2 stackOffset;
        Vector2 yRange;
        bool placed;
        // The stack offset eases, which looks right while the player watches the room. It must not ease
        // the first time after the hint appears, though: the HUD comes back from a full-screen screen
        // with the offsets cleared, and the hints would slide out of each other in plain sight.
        bool settled;
        // Where the arrangement put the hint last frame; a small pull towards it keeps the choice steady.
        Vector2 lastSpot;
        bool hasLastSpot;
        const float StickToLastSpot = 0.35f;

        // Every hint places itself in LateUpdate; right before the canvases render, the hints are moved
        // clear of the HUD and of each other and the pointers turned to their objects.
        static readonly List<FinikRoomBubble> Active = new();
        static bool separationHooked;

        public string InteractionId => interactionId;

        public void Configure(string targetInteraction, Attention mode, Button tapButton, RectTransform bobbingBody, Image attentionGlow,
            RectTransform aimPointer, GameObject countBadge = null, TMP_Text countText = null)
        {
            interactionId = targetInteraction;
            attention = mode;
            button = tapButton;
            body = bobbingBody;
            glow = attentionGlow;
            pointer = aimPointer;
            badge = countBadge;
            badgeText = countText;
        }

        void Awake()
        {
            rect = (RectTransform)transform;
            parentRect = (RectTransform)rect.parent;
            group = GetComponent<CanvasGroup>();
            if (!group) group = gameObject.AddComponent<CanvasGroup>();
            if (button) button.onClick.AddListener(() => WalkTo(interactionId));
            // Hints side by side should not bob in lockstep.
            phase = (interactionId?.GetHashCode() ?? 0) % 7;
            if (badge) badge.SetActive(false);
        }

        // Play Mode starts without a domain reload: statics survive, but the engine drops the
        // willRenderCanvases subscription, so a stale "hooked" flag would switch stacking off for good.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Canvas.willRenderCanvases -= Arrange;
            separationHooked = false;
            Active.Clear();
        }

        void OnEnable()
        {
            Active.Add(this);
            // Hidden until it has been placed: switched on after this frame's LateUpdate (the HUD
            // coming back), it would otherwise draw where it stood last time, on top of the others.
            if (group) SetVisible(false);
            if (separationHooked) return;
            Canvas.willRenderCanvases += Arrange;
            separationHooked = true;
        }

        void OnDisable()
        {
            Active.Remove(this);
            placed = false;
            stackOffset = Vector2.zero;
            settled = false;
            hasLastSpot = false;
        }

        /// <summary>Sends Finik to the object, which opens its screen on arrival. False when he cannot get there.</summary>
        public static bool WalkTo(string interactionId)
        {
            var target = FindTarget(interactionId);
            var controller = FindAnyObjectByType<FinikInteractionController>();
            return target && controller && controller.RequestInteraction(target);
        }

        static FinikInteractionTarget FindTarget(string interactionId)
        {
            foreach (var candidate in FindObjectsByType<FinikInteractionTarget>(FindObjectsInactive.Exclude))
                if (candidate.InteractionId == interactionId) return candidate;
            return null;
        }

        void LateUpdate()
        {
            if (!target && Time.unscaledTime >= nextLookup)
            {
                // Room targets are set up at runtime; look again now and then until the object exists.
                nextLookup = Time.unscaledTime + 1f;
                target = FindTarget(interactionId);
                targetRenderers = target ? target.GetComponentsInChildren<Renderer>() : null;
            }
            var camera = Camera.main;
            if (!target || !camera || !TryAnchor(camera))
            {
                SetVisible(false);
                placed = false;
                return;
            }
            SetVisible(true);
            placed = true;
            Place(desired);

            float t = Time.unscaledTime;
            if (t >= nextAttention)
            {
                nextAttention = t + attentionRefresh;
                RefreshAttention();
            }
            float pulse = wantsAttention ? 0.5f + 0.5f * Mathf.Sin(t * 5f) : 0f;
            if (body)
            {
                body.anchoredPosition = new Vector2(0f, Mathf.Sin(t * bobSpeed + phase) * bobHeight);
                body.localScale = Vector3.one * (1f + 0.08f * pulse);
            }
            if (glow)
            {
                var color = glow.color;
                color.a = wantsAttention ? 0.35f + 0.45f * pulse : 0f;
                glow.color = color;
            }
        }

        void RefreshAttention()
        {
            bool hasProfile = FinikProfileStore.TryLoad(out _);
            int count = 0;
            switch (attention)
            {
                case Attention.Hunger:
                    wantsAttention = hasProfile && FinikGame.NeedsNow.food / 100f < hungryBelow;
                    break;
                case Attention.OpenQuests:
                    count = hasProfile && FinikGame.HasJourney ? FinikGame.OpenQuestCount() : 0;
                    wantsAttention = count > 0;
                    break;
                case Attention.LowMood:
                    wantsAttention = hasProfile && FinikGame.NeedsNow.mood / 100f < sadBelow;
                    break;
            }
            if (badge) badge.SetActive(count > 0);
            if (badgeText && count > 0) badgeText.text = count.ToString();
        }

        /// <summary>
        /// Finds the object's point on the canvas and the hint's spot above it. When the object is out of
        /// frame (portrait crops the room) the hint waits at the screen edge on its side; either way it
        /// stays between the HUD rows and the dock. False only when the object is behind the camera.
        /// </summary>
        bool TryAnchor(Camera camera)
        {
            var bounds = new Bounds(target.transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in targetRenderers)
            {
                if (!r) continue;
                if (!any) bounds = r.bounds;
                else bounds.Encapsulate(r.bounds);
                any = true;
            }
            // The hint hovers above the object's top; the pointer aims at its middle. Aiming at the top
            // would flip the pointer upwards whenever the HUD rows push the hint down over the object.
            if (!TryCanvasPoint(camera, new Vector3(bounds.center.x, bounds.max.y + lift, bounds.center.z), out var top)) return false;
            if (!TryCanvasPoint(camera, bounds.center, out aim)) aim = top;

            var area = parentRect.rect;
            float halfHeight = rect.rect.height / 2f;
            float topReserve = FinikScreenOrientation.IsPortrait(this) ? portraitTopReserve : landscapeTopReserve;
            yRange = new Vector2(area.yMin + bottomReserve + halfHeight, area.yMax - topReserve - halfHeight) - Vector2.one * area.center.y;
            desired = new Vector2(ClampX(top.x), Mathf.Clamp(top.y + hover, yRange.x, yRange.y));
            return true;
        }

        /// <summary>World point → position relative to the parent's centre (may lie off screen). False behind the camera.</summary>
        bool TryCanvasPoint(Camera camera, Vector3 world, out Vector2 local)
        {
            local = default;
            var screen = camera.WorldToScreenPoint(world);
            if (screen.z <= 0f) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screen, null, out var point)) return false;
            local = point - parentRect.rect.center;
            return true;
        }

        /// <summary>Moves the hint and turns its pointer towards the object.</summary>
        void Place(Vector2 position)
        {
            rect.anchoredPosition = position;
            if (!pointer) return;
            var toObject = aim - position;
            // Sitting on the object itself (its middle is under the disc): point straight down at it.
            var direction = toObject.magnitude > rect.rect.height / 2f ? toObject.normalized : Vector2.down;
            pointer.anchoredPosition = direction * pointerDistance;
            // The pointer sprite points down (-Y); rotate that onto the direction.
            pointer.localRotation = Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.down, direction));
        }

        /// <summary>
        /// Places every shown hint on the free spot nearest to where it wants to be: clear of every HUD
        /// widget (<see cref="FinikHudObstacle"/>) and of the hints already placed, inside its band and
        /// side margins. Hints whose objects are higher on screen are placed first, so the vertical order
        /// of the objects is kept. A hint with no free spot left is hidden rather than drawn over
        /// something: its object in the room still takes the tap.
        ///
        /// Positions start from the desired spots every frame, so the result follows the camera without
        /// drifting; only the offset from the desired spot eases, which slides a hint into place.
        /// </summary>
        static void Arrange()
        {
            if (Active.Count == 0) return;
            var shown = new List<FinikRoomBubble>(Active.Count);
            foreach (var bubble in Active)
                if (bubble && bubble.placed && bubble.parentRect) shown.Add(bubble);
            if (shown.Count == 0) return;

            // Higher object first; ties keep the list order, so the result does not flicker.
            var order = new List<int>(shown.Count);
            for (int i = 0; i < shown.Count; i++) order.Add(i);
            order.Sort((i, j) =>
            {
                int byAim = shown[j].aim.y.CompareTo(shown[i].aim.y);
                return byAim != 0 ? byAim : i.CompareTo(j);
            });

            var positions = new Vector2[shown.Count];
            var free = new bool[shown.Count];
            var blockers = new List<Rect>();
            var taken = new List<(RectTransform space, Rect box)>();
            foreach (int i in order)
            {
                var bubble = shown[i];
                blockers.Clear();
                foreach (var obstacle in FinikHudObstacle.All)
                    if (obstacle && obstacle.TryBox(bubble.parentRect, out var box)) blockers.Add(box);
                foreach (var (space, box) in taken)
                    if (space == bubble.parentRect) blockers.Add(box);

                free[i] = bubble.FindFreeSpot(blockers, out positions[i]);
                if (!free[i]) continue;
                var size = bubble.rect.rect.size + Vector2.one * bubble.separationGap;
                taken.Add((bubble.parentRect, new Rect(positions[i] - size / 2f, size)));
            }

            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < shown.Count; i++)
            {
                var bubble = shown[i];
                if (!free[i])
                {
                    bubble.SetVisible(false);
                    bubble.settled = false;
                    continue;
                }
                bubble.lastSpot = positions[i];
                bubble.hasLastSpot = true;
                var target = positions[i] - bubble.desired;
                if (!bubble.settled)
                {
                    bubble.stackOffset = target;
                    bubble.settled = true;
                }
                else
                {
                    bubble.stackOffset = Vector2.Lerp(bubble.stackOffset, target, 1f - Mathf.Exp(-bubble.stackEasing * dt));
                    if ((bubble.stackOffset - target).sqrMagnitude < 0.25f) bubble.stackOffset = target;
                }
                var position = bubble.desired + bubble.stackOffset;
                position.y = Mathf.Clamp(position.y, bubble.yRange.x, bubble.yRange.y);
                position.x = bubble.ClampX(position.x);
                bubble.Place(position);
            }
        }

        /// <summary>
        /// The spot nearest to <see cref="desired"/> where the whole hint covers none of
        /// <paramref name="blockers"/> and stays inside its band and margins. False when there is none.
        ///
        /// A spot is blocked exactly when its centre lies inside a blocker grown by half the hint's size,
        /// so the free area is the allowed box minus those grown boxes. Its point nearest to any target
        /// lies on one of their edges (or is the target itself), so trying every pair of such edge
        /// coordinates finds the true nearest spot, with no pushing back and forth that can end up inside
        /// another widget.
        /// </summary>
        bool FindFreeSpot(List<Rect> blockers, out Vector2 spot)
        {
            var half = rect.rect.size / 2f;
            float xMin = ClampX(float.MinValue), xMax = ClampX(float.MaxValue);
            float yMin = yRange.x, yMax = yRange.y;

            var grown = new List<Rect>(blockers.Count);
            foreach (var box in blockers)
                grown.Add(Rect.MinMaxRect(box.xMin - half.x, box.yMin - half.y, box.xMax + half.x, box.yMax + half.y));

            spot = desired;
            if (!Blocked(desired, grown)) return true;

            var xs = new List<float> { desired.x, xMin, xMax };
            var ys = new List<float> { desired.y, yMin, yMax };
            foreach (var box in grown)
            {
                xs.Add(box.xMin);
                xs.Add(box.xMax);
                ys.Add(box.yMin);
                ys.Add(box.yMax);
            }

            float best = float.MaxValue;
            bool found = false;
            foreach (float rawX in xs)
            {
                float x = Mathf.Clamp(rawX, xMin, xMax);
                foreach (float rawY in ys)
                {
                    var candidate = new Vector2(x, Mathf.Clamp(rawY, yMin, yMax));
                    if (Blocked(candidate, grown)) continue;
                    // Nearest to where the hint wants to be, with a pull towards last frame's spot so two
                    // equally good sides do not make it jump back and forth as the camera moves.
                    float cost = (candidate - desired).magnitude;
                    if (hasLastSpot) cost += StickToLastSpot * (candidate - lastSpot).magnitude;
                    if (cost >= best) continue;
                    best = cost;
                    spot = candidate;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>Inside a grown blocker; touching its edge is fine, that is exactly clear of it.</summary>
        static bool Blocked(Vector2 point, List<Rect> grown)
        {
            const float epsilon = 0.5f;
            foreach (var box in grown)
                if (point.x > box.xMin + epsilon && point.x < box.xMax - epsilon &&
                    point.y > box.yMin + epsilon && point.y < box.yMax - epsilon)
                    return true;
            return false;
        }

        /// <summary>Keeps the hint inside the side margins.</summary>
        float ClampX(float x)
        {
            float half = parentRect.rect.width / 2f;
            return Mathf.Clamp(x, -half + edgeMargin, half - edgeMargin);
        }

        void SetVisible(bool visible)
        {
            group.alpha = visible ? 1f : 0f;
            group.blocksRaycasts = visible;
            group.interactable = visible;
        }
    }
}
