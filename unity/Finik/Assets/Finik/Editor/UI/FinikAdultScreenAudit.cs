using Finik.Core;
using Finik.UI.Adult;
using Finik.UI.Onboarding;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Play Mode check of the adult section at the current Game view resolution: both gate steps, the
    /// report (page by page) and both confirmations. Nothing is reset — the destructive actions are
    /// only rendered.
    ///
    ///     Finik.Editor.UI.FinikAdultScreenAudit.SetResolution(1080, 1920);  // then let a few frames pass
    ///     Finik.Editor.UI.FinikAdultScreenAudit.Run();
    /// </summary>
    public static class FinikAdultScreenAudit
    {
        static readonly (string a, string b)[] IntendedOverlaps =
        {
            // The filling bar sits under the hold button's own label.
            ("Fill", "Label"),
            ("FillArea", "Label"),
            // The switch's word sits on its track, under the sliding knob's free half.
            ("State", "Knob"),
            // A tab's caption lies on its own chip.
            ("Chosen", "Label")
        };

        static readonly string[] ContentImages = { "Icon", "Image", "Mark" };

        // Only real backgrounds a text must stay inside; a sibling card is not a container.
        static readonly string[] Containers = { "Glass", "Track", "Quests", "Days", "Topics" };

        [MenuItem("Finik/UI/Audit Adult Screen")]
        static void Menu() => Debug.Log(Run());

        public static string SetResolution(int width, int height) => FinikUiAudit.SetResolution(width, height);

        public static string EnsureProfile() => FinikUiAudit.EnsureProfile();

        public static string Run()
        {
            if (!EditorApplication.isPlaying) return "Enter Play Mode first.";
            var screen = Object.FindAnyObjectByType<FinikAdultScreen>(FindObjectsInactive.Include);
            if (!screen) return "Screen_Adult is missing: run Finik/UI/Rebuild Adult Screen.";
            if (!screen.IsOpen && !screen.OpenGate()) return "Adult screen did not open: finish onboarding first.";

            var audit = new FinikUiAudit(IntendedOverlaps, ContentImages, Containers);
            var gate = (FinikScreenPanel)FinikUiAudit.Get(screen, "gate");
            var report = (FinikScreenPanel)FinikUiAudit.Get(screen, "report");
            var confirm = (FinikScreenPanel)FinikUiAudit.Get(screen, "confirm");
            var reportScroll = (ScrollRect)FinikUiAudit.Get(screen, "reportScroll");
            var gateColumn = (RectTransform)gate.transform.Find("Column");
            var reportColumn = (RectTransform)report.transform.Find("Column");
            var confirmColumn = (RectTransform)confirm.transform.Find("Column");
            var gateScroll = gateColumn.GetComponent<ScrollRect>();
            var confirmScroll = confirmColumn.GetComponent<ScrollRect>();

            // Step one: hold.
            report.Hide(instant: true);
            confirm.Hide(instant: true);
            gate.Show(instant: true);
            FinikUiAudit.Call(screen, "SetStep", 1);
            audit.CheckScrolling(gateColumn, gateScroll, "шаг 1 — удержание");

            // Step two: the keypad, empty, typed, and after a wrong answer.
            FinikUiAudit.Call(screen, "SetStep", 2);
            audit.CheckScrolling(gateColumn, gateScroll, "шаг 2 — пример");
            var challenge = (FinikAdultChallenge)FinikUiAudit.Get(screen, "challenge");
            int wrong = challenge.Answer + 1;
            foreach (char digit in wrong.ToString()) FinikUiAudit.Call(screen, "Type", digit - '0');
            audit.CheckScrolling(gateColumn, gateScroll, "шаг 2 — ответ набран");
            FinikUiAudit.Call(screen, "SubmitAnswer");
            audit.CheckScrolling(gateColumn, gateScroll, "шаг 2 — неверный ответ");

            // The report, tab by tab; «Управление» again with the reset message under the buttons.
            gate.Hide(instant: true);
            report.Show(instant: true);
            FinikUiAudit.Call(screen, "Render");
            string[] tabs = { "прогресс", "обучение", "управление" };
            for (int i = 0; i < tabs.Length; i++)
            {
                FinikUiAudit.Call(screen, "SelectTab", i);
                audit.CheckScrolling(reportColumn, reportScroll, $"отчёт, вкладка «{tabs[i]}»");
            }
            FinikUiAudit.Call(screen, "ShowResult", "Прогресс сброшен. Игра начинается с первого дня.");
            audit.CheckScrolling(reportColumn, reportScroll, "отчёт после сброса");
            FinikUiAudit.Call(screen, "ShowResult", string.Empty);
            FinikUiAudit.Call(screen, "SelectTab", 0);

            // Both confirmations, rendered without running anything.
            report.Hide(instant: true);
            var type = screen.GetType();
            var pendingField = type.GetField("pending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            foreach (var (pending, what) in new[] { (1, "подтверждение сброса"), (2, "подтверждение удаления") })
            {
                FinikUiAudit.Call(screen, "Ask", System.Enum.ToObject(pendingField.FieldType, pending));
                confirm.Show(instant: true);
                audit.CheckScrolling(confirmColumn, confirmScroll, what);
            }
            FinikUiAudit.Call(screen, "CancelPending");

            return audit.Report("Adult screen", reportColumn, FinikEconomyBalance.Validate());
        }
    }
}
