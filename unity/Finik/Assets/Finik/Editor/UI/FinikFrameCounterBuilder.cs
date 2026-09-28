using System.Linq;
using Tayx.Graphy;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Puts the Graphy overlay (com.tayx.graphy) into the room scene as <c>Graphy_Overlay</c>, switched
    /// off. <see cref="Finik.Core.FinikGraphy"/> activates it at runtime when «Для взрослых» asks for
    /// the frame counter, so the scene carries it but nothing is drawn or measured until then.
    ///
    /// The instance stays a prefab link into the package, so updating Graphy updates the overlay.
    /// </summary>
    public static class FinikFrameCounterBuilder
    {
        const string RootName = "Graphy_Overlay";
        const string PrefabPath = "Packages/com.tayx.graphy/Prefab/[Graphy].prefab";
        /// <summary>Above every Finik canvas (the room screens sit at 25, onboarding higher).</summary>
        const int SortingOrder = 200;
        /// <summary>
        /// Lifted clear of the bottom strip of the room, which the dock and the task-of-the-day card
        /// own. Graphy adds the offset to its own −32 baseline, so this lands the module at 400.
        /// </summary>
        static readonly Vector2 GraphOffset = new(0, 432);
        const float UiScale = 0.8f;

        [MenuItem("Finik/UI/Rebuild Frame Counter")]
        public static void RebuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab) return $"Graphy prefab is missing at {PrefabPath}: is com.tayx.graphy installed?";

            // Rebuilt in place: drop the previous instance so repeated runs cannot stack overlays.
            foreach (var stale in scene.GetRootGameObjects().Where(g => g.name == RootName))
                Object.DestroyImmediate(stale);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = RootName;

            var manager = instance.GetComponent<GraphyManager>();
            if (!manager)
            {
                Object.DestroyImmediate(instance);
                return "The Graphy prefab carries no GraphyManager: the package layout changed.";
            }

            var so = new SerializedObject(manager);
            // Never on by default, and never destroyed between scenes: the parent's switch owns it.
            so.FindProperty("m_enableOnStartup").boolValue = false;
            so.FindProperty("m_keepAlive").boolValue = false;
            // The switch is called «счётчик кадров», so that is all it shows: Graphy's RAM, audio and
            // device modules would cover the HUD and the dock with numbers nobody asked for.
            so.FindProperty("m_fpsModuleState").enumValueIndex = (int)GraphyManager.ModuleState.FULL;
            so.FindProperty("m_ramModuleState").enumValueIndex = (int)GraphyManager.ModuleState.OFF;
            so.FindProperty("m_audioModuleState").enumValueIndex = (int)GraphyManager.ModuleState.OFF;
            so.FindProperty("m_advancedModuleState").enumValueIndex = (int)GraphyManager.ModuleState.OFF;
            // Bottom right, clear of the top HUD pills and lifted above the dock.
            so.FindProperty("m_graphModulePosition").enumValueIndex = (int)GraphyManager.ModulePosition.BOTTOM_RIGHT;
            so.FindProperty("m_graphModuleOffset").vector2Value = GraphOffset;
            so.FindProperty("m_uiScale").floatValue = UiScale;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (instance.TryGetComponent<Canvas>(out var canvas))
            {
                canvas.overrideSorting = true;
                canvas.sortingOrder = SortingOrder;
            }

            instance.SetActive(false);

            EditorUtility.SetDirty(instance);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"Built {RootName}: счётчик кадров Graphy в сцене, выключен до переключателя в «Для взрослых».";
        }
    }
}
