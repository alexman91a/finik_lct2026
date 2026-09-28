using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Games
{
    /// <summary>
    /// Sprite table shared by the three mood games: gems, card pictures, the card back, the basket and
    /// what falls into it. The builder fills it from the sprite baker, the boards ask for a name.
    /// </summary>
    public sealed class FinikGameArt : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public string name;
            public Sprite sprite;
            /// <summary>
            /// What a sliced sprite's borders are drawn at. Hand-made art comes in any resolution, so the
            /// builder stores the same multiplier FinikHudSpriteBaker would give it; 0 means "leave alone".
            /// </summary>
            public float pixelsPerUnit;
        }

        [SerializeField] Entry[] icons = Array.Empty<Entry>();
        [Tooltip("Stands in for any picture the sheet does not have yet, so a board is never a row of empty boxes.")]
        [SerializeField] Sprite fallback;

        readonly Dictionary<string, Entry> byName = new(StringComparer.Ordinal);
        // Not kept over the editor's hot reload: the dictionary comes back empty, so the flag must too.
        [NonSerialized] bool indexed;

        /// <summary>
        /// The sprite for <paramref name="name"/>, or the fallback. The index is built on first use, not
        /// in Awake: the editor runs with domain and scene reload off, and a screen already in the scene
        /// can reach Play Mode without its Awake ever having run.
        /// </summary>
        public Sprite Get(string name) => Lookup(name, out var entry) ? entry.sprite : fallback;

        /// <summary>True when the sheet really has that picture (the fallback does not count).</summary>
        public bool Has(string name) => Lookup(name, out _);

        /// <summary>Puts the named picture on <paramref name="image"/>, including the slicing scale.</summary>
        public void Apply(Image image, string name)
        {
            if (!image) return;
            if (Lookup(name, out var entry))
            {
                image.sprite = entry.sprite;
                if (entry.pixelsPerUnit > 0f) image.pixelsPerUnitMultiplier = entry.pixelsPerUnit;
            }
            else
            {
                image.sprite = fallback;
            }
        }

        bool Lookup(string name, out Entry entry)
        {
            if (!indexed)
            {
                byName.Clear();
                foreach (var candidate in icons)
                    if (!string.IsNullOrEmpty(candidate.name) && candidate.sprite) byName[candidate.name] = candidate;
                indexed = true;
            }
            entry = default;
            return !string.IsNullOrEmpty(name) && byName.TryGetValue(name, out entry);
        }
    }
}
