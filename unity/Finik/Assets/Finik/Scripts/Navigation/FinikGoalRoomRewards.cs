using Finik.Core;
using UnityEngine;
using UnityEngine.AI;

namespace Finik.Navigation
{
    /// <summary>Restores every bought financial goal as a permanent reward in the 3D room.</summary>
    public sealed class FinikGoalRoomRewards : MonoBehaviour
    {
        readonly struct Spec
        {
            public readonly string id;
            public readonly Vector3 position;
            public readonly Vector3 rotation;
            public readonly float size;
            public readonly bool blocksWalking;
            public readonly bool placeOnFloor;

            public Spec(string id, Vector3 position, Vector3 rotation, float size, bool blocksWalking, bool placeOnFloor = true)
            {
                this.id = id;
                this.position = position;
                this.rotation = rotation;
                this.size = size;
                this.blocksWalking = blocksWalking;
                this.placeOnFloor = placeOnFloor;
            }
        }
        static readonly Spec[] Specs =
        {
            new("bike",       new Vector3(1.87f, 0f, -4.79f), new Vector3(0f, 21.773f, 0f), 1.65f, true),
            // These two rewards stand on furniture. Their Y values are deliberate world heights,
            // not floor offsets, so their placement must not be recalculated from mesh bounds.
            new("headphones", new Vector3(-1.69f, 1.02f, -4.732f), new Vector3(-75.48f, 18f, 0f), .3025f, false, placeOnFloor: false),
            new("blocks",     new Vector3(2.572f, .62f, -.454f), new Vector3(0f, -12f, 0f), .5234f, false, placeOnFloor: false),
            new("skateboard", new Vector3(1.9902f, 0f, -1.6131f), new Vector3(0f, 36.389f, 0f), .8703f, false)
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindFirstObjectByType<FinikGoalRoomRewards>()) return;
            new GameObject("Finik_GoalRoomRewards").AddComponent<FinikGoalRoomRewards>();
        }

        void OnEnable()
        {
            FinikGame.Changed += Refresh;
            Refresh();
        }

        void OnDisable() => FinikGame.Changed -= Refresh;

        void Refresh()
        {
            foreach (var spec in Specs)
            {
                string name = "GoalReward_" + spec.id;
                var existing = transform.Find(name);
                bool owned = FinikGame.GoalPurchased(spec.id);
                if (owned && !existing) Spawn(spec, name);
                else if (!owned && existing) Destroy(existing.gameObject);
            }
        }

        void Spawn(Spec spec, string objectName)
        {
            var source = Resources.Load<GameObject>($"GoalRewards/{spec.id}/model");
            if (!source)
            {
                Debug.LogWarning($"[FinikGoalRoomRewards] Missing Resources/GoalRewards/{spec.id}/model");
                return;
            }

            var instance = Instantiate(source, transform);
            instance.name = objectName;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(spec.rotation));
            instance.transform.localScale = Vector3.one;
            ApplyUrpMaterial(instance, spec.id);

            if (!TryBounds(instance, out var bounds))
            {
                instance.transform.position = spec.position;
                return;
            }
            float max = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (max > .0001f) instance.transform.localScale *= spec.size / max;

            if (spec.placeOnFloor && TryBounds(instance, out bounds))
                instance.transform.position = spec.position + Vector3.up * (-bounds.min.y);
            else
                instance.transform.position = spec.position;

            if (spec.blocksWalking && TryBounds(instance, out bounds))
                AddObstacle(instance, bounds);
        }

        static void ApplyUrpMaterial(GameObject root, string id)
        {
            var texture = Resources.Load<Texture2D>($"GoalRewards/{id}/texture");
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (!texture || !shader) return;

            var material = new Material(shader) { name = $"GoalReward_{id}_Material" };
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            else material.mainTexture = texture;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .24f);

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer) continue;
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }
        }

        static bool TryBounds(GameObject root, out Bounds bounds)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            bounds = default;
            bool any = false;
            foreach (var renderer in renderers)
            {
                if (!renderer) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return any;
        }

        static void AddObstacle(GameObject root, Bounds worldBounds)
        {
            var obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = root.transform.InverseTransformPoint(worldBounds.center);
            var localSize = root.transform.InverseTransformVector(worldBounds.size);
            obstacle.size = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
        }
    }
}
