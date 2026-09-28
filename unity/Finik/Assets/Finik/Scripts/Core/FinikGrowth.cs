using System;
using System.Collections.Generic;

namespace Finik.Core
{
    public static class FinikGrowthSource
    {
        public const string Task = "task";
        public const string Week = "week";
        public const string Care = "care";
    }

    [Serializable]
    public sealed class FinikGrowthEvent
    {
        public string id;
        public string source;
        public int xp;
        public string reason;
        public long createdAtMs;
    }

    /// <summary>Pet XP with an idempotent event log: each event id grants XP at most once.</summary>
    [Serializable]
    public sealed class FinikGrowth
    {
        public int xp;
        public List<FinikGrowthEvent> events = new();

        [NonSerialized] HashSet<string> ids;

        public bool Contains(string eventId)
        {
            if (ids == null)
            {
                ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var e in events) if (e != null && e.id != null) ids.Add(e.id);
            }
            return eventId != null && ids.Contains(eventId);
        }

        internal void Append(FinikGrowthEvent growthEvent)
        {
            events.Add(growthEvent);
            if (ids != null) ids.Add(growthEvent.id);
        }

        internal void InvalidateIndex() => ids = null;
    }

    public readonly struct FinikGrowthProgress
    {
        public readonly int level;
        public readonly int xp;
        public readonly int levelStart;
        public readonly int nextLevelAt;

        public FinikGrowthProgress(int level, int xp, int levelStart, int nextLevelAt)
        {
            this.level = level;
            this.xp = xp;
            this.levelStart = levelStart;
            this.nextLevelAt = nextLevelAt;
        }

        public int XpInLevel => Math.Max(0, xp - levelStart);
        public int LevelSpan => Math.Max(1, nextLevelAt - levelStart);
        public int XpToNext => Math.Max(0, nextLevelAt - xp);
    }

    /// <summary>Port of src/domain/petGrowthEngine.ts and the grantXp part of petGrowthStore.ts.</summary>
    public static class FinikGrowthEngine
    {
        public const int MaxLevel = 99;

        /// <summary>Three successful daily quests grant 36 XP: St2 on day 3, St3 on day 7.</summary>
        public static int ThresholdForLevel(int level)
        {
            int safe = Math.Max(1, level);
            return safe <= 4 ? (safe - 1) * 36 : 108 + (safe - 4) * 48;
        }

        public static int LevelFromXp(int xp)
        {
            int safe = Math.Max(0, xp);
            int level = 1;
            while (level < MaxLevel && ThresholdForLevel(level + 1) <= safe) level++;
            return level;
        }

        public static FinikGrowthProgress Progress(int xp)
        {
            int safe = Math.Max(0, xp);
            int level = LevelFromXp(safe);
            return new FinikGrowthProgress(level, safe, ThresholdForLevel(level), ThresholdForLevel(level + 1));
        }

        public static bool Grant(FinikGrowth growth, string id, string source, int xp, string reason, long nowMs)
        {
            if (string.IsNullOrEmpty(id) || xp <= 0 || growth.Contains(id)) return false;
            growth.xp += xp;
            growth.Append(new FinikGrowthEvent { id = id, source = source, xp = xp, reason = reason, createdAtMs = nowMs });
            return true;
        }

        public static int TaskXp(int difficulty, bool weekly) => weekly ? 30 : 8 + Math.Max(1, Math.Min(3, difficulty)) * 4;

        public static int BalancedCareXp(FinikNeeds needs)
        {
            if (needs.Minimum >= 70) return 8;
            if (needs.Minimum >= 55 && needs.Average >= 65) return 4;
            return 0;
        }

        public static string LevelTitle(int level)
        {
            if (level >= 10) return "Финансовый профи";
            if (level >= 7) return "Планировщик";
            if (level >= 4) return "Исследователь";
            return "Новичок";
        }

        public static int StageForLevel(int level) => level >= 7 ? 3 : level >= 4 ? 2 : 1;

        public static int NextStageLevel(int level)
        {
            if (level < 4) return 4;
            if (level < 7) return 7;
            return 0;
        }

        public static FinikGrowth Normalize(FinikGrowth growth)
        {
            if (growth == null) return new FinikGrowth();
            growth.events ??= new List<FinikGrowthEvent>();
            growth.events.RemoveAll(e => e == null);
            growth.InvalidateIndex();
            int total = 0;
            foreach (var e in growth.events) total += Math.Max(0, e.xp);
            if (growth.events.Count > 0) growth.xp = total;
            growth.xp = Math.Max(0, growth.xp);
            return growth;
        }
    }
}
