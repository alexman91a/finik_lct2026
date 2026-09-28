using System;
using System.Collections.Generic;
using Finik.Core;
using Finik.UI.Onboarding;
using TMPro;
using UnityEngine;

namespace Finik.UI.Quests
{
    /// <summary>
    /// «Поймай ред флаги» (web: RedFlagChat): a chat of messages; the child taps every suspicious one
    /// (a flag marks it), then checks. The check is enabled once as many messages are flagged as there
    /// are suspicious ones; a wrong set shows the retry hint and the child keeps trying.
    /// </summary>
    public sealed class FinikRedFlagsView : MonoBehaviour
    {
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text counter;
        [SerializeField] FinikChoiceItem[] messages = Array.Empty<FinikChoiceItem>();
        [SerializeField] TMP_Text[] senders = Array.Empty<TMP_Text>();
        [SerializeField] TMP_Text[] texts = Array.Empty<TMP_Text>();

        readonly HashSet<int> flagged = new();
        FinikQuestMechanic mechanic;

        /// <summary>Raised when a message is flagged or unflagged.</summary>
        public event Action Changed;

        public int FlaggedCount => flagged.Count;
        public int Needed => mechanic?.SuspiciousCount ?? 0;
        public bool ReadyToCheck => mechanic != null && flagged.Count == Needed;

        public void Configure(TMP_Text titleText, TMP_Text counterText, FinikChoiceItem[] items, TMP_Text[] senderTexts, TMP_Text[] messageTexts)
        {
            title = titleText;
            counter = counterText;
            messages = items;
            senders = senderTexts;
            texts = messageTexts;
        }

        void Awake()
        {
            for (int i = 0; i < messages.Length; i++)
            {
                int index = i;
                if (messages[i]) messages[i].Clicked += _ => Toggle(index);
            }
        }

        public void Show(FinikQuestMechanic settings)
        {
            mechanic = settings;
            flagged.Clear();
            if (title) title.text = FinikTypography.Fix(settings.Title);
            for (int i = 0; i < messages.Length; i++)
            {
                bool used = i < settings.Messages.Count;
                if (!messages[i]) continue;
                messages[i].gameObject.SetActive(used);
                messages[i].SetSelected(false);
                if (!used) continue;
                var message = settings.Messages[i];
                if (i < senders.Length && senders[i]) senders[i].text = message.Who;
                if (i < texts.Length && texts[i]) texts[i].text = FinikTypography.Fix(message.Text);
            }
            SetInteractable(true);
            RenderCounter();
        }

        /// <summary>True when exactly the suspicious messages are flagged.</summary>
        public bool IsCorrect()
        {
            if (mechanic == null || flagged.Count != Needed) return false;
            foreach (int index in flagged)
                if (!mechanic.Messages[index].Suspicious) return false;
            return true;
        }

        public void SetInteractable(bool value)
        {
            foreach (var message in messages)
                if (message && message.Button) message.Button.interactable = value;
        }

        /// <summary>Flags the given messages (by id) and nothing else. Used by the screen audit.</summary>
        public void Flag(IEnumerable<string> ids)
        {
            flagged.Clear();
            var wanted = new HashSet<string>(ids);
            for (int i = 0; i < messages.Length && mechanic != null && i < mechanic.Messages.Count; i++)
            {
                bool on = wanted.Contains(mechanic.Messages[i].Id);
                if (on) flagged.Add(i);
                if (messages[i]) messages[i].SetSelected(on);
            }
            RenderCounter();
            Changed?.Invoke();
        }

        void Toggle(int index)
        {
            if (mechanic == null || index >= mechanic.Messages.Count) return;
            bool on = flagged.Add(index);
            if (!on) flagged.Remove(index);
            messages[index].SetSelected(on);
            RenderCounter();
            Changed?.Invoke();
        }

        void RenderCounter()
        {
            if (counter) counter.text = $"{flagged.Count}/{Needed}";
        }
    }
}
