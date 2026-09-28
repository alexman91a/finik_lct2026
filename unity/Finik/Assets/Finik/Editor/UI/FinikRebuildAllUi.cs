using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Rebuilds every UI screen in dependency order (the HUD first: the screens wire themselves to it).
    /// Every builder rebuilds its canvas in place, so a single screen can also be rebuilt on its own
    /// without breaking the references the others hold to it.
    /// </summary>
    public static class FinikRebuildAllUi
    {
        static readonly (string name, Func<string> build)[] Steps =
        {
            ("Home HUD", FinikHomeHudBuilder.Build),
            ("Onboarding", FinikOnboardingBuilder.Build),
            ("Food Screen", FinikFoodScreenBuilder.Build),
            ("Quest Screen", FinikQuestScreenBuilder.Build),
            ("Savings Screen", FinikSavingsScreenBuilder.Build),
            ("Shop Screen", FinikShopScreenBuilder.Build),
            ("Games Screen", FinikGamesScreenBuilder.Build),
            ("Progress Screen", FinikProgressScreenBuilder.Build),
            ("Budget Screen", FinikBudgetScreenBuilder.Build),
            // Settings first: the adult screen is reached from there and links back to it.
            ("Settings Screen", FinikSettingsScreenBuilder.Build),
            ("Adult Screen", FinikAdultScreenBuilder.Build),
            ("Frame Counter", FinikFrameCounterBuilder.Build)
        };

        [MenuItem("Finik/UI/Rebuild All UI", priority = 0)]
        public static void RebuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Stop Play Mode first: builders edit and save the scene.";
            var report = new StringBuilder("Rebuild All UI:");
            for (int i = 0; i < Steps.Length; i++)
            {
                var (name, build) = Steps[i];
                EditorUtility.DisplayProgressBar("Rebuild All UI", name, i / (float)Steps.Length);
                try
                {
                    report.Append($"\n- {build()}");
                }
                catch (Exception e)
                {
                    // Later screens depend on the earlier ones: stop instead of wiring them to a broken build.
                    report.Append($"\n- {name} FAILED: {e.Message}. Stopped; fix it and run again.");
                    Debug.LogException(e);
                    break;
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }
            }
            return report.ToString();
        }
    }
}
