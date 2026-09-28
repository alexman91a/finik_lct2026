using Finik.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Finik.Editor
{
    public static class FinikSt1SkinRepairSetup
    {
        const string Original = "Assets/Characters/Finik/St1/Models/Finik.fbx";
        const string Repaired = "Assets/Characters/Finik/St1/Models/Finik_SkinRepaired.fbx";

        [InitializeOnLoadMethod]
        static void Schedule()
        {
            EditorApplication.delayCall += ApplyToOpenScene;
        }

        [MenuItem("Finik/Characters/Apply St1 Skin Repair")]
        public static void ApplyToOpenScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.path.EndsWith("FinikRoomNavigationPrototype.unity")) return;
            var root = GameObject.Find("Finik_Root");
            if (!root || !root.GetComponent<FinikCharacterSwitcher>()) return;

            Mesh corrected = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(Repaired))
                if (asset is Mesh mesh && (corrected == null || mesh.vertexCount > corrected.vertexCount)) corrected = mesh;
            if (!corrected) return;

            bool changed = false;
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!renderer.sharedMesh || AssetDatabase.GetAssetPath(renderer.sharedMesh) != Original) continue;
                if (corrected.bindposes.Length != renderer.bones.Length)
                {
                    Debug.LogError($"[FinikSt1SkinRepair] Bone count mismatch: {corrected.bindposes.Length} vs {renderer.bones.Length}.");
                    continue;
                }
                Undo.RecordObject(renderer, "Apply Finik St1 skin repair");
                renderer.sharedMesh = corrected;
                EditorUtility.SetDirty(renderer);
                changed = true;
            }
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[FinikSt1SkinRepair] Repaired mesh assigned to St1 without changing the rig or materials.");
            }
        }
    }
}
