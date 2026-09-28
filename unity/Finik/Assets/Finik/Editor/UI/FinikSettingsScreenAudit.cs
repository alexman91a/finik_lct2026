using Finik.Core;
using Finik.UI.Onboarding;
using Finik.UI.Settings;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Play Mode check of «Настройки» at the current Game view resolution: every frame rate mode (the
    /// note under the chips changes length) and both switch states. The saved choices are put back.
    ///
    ///     Finik.Editor.UI.FinikSettingsScreenAudit.SetResolution(1080, 1920);  // then let a few frames pass
    ///     Finik.Editor.UI.FinikSettingsScreenAudit.Run();
    /// </summary>
    public static class FinikSettingsScreenAudit
    {
        static readonly (string a, string b)[] IntendedOverlaps =
        {
            // The switch's word sits on its track, under the sliding knob's free half.
            ("State", "Knob"),
            // The tick badge sits on the pet card's corner.
            ("Check", "Avatar")
        };

        static readonly string[] ContentImages = { "Icon", "Avatar", "Check" };

        static readonly string[] Containers = { "Glass", "Face", "Track" };

        [MenuItem("Finik/UI/Audit Settings Screen")]
        static void Menu() => Debug.Log(Run());

        public static string SetResolution(int width, int height) => FinikUiAudit.SetResolution(width, height);

        public static string EnsureProfile() => FinikUiAudit.EnsureProfile();

        public static string Run()
        {
            if (!EditorApplication.isPlaying) return "Enter Play Mode first.";
            var screen = Object.FindAnyObjectByType<FinikSettingsScreen>(FindObjectsInactive.Include);
            if (!screen) return "Screen_Settings is missing: run Finik/UI/Rebuild Settings Screen.";
            if (!screen.IsOpen && !screen.Open()) return "Settings did not open: finish onboarding first.";

            var audit = new FinikUiAudit(IntendedOverlaps, ContentImages, Containers);
            var panel = (FinikScreenPanel)FinikUiAudit.Get(screen, "panel");
            var scroll = (ScrollRect)FinikUiAudit.Get(screen, "scroll");
            var column = (RectTransform)panel.transform.Find("Column");
            panel.Show(instant: true);

            var savedMode = FinikPerformance.Mode;
            bool savedMusic = FinikAudioSettings.Music, savedSounds = FinikAudioSettings.Sounds;
            try
            {
                foreach (FinikFrameRateMode mode in System.Enum.GetValues(typeof(FinikFrameRateMode)))
                {
                    FinikPerformance.Mode = mode;
                    FinikAudioSettings.Music = mode != FinikFrameRateMode.Battery;
                    FinikAudioSettings.Sounds = mode == FinikFrameRateMode.Max;
                    FinikUiAudit.Call(screen, "Render", false);
                    audit.CheckScrolling(column, scroll, $"плавность «{FinikPerformance.LabelFor(mode)}»");
                }
            }
            finally
            {
                FinikPerformance.Mode = savedMode;
                FinikAudioSettings.Music = savedMusic;
                FinikAudioSettings.Sounds = savedSounds;
                FinikUiAudit.Call(screen, "Render", false);
            }

            return audit.Report("Settings screen", column, null);
        }
    }
}
