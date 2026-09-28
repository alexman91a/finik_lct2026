using System;
using Finik.Core;
using Finik.Navigation;
using UnityEngine;

namespace Finik.UI.Quests
{
    /// <summary>
    /// Reminds the child once after leaving an unfinished quest and staying away from Tasks.
    /// The reminder waits for the room to be idle, so it never talks over another screen or activity.
    /// </summary>
    public sealed class FinikQuestReminderController : MonoBehaviour
    {
        const string TaskKey = "finik.quest.reminder.task";
        const string TitleKey = "finik.quest.reminder.title";
        const string ArmedAtKey = "finik.quest.reminder.armedAt";
        const string NotifiedKey = "finik.quest.reminder.notified";

        [SerializeField, Min(10f)] float reminderDelaySeconds = 90f;
        [SerializeField, Min(.2f)] float pollSeconds = .75f;

        FinikActivityController activity;
        FinikMovementController movement;
        float nextPoll;

        public static void Ensure(FinikActivityController activity, FinikMovementController movement)
        {
            var host = activity ? activity.gameObject : movement ? movement.gameObject : null;
            if (!host) return;
            var controller = host.GetComponent<FinikQuestReminderController>();
            if (!controller) controller = host.AddComponent<FinikQuestReminderController>();
            controller.activity = activity ? activity : host.GetComponent<FinikActivityController>();
            controller.movement = movement ? movement : host.GetComponent<FinikMovementController>();
        }
        public static void Arm(string taskId, string title)
        {
            if (string.IsNullOrWhiteSpace(taskId)) return;
            PlayerPrefs.SetString(TaskKey, taskId);
            PlayerPrefs.SetString(TitleKey, title ?? string.Empty);
            PlayerPrefs.SetString(ArmedAtKey, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            PlayerPrefs.SetInt(NotifiedKey, 0);
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(TaskKey);
            PlayerPrefs.DeleteKey(TitleKey);
            PlayerPrefs.DeleteKey(ArmedAtKey);
            PlayerPrefs.DeleteKey(NotifiedKey);
            PlayerPrefs.Save();
        }

        void Update()
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + pollSeconds;

            if (!TryPending(out string taskId, out string title, out long armedAt, out bool notified)) return;
            if (notified) return;

            var progress = FinikGame.QuestProgress(taskId);
            if (progress == null || progress.status != FinikQuestStatus.Active)
            {
                Clear();
                return;
            }

            long elapsed = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - armedAt);
            if (elapsed < reminderDelaySeconds) return;
            if (FinikScreens.AnyOpen) return;
            if (!movement || !movement.CanStartAutonomous) return;
            if (activity && activity.IsBusy) return;

            ShowReminder(title);
        }
        void ShowReminder(string title)
        {
            string message = string.IsNullOrWhiteSpace(title)
                ? "У нас осталось незавершённое задание. Давай вернёмся и попробуем ещё раз!"
                : $"Мы не закончили «{title}». Давай вернёмся и попробуем ещё раз!";

            // If the room popup host is temporarily unavailable, wait for the next poll instead
            // of silently consuming the one reminder this abandoned quest gets.
            if (!FinikPetPopup.ShowMessage(FinikTypography.Fix(message))) return;

            PlayerPrefs.SetInt(NotifiedKey, 1);
            PlayerPrefs.Save();
            activity?.TryPlayTapReaction();
            FinikAudioManager.Instance.PlayAssistant(FinikAudioManager.VoiceQuestReminder, interruptCurrent: false);
        }

        static bool TryPending(out string taskId, out string title, out long armedAt, out bool notified)
        {
            taskId = PlayerPrefs.GetString(TaskKey, string.Empty);
            title = PlayerPrefs.GetString(TitleKey, string.Empty);
            notified = PlayerPrefs.GetInt(NotifiedKey, 0) != 0;
            armedAt = 0;
            if (string.IsNullOrWhiteSpace(taskId)) return false;
            if (!long.TryParse(PlayerPrefs.GetString(ArmedAtKey, "0"), out armedAt)) return false;
            return armedAt > 0;
        }

#if UNITY_EDITOR
        public static void ArmForTesting(string taskId, string title, long secondsAgo)
        {
            Arm(taskId, title);
            PlayerPrefs.SetString(ArmedAtKey,
                (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - Math.Max(0, secondsAgo)).ToString());
            PlayerPrefs.Save();
        }
#endif
    }
}
