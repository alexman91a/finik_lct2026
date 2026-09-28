using System;
using System.Collections;
using System.Collections.Generic;
using Finik.Core;
using Finik.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.Navigation
{
    public sealed class FinikAudioManager : MonoBehaviour
    {
        [Serializable]
        sealed class AudioSettingsPayload
        {
            public bool soundsEnabled = true;
            public float masterVolume = 0.8f;
            public float musicVolume = 0.5f;
            public float effectsVolume = 0.85f;
            public bool externalMusic;
        }

        static FinikAudioManager instance;

        readonly List<AudioClip> footsteps = new();
        readonly Dictionary<string, AudioClip> interactions = new();
        readonly Dictionary<string, AudioClip> assistantClips = new();
        readonly HashSet<Button> boundButtons = new();

        AudioSource sfxSource;
        AudioSource footstepSource;
        AudioSource musicSource;
        AudioSource assistantSource;
        Coroutine assistantDuckRoutine;
        Coroutine pendingTaskVoiceRoutine;

        AudioClip uiTapClip;
        AudioClip backgroundMusic;

        float masterVolume = 0.8f;
        float musicVolume = 0.5f;
        float effectsVolume = 0.85f;
        bool soundsEnabled = true;
        bool externalMusic;

        float stepTimer;
        int lastStep = -1;
        string lastAssistantId;
        float lastAssistantAt = -100f;
        float assistantMusicDuck = 1f;

        public const string VoiceGreetingHello = "greeting_hello";
        public const string VoiceGreetingReturn = "greeting_return";
        public const string VoiceRetryAgain = "retry_again";
        public const string VoiceSystemDone = "system_done";
        public const string VoiceSuccessSuper = "success_super";
        public const string VoiceTutorialWelcome = "tutorial_01_welcome";
        public const string VoiceTutorialQuests = "tutorial_02_quests";
        public const string VoiceTutorialCare = "tutorial_03_care";
        public const string VoiceTutorialGoal = "tutorial_04_goal";
        public const string VoiceQuestReminder = "quest_reminder";
        public const string VoiceGoalPicker = "goal_picker";
        public const string VoiceGoalReachedBuy = "goal_reached_buy";
        public const string VoiceGoalBought = "goal_bought";
        public const string VoiceQuestBoard = "quest_board";
        public const string VoiceShopNeed = "shop_need";
        public const string VoiceShopWant = "shop_want";
        public const string VoiceShopFridge = "shop_fridge";
        public const string VoicePurchaseFood = "purchase_food";
        public const string VoicePurchaseNeed = "purchase_need";
        public const string VoicePurchaseWant = "purchase_want";

        public static FinikAudioManager Instance => EnsureInstance();

        /// <summary>True while an assistant line is being spoken.</summary>
        public bool IsAssistantSpeaking => assistantSource && assistantSource.isPlaying;

        /// <summary>Taps, steps and room effects.</summary>
        bool EffectsOn => soundsEnabled && FinikAudioSettings.Sounds;

        /// <summary>Spoken assistant lines can be muted independently from effects.</summary>
        bool VoiceOn => soundsEnabled && FinikAudioSettings.Voice;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap() => EnsureInstance();

        static FinikAudioManager EnsureInstance()
        {
            if (instance) return instance;

            instance = FindFirstObjectByType<FinikAudioManager>();
            if (instance) return instance;

            var go = new GameObject("Finik_Audio");
            instance = go.AddComponent<FinikAudioManager>();
            DontDestroyOnLoad(go);
            return instance;
        }

        void Awake()
        {
            if (instance && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            sfxSource = CreateSource("SFX");
            footstepSource = CreateSource("Footsteps");
            musicSource = CreateSource("Music");
            assistantSource = CreateSource("VoiceAssistant");
            musicSource.loop = true;
            musicSource.priority = 128;
            assistantSource.priority = 32;

            for (int i = 0; i < 6; i++)
            {
                var clip = Resources.Load<AudioClip>($"Audio/footstep_soft_{i:00}");
                if (clip) footsteps.Add(clip);
            }

            // Safe fallback while developing if the new soft set is ever missing.
            if (footsteps.Count == 0)
            {
                for (int i = 0; i < 6; i++)
                {
                    var clip = Resources.Load<AudioClip>($"Audio/footstep_0{i}");
                    if (clip) footsteps.Add(clip);
                }
            }

            LoadInteraction("fridge", "Audio/fridge_open");
            LoadInteraction("play-zone", "Audio/pet_reaction");
            LoadInteraction("care-bag", "Audio/bag_cloth");
            LoadInteraction("study-desk", "Audio/book_open");
            LoadInteraction("bed", "Audio/bed_soft");
            LoadInteraction("decor-shelf", "Audio/decor_shelf");
            LoadInteraction("decor-window", "Audio/decor_window");
            LoadInteraction("pet", "Audio/pet_reaction");

            uiTapClip = Resources.Load<AudioClip>("Audio/ui_tap_soft");
            backgroundMusic = Resources.Load<AudioClip>("Audio/music_home_loop");

            LoadAssistant(VoiceGreetingHello);
            LoadAssistant(VoiceGreetingReturn);
            LoadAssistant(VoiceRetryAgain);
            LoadAssistant(VoiceSystemDone);
            LoadAssistant(VoiceSuccessSuper);
            LoadAssistant(VoiceTutorialWelcome);
            LoadAssistant(VoiceTutorialQuests);
            LoadAssistant(VoiceTutorialCare);
            LoadAssistant(VoiceTutorialGoal);
            LoadAssistant(VoiceQuestReminder);
            LoadAssistant(VoiceGoalPicker);
            LoadAssistant(VoiceGoalReachedBuy);
            LoadAssistant(VoiceGoalBought);
            LoadAssistant(VoiceQuestBoard);
            LoadAssistant(VoiceShopNeed);
            LoadAssistant(VoiceShopWant);
            LoadAssistant(VoiceShopFridge);
            LoadAssistant(VoicePurchaseFood);
            LoadAssistant(VoicePurchaseNeed);
            LoadAssistant(VoicePurchaseWant);

            if (backgroundMusic)
            {
                musicSource.clip = backgroundMusic;
                musicSource.Play();
            }

            ApplyVolumes();
            StartCoroutine(BindUiButtonsLoop());

            Debug.Log(
                $"FINIK_AUDIO_READY footsteps={footsteps.Count}; interactions={interactions.Count}; " +
                $"assistant={assistantClips.Count}; uiTap={(uiTapClip ? "yes" : "no")}; music={(backgroundMusic ? "yes" : "no")}"
            );
        }

        void OnEnable()
        {
            if (instance != this) return;
            FinikGame.ReactionRequested += OnGameReaction;
            FinikAudioSettings.Changed += OnSettingsChanged;
        }

        void OnDisable()
        {
            if (instance != this) return;
            FinikGame.ReactionRequested -= OnGameReaction;
            FinikAudioSettings.Changed -= OnSettingsChanged;
        }

        void OnSettingsChanged()
        {
            // Audio stops immediately when its own switch is turned off.
            if (!VoiceOn && assistantSource && assistantSource.isPlaying) assistantSource.Stop();
            if (!EffectsOn && footstepSource && footstepSource.isPlaying) footstepSource.Stop();
            ApplyVolumes();
        }

        AudioSource CreateSource(string sourceName)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.priority = 64;
            return source;
        }

        void LoadInteraction(string id, string resourcePath)
        {
            var clip = Resources.Load<AudioClip>(resourcePath);
            if (clip) interactions[id] = clip;
            else Debug.LogWarning($"FINIK_AUDIO_MISSING {resourcePath}");
        }

        void LoadAssistant(string id)
        {
            string resourcePath = $"Audio/voice/common/{id}";
            var clip = Resources.Load<AudioClip>(resourcePath);
            if (clip) assistantClips[id] = clip;
            else Debug.LogWarning($"FINIK_AUDIO_MISSING {resourcePath}");
        }

        void ApplyVolumes()
        {
            float effectsGain = EffectsOn ? masterVolume * effectsVolume : 0f;
            float voiceGain = VoiceOn ? masterVolume * effectsVolume : 0f;
            float musicGain = soundsEnabled && FinikAudioSettings.Music && !externalMusic ? masterVolume * musicVolume : 0f;

            if (sfxSource) sfxSource.volume = effectsGain * 0.9f;
            if (footstepSource) footstepSource.volume = effectsGain * 0.34f;
            if (assistantSource) assistantSource.volume = voiceGain;
            if (musicSource) musicSource.volume = musicGain * 0.62f * assistantMusicDuck;

            if (musicSource && backgroundMusic && !musicSource.isPlaying)
                musicSource.Play();
        }

        public void SetAudioSettings(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            try
            {
                var settings = new AudioSettingsPayload();
                JsonUtility.FromJsonOverwrite(json, settings);

                soundsEnabled = settings.soundsEnabled;
                masterVolume = Mathf.Clamp01(settings.masterVolume);
                musicVolume = Mathf.Clamp01(settings.musicVolume);
                effectsVolume = Mathf.Clamp01(settings.effectsVolume);
                externalMusic = settings.externalMusic;

                ApplyVolumes();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("FINIK_AUDIO_SETTINGS_INVALID: " + exception.Message);
            }
        }

        public void PlayInteraction(string interactionId)
        {
            if (!interactions.TryGetValue(interactionId, out var clip) || !clip || !EffectsOn)
                return;

            sfxSource.pitch = UnityEngine.Random.Range(0.97f, 1.03f);
            sfxSource.PlayOneShot(clip);
        }

        public void PlayUiTap()
        {
            if (!EffectsOn || !uiTapClip || !sfxSource) return;

            sfxSource.pitch = UnityEngine.Random.Range(0.99f, 1.035f);
            sfxSource.PlayOneShot(uiTapClip, 0.72f);
        }

        /// <summary>
        /// Plays one short reusable assistant line. Voice never stacks: ordinary calls are ignored while
        /// another line is speaking. Important achievements may interrupt the current line explicitly.
        /// </summary>
        public bool PlayAssistant(string clipId, bool interruptCurrent = false)
        {
            if (!VoiceOn || !assistantSource || string.IsNullOrWhiteSpace(clipId)) return false;
            if (!assistantClips.ContainsKey(clipId)) LoadAssistant(clipId);
            if (!assistantClips.TryGetValue(clipId, out var clip) || !clip) return false;

            float now = Time.unscaledTime;
            if (!interruptCurrent && assistantSource.isPlaying) return false;
            if (!interruptCurrent && clipId == lastAssistantId && now - lastAssistantAt < 4f) return false;

            if (assistantDuckRoutine != null) StopCoroutine(assistantDuckRoutine);
            if (assistantSource.isPlaying) assistantSource.Stop();

            assistantSource.pitch = 1f;
            assistantSource.clip = clip;
            assistantSource.Play();
            lastAssistantId = clipId;
            lastAssistantAt = now;
            assistantDuckRoutine = StartCoroutine(DuckMusicForAssistant());
            return true;
        }

        /// <summary>Plays a quest-specific line, loaded only when that quest is opened.</summary>
        public bool PlayQuestVoice(string clipId)
        {
            if (string.IsNullOrWhiteSpace(clipId)) return false;
            if (!assistantClips.ContainsKey(clipId))
            {
                string day = clipId.StartsWith("d5-", StringComparison.Ordinal) ? "day5"
                    : clipId.StartsWith("d4-", StringComparison.Ordinal) ? "day4"
                    : clipId.StartsWith("d3-", StringComparison.Ordinal) ? "day3"
                    : clipId.StartsWith("d2-", StringComparison.Ordinal) ? "day2" : "day1";
                var clip = Resources.Load<AudioClip>($"Audio/voice/quests/{day}/{clipId}");
                if (clip) assistantClips[clipId] = clip;
                else return false;
            }
            return PlayAssistant(clipId, interruptCurrent: true);
        }

        public void StopAssistant()
        {
            if (pendingTaskVoiceRoutine != null) StopCoroutine(pendingTaskVoiceRoutine);
            pendingTaskVoiceRoutine = null;
            if (assistantDuckRoutine != null) StopCoroutine(assistantDuckRoutine);
            assistantDuckRoutine = null;
            if (assistantSource) assistantSource.Stop();
            assistantMusicDuck = 1f;
            ApplyVolumes();
        }

        void OnGameReaction(string type, string intensity)
        {
            switch (type)
            {
                case "task-completed" when string.Equals(intensity, "big", StringComparison.OrdinalIgnoreCase):
                    if (pendingTaskVoiceRoutine != null)
                    {
                        StopCoroutine(pendingTaskVoiceRoutine);
                        pendingTaskVoiceRoutine = null;
                    }
                    PlayAssistant(VoiceSuccessSuper, interruptCurrent: true);
                    break;
                case "task-completed":
                    if (pendingTaskVoiceRoutine != null) StopCoroutine(pendingTaskVoiceRoutine);
                    pendingTaskVoiceRoutine = StartCoroutine(PlayTaskDoneDeferred());
                    break;
                case "goal-reached":
                    PlayAssistant(VoiceSuccessSuper, interruptCurrent: true);
                    break;
            }
        }

        IEnumerator PlayTaskDoneDeferred()
        {
            // A weekly unlock raises a second, "big" event in the same frame. Waiting one frame lets
            // that event replace the neutral line cleanly instead of producing a clipped first syllable.
            yield return null;
            pendingTaskVoiceRoutine = null;
            PlayAssistant(VoiceSystemDone);
        }

        IEnumerator DuckMusicForAssistant()
        {
            while (assistantSource && assistantSource.isPlaying)
            {
                assistantMusicDuck = Mathf.MoveTowards(assistantMusicDuck, 0.45f, Time.unscaledDeltaTime / 0.12f);
                ApplyVolumes();
                yield return null;
            }

            while (assistantMusicDuck < 0.999f)
            {
                assistantMusicDuck = Mathf.MoveTowards(assistantMusicDuck, 1f, Time.unscaledDeltaTime / 0.28f);
                ApplyVolumes();
                yield return null;
            }

            assistantMusicDuck = 1f;
            ApplyVolumes();
            assistantDuckRoutine = null;
        }

        public void TickFootsteps(float speed, float deltaTime)
        {
            if (!EffectsOn || speed < 0.08f || footsteps.Count == 0)
            {
                stepTimer = Mathf.Min(stepTimer, 0.08f);
                return;
            }

            stepTimer -= deltaTime;
            if (stepTimer > 0f) return;

            int index = UnityEngine.Random.Range(0, footsteps.Count);
            if (footsteps.Count > 1 && index == lastStep)
                index = (index + 1) % footsteps.Count;

            lastStep = index;

            float normalized = Mathf.InverseLerp(0.08f, 2.42f, speed);
            footstepSource.pitch = UnityEngine.Random.Range(0.98f, 1.075f);
            footstepSource.PlayOneShot(footsteps[index], Mathf.Lerp(0.64f, 0.76f, normalized));

            stepTimer = Mathf.Lerp(0.45f, 0.235f, normalized);
        }

        IEnumerator BindUiButtonsLoop()
        {
            while (true)
            {
                BindUiButtons();
                yield return new WaitForSecondsRealtime(0.75f);
            }
        }

        void BindUiButtons()
        {
            boundButtons.RemoveWhere(button => !button);

            var buttons = FindObjectsByType<Button>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

            foreach (var button in buttons)
            {
                if (!button || !boundButtons.Add(button)) continue;
                button.onClick.AddListener(PlayUiTap);
                if (!button.GetComponent<FinikUiActivityInterrupt>())
                    button.gameObject.AddComponent<FinikUiActivityInterrupt>();
            }
        }
    }
}
