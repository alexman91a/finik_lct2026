using System.Linq;
using Finik.Navigation;
using Finik.UI;
using Finik.UI.Quests;
using NUnit.Framework;
using UnityEngine;

namespace Finik.Editor.Tests
{
    public sealed class FinikProgressAndReminderTests
    {
        const string TaskKey = "finik.quest.reminder.task";
        const string TitleKey = "finik.quest.reminder.title";
        const string ArmedAtKey = "finik.quest.reminder.armedAt";
        const string NotifiedKey = "finik.quest.reminder.notified";

        [TearDown]
        public void Cleanup()
        {
            FinikQuestReminderController.Clear();
        }

        [Test]
        public void GlossaryContainsTheTenRequiredChildFriendlyTerms()
        {
            var terms = FinikGlossary.All.Select(x => x.Term).ToArray();
            Assert.That(FinikGlossary.Count, Is.EqualTo(10));
            Assert.That(terms, Is.EqualTo(new[]
            {
                "Бюджет", "Доход", "Нужно", "Хочу", "Накопления",
                "Цель", "Цена", "Баланс", "План", "Факт"
            }));
            Assert.That(FinikGlossary.All.All(x =>
                !string.IsNullOrWhiteSpace(x.Definition) &&
                !string.IsNullOrWhiteSpace(x.Example)), Is.True);
        }

        [Test]
        public void AbandonedQuestReminderPersistsUntilTasksAreOpenedAgain()
        {
            FinikQuestReminderController.ArmForTesting(
                "d2-smart-purchase", "Умная покупка", secondsAgo: 90);

            Assert.That(PlayerPrefs.GetString(TaskKey), Is.EqualTo("d2-smart-purchase"));
            Assert.That(PlayerPrefs.GetString(TitleKey), Is.EqualTo("Умная покупка"));
            Assert.That(long.TryParse(PlayerPrefs.GetString(ArmedAtKey), out var armedAt), Is.True);
            Assert.That(armedAt, Is.GreaterThan(0));
            Assert.That(PlayerPrefs.GetInt(NotifiedKey, 1), Is.Zero);

            FinikQuestReminderController.Clear();
            Assert.That(PlayerPrefs.HasKey(TaskKey), Is.False);
            Assert.That(PlayerPrefs.HasKey(TitleKey), Is.False);
            Assert.That(PlayerPrefs.HasKey(ArmedAtKey), Is.False);
            Assert.That(PlayerPrefs.HasKey(NotifiedKey), Is.False);
        }

        [Test]
        public void QuestReminderVoiceIsShippedInResources()
        {
            var clip = Resources.Load<AudioClip>(
                "Audio/voice/common/" + FinikAudioManager.VoiceQuestReminder);
            Assert.That(clip, Is.Not.Null);
            Assert.That(clip.length, Is.GreaterThan(2f));
        }
    }
}
