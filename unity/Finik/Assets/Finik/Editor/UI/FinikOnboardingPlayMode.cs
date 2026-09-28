using Finik.UI.Onboarding;
using UnityEditor;
using UnityEngine;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Editor convenience: while enabled, every press of Play clears the local profile so the
    /// onboarding starts from the title screen. Toggle via Finik/Profile menu.
    /// </summary>
    [InitializeOnLoad]
    static class FinikOnboardingPlayMode
    {
        const string MenuPath = "Finik/Profile/Always Start With Onboarding";
        const string PrefKey = "Finik.AlwaysStartWithOnboarding";

        static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, true);
            set => EditorPrefs.SetBool(PrefKey, value);
        }

        static FinikOnboardingPlayMode()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.ExitingEditMode || !Enabled) return;
                FinikProfileStore.Clear();
                Finik.Core.FinikGame.Clear();
                Debug.Log("[FinikProfile] Starting from onboarding (Finik/Profile/Always Start With Onboarding is on).");
            };
        }

        [MenuItem(MenuPath, priority = 0)]
        static void Toggle() => Enabled = !Enabled;

        [MenuItem(MenuPath, true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }
    }
}
