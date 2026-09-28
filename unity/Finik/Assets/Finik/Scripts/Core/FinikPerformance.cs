using System;
using UnityEngine;

namespace Finik.Core
{
    /// <summary>How smooth the game tries to be, at the cost of battery.</summary>
    public enum FinikFrameRateMode
    {
        /// <summary>30 кадров: Unity's own mobile default, the kindest to the battery.</summary>
        Battery = 0,
        /// <summary>60 кадров: smooth on every phone we target, still modest on power.</summary>
        Smooth = 1,
        /// <summary>Whatever the screen can show (90/120 on the phones we test on).</summary>
        Max = 2
    }

    /// <summary>
    /// Frame rate and the on-screen frame counter, both set from «Для взрослых».
    ///
    /// Unity renders mobile builds at 30 fps whenever <see cref="Application.targetFrameRate"/> is left
    /// at -1, so a build that never touches it is a 30 fps build — that is what the game shipped as.
    /// This applies the saved choice before the first scene loads, and again whenever the parent
    /// changes it, so there is exactly one place that decides the frame rate.
    ///
    /// <see cref="QualitySettings.vSyncCount"/> stays 0: on Android it is ignored (the compositor owns
    /// vsync) and on desktop it would override the target outright.
    /// </summary>
    public static class FinikPerformance
    {
        const string ModeKey = "finik.perf.frameRateMode";
        const string OverlayKey = "finik.perf.showFrameCounter";
        static bool focusHooked;

        /// <summary>Fallback when the screen does not report a sane refresh rate (editor, emulators).</summary>
        public const int FallbackRefreshRate = 60;

        /// <summary>The shipped default. 60 keeps the room and the UI animations smooth on a phone.</summary>
        public const FinikFrameRateMode DefaultMode = FinikFrameRateMode.Smooth;

        /// <summary>Raised after the mode or the counter changes, so open screens can re-render.</summary>
        public static event Action Changed;

        public static FinikFrameRateMode Mode
        {
            get
            {
                int raw = PlayerPrefs.GetInt(ModeKey, (int)DefaultMode);
                return Enum.IsDefined(typeof(FinikFrameRateMode), raw) ? (FinikFrameRateMode)raw : DefaultMode;
            }
            set
            {
                if (Mode == value) { Apply(); return; }
                PlayerPrefs.SetInt(ModeKey, (int)value);
                PlayerPrefs.Save();
                Apply();
                Changed?.Invoke();
            }
        }

        public static bool ShowFrameCounter
        {
            get => PlayerPrefs.GetInt(OverlayKey, 0) == 1;
            set
            {
                if (ShowFrameCounter == value) return;
                PlayerPrefs.SetInt(OverlayKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Apply();
                Changed?.Invoke();
            }
        }

        /// <summary>The refresh rate the screen reports, rounded, or <see cref="FallbackRefreshRate"/>.</summary>
        public static int ScreenRefreshRate
        {
            get
            {
                var current = Screen.currentResolution;
                double hz = current.refreshRateRatio.denominator == 0 ? 0 : current.refreshRateRatio.value;
                foreach (var resolution in Screen.resolutions)
                    if (resolution.width == current.width && resolution.height == current.height
                        && resolution.refreshRateRatio.denominator != 0 && resolution.refreshRateRatio.value > hz)
                        hz = resolution.refreshRateRatio.value;
                int rounded = (int)Math.Round(hz);
                // Emulators and some editors report 0 or an absurd rate; anything outside a real
                // panel's range is not worth trusting as a frame budget.
                return rounded is >= 24 and <= 480 ? rounded : FallbackRefreshRate;
            }
        }

        /// <summary>Frames per second <paramref name="mode"/> asks for right now.</summary>
        public static int TargetFor(FinikFrameRateMode mode) => mode switch
        {
            FinikFrameRateMode.Battery => 30,
            FinikFrameRateMode.Smooth => Math.Min(60, ScreenRefreshRate),
            FinikFrameRateMode.Max => ScreenRefreshRate,
            _ => 60
        };

        /// <summary>What the current mode asks for, for labels in the settings.</summary>
        public static int Target => TargetFor(Mode);

        public static string LabelFor(FinikFrameRateMode mode) => mode switch
        {
            FinikFrameRateMode.Battery => "Экономно",
            FinikFrameRateMode.Smooth => "Обычно",
            FinikFrameRateMode.Max => "Максимум",
            _ => "Обычно"
        };

        public static string DescriptionFor(FinikFrameRateMode mode) => mode switch
        {
            // One short line each: the settings card keeps a single line for it.
            FinikFrameRateMode.Battery => "30 кадров в секунду: дольше держит заряд.",
            FinikFrameRateMode.Smooth => $"{TargetFor(FinikFrameRateMode.Smooth)} кадров в секунду: плавно и бережно к батарее.",
            FinikFrameRateMode.Max => $"До {ScreenRefreshRate} кадров в секунду: плавнее всего, но расход выше.",
            _ => string.Empty
        };

        /// <summary>
        /// Puts the saved choice into effect. Runs before the first scene so the very first frame is
        /// already on the right budget; the settings screen calls it again on every change.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Apply()
        {
            if (!focusHooked)
            {
                Application.focusChanged += OnFocusChanged;
                focusHooked = true;
            }
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Target;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Mode == FinikFrameRateMode.Max)
            {
                var current = Screen.currentResolution;
                var preferred = current.refreshRateRatio;
                foreach (var resolution in Screen.resolutions)
                    if (resolution.width == current.width && resolution.height == current.height
                        && resolution.refreshRateRatio.denominator != 0
                        && resolution.refreshRateRatio.value > preferred.value)
                        preferred = resolution.refreshRateRatio;
                if (preferred.value > current.refreshRateRatio.value)
                    Screen.SetResolution(Screen.width, Screen.height, Screen.fullScreenMode, preferred);
            }
#endif
            FinikGraphy.SetVisible(ShowFrameCounter);
        }

        static void OnFocusChanged(bool focused)
        {
            if (focused) Apply();
        }
    }
}
