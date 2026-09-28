using System;
using System.Collections.Generic;
using UnityEngine;

namespace Finik.Accessories
{
    /// <summary>
    /// Надевает аксессуары из каталога на Финика: один предмет на слот.
    ///
    /// FBX аксессуара содержит детали в координатах персонажа и копию скелета. Пространства
    /// совмещаются по положениям суставов (alignJoints) в bind-позе: положения суставов не
    /// зависят от ориентации осей костей, поэтому различия осей, единиц и поворота корня
    /// между FBX персонажа и FBX аксессуара поглощаются автоматически. Затем детали вешаются
    /// на целевую кость через bindpose персонажа и дальше двигаются вместе с анимацией.
    /// </summary>
    public sealed class FinikAccessoryRig : MonoBehaviour
    {
        [SerializeField] FinikAccessoryCatalog catalog;
        [Tooltip("SkinnedMeshRenderer тела. Пусто — берётся самый крупный в иерархии.")]
        [SerializeField] SkinnedMeshRenderer body;
        [SerializeField] string[] equipOnStart = Array.Empty<string>();

        readonly Dictionary<string, List<GameObject>> equipped = new Dictionary<string, List<GameObject>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> equippedIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool accessoriesEnabled = true;
        SkinnedMeshRenderer equippedBody;

        public FinikAccessoryCatalog Catalog
        {
            get => catalog;
            set => catalog = value;
        }

        public IReadOnlyDictionary<string, string> EquippedBySlot => equippedIds;
        public bool AccessoriesEnabled => accessoriesEnabled;

        void OnEnable()
        {
            // With Enter Play Mode scene reload disabled, generated children can survive from the
            // previous run while this component starts with empty dictionaries.
            if (Application.isPlaying && equippedIds.Count == 0)
                PurgeAllGeneratedParts();
        }

        public void SetAccessoriesEnabled(bool enabled)
        {
            if (enabled && accessoriesEnabled && equippedIds.Count > 0) return;
            accessoriesEnabled = enabled;
            if (enabled)
            {
                // Enter Play Mode may preserve scene objects while the managed dictionaries are
                // recreated. Start from a clean visual state; the profile is restored immediately
                // afterwards by FinikOnboardingFlow.ShowHome.
                UnequipAll();
                PurgeAllGeneratedParts();
            }
            else
                UnequipAll();
        }

        void Start()
        {
            if (!accessoriesEnabled) return;
            foreach (var id in equipOnStart)
                if (!string.IsNullOrEmpty(id))
                    Equip(id);
        }

        void LateUpdate() => RefreshForActiveCharacter();

        public void RefreshForActiveCharacter()
        {
            if (!accessoriesEnabled || equippedIds.Count == 0)
                return;

            var shownBody = ResolveBody();
            if (shownBody == equippedBody)
                return;

            // CharacterSwitcher keeps every pet/stage under this root and toggles the
            // selected visual. Accessory parts are parented to bones, so they must be
            // rebuilt when the active skeleton changes; otherwise they remain on the
            // now-hidden previous character.
            var ids = new List<string>(equippedIds.Values);
            UnequipAll();
            body = shownBody;
            foreach (var id in ids)
                Equip(id);
        }

        // ---- совместимо с SendMessage из мобильного моста: одна строка-аргумент
        public void EquipAccessory(string id) => Equip(id);
        public void UnequipSlot(string slot) => Unequip(slot);

