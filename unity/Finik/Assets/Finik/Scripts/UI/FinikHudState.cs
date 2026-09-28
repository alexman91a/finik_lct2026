using System;
using UnityEngine;

namespace Finik.UI
{
    public enum FinikHudTab
    {
        /// <summary>
        /// The room itself. It has no button in the dock — the room is what the dock sits on — so
        /// selecting it is how the HUD says "no tab is lit", which is the state over the room.
        /// </summary>
        Home,
        Budget,
        Quests,
        Shopping,
        Savings
    }

    /// <summary>Snapshot of everything the home HUD displays.</summary>
    [Serializable]
    public struct FinikHudState
    {
        public string playerName;
        public int level;
        public int xp;
        public int xpToNextLevel;
        public int coins;
        public int savings;
        [Range(0f, 1f)] public float food;
        [Range(0f, 1f)] public float mood;
        public int unreadMail;

        public float XpProgress => xpToNextLevel > 0 ? Mathf.Clamp01((float)xp / xpToNextLevel) : 0f;
    }
}
