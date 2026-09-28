using System;
using System.Collections;
using System.Collections.Generic;
using Finik.Core;
using UnityEngine;

namespace Finik.Navigation
{
    [RequireComponent(typeof(FinikMovementController))]
    public sealed class FinikActivityController : MonoBehaviour
    {
        [Serializable]
        sealed class PetStatePayload
        {
            public float care = 55f;
            public float energy = 55f;
            public float mood = 55f;
            public string updatedAt;
            public string lastAction;
        }

        [Serializable]
        sealed class ReactionPayload
        {
            public string eventId;
            public string type;
            public string intensity;
        }

        [SerializeField] Animator animator;
        [SerializeField] Vector2 idleIntervalSeconds = new Vector2(9f, 22f);
        [SerializeField] float lowNeedThreshold = 42f;
        [SerializeField] float criticalNeedThreshold = 26f;
        [SerializeField] float needReminderIntervalSeconds = 75f;
        [SerializeField] float tapReactionCooldown = 1.5f;
        [SerializeField] int randomSeed = 4831;

        static readonly string[] AllTriggers =
        {
            "Idle12", "Idle3", "Idle4", "Idle9", "LookAround", "TurnLeft", "TurnRight",
            "HappySway", "NeedFood", "NeedPlay", "NeedCare", "Hello", "Celebrate",
            "CelebrateBig", "PlayJump", "Playful", "Rested"
        };

        static readonly string[] NeutralIdleTriggers =
        {
            "Idle12", "Idle3", "Idle4", "Idle9", "LookAround", "TurnLeft", "TurnRight"
        };

        readonly struct NeedPhrase
        {
            public readonly string Text;
            public readonly string VoiceId;

            public NeedPhrase(string text, string voiceId)
            {
                Text = text;
                VoiceId = voiceId;
            }
        }

        static readonly NeedPhrase[] FoodLowPhrases =
        {
            new("Кажется, пора перекусить.", "need_food_low_01"),
            new("Я проголодался.", "need_food_low_02"),
            new("Может, заглянем к холодильнику?", "need_food_low_03"),
            new("Я бы сейчас что-нибудь съел.", "need_food_low_04")
        };

        static readonly NeedPhrase[] FoodCriticalPhrases =
        {
            new("У меня животик урчит!", "need_food_critical_01"),
            new("Я очень хочу есть!", "need_food_critical_02"),
            new("Ням-ням бы сейчас…", "need_food_critical_03")
        };

        static readonly NeedPhrase[] MoodLowPhrases =
        {
            new("Мне немного скучно…", "need_play_low_01"),
            new("Хочется чего-нибудь интересного.", "need_play_low_02"),
            new("Чем займёмся?", "need_play_low_03"),
            new("Может, пора себя порадовать?", "need_play_low_04"),
            new("Давай что-нибудь придумаем!", "need_play_low_05")
        };

        static readonly NeedPhrase[] MoodCriticalPhrases =
        {
            new("Что-то я совсем загрустил…", "need_play_critical_01"),
            new("Мне хочется внимания.", "need_play_critical_02"),
            new("Давай найдём, чем себя порадовать.", "need_play_critical_03"),
            new("Побудь со мной немного.", "need_play_critical_04")
        };

        FinikMovementController movement;
        System.Random random;
        PetStatePayload petState = new PetStatePayload();
        readonly Queue<ReactionPayload> reactionQueue = new Queue<ReactionPayload>();
        readonly HashSet<string> seenReactionIds = new HashSet<string>();
        readonly Queue<string> seenReactionOrder = new Queue<string>();
        float nextIdleAt;
        float nextNeedReminderAt;
        float tapReactionAllowedAt;
        bool activityPlaying;
        Coroutine activeRoutine;
        void Awake()
        {
            movement = GetComponent<FinikMovementController>();
            if (!animator) animator = GetComponentInChildren<Animator>();
            random = new System.Random(randomSeed);
            ScheduleIdle();
            nextNeedReminderAt = Time.time + needReminderIntervalSeconds;
        }

        public void SetAnimator(Animator nextAnimator)
        {
            if (animator)
                foreach (string trigger in AllTriggers) animator.ResetTrigger(trigger);
            animator = nextAnimator;
            if (animator)
                foreach (string trigger in AllTriggers) animator.ResetTrigger(trigger);
        }

        void Update()
        {
            if (activityPlaying || movement == null) return;

            if (reactionQueue.Count > 0 && movement.CanStartAutonomous)
            {
                var reaction = reactionQueue.Dequeue();
                StartActivity(PlayReactionRoutine(reaction));
                return;
            }

        }

        public bool TryPlayNeedReminder()
        {
            if (activityPlaying || movement == null || !movement.CanStartAutonomous || Time.time < nextNeedReminderAt)
                return false;
            if (!TryGetNeedReminder(out string trigger, out NeedPhrase phrase)) return false;
            nextNeedReminderAt = Time.time + needReminderIntervalSeconds;
            if (!string.IsNullOrWhiteSpace(phrase.Text))
            {
                FinikPetPopup.ShowMessage(phrase.Text);
                FinikAudioManager.Instance.PlayAssistant(phrase.VoiceId);
            }
            StartActivity(PlayTriggerRoutine(trigger, 2.5f));
            return true;
        }

