using Finik.UI;
using UnityEditor;
using UnityEngine;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Flips the editor-only notch simulation from the menu. The Device Simulator view (Window &gt;
    /// General &gt; Device Simulator) is the real thing and needs none of this; these entries exist so
    /// the plain Game view — which always reports a full-screen safe area — can be checked too, and so
    /// the layout audits can drive the same setting from a script.
    /// </summary>
    static class FinikSafeAreaMenu
    {
        const string Root = "Finik/Testing/Safe Area/";

        [MenuItem(Root + "Без выреза", priority = 0)]
        static void None() => Set(FinikNotch.None);

        [MenuItem(Root + "iPhone Dynamic Island", priority = 1)]
        static void DynamicIsland() => Set(FinikNotch.IPhoneDynamicIsland);

        [MenuItem(Root + "iPhone с чёлкой", priority = 2)]
        static void Notch() => Set(FinikNotch.IPhoneNotch);

        [MenuItem(Root + "Android, дырка под камеру", priority = 3)]
        static void PunchHole() => Set(FinikNotch.AndroidPunchHole);

        [MenuItem(Root + "Android, дырка + кнопки навигации", priority = 4)]
        static void NavBar() => Set(FinikNotch.AndroidNavBar);

        [MenuItem(Root + "Без выреза", true)] static bool VNone() => Check(FinikNotch.None);
        [MenuItem(Root + "iPhone Dynamic Island", true)] static bool VIsland() => Check(FinikNotch.IPhoneDynamicIsland);
        [MenuItem(Root + "iPhone с чёлкой", true)] static bool VNotch() => Check(FinikNotch.IPhoneNotch);
        [MenuItem(Root + "Android, дырка под камеру", true)] static bool VPunch() => Check(FinikNotch.AndroidPunchHole);
        [MenuItem(Root + "Android, дырка + кнопки навигации", true)] static bool VNav() => Check(FinikNotch.AndroidNavBar);

        static void Set(FinikNotch notch) => Debug.Log(FinikUiAudit.SetNotch(notch));

        static bool Check(FinikNotch notch)
        {
            Menu.SetChecked(Root + MenuName(notch), FinikSafeAreaSimulation.Notch == notch);
            return true;
        }

        static string MenuName(FinikNotch notch) => notch switch
        {
            FinikNotch.IPhoneDynamicIsland => "iPhone Dynamic Island",
            FinikNotch.IPhoneNotch => "iPhone с чёлкой",
            FinikNotch.AndroidPunchHole => "Android, дырка под камеру",
            FinikNotch.AndroidNavBar => "Android, дырка + кнопки навигации",
            _ => "Без выреза"
        };
    }
}
