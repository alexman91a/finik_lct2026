using System.Collections.Generic;
using System.Linq;
using Finik.Core;
using Finik.Navigation;
using Finik.UI;
using Finik.UI.Onboarding;
using Finik.UI.Settings;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static Finik.Editor.UI.FinikUiKit;
using static Finik.Editor.UI.FinikMenuPanels;
using Object = UnityEngine.Object;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Builds Screen_Settings: the pet, music and sounds, game smoothness and the way into the adult
    /// section. Run before the adult screen, which links back here.
    /// </summary>
    public static class FinikSettingsScreenBuilder
    {
        public const string RootName = "Screen_Settings";
        const float PetCardHeight = 160f;
        const float SwitchWidth = 180f, SwitchHeight = 80f;

        static readonly (string id, string label, string avatar)[] Pets =
        {
            ("fox", "Лисёнок", "avatar_finik"),
            ("cat", "Кошечка", "avatar_cat"),
            ("raccoon", "Енотик", "avatar_raccoon")
        };

        [MenuItem("Finik/UI/Rebuild Settings Screen")]
        public static void RebuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            if (!Prepare(out string message)) return message;
            var scene = EditorSceneManager.GetActiveScene();

            var finik = GameObject.Find("Finik_Root");
            if (!finik) return "Finik_Root is missing from the scene.";
            var cameraGo = GameObject.Find("camera_gameplay");
            var showcase = cameraGo ? cameraGo.GetComponent<FinikShowcaseCamera>() : null;
            if (!showcase) return "FinikShowcaseCamera is missing: run Finik/UI/Rebuild Onboarding first.";
            var hud = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "HUD_Home");
            if (!hud) return "Build the home HUD first (Finik/UI/Rebuild Home HUD).";

            var root = ReuseOrCreateCanvas(RootName, 26);
            var screen = GetOrAdd<FinikSettingsScreen>(root);
            var safe = Rect("SafeArea", root.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<FinikSafeArea>();

            var menu = FinikMenuPanels.Build(safe, "Settings", footer: true, wide: true);
            SetField(screen, "panel", menu.panel);
            SetField(screen, "scroll", menu.scroll);
            Header(menu, "icon_settings", "НАСТРОЙКИ", Accent, "Настройки");

            var page = Page(menu.content, "Page");
            BuildPets(page, screen);
            BuildSound(page, screen);
            BuildPerformance(page, screen);
            BuildAdultEntry(page, screen);

            var done = WideButton(menu.footer, "Done", "ui_btn_green", "Готово", 34, Color.white, FooterHeight, 440, "ui_decor_paw_green");
            SetField(screen, "doneButton", done);
            SetField(screen, "closeButton", CloseCorner(menu));
            Dismiss(menu.panel, done);

            SetField(screen, "showcase", showcase);
            SetField(screen, "movement", finik.GetComponent<FinikMovementController>());
            SetField(screen, "hudRoot", hud);
            var paused = new List<Object>();
            foreach (var behaviour in new Behaviour[] { finik.GetComponent<FinikInputController>(), finik.GetComponent<FinikWanderController>() })
                if (behaviour) paused.Add(behaviour);
            SetField(screen, "pauseWhileOpen", paused.ToArray());

            // The adult screen is built after this one; when it already exists, link both ways now.
            var adult = Object.FindAnyObjectByType<Finik.UI.Adult.FinikAdultScreen>(FindObjectsInactive.Include);
            if (adult)
            {
                SetField(screen, "adultScreen", adult);
                SetField(adult, "settingsScreen", screen);
            }

            var binder = hud.GetComponent<FinikHudBinder>();
            if (binder) SetField(binder, "settingsScreen", screen);

            EnsureTapTargets(root);
            EnsureEventSystem();
            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"Built {RootName}: питомец, звук, плавность и вход для взрослых.";
        }

        // ------------------------------------------------------------------ sections
        // Four compact sections. Upright they stack; on a wide screen they pair up in hierarchy order
        // (pet | sound, smoothness | adult), so the whole menu fits a landscape phone without scrolling.

        static void BuildPets(Transform page, FinikSettingsScreen screen)
        {
            var section = Section(page, "Pet", "ПИТОМЕЦ", Violet);
            var row = Row(section, "Cards", PetCardHeight, 16, TextAnchor.MiddleCenter);
            var cards = row.GetComponent<HorizontalLayoutGroup>();
            cards.childForceExpandWidth = cards.childForceExpandHeight = true;
            var choices = new List<Object>();
            foreach (var (id, label, avatar) in Pets)
                choices.Add(PetCard(row, id, label, avatar));
            SetField(screen, "petChoices", choices.ToArray());
        }

        /// <summary>A card with the pet's portrait; an orange frame and a tick when it is the one in the room.</summary>
        static FinikChoiceItem PetCard(Transform row, string id, string label, string avatar)
        {
            var slot = Rect("Pet_" + id, row);
            Size(slot, flexibleWidth: 1);
            slot.gameObject.AddComponent<CanvasGroup>();
            var body = Rect("Body", slot);
            Stretch(body);
            var press = body.gameObject.AddComponent<FinikPressFeedback>();
            var face = Img("Face", body, "ui_card_needs", sliced: true, raycast: true);
            Stretch(face.rectTransform);
            var button = MakeButton(body, face);
            var frame = Img("Selected", body, "ui_card_selected", sliced: true, color: new Color(1, 1, 1, 0));
            Stretch(frame.rectTransform);

            var portrait = Img("Avatar", body, avatar, preserveAspect: true);
            Place(portrait.rectTransform, TopCenter, TopCenter, new Vector2(0, -12), new Vector2(96, 96));
            var name = Text("Label", body, label, 28, Ink, TextAlignmentOptions.Center, outlined: false);
            AutoSize(name, 24, 28);
            Place(name.rectTransform, BottomCenter, BottomCenter, new Vector2(0, 14), new Vector2(230, 38));

            var check = Img("Check", body, "ui_check", preserveAspect: true);
            Place(check.rectTransform, TopRight, Center, new Vector2(-22, -22), new Vector2(52, 52));
            check.gameObject.SetActive(false);

            var choice = slot.gameObject.AddComponent<FinikChoiceItem>();
            choice.Configure(id, button, press, frame, check.gameObject, name);
            // The caption keeps its ink when chosen: the frame and the tick already say so.
            SetField(choice, "selectedLabelColor", Ink);
            SetField(choice, "selectedScale", 1.04f);
            return choice;
        }

        /// <summary>Music, effects and voice side by side: a word and a switch each.</summary>
        static void BuildSound(Transform page, FinikSettingsScreen screen)
        {
            var section = Section(page, "Sound", "ЗВУК", Violet);
            var row = Row(section, "Switches", SwitchHeight, 36, TextAnchor.MiddleCenter);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            SetField(screen, "musicSwitch", SoundSwitch(row, "Music", "Музыка"));
            SetField(screen, "soundsSwitch", SoundSwitch(row, "Sounds", "Звуки"));
            SetField(screen, "voiceSwitch", SoundSwitch(row, "Voice", "Озвучка"));
        }

        static FinikToggleSwitch SoundSwitch(Transform row, string name, string title)
        {
            var half = Row(row, name, SwitchHeight, 12);
            Size(half, flexibleWidth: 1);
            var label = Text("Title", half, title, 30, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            AutoSize(label, 24, 30);
            Size(label, flexibleWidth: 1);
            var slot = Rect("Control", half);
            Size(slot, SwitchWidth, SwitchHeight);
            return ToggleSwitch(slot, SwitchHeight);
        }

        static void BuildPerformance(Transform page, FinikSettingsScreen screen)
        {
            var section = Section(page, "FrameRate", "ПЛАВНОСТЬ ИГРЫ", Violet);
            var chips = Row(section, "Chips", 76, 14, TextAnchor.MiddleCenter);
            chips.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            chips.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;
            var modes = (FinikFrameRateMode[])System.Enum.GetValues(typeof(FinikFrameRateMode));
            var items = new Object[modes.Length];
            for (int i = 0; i < modes.Length; i++)
            {
                var chip = Chip(chips, modes[i].ToString(), FinikPerformance.LabelFor(modes[i]));
                Size(chip, flexibleWidth: 1);
                items[i] = chip;
            }
            SetField(screen, "frameRateChips", items);
            var note = Paragraph(section, "Note", FinikPerformance.DescriptionFor(FinikPerformance.DefaultMode), 24, InkSoft);
            SetField(screen, "frameRateNote", note);
        }

        static void BuildAdultEntry(Transform page, FinikSettingsScreen screen)
        {
            var section = Section(page, "Adult", null, Accent, "ui_card_task");
            var slot = SettingRow(section, "Entry", "Для взрослых", "Прогресс ребёнка и управление профилем.",
                210, 84, out _, icon: "game_lock");
            var open = WideButton(slot, "Open", "ui_btn_orange", "Открыть", 30, Color.white, 84, 210);
            Stretch((RectTransform)open.transform.parent);
            SetField(screen, "adultButton", open);
        }
    }
}
