using System;
using System.IO;
using System.Linq;
using Finik.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

public static class FinikCharacterUpgradeSetup
{
    const string ScenePath = "Assets/Finik/Scenes/FinikRoomNavigationPrototype.unity";
    const string TempScenePath = "Assets/Finik/Scenes/FinikRoomNavigationPrototype_stage1_tmp.unity";
    const string CharacterPath = "Assets/Characters/Finik/St1/Models/Finik.fbx";
    const string BaseColorPath = "Assets/Characters/Finik/St1/Textures/FinikBaseColor.png";
    const string NormalPath = "Assets/Characters/Finik/St1/Textures/FinikNormal.png";
    const string ControllerPath = "Assets/Characters/Finik/St1/Animations/Finik.controller";
    const string MaterialPath = "Assets/Characters/Finik/St1/Materials/Finik.mat";

    static readonly string[] LoopClips = { "Idle_11", "walking_2_inplace", "Walking", "Running" };

    [MenuItem("Finik/Character/Apply Stage 1 Rig + Behaviours")]
    public static string Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        ConfigureModelImporter();
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var root = GameObject.Find("Finik_Root");
        if (!root) throw new InvalidOperationException("Finik_Root missing.");

        var oldChildren = root.transform.Cast<Transform>().ToArray();
        var oldRenderers = root.GetComponentsInChildren<Renderer>(true);
        float targetHeight = 0f;
        if (oldRenderers.Length > 0)
        {
            Bounds oldBounds = oldRenderers[0].bounds;
            foreach (var oldRenderer in oldRenderers.Skip(1)) oldBounds.Encapsulate(oldRenderer.bounds);
            targetHeight = oldBounds.size.y;
        }
        foreach (var child in oldChildren) UnityEngine.Object.DestroyImmediate(child.gameObject);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath);
        if (!prefab) throw new InvalidOperationException("Stage 1 FBX could not be imported.");

        var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        visual.name = "Finik";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;

        foreach (string helperName in new[] { "Icosphere", "РРєРѕСЃС„РµСЂР°" })
        {
            var helper = FindDeepChild(visual.transform, helperName);
            if (helper) UnityEngine.Object.DestroyImmediate(helper.gameObject);
        }

        var renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) throw new InvalidOperationException("Stage 1 model has no renderers.");
        var material = ConfigureMaterial();
        foreach (var renderer in renderers) renderer.sharedMaterial = material;

        // SkinnedMeshRenderer.bounds are the imported (padded) local bounds, not the skinned
        // vertices, which lifted Finik ~15 cm off the floor. Measure the real bind-pose surface.
        Bounds bounds = SurfaceBounds(renderers);
        if (targetHeight > .01f && bounds.size.y > .01f)
        {
            float scale = targetHeight / bounds.size.y;
            visual.transform.localScale *= scale;
            bounds = SurfaceBounds(renderers);
        }
        visual.transform.position += Vector3.up * (root.transform.position.y - bounds.min.y);

        var animator = visual.GetComponent<Animator>();
        if (!animator) animator = visual.AddComponent<Animator>();
        animator.applyRootMotion = false;
        animator.runtimeAnimatorController = BuildController();

        var movement = root.GetComponent<FinikMovementController>();
        if (!movement) movement = root.AddComponent<FinikMovementController>();
        var activity = root.GetComponent<FinikActivityController>();
        if (!activity) activity = root.AddComponent<FinikActivityController>();
        if (!root.GetComponent<FinikWanderController>()) root.AddComponent<FinikWanderController>();
        if (!root.GetComponent<FinikInputController>()) root.AddComponent<FinikInputController>();
        if (!root.GetComponent<FinikInteractionController>()) root.AddComponent<FinikInteractionController>();

        SetReference(movement, "animator", animator);
        SetReference(movement, "visual", visual.transform);
        SetReference(activity, "animator", animator);
        var agent = root.GetComponent<NavMeshAgent>();
        if (!agent) agent = root.AddComponent<NavMeshAgent>();
        agent.radius = .15f;
        agent.height = 1.20f;
        agent.stoppingDistance = .05f;
        agent.speed = 1.55f;
        agent.acceleration = 6.5f;
        agent.angularSpeed = 420f;
        agent.autoBraking = true;
        agent.updateRotation = false;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;

        if (NavMesh.SamplePosition(root.transform.position, out var hit, 1.5f, NavMesh.AllAreas))
            agent.Warp(hit.position);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
        {
            if (!EditorSceneManager.SaveScene(scene, TempScenePath, true))
                throw new IOException("Could not save Stage 1 scene or fallback copy.");
            Debug.LogWarning("FINIK_STAGE1_SCENE_FALLBACK=" + TempScenePath);
        }
        AssetDatabase.SaveAssets();

        bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        string report = $"visual={visual.name}; oldHeight={targetHeight:F3}; height={bounds.size.y:F3}; " +
                        $"scale={visual.transform.localScale.x:F3}; clips={RuntimeClipNames().Length}; " +
                        $"controller={ControllerPath}; material={MaterialPath}; agentSpeed={agent.speed:F2}";
        Directory.CreateDirectory("BuildReports");
        File.WriteAllText("BuildReports/finik_stage1_integration.txt", report);
        Debug.Log("FINIK_STAGE1_INTEGRATION=" + report);
        return report;
    }
    public static void ApplyFromCommandLine()
    {
        try
        {
            Apply();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    static void ConfigureModelImporter()
    {
        AssetDatabase.ImportAsset(CharacterPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(CharacterPath) as ModelImporter;
        if (!importer) throw new InvalidOperationException("ModelImporter missing for Stage 1 FBX.");

        importer.importAnimation = true;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

        var clips = importer.defaultClipAnimations;
        foreach (var clip in clips)
        {
            bool loop = LoopClips.Any(token => clip.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
            clip.loopTime = loop;
            clip.loopPose = loop;
        }
        if (clips.Length > 0) importer.clipAnimations = clips;
        importer.SaveAndReimport();

        string[] actual = RuntimeClipNames();
        string[] required =
        {
            "Idle_11", "Idle_12", "Idle_3", "Idle_4", "Idle_9",
            "Idle_Turn_Left", "Idle_Turn_Right", "Long_Breathe_and_Look_Around",
            "walking_2_inplace", "Walking", "Running", "Happy_Sway_Standing",
            "Wave_for_Help_3", "Wave_for_Help_1", "Frustrated_Turn_Right",
            "Big_Wave_Hello", "Cheer_with_Both_Hands_Up", "Backflip_Jump",
            "Happy_jump_f", "Tightrope_Walk_inplace"
        };
        var missing = required.Where(token => !actual.Any(name =>
            name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException("Missing Stage 1 animation clips: " + string.Join(", ", missing));
    }

    static RuntimeAnimatorController BuildController()
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("MoveMode", AnimatorControllerParameterType.Int);
        string[] triggers =
        {
            "Idle12", "Idle3", "Idle4", "Idle9", "LookAround", "TurnLeft", "TurnRight",
            "HappySway", "NeedFood", "NeedPlay", "NeedCare", "Hello", "Celebrate",
            "CelebrateBig", "PlayJump", "Playful", "Rested"
        };
        foreach (string trigger in triggers) controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;
        var idle = sm.AddState("Idle_Base");
        idle.motion = Clip("Idle_11");
        sm.defaultState = idle;

        var autoWalk = sm.AddState("Walk_Autonomous");
        autoWalk.motion = Clip("walking_2_inplace");
        var userWalk = sm.AddState("Walk_User");
        userWalk.motion = Clip("Walking");
        var userRun = sm.AddState("Run_User");
        userRun.motion = Clip("Running");

        AddMoveEntry(sm, autoWalk, 1);
        AddMoveEntry(sm, userWalk, 2);
        AddMoveEntry(sm, userRun, 3);
        AddMoveExit(autoWalk, idle);
        AddMoveExit(userWalk, idle);
        AddMoveExit(userRun, idle);
        AddOneShot(sm, idle, "Idle_12", "Idle12");
        AddOneShot(sm, idle, "Idle_3", "Idle3");
        AddOneShot(sm, idle, "Idle_4", "Idle4");
        AddOneShot(sm, idle, "Idle_9", "Idle9");
        AddOneShot(sm, idle, "Long_Breathe_and_Look_Around", "LookAround");
        AddOneShot(sm, idle, "Idle_Turn_Left", "TurnLeft");
        AddOneShot(sm, idle, "Idle_Turn_Right", "TurnRight");
        AddOneShot(sm, idle, "Happy_Sway_Standing", "HappySway");
        AddOneShot(sm, idle, "Wave_for_Help_3", "NeedFood");
        AddOneShot(sm, idle, "Wave_for_Help_1", "NeedPlay");
        AddOneShot(sm, idle, "Frustrated_Turn_Right", "NeedCare");
        AddOneShot(sm, idle, "Big_Wave_Hello", "Hello");
        AddOneShot(sm, idle, "Cheer_with_Both_Hands_Up", "Celebrate");
        AddOneShot(sm, idle, "Backflip_Jump", "CelebrateBig");
        AddOneShot(sm, idle, "Happy_jump_f", "PlayJump");
        AddOneShot(sm, idle, "Tightrope_Walk_inplace", "Playful");
        AddOneShot(sm, idle, "Long_Breathe_and_Look_Around", "Rested");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    static void AddMoveEntry(AnimatorStateMachine sm, AnimatorState state, int mode)
    {
        var transition = sm.AddAnyStateTransition(state);
        transition.hasExitTime = false;
        transition.duration = .12f;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.Equals, mode, "MoveMode");
    }

    static void AddMoveExit(AnimatorState from, AnimatorState idle)
    {
        var transition = from.AddTransition(idle);
        transition.hasExitTime = false;
        transition.duration = .16f;
        transition.AddCondition(AnimatorConditionMode.Equals, 0f, "MoveMode");
    }

    static void AddOneShot(AnimatorStateMachine sm, AnimatorState idle, string clipToken, string trigger)
    {
        var state = sm.AddState(trigger);
        state.motion = Clip(clipToken);
        var enter = sm.AddAnyStateTransition(state);
        enter.hasExitTime = false;
        enter.duration = .10f;
        enter.canTransitionToSelf = false;
        enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);

        var exit = state.AddTransition(idle);
        exit.hasExitTime = true;
        exit.exitTime = .96f;
        exit.duration = .14f;
    }
    static AnimationClip Clip(string token)
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(CharacterPath)
            .OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return clips.FirstOrDefault(c =>
                   string.Equals(ActionName(c.name), token, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException("Animation clip missing: " + token);
    }

    static string ActionName(string clipName)
    {
        int separator = clipName.LastIndexOf('|');
        return separator >= 0 ? clipName.Substring(separator + 1) : clipName;
    }

    static string[] RuntimeClipNames()
    {
        return AssetDatabase.LoadAllAssetsAtPath(CharacterPath)
            .OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.name)
            .Distinct()
            .ToArray();
    }

    static Material ConfigureMaterial()
    {
        ConfigureTexture(NormalPath, TextureImporterType.NormalMap, false);
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) throw new InvalidOperationException("URP Lit shader missing.");

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!material)
        {
            material = new Material(shader) { name = "Finik" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        material.shader = shader;
        material.SetColor("_BaseColor", Color.white);
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(BaseColorPath));
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath));
        material.SetFloat("_BumpScale", 1f);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", .32f);
        material.EnableKeyword("_NORMALMAP");
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    static void ConfigureTexture(string path, TextureImporterType type, bool srgb)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (!importer) throw new InvalidOperationException("Texture missing: " + path);
        if (importer.textureType == type && importer.sRGBTexture == srgb) return;
        importer.textureType = type;
        importer.sRGBTexture = srgb;
        importer.SaveAndReimport();
    }

    static Transform FindDeepChild(Transform root, string name)
    {
        foreach (Transform child in root)
        {
            if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase)) return child;
            var nested = FindDeepChild(child, name);
            if (nested) return nested;
        }
        return null;
    }

    static void SetReference(UnityEngine.Object target, string field, UnityEngine.Object value)
    {
        var serialized = new SerializedObject(target);
        var property = serialized.FindProperty(field);
        if (property == null) throw new InvalidOperationException($"Missing serialized field {field} on {target.name}");
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    /// <summary>World bounds of the actual vertices (skinned meshes baked in their current pose).</summary>
    static Bounds SurfaceBounds(Renderer[] renderers)
    {
        bool any = false;
        var result = new Bounds();
        var baked = new Mesh();
        foreach (var renderer in renderers)
        {
            Vector3[] vertices;
            Matrix4x4 toWorld;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                skinned.BakeMesh(baked, true);
                vertices = baked.vertices;
                toWorld = skinned.transform.localToWorldMatrix;
            }
            else if (renderer.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh)
            {
                vertices = filter.sharedMesh.vertices;
                toWorld = renderer.transform.localToWorldMatrix;
            }
            else continue;
            foreach (var v in vertices)
            {
                var p = toWorld.MultiplyPoint3x4(v);
                if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                else result.Encapsulate(p);
            }
        }
        UnityEngine.Object.DestroyImmediate(baked);
        return any ? result : renderers[0].bounds;
    }
}