        public bool TryPlayAmbientIdle()
        {
            if (activityPlaying || movement == null || !movement.CanStartAutonomous || !animator) return false;
            StartActivity(PlayTriggerRoutine(ChooseIdleTrigger(), 3f));
            return true;
        }
        public void SetPetState(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            try
            {
                var next = JsonUtility.FromJson<PetStatePayload>(json);
                if (next == null) return;
                next.care = Mathf.Clamp(next.care, 0f, 100f);
                next.energy = Mathf.Clamp(next.energy, 0f, 100f);
                next.mood = Mathf.Clamp(next.mood, 0f, 100f);
                petState = next;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("FINIK_PET_STATE_INVALID: " + exception.Message);
            }
        }

        /// <summary>Needs from the Unity game state (0..100). Low values trigger the "I need …" gestures.</summary>
        public void SetNeeds(FinikNeeds needs)
        {
            // Payload keeps the web app's names: energy = satiety (HUD food). Unity has no care need,
            // so it stays full and never asks for the "NeedCare" gesture.
            petState.energy = Mathf.Clamp(needs.food, 0f, 100f);
            petState.mood = Mathf.Clamp(needs.mood, 0f, 100f);
            petState.care = 100f;
        }

        /// <summary>
        /// Plays a reaction ("fed", "played-paid", …) right away, even while gameplay is paused by a
        /// full-screen flow. Returns false when another clip is already playing.
        /// </summary>
        public bool PlayReactionNow(string type, string intensity = null)
        {
            if (activityPlaying || !animator || string.IsNullOrWhiteSpace(type)) return false;
            StartActivity(PlayReactionRoutine(new ReactionPayload { type = type, intensity = intensity }));
            return true;
        }

        public void PlayReaction(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            ReactionPayload payload;
            try { payload = JsonUtility.FromJson<ReactionPayload>(json); }
            catch (Exception exception)
            {
                Debug.LogWarning("FINIK_REACTION_INVALID: " + exception.Message);
                return;
            }

            if (payload == null || string.IsNullOrWhiteSpace(payload.type)) return;
            if (!string.IsNullOrWhiteSpace(payload.eventId) && !RememberReaction(payload.eventId)) return;
            reactionQueue.Enqueue(payload);
        }
        /// <summary>True while a clip-driven activity (reaction, idle variation) holds Finik in place.</summary>
        public bool IsBusy => activityPlaying;

        /// <summary>Big joy moment (e.g. finishing onboarding). Plays even while onboarding pauses this component.</summary>
        public void PlayCelebration()
        {
            if (activityPlaying || !animator) return;
            StartActivity(PlayTriggerRoutine("Celebrate", 3f));
        }

        public void PlayQuestDance()
        {
            if (activityPlaying || !animator) return;
            StartActivity(PlayTriggerRoutine("Playful", 3f));
        }

        public bool TryPlayTapReaction(bool playful = false)
        {
            if (activityPlaying || Time.time < tapReactionAllowedAt || !animator) return false;
            tapReactionAllowedAt = Time.time + tapReactionCooldown;

            // A tap is a direct interaction with the child: face the camera before the reaction
            // starts, regardless of the direction Finik was walking or idling in.
            movement?.FaceCamera();
            StartActivity(PlayTriggerRoutine(playful ? "Playful" : "Hello", 2.5f));
            return true;
        }

        /// <summary>Stops a nonessential gesture so a confirmed player click takes effect immediately.</summary>
        public void InterruptForUserMovement()
        {
            if (!activityPlaying) return;
            if (activeRoutine != null) StopCoroutine(activeRoutine);
            activeRoutine = null;
            activityPlaying = false;
            ResetTriggers();

            if (animator)
                animator.CrossFadeInFixedTime("Walk_User", 0.08f, 0, 0f);

            movement.EndActivity();
            ScheduleIdle();
        }

        /// <summary>Releases an optional gesture before a UI action. Menus must never wait for Finik.</summary>
        public void InterruptForUserInput()
        {
            if (!activityPlaying) return;
            if (activeRoutine != null) StopCoroutine(activeRoutine);
            activeRoutine = null;
            activityPlaying = false;
            ResetTriggers();

            if (animator)
                animator.CrossFadeInFixedTime("Idle_Base", 0.08f, 0, 0f);

            movement.EndActivity();
            ScheduleIdle();
        }

        void StartActivity(IEnumerator routine)
        {
            activeRoutine = StartCoroutine(routine);
        }

