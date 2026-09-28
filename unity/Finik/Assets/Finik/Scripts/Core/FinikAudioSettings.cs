using System;
using UnityEngine;

namespace Finik.Core
{
    /// <summary>
    /// Music, effects and voice on or off, set from «Настройки» and kept on this device.
    ///
    /// Three switches, not sliders: a child can read an on/off pill at a glance, and the phone's own
    /// volume keys already cover "quieter". The audio manager listens to <see cref="Changed"/>.
    /// </summary>
    public static class FinikAudioSettings
    {
        const string MusicKey = "finik.audio.music";
        const string SoundsKey = "finik.audio.sounds";
        const string VoiceKey = "finik.audio.voice";

        /// <summary>Raised after any audio switch changes.</summary>
        public static event Action Changed;

        /// <summary>The background music loop.</summary>
        public static bool Music
        {
            get => PlayerPrefs.GetInt(MusicKey, 1) == 1;
            set => Set(MusicKey, Music, value);
        }

        /// <summary>Effects: taps, footsteps and the room's objects.</summary>
        public static bool Sounds
        {
            get => PlayerPrefs.GetInt(SoundsKey, 1) == 1;
            set => Set(SoundsKey, Sounds, value);
        }

        /// <summary>Spoken assistant lines: onboarding, quests and pet reminders.</summary>
        public static bool Voice
        {
            get => PlayerPrefs.GetInt(VoiceKey, 1) == 1;
            set => Set(VoiceKey, Voice, value);
        }

        static void Set(string key, bool current, bool value)
        {
            if (current == value) return;
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
