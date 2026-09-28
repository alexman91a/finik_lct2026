using System;
using System.Collections;
using Finik.Accessories;
using Finik.Core;
using Finik.Navigation;
using Finik.UI.Savings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Onboarding
{
    /// <summary>
    /// First-run flow over the 3D room: title → money basics → meet the pets → pet name →
    /// celebration → home HUD. Skipped when a profile is already saved on the device.
    /// </summary>
    public sealed class FinikOnboardingFlow : MonoBehaviour
    {
        [Serializable]
        public sealed class PetOption
        {
            public string id;
            public string name;
            public string description;
            [Tooltip("Accusative form for the button: «Выбрать Финика».")]
            public string accusative;
            public string role;
            [TextArea] public string quote;
            [Tooltip("Curiosity, order, savvy — 1..3 each.")]
            public int[] traits = { 2, 2, 2 };
            public Sprite avatar;
            [Tooltip("Full-body art shown in place of the 3D model until the pet has one.")]
            public Sprite fullBody;
            public string[] nameIdeas = Array.Empty<string>();
            [Tooltip("True when a 3D model for this pet exists in the room.")]
            public bool has3DModel;
        }

        /// <summary>One of the Нужно / Хочу / Коплю cards: tapping it is optional and only makes Finik react.</summary>
        [Serializable]
        public sealed class BasicCard
        {
            public Button button;
            public FinikPressFeedback feedback;
            [TextArea] public string reaction;
        }

        [Header("Screens")]
        [SerializeField] FinikScreenPanel titleScreen;
        [SerializeField] FinikScreenPanel basicsScreen;
        [SerializeField] FinikScreenPanel petsScreen;
        [SerializeField] FinikScreenPanel customizeScreen;
        [SerializeField] FinikScreenPanel finaleScreen;

        [Header("Title")]
        [SerializeField] Button playButton;
        [SerializeField] Button demoButton;

        [Header("Basics")]
        [SerializeField] BasicCard[] basics = Array.Empty<BasicCard>();
        [SerializeField] FinikTypewriter speech;
        [TextArea, SerializeField] string basicsIntro;
        [SerializeField] Button basicsNext;
        [SerializeField] Button basicsBack;
        [Tooltip("Hand that shows the cards can be tapped; leads to the ones not tapped yet.")]
        [SerializeField] FinikTapHint basicsHint;

        [Header("Pets")]
        [SerializeField] FinikScreenPanel petCard;
        [SerializeField] FinikSwipeArea petSwipe;
        [SerializeField] Image petAvatar;
        [SerializeField] TMP_Text petTitle;
        [SerializeField] TMP_Text petRole;
        [SerializeField] FinikTypewriter petSpeech;
        [Tooltip("Trait dots, row-major: 3 traits x 3 dots.")]
        [SerializeField] Graphic[] traitDots = Array.Empty<Graphic>();
        [SerializeField] Graphic[] petDots = Array.Empty<Graphic>();
        [SerializeField] Button prevPet;
        [SerializeField] Button nextPet;
        [SerializeField] Button choosePet;
        [SerializeField] TMP_Text choosePetLabel;
        [SerializeField] Button petsBack;
        [SerializeField] FinikScreenPanel petStage;
        [SerializeField] Image petStageImage;
        [SerializeField] Color traitOn = new(1f, 0.6f, 0.18f, 1f);
        [SerializeField] Color traitOff = new(0.12f, 0.15f, 0.36f, 0.15f);


        [Header("Customize")]
        [SerializeField] TMP_InputField petNameInput;
        [SerializeField] FinikChoiceItem[] petNameChips = Array.Empty<FinikChoiceItem>();
        [SerializeField] Button customizeDone;
        [SerializeField] CanvasGroup customizeDoneGroup;
        [SerializeField] Button customizeBack;

        [Header("Finale")]
        [SerializeField] TMP_Text finaleTitle;
        [SerializeField] FinikConfettiBurst confetti;

        [Header("How to play")]
        [SerializeField] FinikScreenPanel tutorialScreen;
        [SerializeField] TMP_Text tutorialStep;
        [SerializeField] TMP_Text tutorialTitle;
        [SerializeField] TMP_Text tutorialBody;
        [SerializeField] Image tutorialIcon;
        [SerializeField] Sprite[] tutorialIcons = Array.Empty<Sprite>();
        [SerializeField] Graphic[] tutorialDots = Array.Empty<Graphic>();
        [SerializeField] Button tutorialSkip;
        [SerializeField] Button tutorialReplay;
        [SerializeField] Button tutorialNext;
        [SerializeField] TMP_Text tutorialNextLabel;

        [Header("World")]
        [SerializeField] PetOption[] pets = Array.Empty<PetOption>();
        [SerializeField] FinikShowcaseCamera showcase;
        [SerializeField] FinikAccessoryRig rig;
        [SerializeField] FinikMovementController movement;
        [SerializeField] FinikActivityController activity;
        [SerializeField] FinikCharacterSwitcher characterSwitcher;
        [Tooltip("Gameplay behaviours paused while onboarding runs (tap-to-walk, wandering, random dances).")]
        [SerializeField] Behaviour[] pauseDuringOnboarding = Array.Empty<Behaviour>();
        [SerializeField] GameObject hudRoot;
        [SerializeField] FinikHudView hudView;
        [SerializeField] Color disabledTint = new(1f, 1f, 1f, 0.45f);

        Coroutine basicsIntroPop;
        Coroutine basicsGuide;
        bool[] basicsSeen = Array.Empty<bool>();
        int basicsTaps;
        int petIndex;
        string petId;
        Renderer[] finikRenderers = Array.Empty<Renderer>();
        FinikScreenPanel current;
        int tutorialIndex;
        bool tutorialFinished;
        FinikProfile tutorialProfile;

        static readonly string[] TutorialTitles =
        {
            "Учимся обращаться с деньгами",
            "Задания помогают расти",
            "Заботься о питомце",
            "Покупай и копи на цель"
        };

        static readonly string[] TutorialBodies =
        {
            "Здесь ты будешь учиться обращаться с деньгами: выбирать, на что потратить монеты, и решать, что лучше отложить.",
            "Каждый день тебя ждут небольшие задания. Выполняй их внимательно — и твой питомец будет расти, становиться старше и открывать новые возможности.",
            "Не забывай про питомца! С ним можно играть, его можно кормить и поднимать ему настроение.",
            "Монеты можно тратить на нужные вещи и интересные игрушки. А если часть откладывать и не тратить всё сразу — ты быстрее накопишь на свою цель."
        };

        static readonly string[] TutorialVoiceIds =
        {
            FinikAudioManager.VoiceTutorialWelcome,
            FinikAudioManager.VoiceTutorialQuests,
            FinikAudioManager.VoiceTutorialCare,
            FinikAudioManager.VoiceTutorialGoal
        };

        void Start()
        {
            EnsureTutorialUi();
            WireUi();
            if (FinikProfileStore.TryLoad(out var profile))
            {
                FinikGame.EnsureJourney();
                ShowHome(profile);
                HideAllScreens();

                if (!profile.demo && !profile.tutorialSeen)
                {
                    StartCoroutine(ResumeTutorial(profile));
                    return;
                }

                ResumeGameplay();
                FinikAudioManager.Instance.PlayAssistant(FinikAudioManager.VoiceGreetingReturn);
                gameObject.SetActive(false);
                return;
            }
            Begin();
        }

        void Begin()
        {
            if (hudRoot) hudRoot.SetActive(false);
            foreach (var behaviour in pauseDuringOnboarding) if (behaviour) behaviour.enabled = false;
            if (movement) movement.BeginActivity(this);
            if (showcase) showcase.Engage();
            HideAllScreens();
            petId = null;
            petIndex = 0;
            basicsSeen = new bool[basics.Length];
            ShowCharacter("fox");
            Go(titleScreen);
            FinikAudioManager.Instance.PlayAssistant(FinikAudioManager.VoiceGreetingHello);
        }

        void EnsureTutorialUi()
        {
            if (tutorialScreen) return;

            var view = FinikTutorialRuntimeUi.Build(
                titleScreen, customizeScreen, basics, speech, petTitle, petRole, petDots, basicsBack, basicsNext);
            if (view == null)
            {
                Debug.LogWarning("[FinikOnboarding] Tutorial UI could not be built from the onboarding presets.");
                return;
            }

            tutorialScreen = view.panel;
            tutorialStep = view.step;
            tutorialTitle = view.title;
            tutorialBody = view.body;
            tutorialIcon = view.icon;
            tutorialIcons = view.icons;
            tutorialDots = view.dots;
            tutorialSkip = view.skip;
            tutorialReplay = view.replay;
            tutorialNext = view.next;
            tutorialNextLabel = view.nextLabel;
        }

        void WireUi()
        {
            playButton.onClick.AddListener(EnterBasics);
            demoButton.onClick.AddListener(StartDemo);

            for (int i = 0; i < basics.Length; i++)
            {
                int index = i;
                if (basics[i].button) basics[i].button.onClick.AddListener(() => SelectBasic(index));
            }
            basicsNext.onClick.AddListener(() => EnterPets(0));
            basicsBack.onClick.AddListener(() => Go(titleScreen));

            prevPet.onClick.AddListener(() => ShowPet(petIndex - 1, direction: -1));
            nextPet.onClick.AddListener(() => ShowPet(petIndex + 1, direction: 1));
            if (petSwipe) petSwipe.Swiped += dir => ShowPet(petIndex - dir, direction: -dir);
            choosePet.onClick.AddListener(ChoosePet);
            petsBack.onClick.AddListener(() =>
            {
                ShowCharacter("fox");
                if (petStage) petStage.Hide();
                EnterBasics();
            });


            petNameInput.characterLimit = FinikProfile.MaxNameLength;
            petNameInput.onValueChanged.AddListener(_ => RefreshCustomize());
            foreach (var chip in petNameChips)
                chip.Clicked += c => { petNameInput.text = ChipLabel(c); RefreshCustomize(); };
            customizeDone.onClick.AddListener(Finish);
            customizeBack.onClick.AddListener(() => EnterPets(Math.Max(0, Array.FindIndex(pets, p => p.id == petId))));

            if (tutorialSkip) tutorialSkip.onClick.AddListener(CompleteTutorial);
            if (tutorialReplay) tutorialReplay.onClick.AddListener(ReplayTutorialVoice);
            if (tutorialNext) tutorialNext.onClick.AddListener(NextTutorialStep);
        }

        // ------------------------------------------------------------------ navigation

        void Go(FinikScreenPanel next)
        {
            if (current == next) return;
            FinikAudioManager.Instance.StopAssistant();
            if (next != basicsScreen) StopBasicsGuide();
            if (current) current.Hide();
            current = next;
            if (next) next.Show();
            if (next && next == customizeScreen)
                FinikAudioManager.Instance.PlayAssistant("intro_name", interruptCurrent: true);
        }

        void HideAllScreens()
        {
            foreach (var screen in new[] { titleScreen, basicsScreen, petsScreen, customizeScreen, finaleScreen, tutorialScreen, petStage })
                if (screen) screen.Hide(instant: true);
            current = null;
        }

        // ------------------------------------------------------------------ basics

        void EnterBasics()
        {
            Go(basicsScreen);
            FinikAudioManager.Instance.PlayAssistant("intro_basics", interruptCurrent: true);
            speech.Play(FinikTypography.Fix(basicsIntro));
            if (basicsIntroPop != null) StopCoroutine(basicsIntroPop);
            basicsIntroPop = StartCoroutine(PopBasics());
            if (basicsSeen.Length != basics.Length) basicsSeen = new bool[basics.Length];
            StopBasicsGuide();
            if (basicsHint) basicsGuide = StartCoroutine(GuideBasics());
        }

        /// <summary>
        /// Nothing on the cards says they can be tapped, so a hand taps the first one not tapped yet
        /// once Finik has finished talking, and moves on after each tap until all three were heard.
        /// </summary>
        IEnumerator GuideBasics()
        {
            // Quiet for this long (no typing, no voice) before the hand appears.
            const float Pause = 0.5f;
            // Never wait longer than this for the talking to stop.
            const float Patience = 5f;
            while (true)
            {
                float since = Time.unscaledTime, quiet = since;
                while (true)
                {
                    float now = Time.unscaledTime;
                    if (!speech.IsDone || FinikAudioManager.Instance.IsAssistantSpeaking) quiet = now;
                    if (now - quiet >= Pause || now - since >= Patience) break;
                    yield return null;
                }

                int next = Array.IndexOf(basicsSeen, false);
                var target = next >= 0 && basics[next].button ? (RectTransform)basics[next].button.transform : null;
                if (!target)
                {
                    basicsHint.Hide();
                    break;
                }
                int taps = basicsTaps;
                basicsHint.Show(target);
                while (basicsTaps == taps) yield return null;
                basicsHint.Hide();
            }
            basicsGuide = null;
        }

        void StopBasicsGuide()
        {
            if (basicsGuide != null) StopCoroutine(basicsGuide);
            basicsGuide = null;
            if (basicsHint) basicsHint.Hide();
        }

        /// <summary>The cards pop one after another as the screen appears, top to bottom.</summary>
        IEnumerator PopBasics()
        {
            foreach (var card in basics)
            {
                yield return new WaitForSecondsRealtime(0.12f);
                if (card.feedback) card.feedback.Punch(0.8f);
            }
            basicsIntroPop = null;
        }

        /// <summary>Finik comments on the tapped card; the card itself only springs, so all three keep one size.</summary>
        void SelectBasic(int index)
        {
            if (index < 0 || index >= basics.Length) return;
            if (index < basicsSeen.Length) basicsSeen[index] = true;
            basicsTaps++;
            string[] voices = { "intro_need", "intro_want", "intro_save" };
            if (index < voices.Length) FinikAudioManager.Instance.PlayAssistant(voices[index], interruptCurrent: true);
            speech.Play(FinikTypography.Fix(basics[index].reaction));
            if (activity) activity.TryPlayTapReaction();
        }

        // ------------------------------------------------------------------ pets

        void EnterPets(int index)
        {
            Go(petsScreen);
            ShowPet(Mathf.Max(0, index), direction: 0);
        }

        void ShowPet(int index, int direction)
        {
            if (pets.Length == 0) return;
            petIndex = (index % pets.Length + pets.Length) % pets.Length;
            var pet = pets[petIndex];
            FinikAudioManager.Instance.PlayAssistant("intro_pet_" + pet.id, interruptCurrent: true);
            petAvatar.sprite = pet.avatar;
            petTitle.text = pet.name;
            petRole.text = pet.role;
            petSpeech.Play(pet.quote);
            for (int t = 0; t < 3; t++)
                for (int d = 0; d < 3; d++)
                {
                    int i = t * 3 + d;
                    if (i >= traitDots.Length) continue;
                    int level = pet.traits != null && t < pet.traits.Length ? pet.traits[t] : 0;
                    traitDots[i].color = d < level ? traitOn : traitOff;
                }
            for (int i = 0; i < petDots.Length; i++)
            {
                var c = petDots[i].color;
                c.a = i == petIndex ? 1f : 0.3f;
                petDots[i].color = c;
                petDots[i].rectTransform.localScale = Vector3.one * (i == petIndex ? 1.25f : 1f);
            }
            choosePetLabel.text = $"Выбрать {(string.IsNullOrEmpty(pet.accusative) ? pet.name : pet.accusative)}";
            if (petCard) petCard.Show();

            bool show3D = ShowPetInRoom(pet);
            if (show3D && direction != 0 && activity) activity.TryPlayTapReaction();
        }

        void ChoosePet()
        {
            petId = pets[petIndex].id;
            EnterCustomize();
        }

        /// <summary>
        /// Shows the pet's 3D character in the room; a pet without one (or whose model is missing) stands
        /// in as 2D art where Finik would be. Returns true when the 3D character is shown.
        /// </summary>
        bool ShowPetInRoom(PetOption pet)
        {
            bool show3D = pet == null ? ShowCharacter("fox") : pet.has3DModel && ShowCharacter(pet.id);
            if (!show3D) HideCharacter();
            if (!petStage) return show3D;
            if (show3D || pet == null) petStage.Hide();
            else
            {
                petStageImage.sprite = pet.fullBody ? pet.fullBody : pet.avatar;
                petStage.Show();
            }
            return show3D;
        }

        bool ShowCharacter(string id)
        {
            if (characterSwitcher && characterSwitcher.Show(id)) return true;
            bool isFox = id == "fox";
            SetFinikVisible(isFox);
            return isFox;
        }

        void HideCharacter()
        {
            if (characterSwitcher) characterSwitcher.HideAll();
            else SetFinikVisible(false);
        }

        void SetFinikVisible(bool visible)
        {
            if (finikRenderers.Length == 0 && movement)
                finikRenderers = movement.GetComponentsInChildren<Renderer>(true);
            foreach (var r in finikRenderers) if (r) r.enabled = visible;
        }

        // ------------------------------------------------------------------ customize

        void EnterCustomize()
        {
            var pet = Pet(petId);
            var ideas = pet?.nameIdeas ?? Array.Empty<string>();
            for (int i = 0; i < petNameChips.Length; i++)
            {
                bool used = i < ideas.Length;
                petNameChips[i].gameObject.SetActive(used);
                if (used) SetChipLabel(petNameChips[i], ideas[i]);
            }
            // Keep a name the player typed themselves; otherwise suggest this pet's default name
            // (also replaces a suggestion left over from a previously chosen pet).
            string typed = FinikProfile.NormalizeName(petNameInput.text);
            bool ownName = typed.Length > 0 && !IsIdeaOfAnyPet(typed);
            if (!ownName && Array.IndexOf(ideas, typed) < 0) petNameInput.text = pet?.name ?? "Финик";
            ShowPetInRoom(pet);
            Go(customizeScreen);
            RefreshCustomize();
        }

        bool IsIdeaOfAnyPet(string value)
        {
            foreach (var pet in pets)
                if (pet.name == value || Array.IndexOf(pet.nameIdeas, value) >= 0)
                    return true;
            return false;
        }

        void RefreshCustomize()
        {
            string normalized = FinikProfile.NormalizeName(petNameInput.text);
            foreach (var chip in petNameChips) chip.SetSelected(ChipLabel(chip) == normalized);
            SetEnabled(customizeDone, customizeDoneGroup, FinikProfile.IsValidName(petNameInput.text));
        }

        // ------------------------------------------------------------------ how to play

        public void OpenTutorialAgain()
        {
            if (!FinikProfileStore.TryLoad(out var profile)) return;
            gameObject.SetActive(true);
            EnsureTutorialUi();
            StopAllCoroutines();
            StartCoroutine(ResumeTutorial(profile));
        }

        IEnumerator ResumeTutorial(FinikProfile profile)
        {
            foreach (var behaviour in pauseDuringOnboarding) if (behaviour) behaviour.enabled = false;
            if (movement) movement.BeginActivity(this);
            HideAllScreens();

            ShowTutorial(profile);
            while (!tutorialFinished) yield return null;

            ResumeGameplay();
            if (!FinikGame.HasSelectedGoal)
            {
                var savings = FindFirstObjectByType<FinikSavingsScreen>(FindObjectsInactive.Include);
                if (savings) savings.Open();
            }
            gameObject.SetActive(false);
        }

        void ShowTutorial(FinikProfile profile)
        {
            tutorialProfile = profile;
            tutorialIndex = 0;
            tutorialFinished = false;

            if (!tutorialScreen)
            {
                CompleteTutorial();
                return;
            }

            Go(tutorialScreen);
            RenderTutorial();
        }

        void NextTutorialStep()
        {
            if (tutorialFinished) return;
            if (tutorialIndex >= TutorialTitles.Length - 1)
            {
                CompleteTutorial();
                return;
            }

            tutorialIndex++;
            RenderTutorial();
        }

        void ReplayTutorialVoice()
        {
            if (!Application.isPlaying) return;
            if (tutorialFinished || tutorialIndex < 0 || tutorialIndex >= TutorialVoiceIds.Length) return;
            FinikAudioManager.Instance.PlayAssistant(TutorialVoiceIds[tutorialIndex], interruptCurrent: true);
        }

        void RenderTutorial()
        {
            int count = TutorialTitles.Length;
            tutorialIndex = Mathf.Clamp(tutorialIndex, 0, count - 1);

            if (tutorialStep) tutorialStep.text = $"{tutorialIndex + 1} из {count}";
            if (tutorialTitle) tutorialTitle.text = FinikTypography.Fix(TutorialTitles[tutorialIndex]);
            if (tutorialBody) tutorialBody.text = FinikTypography.Fix(TutorialBodies[tutorialIndex]);
            if (tutorialIcon && tutorialIndex < tutorialIcons.Length && tutorialIcons[tutorialIndex])
                tutorialIcon.sprite = tutorialIcons[tutorialIndex];

            for (int i = 0; i < tutorialDots.Length; i++)
            {
                if (!tutorialDots[i]) continue;
                var color = tutorialDots[i].color;
                color.a = i == tutorialIndex ? 1f : 0.24f;
                tutorialDots[i].color = color;
                tutorialDots[i].rectTransform.localScale = Vector3.one * (i == tutorialIndex ? 1.25f : 0.9f);
            }

            if (tutorialNextLabel)
                tutorialNextLabel.text = tutorialIndex == count - 1 ? "Начать!" : "Дальше";

            ReplayTutorialVoice();
        }

        void CompleteTutorial()
        {
            if (tutorialFinished) return;
            tutorialFinished = true;

            if (tutorialProfile != null)
            {
                tutorialProfile.tutorialSeen = true;
                FinikProfileStore.Save(tutorialProfile);
            }

            Go(null);
        }

        // ------------------------------------------------------------------ finish

        void StartDemo()
        {
            var profile = new FinikProfile
            {
                petId = "fox",
                petName = Pet("fox")?.name ?? "Финик",
                // Outfits come from the shop later, the demo starts undressed too.
                accessories = Array.Empty<string>(),
                demo = true,
                tutorialSeen = true
            };
            FinikGame.StartDemo();
            FinikProfileStore.Save(profile);
            StartCoroutine(Celebrate(profile));
        }

        void Finish()
        {
            if (!FinikProfile.IsValidName(petNameInput.text)) return;
            var profile = new FinikProfile
            {
                petId = petId,
                petName = FinikProfile.NormalizeName(petNameInput.text),
                // No outfit at the start: accessories are bought in the shop later.
                accessories = Array.Empty<string>()
            };
            FinikGame.StartJourney();
            FinikProfileStore.Save(profile);
            StartCoroutine(Celebrate(profile));
        }

        IEnumerator Celebrate(FinikProfile profile)
        {
            finaleTitle.text = $"Ура!\n{profile.petName} ждёт тебя дома";
            Go(finaleScreen);
            if (confetti) confetti.Burst(Vector2.zero);
            if (activity) activity.PlayCelebration();
            FinikAudioManager.Instance.PlayAssistant(FinikAudioManager.VoiceSuccessSuper);
            yield return new WaitForSecondsRealtime(2.2f);
            Go(null);
            bool released = false;
            if (showcase) showcase.Release(() => released = true);
            else released = true;
            while (!released) yield return null;
            ShowHome(profile);

            // The first-run tutorial sits over the real room, after the pet has been created and
            // before the child chooses a savings goal. Movement stays paused so the overlay is the
            // only interactive layer and every step can be heard/read without accidental taps.
            if (!profile.demo && !profile.tutorialSeen)
            {
                ShowTutorial(profile);
                while (!tutorialFinished) yield return null;
            }

            while (activity && activity.IsBusy) yield return null;
            ResumeGameplay();

            // A new player must pick a savings goal before the free 30 coins can be moved anywhere.
            // Opening the savings screen without a selected goal shows only the goal picker.
            if (!profile.demo && !FinikGame.HasSelectedGoal)
            {
                var savings = FindFirstObjectByType<FinikSavingsScreen>(FindObjectsInactive.Include);
                if (savings) savings.Open();
            }

            gameObject.SetActive(false);
        }

        void ApplyProfileToWorld(FinikProfile profile)
        {
            ShowHome(profile);
            ResumeGameplay();
        }

        void ResumePlayerControl()
        {
            foreach (var behaviour in pauseDuringOnboarding)
                if (behaviour is FinikInputController)
                    behaviour.enabled = true;
        }

        void ResumeGameplay()
        {
            foreach (var behaviour in pauseDuringOnboarding) if (behaviour) behaviour.enabled = true;
            if (movement) movement.EndActivity(this);
        }

        void ShowHome(FinikProfile profile)
        {
            if (!ShowCharacter(profile.petId)) ShowCharacter("fox");

            if (rig)
            {
                rig.SetAccessoriesEnabled(true);

                if (rig.Catalog)
                {
                    foreach (var id in profile.accessories ?? Array.Empty<string>())
                    {
                        try { rig.Equip(id); }
                        catch (Exception e) { Debug.LogWarning($"[FinikOnboarding] Saved accessory '{id}' skipped: {e.Message}"); }
                    }
                }
            }

            if (hudRoot) hudRoot.SetActive(true);
            if (hudView) hudView.SetAvatar(Pet(profile.petId)?.avatar);
        }

        // ------------------------------------------------------------------ helpers

        PetOption Pet(string id)
        {
            foreach (var pet in pets) if (pet.id == id) return pet;
            return null;
        }

        public Sprite AvatarFor(string id) => Pet(id)?.avatar;

        void SetEnabled(Button button, CanvasGroup group, bool enabled)
        {
            button.interactable = enabled;
            if (group) group.alpha = enabled ? 1f : disabledTint.a;
        }

        static string ChipLabel(FinikChoiceItem chip)
        {
            var text = chip.GetComponentInChildren<TMP_Text>(true);
            return text ? text.text : chip.Id;
        }

        static void SetChipLabel(FinikChoiceItem chip, string value)
        {
            var text = chip.GetComponentInChildren<TMP_Text>(true);
            if (text) text.text = value;
        }

        [ContextMenu("Restart onboarding (clears saved profile)")]
        void RestartOnboarding()
        {
            FinikProfileStore.Clear();
            if (!Application.isPlaying) return;
            gameObject.SetActive(true);
            Begin();
        }
    }
}