        IEnumerator PlayReactionRoutine(ReactionPayload payload)
        {
            string trigger = payload.type switch
            {
                "task-completed" => string.Equals(payload.intensity, "big", StringComparison.OrdinalIgnoreCase)
                    ? "CelebrateBig" : "Celebrate",
                "fed" => "HappySway",
                "played-paid" => "PlayJump",
                "played-free" => "Playful",
                "cared" => "HappySway",
                // Piggy bank: a happy sway per deposit, the big celebration when the goal is reached.
                "saved" => "HappySway",
                "goal-reached" => "CelebrateBig",
                "goal-purchased" => "CelebrateBig",
                "rested" => "Rested",
                _ => null
            };

            if (string.IsNullOrEmpty(trigger)) yield break;
            yield return PlayTriggerRoutine(trigger, 3f);
        }

        IEnumerator PlayTriggerRoutine(string trigger, float fallbackDuration)
        {
            if (!animator || activityPlaying) yield break;
            activityPlaying = true;
            movement.BeginActivity();
            ResetTriggers();
            animator.SetTrigger(trigger);

            float duration = Mathf.Max(.35f, FindClipLength(trigger, fallbackDuration));
            yield return new WaitForSeconds(duration + .12f);

            movement.EndActivity();
            activityPlaying = false;
            activeRoutine = null;
            ScheduleIdle();
        }
        void ResetTriggers()
        {
            foreach (string trigger in AllTriggers) animator.ResetTrigger(trigger);
        }

        float FindClipLength(string trigger, float fallback)
        {
            if (!animator || animator.runtimeAnimatorController == null) return fallback;
            string[] tokens = ClipTokensForTrigger(trigger);
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                if (!clip) continue;
                foreach (string token in tokens)
                    if (!string.IsNullOrEmpty(token) && clip.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                        return clip.length;
            }
            return fallback;
        }

        string ChooseIdleTrigger()
        {
            if (petState.mood >= 72f && random.NextDouble() < .28) return "HappySway";
            return NeutralIdleTriggers[random.Next(NeutralIdleTriggers.Length)];
        }

        bool TryGetNeedReminder(out string trigger, out NeedPhrase phrase)
        {
            trigger = null;
            phrase = default;
            float minimum = Mathf.Min(petState.energy, Mathf.Min(petState.mood, petState.care));
            if (minimum > lowNeedThreshold) return false;

            if (petState.energy <= petState.mood && petState.energy <= petState.care)
            {
                trigger = "NeedFood";
                phrase = ChoosePhrase(petState.energy <= criticalNeedThreshold ? FoodCriticalPhrases : FoodLowPhrases);
            }
            else if (petState.mood <= petState.care)
            {
                trigger = "NeedPlay";
                phrase = ChoosePhrase(petState.mood <= criticalNeedThreshold ? MoodCriticalPhrases : MoodLowPhrases);
            }
            else
            {
                trigger = "NeedCare";
            }

            if (minimum <= criticalNeedThreshold) nextIdleAt = Time.time + 2f;
            return true;
        }

        NeedPhrase ChoosePhrase(NeedPhrase[] phrases)
        {
            if (phrases == null || phrases.Length == 0) return default;
            return phrases[random?.Next(phrases.Length) ?? 0];
        }

        bool RememberReaction(string eventId)
        {
            if (!seenReactionIds.Add(eventId)) return false;
            seenReactionOrder.Enqueue(eventId);
            while (seenReactionOrder.Count > 48)
                seenReactionIds.Remove(seenReactionOrder.Dequeue());
            return true;
        }

        void ScheduleIdle()
        {
            double t = random == null ? .5 : random.NextDouble();
            nextIdleAt = Time.time + Mathf.Lerp(idleIntervalSeconds.x, idleIntervalSeconds.y, (float)t);
        }

        static string[] ClipTokensForTrigger(string trigger) => trigger switch
        {
            "Idle12" => new[] { "Idle_12" },
            "Idle3" => new[] { "Idle_3" },
            "Idle4" => new[] { "Shrug", "Idle_4" },
            "Idle9" => new[] { "Confused_Scratch", "Idle_9" },
            "LookAround" => new[] { "Long_Breathe_and_Look_Around" },
            "TurnLeft" => new[] { "Idle_Turn_Left" },
            "TurnRight" => new[] { "Idle_Turn_Right" },
            "HappySway" => new[] { "Happy_Sway_Standing" },
            "NeedFood" => new[] { "Hunger", "01a0b93c-fae1-7755-b839-64c69a52185e", "Wave_for_Help_3" },
            "NeedPlay" => new[] { "Indoor_Play", "Wave_for_Help_1" },
            "NeedCare" => new[] { "Catching_Breath", "Frustrated_Turn_Right" },
            "Hello" => new[] { "Big_Wave_Hello" },
            "Celebrate" => new[] { "Victory_Cheer", "Victory_Fist_Pump", "Cheer_with_Both_Hands_Up" },
            "CelebrateBig" => new[] { "Backflip", "Backflip_Jump" },
            "PlayJump" => new[] { "Happy_jump_f" },
            "Playful" => new[] { "FunnyDancing_01", "Tightrope_Walk_inplace" },
            "Rested" => new[] { "Long_Breathe_and_Look_Around", "Happy_Sway_Standing" },
            _ => new[] { trigger }
        };
    }
}
