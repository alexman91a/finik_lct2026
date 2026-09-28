using System;
using System.Collections.Generic;
using UnityEngine;

namespace Finik.Accessories
{
    /// <summary>Каталог аксессуаров Финика. Собирается из accessories_manifest.json
    /// меню Finik/Accessories/Import From Manifest.</summary>
    [CreateAssetMenu(menuName = "Finik/Accessory Catalog", fileName = "FinikAccessoryCatalog")]
    public sealed class FinikAccessoryCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string id;
            public string title;
            public string slot;
            public string bone;
            public GameObject model;
        }

        [Tooltip("Суставы, по положениям которых совмещаются пространства аксессуара и персонажа.")]
        public string[] alignJoints = { "Hips", "Head", "LeftHand" };

        public List<Entry> entries = new List<Entry>();

        public Entry Find(string id)
        {
            foreach (var e in entries)
                if (string.Equals(e.id, id, StringComparison.OrdinalIgnoreCase))
                    return e;
            return null;
        }
    }
}
