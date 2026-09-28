using System;
using Finik.Core;
using Finik.Navigation;
using Finik.UI.Adult;
using Finik.UI.Onboarding;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Settings
{
    /// <summary>
    /// «Настройки», opened by the gear on the home screen: which pet lives in the room, music and
    /// sounds, how smooth the game runs, and the way into the adult section.
    ///
    /// Everything here is safe for a child to change; nothing touches progress. The adult section
    /// keeps its own hold-and-answer gate, so this screen only hands over to it.
    /// </summary>
    public sealed class FinikSettingsScreen : MonoBehaviour, IFinikScreen
    {
        [SerializeField] FinikScreenPanel panel;
        [SerializeField] ScrollRect scroll;

        [Header("Pet")]
        [Tooltip("One card per pet; the card's id is the pet id (fox, cat, raccoon).")]
        [SerializeField] FinikChoiceItem[] petChoices = Array.Empty<FinikChoiceItem>();

        [Header("Sound")]
        [SerializeField] FinikToggleSwitch musicSwitch;
        [SerializeField] FinikToggleSwitch soundsSwitch;
        [SerializeField] FinikToggleSwitch voiceSwitch;

        [Header("Performance")]
        [Tooltip("One chip per FinikFrameRateMode, in the order of the enum.")]
        [SerializeField] FinikChoiceItem[] frameRateChips = Array.Empty<FinikChoiceItem>();
        [SerializeField] TMP_Text frameRateNote;

        [Header("Adult")]
        [SerializeField] Button adultButton;
        [SerializeField] FinikAdultScreen adultScreen;

        [Header("Shared")]
        [SerializeField] Button closeButton;
        [SerializeField] Button doneButton;
        [SerializeField] GameObject hudRoot;
        [SerializeField] FinikMovementController movement;
        [SerializeField] FinikShowcaseCamera showcase;
        [SerializeField] Behaviour[] pauseWhileOpen = Array.Empty<Behaviour>();

        bool open;
        bool hooked;

        public bool IsOpen => open;

        /// <summary>Like every menu over the room: the HUD goes away and the pet stands still.</summary>
        public bool HoldsRoom => true;

        void Awake()
        {
            EnsureVoiceSwitch();
            Hook();
        }

        void OnEnable()
        {
            EnsureVoiceSwitch();
            Hook();
            FinikScreens.Register(this);
            FinikPerformance.Changed += RenderPerformance;
            FinikAudioSettings.Changed += RenderSound;
        }

        void OnDisable()
        {
            FinikScreens.Unregister(this);
            FinikPerformance.Changed -= RenderPerformance;
            FinikAudioSettings.Changed -= RenderSound;
        }

        void EnsureVoiceSwitch()
        {
            if (voiceSwitch || !soundsSwitch) return;
            var sourceRow = soundsSwitch.transform.parent;
            if (!sourceRow || !sourceRow.parent) return;

            var clone = Instantiate(sourceRow.gameObject, sourceRow.parent, false);
            clone.name = "Voice";
            var label = clone.transform.Find("Title")?.GetComponent<TMP_Text>();
            if (label) label.text = "Озвучка";
            voiceSwitch = clone.GetComponentInChildren<FinikToggleSwitch>(true);
        }

        /// <summary>Guarded and repeated on enable: the editor runs without domain reload, see FinikChoiceItem.</summary>
        void Hook()
        {
            if (hooked) return;
            hooked = true;
            foreach (var choice in petChoices)
                if (choice) choice.Clicked += item => SelectPet(item.Id);
            for (int i = 0; i < frameRateChips.Length; i++)
            {
                var mode = (FinikFrameRateMode)i;
                if (frameRateChips[i]) frameRateChips[i].Clicked += _ => FinikPerformance.Mode = mode;
            }
            if (musicSwitch) musicSwitch.Toggled += value => FinikAudioSettings.Music = value;
            if (soundsSwitch) soundsSwitch.Toggled += value => FinikAudioSettings.Sounds = value;
            if (voiceSwitch) voiceSwitch.Toggled += value => FinikAudioSettings.Voice = value;
            if (adultButton) adultButton.onClick.AddListener(OpenAdult);
            if (closeButton) closeButton.onClick.AddListener(Close);
            if (doneButton) doneButton.onClick.AddListener(Close);
        }

        public bool Open()
        {
            if (open) return true;
            if (!FinikProfileStore.TryLoad(out _)) return false;

            open = true;
            FinikScreens.CloseOthers(this);
            // Coming back from the adult section, the room is already taken over: take it quietly.
            if (hudRoot) hudRoot.SetActive(false);
            foreach (var behaviour in pauseWhileOpen) if (behaviour) behaviour.enabled = false;
            if (movement) movement.BeginActivity(this);
            if (showcase) showcase.Engage();

            Render(animate: false);
            if (panel) panel.Show();
            FinikFitColumn.RefreshUnder(panel);
            if (scroll) scroll.verticalNormalizedPosition = 1f;
            return true;
        }

        public void Close()
        {
            if (!open) return;
            open = false;
            if (panel) panel.Hide();
            // Another menu has already taken the HUD, the camera and the pet: leave all three to it.
            // This menu's own hold on Finik goes whoever takes the room next; the new menu holds its own.
            if (movement) movement.EndActivity(this);
            if (FinikScreens.RoomTakenOver(this)) return;
            foreach (var behaviour in pauseWhileOpen) if (behaviour) behaviour.enabled = true;
            if (showcase) showcase.Release();
            if (hudRoot) hudRoot.SetActive(true);
        }

        void OpenAdult()
        {
            if (adultScreen) adultScreen.OpenGate();
            else Debug.LogWarning("[FinikSettings] Adult screen is missing: run Finik/UI/Rebuild Adult Screen.");
        }

        // ------------------------------------------------------------------ render

        void Render(bool animate)
        {
            RenderPets();
            RenderSound(animate);
            RenderPerformance();
        }

        void RenderSound() => RenderSound(animate: true);

        void RenderSound(bool animate)
        {
            if (musicSwitch) musicSwitch.SetIsOn(FinikAudioSettings.Music, animate);
            if (soundsSwitch) soundsSwitch.SetIsOn(FinikAudioSettings.Sounds, animate);
            if (voiceSwitch) voiceSwitch.SetIsOn(FinikAudioSettings.Voice, animate);
        }

        void RenderPerformance()
        {
            var mode = FinikPerformance.Mode;
            for (int i = 0; i < frameRateChips.Length; i++)
                if (frameRateChips[i]) frameRateChips[i].SetSelected(i == (int)mode);
            if (frameRateNote) frameRateNote.text = FinikTypography.Fix(FinikPerformance.DescriptionFor(mode));
        }

        void RenderPets()
        {
            if (!FinikProfileStore.TryLoad(out var profile)) return;
            var switcher = Switcher();
            foreach (var choice in petChoices)
            {
                if (!choice) continue;
                bool available = Available(switcher, choice.Id);
                choice.Button.interactable = available;
                var group = choice.GetComponent<CanvasGroup>();
                if (group) group.alpha = available ? 1f : 0.45f;
                choice.SetSelected(profile.petId == choice.Id);
            }
        }

        void SelectPet(string id)
        {
            var switcher = Switcher();
            if (!Available(switcher, id) || !FinikProfileStore.TryLoad(out var profile)) return;
            if (profile.petId != id && switcher.Show(id, switcher.ActiveStage))
            {
                profile.petId = id;
                // Saving raises FinikProfileStore.Changed: the HUD avatar follows on its own.
                FinikProfileStore.Save(profile);
            }
            RenderPets();
        }

        static FinikCharacterSwitcher Switcher() =>
            FindFirstObjectByType<FinikCharacterSwitcher>(FindObjectsInactive.Include);

        static bool Available(FinikCharacterSwitcher switcher, string id) =>
            switcher && switcher.HasCharacter(id, switcher.ActiveStage);
    }
}
