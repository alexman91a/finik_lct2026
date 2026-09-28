using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Finik.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FinikStage1Validator
{
    const string ScenePath = "Assets/Finik/Scenes/FinikRoomNavigationPrototype.unity";
    const string CharacterPath = "Assets/Characters/Finik/St1/Models/Finik.fbx";
    const string ControllerPath = "Assets/Characters/Finik/St1/Animations/Finik.controller";

    static readonly string[] RequiredClips =
    {
        "Backflip_Jump", "Big_Wave_Hello", "Cheer_with_Both_Hands_Up",
        "Frustrated_Turn_Right", "Happy_jump_f", "Happy_Sway_Standing",
        "Idle_11", "Idle_12", "Idle_3", "Idle_4", "Idle_9",
        "Idle_Turn_Left", "Idle_Turn_Right", "Long_Breathe_and_Look_Around",
        "Running", "Tightrope_Walk_inplace", "Walking", "walking_2_inplace",
        "Wave_for_Help_1", "Wave_for_Help_3", "restpose"
    };
    public static void RunFromCommandLine()
    {
        try
        {
            var issues = Validate();
            if (issues.Count > 0)
            {
                foreach (string issue in issues) Debug.LogError("FINIK_STAGE1_VALIDATE: " + issue);
                File.WriteAllLines("BuildReports/finik_stage1_validation.txt", issues.Select(x => "FAIL: " + x));
                EditorApplication.Exit(1);
                return;
            }

            const string ok = "PASS: Stage1 scene, 21 clips, controller, references, material and scripts validated.";
            File.WriteAllText("BuildReports/finik_stage1_validation.txt", ok);
            Debug.Log("FINIK_STAGE1_VALIDATE=" + ok);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    static List<string> Validate()
    {
        var issues = new List<string>();
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var root = GameObject.Find("Finik_Root");
        if (!root) { issues.Add("Finik_Root missing."); return issues; }

        var visual = root.transform.Cast<Transform>().FirstOrDefault(x => x.name == "Finik");
        if (!visual) issues.Add("Finik visual missing.");
        if (root.transform.Cast<Transform>().Any(x => x.name.IndexOf("Meshy", StringComparison.OrdinalIgnoreCase) >= 0))
            issues.Add("Old Meshy visual is still attached.");

        var animator = visual ? visual.GetComponent<Animator>() : null;
        if (!animator) issues.Add("Stage1 Animator missing.");
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (!controller) issues.Add("FinikStage1.controller missing.");
        if (animator && animator.runtimeAnimatorController != controller)
            issues.Add("Stage1 Animator is not using FinikStage1.controller.");

        var source = visual ? PrefabUtility.GetCorrespondingObjectFromSource(visual.gameObject) : null;
        if (source && AssetDatabase.GetAssetPath(source) != CharacterPath)
            issues.Add("Stage1 visual source is not corrected FBX.");

        var clips = AssetDatabase.LoadAllAssetsAtPath(CharacterPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (clips.Length != 21) issues.Add($"Expected 21 clips, found {clips.Length}.");
        foreach (string token in RequiredClips)
            if (!clips.Any(c => c.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
                issues.Add("Missing clip: " + token);

        if (controller)
        {
            string[] requiredParams =
            {
                "Speed", "MoveMode", "Idle12", "Idle3", "Idle4", "Idle9", "LookAround",
                "TurnLeft", "TurnRight", "HappySway", "NeedFood", "NeedPlay", "NeedCare",
                "Hello", "Celebrate", "CelebrateBig", "PlayJump", "Playful", "Rested"
            };
            foreach (string parameter in requiredParams)
                if (!controller.parameters.Any(p => p.name == parameter))
                    issues.Add("Missing Animator parameter: " + parameter);

            var states = controller.layers[0].stateMachine.states.Select(s => s.state).ToArray();
            CheckStateMotion(states, "Idle_Base", "Idle_11", issues);
            CheckStateMotion(states, "Walk_Autonomous", "walking_2_inplace", issues);
            CheckStateMotion(states, "Walk_User", "Walking", issues);
            CheckStateMotion(states, "Run_User", "Running", issues);
        }
        var movement = root.GetComponent<FinikMovementController>();
        var activity = root.GetComponent<FinikActivityController>();
        if (!movement) issues.Add("FinikMovementController missing.");
        if (!activity) issues.Add("FinikActivityController missing.");
        if (!root.GetComponent<FinikWanderController>()) issues.Add("FinikWanderController missing.");
        if (!root.GetComponent<FinikInteractionController>()) issues.Add("FinikInteractionController missing.");

        if (movement && animator)
        {
            var so = new SerializedObject(movement);
            if (so.FindProperty("animator").objectReferenceValue != animator) issues.Add("Movement animator reference mismatch.");
            if (so.FindProperty("visual").objectReferenceValue != visual) issues.Add("Movement visual reference mismatch.");
        }

        if (activity && animator)
        {
            var so = new SerializedObject(activity);
            if (so.FindProperty("animator").objectReferenceValue != animator) issues.Add("Activity animator reference mismatch.");
        }

        if (visual)
        {
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) issues.Add("No Stage1 renderers.");
            else if (renderers.Any(r => !r.sharedMaterial || r.sharedMaterial.name != "Finik"))
                issues.Add("Stage1 material not assigned to every renderer.");
        }
        int missingScripts = 0;
        foreach (var go in scene.GetRootGameObjects())
            foreach (var transform in go.GetComponentsInChildren<Transform>(true))
                missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
        if (missingScripts > 0) issues.Add($"Scene has {missingScripts} missing script component(s).");

        var importer = AssetImporter.GetAtPath(CharacterPath) as ModelImporter;
        if (!importer) issues.Add("Stage1 ModelImporter missing.");
        else
        {
            foreach (var clip in importer.clipAnimations)
            {
                bool shouldLoop =
                    clip.name.IndexOf("Idle_11", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    clip.name.IndexOf("walking_2_inplace", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    clip.name.IndexOf("Walking", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    clip.name.IndexOf("Running", StringComparison.OrdinalIgnoreCase) >= 0;
                if (clip.loopTime != shouldLoop)
                    issues.Add($"Loop flag mismatch: {clip.name}, expected {shouldLoop}.");
            }
        }

        return issues;
    }

    static void CheckStateMotion(AnimatorState[] states, string stateName, string actionName, List<string> issues)
    {
        var state = states.FirstOrDefault(s => s.name == stateName);
        if (state == null) { issues.Add(stateName + " state missing."); return; }
        var clip = state.motion as AnimationClip;
        if (!clip) { issues.Add(stateName + " has no AnimationClip motion."); return; }
        string actual = clip.name;
        int separator = actual.LastIndexOf('|');
        if (separator >= 0) actual = actual.Substring(separator + 1);
        if (!string.Equals(actual, actionName, StringComparison.OrdinalIgnoreCase))
            issues.Add($"{stateName} uses {actual}, expected {actionName}.");
    }
}
