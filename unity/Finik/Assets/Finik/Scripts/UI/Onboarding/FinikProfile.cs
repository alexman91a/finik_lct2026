using System;
using UnityEngine;

namespace Finik.UI.Onboarding
{
    /// <summary>Player profile created by onboarding. Persisted locally as JSON in PlayerPrefs.</summary>
    [Serializable]
    public sealed class FinikProfile
    {
        public const int MinNameLength = 2;
        public const int MaxNameLength = 20;

        public string petId;
        public string petName;
        public string[] accessories = Array.Empty<string>();
        public bool demo;
        public bool tutorialSeen;

        /// <summary>Trims and collapses whitespace, caps the length (same rules as the web app).</summary>
        public static string NormalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var parts = value.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            string joined = string.Join(" ", parts);
            return joined.Length > MaxNameLength ? joined.Substring(0, MaxNameLength).TrimEnd() : joined;
        }

        public static bool IsValidName(string value) => NormalizeName(value).Length >= MinNameLength;
    }

    public static class FinikProfileStore
    {
        const string Key = "finik.profile.v1";

        public static event Action<FinikProfile> Changed;

        public static bool TryLoad(out FinikProfile profile)
        {
            profile = null;
            string json = PlayerPrefs.GetString(Key, string.Empty);
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                profile = JsonUtility.FromJson<FinikProfile>(json);
            }
            catch (ArgumentException e)
            {
                Debug.LogWarning($"[FinikProfile] Stored profile is corrupt and will be ignored: {e.Message}");
                return false;
            }
            // The player is never asked for a name; a profile is a chosen pet with a name. Old saves
            // that still carry a nickname load fine: JsonUtility ignores the extra field.
            return profile != null && !string.IsNullOrEmpty(profile.petId) && FinikProfile.IsValidName(profile.petName);
        }

        public static void Save(FinikProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(profile));
            PlayerPrefs.Save();
            Changed?.Invoke(profile);
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