        /// <summary>Надевает предмет; предмет того же слота снимается. Возвращает созданные детали.</summary>
        public IReadOnlyList<GameObject> Equip(string id)
        {
            if (!accessoriesEnabled)
                return Array.Empty<GameObject>();

            if (catalog == null)
                throw new InvalidOperationException("FinikAccessoryRig: catalog не назначен.");
            var entry = catalog.Find(id);
            if (entry == null || entry.model == null)
                throw new ArgumentException($"FinikAccessoryRig: нет аксессуара '{id}' в каталоге.");

            var skin = ResolveBody();
            var target = FindBone(skin, entry.bone);
            if (target == null)
                throw new InvalidOperationException($"FinikAccessoryRig: у персонажа нет кости '{entry.bone}'.");

            Unequip(entry.slot);
            PurgeOrphanedSlotParts(target, entry.slot);

            var instance = Instantiate(entry.model);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            try
            {
                var accToWorld = SolveAlignment(skin, instance.transform);
                var boneBind = BindPoseWorld(skin, target);
                var worldToBone = boneBind.inverse;

                var parts = new List<GameObject>();
                foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (renderer == null)
                        continue;
                    var go = new GameObject($"Acc_{entry.id}_{filter.name}");
                    go.layer = gameObject.layer;
                    go.transform.SetParent(target, false);
                    ApplyMatrix(go.transform, worldToBone * accToWorld * filter.transform.localToWorldMatrix);
                    ApplyObviousFitCorrection(go.transform, skin, entry.slot);
                    go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterials = renderer.sharedMaterials;
                    r.shadowCastingMode = renderer.shadowCastingMode;
                    parts.Add(go);
                }
                ApplyVariantFit(parts, skin, entry);
                equipped[entry.slot] = parts;
                equippedIds[entry.slot] = entry.id;
                equippedBody = skin;
                return parts;
            }
            finally
            {
                DestroyNow(instance);
            }
        }

        public void Unequip(string slot)
        {
            if (!equipped.TryGetValue(slot, out var parts))
                return;
            foreach (var p in parts)
                if (p != null)
                    DestroyNow(p);
            equipped.Remove(slot);
            equippedIds.Remove(slot);
            if (equippedIds.Count == 0)
                equippedBody = null;
        }

        public void UnequipAll()
        {
            foreach (var slot in new List<string>(equipped.Keys))
                Unequip(slot);
        }

        // A script reload in Play Mode resets the dictionaries but can leave the generated mesh
        // objects attached to the animated bone. Remove those stale parts before rebuilding a slot.
        void PurgeOrphanedSlotParts(Transform target, string slot)
        {
            if (target == null || catalog == null) return;
            var prefixes = new List<string>();
            foreach (var candidate in catalog.entries)
                if (candidate != null && string.Equals(candidate.slot, slot, StringComparison.OrdinalIgnoreCase))
                    prefixes.Add($"Acc_{candidate.id}_");

            foreach (var child in target.GetComponentsInChildren<Transform>(true))
            {
                if (child == target) continue;
                foreach (var prefix in prefixes)
                    if (child.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        DestroyNow(child.gameObject);
                        break;
                    }
            }
        }

        void PurgeAllGeneratedParts()
        {
            foreach (var child in GetComponentsInChildren<Transform>(true))
                if (child != transform && child.name.StartsWith("Acc_", StringComparison.OrdinalIgnoreCase))
                    DestroyNow(child.gameObject);
            equipped.Clear();
            equippedIds.Clear();
            equippedBody = null;
        }

        // ------------------------------------------------------------------ математика

