using UnityEngine;

namespace Finik.UI
{
    /// <summary>
    /// A notch to pretend the editor has. The Device Simulator view drives Screen.safeArea itself and
    /// is the ground truth; this exists so the layout audits can run the same check headless in the
    /// Game view, where Screen.safeArea is always the full screen.
    /// </summary>
    public enum FinikNotch
    {
        None,
        /// <summary>iPhone 14/15/16 Pro: Dynamic Island on top, home indicator below.</summary>
        IPhoneDynamicIsland,
        /// <summary>iPhone X..13: the older, shallower notch.</summary>
        IPhoneNotch,
        /// <summary>Punch-hole camera with gesture navigation: a top inset only.</summary>
        AndroidPunchHole,
        /// <summary>The same punch hole plus a three-button navigation bar at the bottom.</summary>
        AndroidNavBar
    }

    /// <summary>
    /// Normalised safe areas per notch, as fractions of the screen. Taken from the devices' point
    /// sizes (iPhone 15 Pro is 393x852 pt with 59 pt of Dynamic Island and 34 pt of home indicator;
    /// iPhone 13 is 390x844 pt with a 47 pt notch), and rounded for Android, where the inset depends
    /// on the vendor. They are close enough to catch a layout that does not survive a notch; measure
    /// on the Device Simulator or a real device before trusting the last few units.
    /// </summary>
    public static class FinikSafeAreaSimulation
    {
        /// <summary>Editor-only override. None (the default) leaves Screen.safeArea alone.</summary>
        public static FinikNotch Notch;

        // Rect(x, y, width, height) as fractions of the screen, portrait first.
        static readonly Rect[] IPhoneDynamicIsland = { new(0f, 0.0399f, 1f, 0.8908f), new(0.0693f, 0.0534f, 0.8615f, 0.9466f) };
        static readonly Rect[] IPhoneNotch = { new(0f, 0.0403f, 1f, 0.9040f), new(0.0557f, 0.0538f, 0.8886f, 0.9462f) };
        // A punch hole eats one end of the long side: the top in portrait, one side in landscape.
        // Both sides are inset here, so a layout checked in landscape survives either rotation.
        static readonly Rect[] AndroidPunchHole = { new(0f, 0f, 1f, 0.96f), new(0.04f, 0f, 0.92f, 1f) };
        static readonly Rect[] AndroidNavBar = { new(0f, 0.0525f, 1f, 0.9075f), new(0.04f, 0.075f, 0.92f, 0.925f) };

        /// <summary>The safe area to lay out against: the simulated notch in the editor, else the real one.</summary>
        public static Rect Area
        {
            get
            {
                var real = Screen.safeArea;
                if (!Application.isEditor || Notch == FinikNotch.None) return real;
                int width = Screen.width, height = Screen.height;
                if (width <= 0 || height <= 0) return real;
                var table = Notch switch
                {
                    FinikNotch.IPhoneDynamicIsland => IPhoneDynamicIsland,
                    FinikNotch.IPhoneNotch => IPhoneNotch,
                    FinikNotch.AndroidPunchHole => AndroidPunchHole,
                    FinikNotch.AndroidNavBar => AndroidNavBar,
                    _ => null
                };
                if (table == null) return real;
                var n = table[height > width ? 0 : 1];
                return new Rect(n.x * width, n.y * height, n.width * width, n.height * height);
            }
        }
    }

    /// <summary>Fits this rect to the safe area (notches, rounded corners, gesture bars).</summary>
    [ExecuteAlways, RequireComponent(typeof(RectTransform))]
    public sealed class FinikSafeArea : MonoBehaviour
    {
        RectTransform rect;
        Rect appliedArea;
        Vector2Int appliedScreen;

        void OnEnable()
        {
            rect = (RectTransform)transform;
            appliedScreen = default;
            Apply();
        }

        void Update() => Apply();

        void Apply()
        {
            var screen = new Vector2Int(Screen.width, Screen.height);
            Rect area = FinikSafeAreaSimulation.Area;
            if (area == appliedArea && screen == appliedScreen) return;
            appliedArea = area;
            appliedScreen = screen;
            if (screen.x <= 0 || screen.y <= 0) return;

            rect.anchorMin = new Vector2(area.xMin / screen.x, area.yMin / screen.y);
            rect.anchorMax = new Vector2(area.xMax / screen.x, area.yMax / screen.y);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
