using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI
{
    /// <summary>
    /// Swaps the CanvasScaler reference between landscape and portrait and also keeps
    /// secondary copy readable on very narrow/high-density phone surfaces such as Fold cover screens.
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(CanvasScaler))]
    public sealed class FinikCanvasOrientation : MonoBehaviour
    {
        [SerializeField] Vector2 landscapeReference = new(1920f, 1080f);

        const int CompactShortSidePx = 1000;
        const float CompactAutoMax = 36f;

        static readonly string[] SecondaryNameTokens =
        {
            "hint", "subtitle", "description", "caption", "setup", "advice",
            "helper", "note", "reaction", "status", "message", "progress"
        };

        readonly Dictionary<TMP_Text, TextState> textStates = new();

        CanvasScaler scaler;
        Vector2Int appliedScreen;
        bool appliedPortrait;
        int nextTextScanFrame;

        readonly struct TextState
        {
            public readonly float size;
            public readonly bool auto;
            public readonly float min;
            public readonly float max;

            public TextState(TMP_Text text)
            {
                size = text.fontSize;
                auto = text.enableAutoSizing;
                min = text.fontSizeMin;
                max = text.fontSizeMax;
            }
        }

        void OnEnable()
        {
            scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            appliedScreen = default;
            nextTextScanFrame = 0;
            Apply(forceLayout: true);
        }

        void Update()
        {
            Apply(forceLayout: false);

            // Some result cards and runtime labels appear after the first layout pass.
            if (Application.isPlaying && Time.frameCount >= nextTextScanFrame)
            {
                ApplyCompactTypography(IsCompactDisplay());
                nextTextScanFrame = Time.frameCount + 60;
            }
        }

        void Apply(bool forceLayout)
        {
            if (!scaler) return;

            var screen = new Vector2Int(Screen.width, Screen.height);
            bool portrait = FinikScreenOrientation.IsPortrait(this);
            if (!forceLayout && screen == appliedScreen && portrait == appliedPortrait) return;

            appliedScreen = screen;
            appliedPortrait = portrait;
            Vector2 reference = portrait ? new Vector2(landscapeReference.y, landscapeReference.x) : landscapeReference;
            scaler.referenceResolution = reference;

            if (Application.isPlaying)
                ApplyCompactTypography(IsCompactDisplay());

            // Foldables can change the render surface without a normal orientation change.
            // Rebuild the entire canvas immediately instead of keeping the previous stretched layout.
            var canvas = GetComponentInParent<Canvas>();
            if (canvas) canvas = canvas.rootCanvas;
            if (canvas)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.transform);
                Canvas.ForceUpdateCanvases();
            }
        }

        bool IsCompactDisplay() =>
            Mathf.Min(Screen.width, Screen.height) <= CompactShortSidePx;

        void ApplyCompactTypography(bool compact)
        {
            var texts = GetComponentsInChildren<TMP_Text>(true);
            foreach (var text in texts)
            {
                if (!text || string.IsNullOrWhiteSpace(text.text)) continue;
                if (!textStates.TryGetValue(text, out var state))
                {
                    state = new TextState(text);
                    textStates[text] = state;
                }

                if (!compact || !ShouldBoost(text, state))
                {
                    Restore(text, state);
                    continue;
                }

                if (state.auto)
                {
                    // Let the text use more of the available room, but keep its original minimum.
                    // Forcing a larger minimum broke long hints and card copy on the Fold cover.
                    text.enableAutoSizing = true;
                    bool isNote = text.gameObject.name.Equals("Note", System.StringComparison.OrdinalIgnoreCase);
                    text.fontSizeMin = state.min;
                    float targetMax = isNote ? 30f : CompactAutoMax;
                    text.fontSizeMax = Mathf.Max(state.max, targetMax);
                    text.fontSize = Mathf.Max(state.size, text.fontSizeMax);
                }
                else
                {
                    // Fixed labels keep their authored geometry. The short goal helper grows on the
                    // Fold cover, but only as far as its row allows: a flat 32 spilled out of the
                    // goal card's 34-unit line.
                    bool goalHelper = text.text.IndexOf("выбрать цель", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    if (goalHelper)
                    {
                        text.enableAutoSizing = true;
                        text.fontSizeMin = state.size;
                        text.fontSizeMax = Mathf.Max(state.size, 32f);
                    }
                    text.fontSize = goalHelper ? text.fontSizeMax : state.size;
                }
            }
        }

        static bool ShouldBoost(TMP_Text text, TextState state)
        {
            float baseSize = state.auto ? state.min : state.size;
            if (baseSize > 30f) return false;
            if (text.text.IndexOf("выбрать цель", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            string name = text.gameObject.name.ToLowerInvariant();
            foreach (var token in SecondaryNameTokens)
                if (name.Contains(token))
                    return true;

            return false;
        }

        static void Restore(TMP_Text text, TextState state)
        {
            text.enableAutoSizing = state.auto;
            text.fontSize = state.size;
            text.fontSizeMin = state.min;
            text.fontSizeMax = state.max;
        }
    }
}