        /// <summary>Матрица «пространство аксессуара → мир» по трём суставам в bind-позе.</summary>
        Matrix4x4 SolveAlignment(SkinnedMeshRenderer skin, Transform accessoryRoot)
        {
            var j = catalog.alignJoints;
            if (j == null || j.Length < 3)
                throw new InvalidOperationException("FinikAccessoryRig: нужно три сустава для выравнивания.");
            var c = new Vector3[3];
            var a = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                var cb = FindBone(skin, j[i]);
                var ab = FindDeep(accessoryRoot, j[i]);
                if (cb == null || ab == null)
                    throw new InvalidOperationException($"FinikAccessoryRig: сустав '{j[i]}' не найден.");
                c[i] = BindPoseWorld(skin, cb).GetColumn(3);
                a[i] = ab.position;
            }
            float sa = (a[1] - a[0]).magnitude, sc = (c[1] - c[0]).magnitude;
            if (sa < 1e-6f)
                throw new InvalidOperationException("FinikAccessoryRig: вырожденный скелет аксессуара.");
            var scale = Matrix4x4.Scale(Vector3.one * (sc / sa));
            return Frame(c) * scale * Frame(a).inverse;
        }

        /// <summary>Ортонормированный репер: начало в j[0], Y к j[1], X к j[2].</summary>
        static Matrix4x4 Frame(Vector3[] j)
        {
            var y = (j[1] - j[0]).normalized;
            var x = Vector3.ProjectOnPlane(j[2] - j[0], y).normalized;
            var z = Vector3.Cross(x, y);
            var m = Matrix4x4.identity;
            m.SetColumn(0, x);
            m.SetColumn(1, y);
            m.SetColumn(2, z);
            m.SetColumn(3, new Vector4(j[0].x, j[0].y, j[0].z, 1f));
            return m;
        }

        /// <summary>Мировая матрица кости в bind-позе (не зависит от текущей анимации).</summary>
        static Matrix4x4 BindPoseWorld(SkinnedMeshRenderer skin, Transform bone)
        {
            int index = Array.IndexOf(skin.bones, bone);
            if (index < 0 || skin.sharedMesh == null || index >= skin.sharedMesh.bindposeCount)
                return bone.localToWorldMatrix;
            return skin.transform.localToWorldMatrix * skin.sharedMesh.bindposes[index].inverse;
        }

        static void ApplyMatrix(Transform t, Matrix4x4 m)
        {
            Vector3 c0 = m.GetColumn(0), c1 = m.GetColumn(1), c2 = m.GetColumn(2);
            t.localPosition = m.GetColumn(3);
            t.localRotation = Quaternion.LookRotation(c2, c1);
            t.localScale = new Vector3(c0.magnitude, c1.magnitude, c2.magnitude);
        }

        static void ApplyObviousFitCorrection(Transform part, SkinnedMeshRenderer skin, string slot)
        {
            if (!string.Equals(slot, "cap", StringComparison.OrdinalIgnoreCase))
                return;

            var hips = FindBone(skin, "Hips");
            var head = FindBone(skin, "Head");
            if (hips == null || head == null)
                return;

            var hipsBind = BindPoseWorld(skin, hips).GetColumn(3);
            var headBind = BindPoseWorld(skin, head).GetColumn(3);
            var bodyUp = headBind - hipsBind;
            if (bodyUp.sqrMagnitude < 1e-6f)
                return;

            // The shared hat meshes were authored on a smaller reference head. Without
            // this clearance their brim is at eye level on later stages and fully buried
            // in the larger stage-1 heads. Scale the lift with the character skeleton.
            var worldLift = bodyUp.normalized * bodyUp.magnitude * 0.10f;
            part.localPosition += BindPoseWorld(skin, head).inverse.MultiplyVector(worldLift);
        }

        void ApplyVariantFit(List<GameObject> parts, SkinnedMeshRenderer skin, FinikAccessoryCatalog.Entry entry)
        {
            var visual = skin.transform;
            while (visual.parent != null && visual.parent != transform)
                visual = visual.parent;

            if (string.Equals(entry.slot, "glasses", StringComparison.OrdinalIgnoreCase))
            {
                ApplyGlassesFit(parts, skin, visual);
                return;
            }

            if (entry.slot == "watch" && (visual.name == "Cat_St2" || visual.name == "Cat_St3" ||
                visual.name == "Racoon" || visual.name == "Racoon_St3" || visual.name == "Finik_St3"))
            {
                ApplyWristFit(parts, skin, visual);
                return;
            }
            if (entry.slot == "cap" && (visual.name == "Cat_St2" || visual.name == "Racoon" || visual.name == "Finik_St3" ||
                (entry.id == "gear_cap" && visual.name == "Racoon_St3")))
            {
                var headBind = BindPoseWorld(skin, FindBone(skin, "Head"));
                Vector3 anchor = headBind.GetColumn(3);
                float length = Vector3.Distance(anchor, BindPoseWorld(skin, FindBone(skin, "Hips")).GetColumn(3));
                float scale = visual.name == "Finik_St3" ? .94f : 1f;
                Vector3 offset = visual.name == "Cat_St2" ? new Vector3(-.025f, -.09f, .015f)
                    : visual.name == "Racoon" ? new Vector3(-.02f, .055f, -.04f)
                    : visual.name == "Racoon_St3" ? new Vector3(0f, -.015f, -.08f)
                    : new Vector3(-.025f, .045f, .025f);
                var rotation = visual.name == "Racoon_St3"
                    ? Quaternion.AngleAxis(20f, visual.right) : Quaternion.identity;
                var correction = Matrix4x4.Translate(anchor + visual.TransformDirection(offset) * length)
                    * Matrix4x4.Rotate(rotation) * Matrix4x4.Scale(Vector3.one * scale) * Matrix4x4.Translate(-anchor);
                foreach (var part in parts)
                    ApplyMatrix(part.transform, headBind.inverse * correction * headBind *
                        Matrix4x4.TRS(part.transform.localPosition, part.transform.localRotation, part.transform.localScale));
                return;
            }

            float headScale;
            float capLiftFactor;
            switch (visual.name)
            {
                case "Finik":
                    headScale = 1.18f;
                    capLiftFactor = 0.07f;
                    break;
                case "Finik_St3":
                    headScale = 1.08f;
                    capLiftFactor = 0.07f;
                    break;
                case "Racoon_St1":
                    headScale = 1.08f;
                    capLiftFactor = 0.06f;
                    break;
                case "Cat":
                    headScale = 1.32f;
                    capLiftFactor = 0.07f;
                    break;
                default:
                    return;
            }

            var hips = FindBone(skin, "Hips");
            var head = FindBone(skin, "Head");
            if (hips == null || head == null)
                return;
            float bodyLength = Vector3.Distance(hips.position, head.position);

            bool onHead = string.Equals(entry.slot, "cap", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(entry.slot, "headphones", StringComparison.OrdinalIgnoreCase);
            if (onHead)
            {
                var anchor = head.position;
                foreach (var part in parts)
                {
                    part.transform.position = anchor + (part.transform.position - anchor) * headScale;
                    part.transform.localScale *= headScale;
                }

                var headFront = FindBone(skin, "headfront");
                if (TryRendererCenter(parts, out var accessoryCenter))
                {
                    if (string.Equals(entry.slot, "headphones", StringComparison.OrdinalIgnoreCase))
                    {
                        var earCenter = headFront != null
                            ? new Vector3(head.position.x, headFront.position.y, head.position.z)
                            : head.position;
                        MoveParts(parts, earCenter - accessoryCenter);
                    }
                }

                if (string.Equals(entry.slot, "cap", StringComparison.OrdinalIgnoreCase))
                {
                    MoveParts(parts, (head.position - hips.position).normalized * bodyLength * capLiftFactor);
                    if (visual.name == "Cat")
                        MoveParts(parts, (head.position - hips.position).normalized * bodyLength * 0.10f);
                }
            }

            if (string.Equals(entry.slot, "watch", StringComparison.OrdinalIgnoreCase))
            {
                var forearm = FindBone(skin, "LeftForeArm");
                var hand = FindBone(skin, "LeftHand");
                if (forearm != null && hand != null && TryRendererCenter(parts, out var center))
                {
                    var wrist = Vector3.Lerp(forearm.position, hand.position, 0.88f);
                    var outward = Vector3.ProjectOnPlane(visual.forward, Vector3.up).normalized;
                    if (outward.sqrMagnitude > 0.1f)
                        wrist += outward * bodyLength * 0.035f;
                    MoveParts(parts, wrist - center);
                    foreach (var part in parts)
                    {
                        part.transform.position = wrist + (part.transform.position - wrist) * 1.20f;
                        part.transform.localScale *= 1.20f;
                    }
                }
            }
        }

        static void MoveParts(List<GameObject> parts, Vector3 worldDelta)
        {
            foreach (var part in parts)
                part.transform.position += worldDelta;
        }

        static void ApplyWristFit(List<GameObject> parts, SkinnedMeshRenderer skin, Transform visual)
        {
            var forearm = FindBone(skin, "LeftForeArm");
            var hand = FindBone(skin, "LeftHand");
            var strap = parts.Find(p => p.name.EndsWith("Strap", StringComparison.OrdinalIgnoreCase));
            if (!forearm || !hand || !strap) return;
            var bind = BindPoseWorld(skin, forearm);
            Vector3 elbow = bind.GetColumn(3), wrist = BindPoseWorld(skin, hand).GetColumn(3);
            Vector3 axis = (wrist - elbow).normalized;
            Vector3 outward = Vector3.ProjectOnPlane(visual.forward, axis).normalized;
            // The authored strap encircles its mesh X axis; its face points along mesh Z.
            var t = strap.transform;
            var source = bind * Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
            Vector3 center = source.MultiplyPoint3x4(strap.GetComponent<MeshFilter>().sharedMesh.bounds.center);
            var sourceRotation = Quaternion.LookRotation(source.MultiplyVector(Vector3.forward), source.MultiplyVector(Vector3.up));
            var rotation = Quaternion.LookRotation(outward, Vector3.Cross(outward, axis)) * Quaternion.Inverse(sourceRotation);
            float scale = visual.name == "Cat_St2" || visual.name == "Cat_St3" ? .66f : .80f;
            var correction = Matrix4x4.TRS(Vector3.Lerp(elbow, wrist, .88f), rotation, Vector3.one * scale)
                * Matrix4x4.Translate(-center);
            foreach (var part in parts)
                ApplyMatrix(part.transform, bind.inverse * correction * bind *
                    Matrix4x4.TRS(part.transform.localPosition, part.transform.localRotation, part.transform.localScale));
        }

        static void ApplyGlassesFit(List<GameObject> parts, SkinnedMeshRenderer skin, Transform visual)
        {
            var head = FindBone(skin, "Head");
            var hips = FindBone(skin, "Hips");
            if (!head || !hips || parts.Count == 0) return;
            var headBind = BindPoseWorld(skin, head);
            float length = Vector3.Distance(headBind.GetColumn(3), BindPoseWorld(skin, hips).GetColumn(3));
            float scale = 1f;
            Vector3 offset = Vector3.zero;
            // X/Y/Z are right/up/forward in model space, measured in bind hips-to-head lengths.
            // Finik St2 is the authored reference; the other eight heads have different proportions.
            switch (visual.name)
            {
                case "Finik": scale = 1.20f; offset = new Vector3(-.027f, .113f, .20f); break;
                case "Finik_St2": break;
                case "Finik_St3": scale = .95f; offset = new Vector3(-.05f, -.032f, .06f); break;
                case "Cat": scale = 1.55f; offset = new Vector3(-.046f, .257f, .15f); break;
                case "Cat_St2": scale = .94f; offset = new Vector3(-.025f, -.037f, .03f); break;
                case "Cat_St3": scale = 1.02f; offset = new Vector3(-.029f, -.043f, .04f); break;
                case "Racoon_St1": scale = 1.25f; offset = new Vector3(.015f, .101f, .15f); break;
                case "Racoon": scale = 1.15f; offset = new Vector3(-.027f, .163f, .105f); break;
                case "Racoon_St3": offset = new Vector3(0, -.01f, .025f); break;
            }

            // Fit in bind space, then store the result in the head's local space. Sampling the
            // animated head here made identical glasses land differently on each equip.
            var lens = parts.Find(p => p.name.IndexOf("Lens", StringComparison.OrdinalIgnoreCase) >= 0) ?? parts[0];
            var lensMesh = lens.GetComponent<MeshFilter>().sharedMesh;
            var lensLocal = Matrix4x4.TRS(lens.transform.localPosition, lens.transform.localRotation, lens.transform.localScale);
            Vector3 center = (headBind * lensLocal).MultiplyPoint3x4(lensMesh.bounds.center);
            Vector3 delta = visual.TransformDirection(offset) * length;
            var correction = Matrix4x4.Translate(center + delta) * Matrix4x4.Scale(Vector3.one * scale) * Matrix4x4.Translate(-center);
            foreach (var part in parts)
            {
                var local = Matrix4x4.TRS(part.transform.localPosition, part.transform.localRotation, part.transform.localScale);
                ApplyMatrix(part.transform, headBind.inverse * correction * headBind * local);
            }
        }

        static bool TryRendererCenter(List<GameObject> parts, out Vector3 center)
        {
            Bounds bounds = default;
            bool found = false;
            foreach (var part in parts)
            {
                var renderer = part.GetComponent<Renderer>();
                if (renderer == null) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            center = found ? bounds.center : Vector3.zero;
            return found;
        }

        /// <summary>
        /// Тело, на которое вешаются аксессуары: самый плотный SkinnedMeshRenderer среди **показанных**.
        ///
        /// Под ригой лежат все стадии роста персонажа, а включена всегда одна (FinikCharacterSwitcher).
        /// Поиск по всей иерархии, включая выключенные объекты, выбирал бы самую плотную модель
        /// независимо от того, какая сейчас на экране, и одежда оказывалась бы на спрятанном персонаже.
        /// Поэтому запомненное тело сбрасывается, как только перестаёт быть показанным.
        /// </summary>
        SkinnedMeshRenderer ResolveBody()
        {
            if (body != null && body.gameObject.activeInHierarchy)
                return body;

            SkinnedMeshRenderer shown = null, any = null;
            foreach (var s in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (s.sharedMesh == null) continue;
                if (any == null || s.sharedMesh.vertexCount > any.sharedMesh.vertexCount) any = s;
                if (!s.gameObject.activeInHierarchy) continue;
                if (shown == null || s.sharedMesh.vertexCount > shown.sharedMesh.vertexCount) shown = s;
            }
            // Ни одной показанной модели нет (персонаж ещё не выбран) — берём любую, чтобы не падать.
            body = shown ?? any ?? throw new InvalidOperationException("FinikAccessoryRig: у персонажа нет SkinnedMeshRenderer.");
            return body;
        }

        static Transform FindBone(SkinnedMeshRenderer skin, string name)
        {
            foreach (var b in skin.bones)
                if (b != null && SameBone(b.name, name))
                    return b;
            return skin.rootBone != null ? FindDeep(skin.rootBone.root, name) : null;
        }

        /// <summary>
        /// Имена костей сравниваются без префикса пространства имён: «mixamorig:Head» == «Head».
        /// Так один каталог аксессуаров подходит и к риггу Meshy, и к Mixamo-риггу Stage1.
        /// </summary>
        static bool SameBone(string a, string b) =>
            string.Equals(Canonical(a), Canonical(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>Meshy numbers spine bones «Spine01/Spine02», Mixamo «Spine1/Spine2».</summary>
        static string Canonical(string boneName)
        {
            int colon = boneName.LastIndexOf(':');
            string name = colon >= 0 ? boneName.Substring(colon + 1) : boneName;
            return name switch
            {
                "Spine01" => "Spine1",
                "Spine02" => "Spine2",
                _ => name
            };
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (SameBone(root.name, name))
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDeep(root.GetChild(i), name);
                if (r != null)
                    return r;
            }
            return null;
        }

        static void DestroyNow(GameObject go)
        {
            if (Application.isPlaying)
            {
                // Destroy is deferred to the end of the frame; hiding first prevents two
                // accessories being visible together during a replace/preview operation.
                go.SetActive(false);
                Destroy(go);
            }
            else
                DestroyImmediate(go);
        }
    }
}
