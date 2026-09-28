using Finik.UI.Onboarding;
using UnityEditor;
using UnityEngine;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Play Mode check of the onboarding at the current Game view resolution: the title, the basics
    /// cards, the pet picker with every pet in the catalog, the naming step and the finale. The flow
    /// is driven by showing each screen and pushing each pet through the picker — no profile is saved.
    ///
    ///     Finik.Editor.UI.FinikOnboardingAudit.SetResolution(1080, 1920);  // then let a few frames pass
    ///     Finik.Editor.UI.FinikOnboardingAudit.Run();
    /// </summary>
    public static class FinikOnboardingAudit
    {
        static readonly (string a, string b)[] IntendedOverlaps =
        {
            // The pet card is a poster: the avatar sits behind its title and the dots ride its edge.
            ("Avatar", "Title"),
            ("Avatar", "Role"),
            ("Dots", "Avatar"),
            ("Stage", "Avatar"),
            ("Speech", "Avatar")
        };

        static readonly string[] ContentImages = { "Avatar", "Icon", "Picture" };

        static readonly string[] Containers = { "Face", "Hero", "Plate", "Card", "Bubble" };

        [MenuItem("Finik/UI/Audit Onboarding")]
        static void Menu() => Debug.Log(Run());

        public static string SetResolution(int width, int height) => FinikUiAudit.SetResolution(width, height);

        public static string Run()
        {
            if (!EditorApplication.isPlaying) return "Enter Play Mode first.";
            var flow = Object.FindAnyObjectByType<FinikOnboardingFlow>(FindObjectsInactive.Include);
            if (!flow) return "UI_Onboarding is missing: run Finik/UI/Rebuild Onboarding.";

            var audit = new FinikUiAudit(IntendedOverlaps, ContentImages, Containers);

            // The flow switches itself off once a profile exists; its canvas has to be on for any
            // rect to be measurable at all.
            var canvas = flow.GetComponentInParent<Canvas>(true);
            var root = canvas ? canvas.rootCanvas.gameObject : flow.gameObject;
            bool wasActive = root.activeSelf;
            root.SetActive(true);
            bool flowWasActive = flow.gameObject.activeSelf;
            flow.gameObject.SetActive(true);

            var screens = new (string field, string what)[]
            {
                ("titleScreen", "заставка"),
                ("basicsScreen", "основы"),
                ("petsScreen", "выбор питомца"),
                ("customizeScreen", "имя питомца"),
                ("finaleScreen", "финал"),
                ("tutorialScreen", "как играть")
            };

            RectTransform ColumnOf(FinikScreenPanel panel)
            {
                var column = panel.transform.Find("Column") ?? panel.transform.Find("Card");
                return (RectTransform)(column ? column : panel.transform);
            }

            FinikScreenPanel Panel(string field) => (FinikScreenPanel)FinikUiAudit.Get(flow, field);

            // One screen at a time, so a hidden panel never counts as an overlap on the visible one.
            foreach (var (field, what) in screens)
            {
                foreach (var (other, _) in screens) Panel(other).Hide(instant: true);
                var panel = Panel(field);
                panel.Show(instant: true);

                if (field != "petsScreen")
                {
                    audit.Check(ColumnOf(panel), what);
                    continue;
                }

                // The picker is the widest state: every pet has its own title, role and speech.
                var pets = (System.Array)FinikUiAudit.Get(flow, "pets");
                var speech = (FinikTypewriter)FinikUiAudit.Get(flow, "petSpeech");
                Panel("petCard").Show(instant: true);
                for (int i = 0; i < pets.Length; i++)
                {
                    FinikUiAudit.Call(flow, "ShowPet", i, 0);
                    // The pet's line types itself out; measure the finished sentence, not one frame of it.
                    if (speech) speech.Complete();
                    audit.Check(ColumnOf(panel), $"{what}: {i + 1} из {pets.Length}");
                }
            }

            foreach (var (field, _) in screens) Panel(field).Hide(instant: true);
            var title = Panel("titleScreen");
            title.Show(instant: true);
            var reportColumn = ColumnOf(title);
            string report = audit.Report("Onboarding", reportColumn, System.Array.Empty<string>());

            flow.gameObject.SetActive(flowWasActive);
            root.SetActive(wasActive);
            return report;
        }
    }
}
