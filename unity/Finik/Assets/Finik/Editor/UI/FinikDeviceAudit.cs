using System;
using System.Collections;
using System.IO;
using System.Text;
using Finik.UI;
using Finik.UI.Onboarding;
using UnityEditor;
using UnityEngine;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Runs every screen audit on a handful of phone shapes instead of on whatever the Game view
    /// happens to be: a notched portrait and its landscape, an Android punch hole, a three-button
    /// navigation bar and the Fold's narrow cover screen. Each shape sets both the resolution and the
    /// simulated safe area, so the checks see the same rect a real device would give them.
    ///
    /// Needs Play Mode. Start it from Finik/UI/Audit Devices; it walks the list over several editor
    /// frames (the screens need frames to lay out) and writes the report to <see cref="ReportPath"/>.
    /// </summary>
    public static class FinikDeviceAudit
    {
        /// <summary>Under Temp/, so the report never lands in the project or in git.</summary>
        public const string ReportPath = "Temp/FinikDeviceAudit.txt";

        readonly struct Device
        {
            public readonly string Name;
            public readonly int Width, Height;
            public readonly FinikNotch Notch;

            public Device(string name, int width, int height, FinikNotch notch)
            {
                Name = name;
                Width = width;
                Height = height;
                Notch = notch;
            }
        }

        static readonly Device[] Devices =
        {
            new("iPhone 16 Pro Max, портрет", 1320, 2868, FinikNotch.IPhoneDynamicIsland),
            new("iPhone 16 Pro Max, ландшафт", 2868, 1320, FinikNotch.IPhoneDynamicIsland),
            new("Galaxy Z Fold6, основной экран, портрет", 1856, 2160, FinikNotch.None),
            new("Galaxy Z Fold6, основной экран, ландшафт", 2160, 1856, FinikNotch.None),
            new("Galaxy Z Fold6, внешний экран, портрет", 968, 2376, FinikNotch.AndroidPunchHole),
            new("Galaxy Z Fold6, внешний экран, ландшафт", 2376, 968, FinikNotch.AndroidPunchHole)
        };

        static readonly (string name, Func<string> run)[] Screens =
        {
            ("Home HUD", FinikHomeHudAudit.Run),
            ("Onboarding", FinikOnboardingAudit.Run),
            ("Food", FinikFoodScreenAudit.Run),
            ("Quests", FinikQuestScreenAudit.Run),
            ("Savings", FinikSavingsScreenAudit.Run),
            ("Shop", FinikShopScreenAudit.Run),
            ("Progress", FinikProgressScreenAudit.Run),
            ("Budget", FinikBudgetScreenAudit.Run),
            ("Settings", FinikSettingsScreenAudit.Run),
            ("Adult", FinikAdultScreenAudit.Run)
        };

        // Frames to let a new resolution and safe area settle before measuring. The canvas scaler, the
        // safe-area fitter and the orientation layouts apply on their own update, and the room hints
        // then ease into their stack over about a third of a second — measure before that and they are
        // still on top of each other.
        const int SettleFrames = 45;
        // After the onboarding demo the showcase camera is still framing the pet, which projects the
        // fridge and the desk onto nearly the same spot and piles their hints on top of each other.
        // Wait for it to fly back to the room, then let the hints reach their places.
        const int AfterProfileFrames = 90;
        const int ProfileTimeoutFrames = 1500;
        const int CameraTimeoutFrames = 900;

        static IEnumerator routine;
        // The Game view's size is editor state and survives Play Mode: without putting it back, the
        // editor keeps rendering the last phone in the list long after the run is over.
        static int restoreGameViewSize = -1;

        [MenuItem("Finik/UI/Audit Devices", priority = 1)]
        static void Menu()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("Finik/UI/Audit Devices: запусти Play Mode — аудиты меряют живые экраны.");
                return;
            }
            if (routine != null)
            {
                Debug.LogWarning("Finik/UI/Audit Devices: прогон уже идёт.");
                return;
            }
            restoreGameViewSize = FinikUiAudit.SelectedGameViewSize();
            routine = Sweep();
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            bool alive;
            try
            {
                alive = routine != null && routine.MoveNext();
            }
            catch (Exception e)
            {
                // A broken screen must not leave the editor ticking a dead routine forever.
                Debug.LogException(e);
                alive = false;
            }
            if (alive && EditorApplication.isPlaying) return;
            // Runs however the sweep ended — finished, thrown, or Play Mode stopped under it — so the
            // editor never keeps the simulated notch or the last phone's resolution.
            EditorApplication.update -= Tick;
            routine = null;
            FinikUiAudit.SetNotch(FinikNotch.None);
            FinikUiAudit.SelectGameViewSize(restoreGameViewSize);
            restoreGameViewSize = -1;
        }

        static IEnumerator Sweep()
        {
            var report = new StringBuilder($"Finik device audit, {DateTime.Now:yyyy-MM-dd HH:mm}\n");

            // The screens refuse to open without a profile, and Play Mode starts by wiping it.
            Debug.Log(FinikUiAudit.EnsureProfile());
            for (int frame = 0; frame < ProfileTimeoutFrames && !FinikProfileStore.TryLoad(out _); frame++) yield return null;
            if (!FinikProfileStore.TryLoad(out _))
            {
                report.AppendLine("! онбординг не создал профиль — экраны не открыть, прогон прерван.");
                Write(report.ToString());
                yield break;
            }
            var showcase = UnityEngine.Object.FindAnyObjectByType<FinikShowcaseCamera>(FindObjectsInactive.Include);
            for (int frame = 0; frame < CameraTimeoutFrames && showcase && showcase.IsActive; frame++) yield return null;
            for (int frame = 0; frame < AfterProfileFrames; frame++) yield return null;

            foreach (var device in Devices)
            {
                // Every screen audit leaves its menu open. Start each phone from the bare room: with a
                // menu up, the camera holds its close-up and the room hints pile onto one spot.
                foreach (var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                    if (behaviour is IFinikScreen screen && screen.IsOpen) screen.Close();
                for (int frame = 0; frame < CameraTimeoutFrames && showcase && showcase.IsActive; frame++) yield return null;

                FinikUiAudit.SetResolution(device.Width, device.Height);
                FinikUiAudit.SetNotch(device.Notch);
                for (int frame = 0; frame < SettleFrames; frame++) yield return null;

                report.AppendLine($"\n================ {device.Name} ({device.Width}x{device.Height}, {device.Notch}) ================");
                foreach (var (name, run) in Screens)
                {
                    string result;
                    try
                    {
                        result = run();
                    }
                    catch (Exception e)
                    {
                        result = $"{name}: упал — {e.Message}";
                        Debug.LogException(e);
                    }
                    report.AppendLine(result.TrimEnd());
                    // Closing a screen and opening the next one takes a frame or two.
                    for (int frame = 0; frame < 4; frame++) yield return null;
                }
            }

            FinikUiAudit.SetNotch(FinikNotch.None);
            Write(report.ToString());
        }

        static void Write(string report)
        {
            string path = Path.GetFullPath(ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, report);
            Debug.Log($"FINIK_DEVICE_AUDIT: отчёт записан в {path}\n{report}");
        }
    }
}
