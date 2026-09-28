using System;
using System.Collections.Generic;
using System.Linq;
using Finik.Navigation;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

public static class FinikNavigationPolish
{
    const string ScenePath = "Assets/Finik/Scenes/FinikRoomNavigationPrototype.unity";
    const string ControllerPath = "Assets/Finik/Animations/FinikMeshyNavigation.controller";

    [MenuItem("Finik/Navigation/Polish Movement And NavMesh")]
    public static string Apply()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var root = GameObject.Find("Finik_Root") ?? throw new InvalidOperationException("Finik_Root missing");
        var animator = root.GetComponentInChildren<Animator>() ?? throw new InvalidOperationException("Finik Animator missing");

        AlignVisualToAgent(root.transform, animator.transform);
        TuneAnimatorController();
        TuneRuntimeComponents(root);
        int blockerCount = ConfigureNavMesh(scene, root);

        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        string result = $"POLISHED blockers={blockerCount}; scene={ScenePath}; sceneDirty=true (inspect, then save manually)";
        Debug.Log("FINIK_NAVIGATION_POLISH=" + result);
        return result;
    }

    static void AlignVisualToAgent(Transform root, Transform visual)
    {
        visual.localRotation = Quaternion.identity;
        // Keep the artist/user-authored scale. Navigation polish must not resize Finik.
        visual.localPosition = Vector3.zero;

        var renderers = visual.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
        Vector3 delta = new Vector3(
            root.position.x - bounds.center.x,
            root.position.y - bounds.min.y,
            root.position.z - bounds.center.z);
        visual.position += delta;
    }

    static void TuneRuntimeComponents(GameObject root)
    {
        var movement = root.GetComponent<FinikMovementController>();
        if (movement)
        {
            var so = new SerializedObject(movement);
            var speed = so.FindProperty("movementSpeed");
            if (speed != null) speed.floatValue = 1.55f;
            var acceleration = so.FindProperty("movementAcceleration");
            if (acceleration != null) acceleration.floatValue = 6.5f;
            var animationSpeed = so.FindProperty("walkAnimationSpeed");
            if (animationSpeed != null) animationSpeed.floatValue = 1.3f;
            var runtimeSnap = so.FindProperty("runtimeRecoverySnapRadius");
            if (runtimeSnap != null) runtimeSnap.floatValue = 0.12f;
            var startSnap = so.FindProperty("navMeshStartSnapRadius");
            if (startSnap != null) startSnap.floatValue = 0.45f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        var agent = root.GetComponent<NavMeshAgent>();
        if (agent)
        {
            agent.radius = 0.15f;
            agent.speed = 1.55f;
            agent.acceleration = 6.5f;
        }

        var wander = root.GetComponent<FinikWanderController>();
        if (wander)
        {
            var so = new SerializedObject(wander);
            var fixedSeed = so.FindProperty("useFixedRandomSeed");
            if (fixedSeed != null) fixedSeed.boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static void TuneAnimatorController()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (!controller) throw new InvalidOperationException("Animator controller missing");
        var sm = controller.layers[0].stateMachine;
        var idle = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Idle");
        var walk = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Walk");
        if (!idle || !walk) throw new InvalidOperationException("Idle/Walk states missing");

        foreach (var t in idle.transitions.Where(t => t.destinationState == walk))
        {
            foreach (var c in t.conditions.ToArray()) t.RemoveCondition(c);
            t.AddCondition(AnimatorConditionMode.Greater, 0.08f, "Speed");
            t.hasExitTime = false;
            t.duration = 0.12f;
        }
        foreach (var t in walk.transitions.Where(t => t.destinationState == idle))
        {
            foreach (var c in t.conditions.ToArray()) t.RemoveCondition(c);
            t.AddCondition(AnimatorConditionMode.Less, 0.025f, "Speed");
            t.hasExitTime = false;
            t.duration = 0.14f;
        }
        EditorUtility.SetDirty(controller);
    }

    static int ConfigureNavMesh(UnityEngine.SceneManagement.Scene scene, GameObject finikRoot)
    {
        var surface = UnityEngine.Object.FindFirstObjectByType<NavMeshSurface>();
        if (!surface) throw new InvalidOperationException("NavMeshSurface missing");
        var floorRoot = GameObject.Find("Floor") ?? GameObject.Find("room_floor");
        if (!floorRoot) throw new InvalidOperationException("Floor/room_floor missing");
        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0) throw new InvalidOperationException("Ground layer missing");

        foreach (var root in scene.GetRootGameObjects())
            ResetGroundLayer(root.transform, groundLayer);

        var floorRenderer = FindActualFloorRenderer(floorRoot);
        if (!floorRenderer) throw new InvalidOperationException("Could not identify actual floor renderer");
        var floorObject = floorRenderer.gameObject;
        floorObject.layer = groundLayer;
        EnsureFloorCollider(floorObject);

        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = 1 << groundLayer;
        surface.overrideTileSize = true;
        surface.tileSize = 64;
        surface.overrideVoxelSize = true;
        surface.voxelSize = 0.04f;

        Bounds floorBounds = floorRenderer.bounds;
        float floorY = floorBounds.max.y;
        var blockerRoot = GameObject.Find("Finik_NavMeshBlockers");
        if (!blockerRoot) blockerRoot = new GameObject("Finik_NavMeshBlockers");
        blockerRoot.layer = groundLayer;
        while (blockerRoot.transform.childCount > 0)
            UnityEngine.Object.DestroyImmediate(blockerRoot.transform.GetChild(0).gameObject);

        var grouped = CollectBlockerBounds(floorRenderer, finikRoot, blockerRoot, floorBounds, floorY);
        int blocked = 0;
        foreach (var pair in grouped)
        {
            Bounds tunedBounds = TuneBlockerBounds(pair.Key.name, pair.Value);
            CreateBlocker(blockerRoot.transform, pair.Key.name, tunedBounds, floorY, groundLayer, blocked++);
        }

        surface.RemoveData();
        surface.BuildNavMesh();
        var triangulation = NavMesh.CalculateTriangulation();
        int elevated = triangulation.vertices.Count(v => v.y > floorY + 0.20f);
        Debug.Log($"FINIK_NAVMESH floor={floorObject.name}; blockers={blocked}; vertices={triangulation.vertices.Length}; elevatedVertices={elevated}");
        return blocked;
    }

    static Renderer FindActualFloorRenderer(GameObject floorRoot)
    {
        var candidates = floorRoot.GetComponentsInChildren<Renderer>(true)
            .Where(r => r.bounds.size.y <= 0.35f)
            .OrderByDescending(r => r.bounds.size.x * r.bounds.size.z)
            .ToArray();
        if (candidates.Length > 0) return candidates[0];
        return floorRoot.GetComponent<Renderer>();
    }

    static void EnsureFloorCollider(GameObject floorObject)
    {
        if (floorObject.GetComponent<Collider>()) return;
        var filter = floorObject.GetComponent<MeshFilter>();
        if (!filter || !filter.sharedMesh) throw new InvalidOperationException("Actual floor has no mesh for collider");
        var collider = floorObject.AddComponent<MeshCollider>();
        collider.sharedMesh = filter.sharedMesh;
        collider.convex = false;
    }

    static void ResetGroundLayer(Transform root, int groundLayer)
    {
        if (root.gameObject.layer == groundLayer) root.gameObject.layer = 0;
        foreach (Transform child in root) ResetGroundLayer(child, groundLayer);
    }

    static Dictionary<GameObject, Bounds> CollectBlockerBounds(Renderer floorRenderer, GameObject finikRoot, GameObject blockerRoot, Bounds floorBounds, float floorY)
    {
        var result = new Dictionary<GameObject, Bounds>();
        float floorArea = Mathf.Max(0.01f, floorBounds.size.x * floorBounds.size.z);
        foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (renderer == floorRenderer) continue;
            if (renderer.transform.IsChildOf(finikRoot.transform)) continue;
            if (renderer.transform.IsChildOf(blockerRoot.transform)) continue;

            GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(renderer.gameObject) ?? renderer.gameObject;
            if (!result.TryGetValue(root, out var bounds)) result[root] = renderer.bounds;
            else { bounds.Encapsulate(renderer.bounds); result[root] = bounds; }
        }

        var filtered = new Dictionary<GameObject, Bounds>();
        foreach (var pair in result)
        {
            Bounds b = pair.Value;
            float footprint = b.size.x * b.size.z;
            bool touchesFloor = b.min.y <= floorY + 0.25f;
            bool substantialHeight = b.size.y >= 0.18f;
            bool roomSized = footprint >= floorArea * 0.65f;
            if (!touchesFloor || !substantialHeight || roomSized) continue;
            filtered[pair.Key] = b;
        }
        return filtered;
    }

    static Bounds TuneBlockerBounds(string sourceName, Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;

        // Open the user-marked inner walking lanes while keeping the wall-facing
        // sides protected. Values are world-space metres.
        switch (sourceName)
        {
            case "Sofa":
                min.x += 0.35f;
                min.z += 0.45f;
                break;
            case "Bed":
                min.x += 0.35f;
                max.z -= 0.30f;
                break;
            case "Desk":
                max.x -= 0.15f;
                max.z -= 0.45f;
                break;
            case "Backpack":
                max.x -= 0.45f;
                min.z += 0.08f;
                max.z -= 0.08f;
                break;
            case "Fridge":
                max.x -= 0.25f;
                max.z -= 0.20f;
                break;
        }

        if (max.x - min.x < 0.12f) max.x = min.x + 0.12f;
        if (max.z - min.z < 0.12f) max.z = min.z + 0.12f;
        bounds.SetMinMax(min, max);
        return bounds;
    }

    static void CreateBlocker(Transform parent, string sourceName, Bounds bounds, float floorY, int layer, int index)
    {
        var go = new GameObject($"Blocker_{index:000}_{sourceName}");
        go.transform.SetParent(parent, false);
        go.layer = layer;
        go.transform.position = new Vector3(bounds.center.x, floorY + 0.30f, bounds.center.z);
        var volume = go.AddComponent<NavMeshModifierVolume>();
        volume.area = NavMesh.GetAreaFromName("Not Walkable");
        volume.center = Vector3.zero;
        float padding = sourceName is "Sofa" or "Bed" or "Desk" or "Backpack" or "Fridge"
            ? 0.02f
            : 0.05f;
        volume.size = new Vector3(
            Mathf.Max(0.12f, bounds.size.x + padding * 2f),
            0.60f,
            Mathf.Max(0.12f, bounds.size.z + padding * 2f));
    }
}