using Finik.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Finik.Editor
{
    public static class CatSt1DressRepairSetup
    {
        const string Original = "Assets/Characters/Cat/St1/Models/Cat.fbx";
        const string Repaired = "Assets/Characters/Cat/St1/Models/Cat_DressRepaired.fbx";

        [InitializeOnLoadMethod]
        static void Schedule() => EditorApplication.delayCall += ApplyToOpenScene;

        [MenuItem("Finik/Characters/Apply Cat St1 Dress Repair")]
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
                    Debug.LogError($"[CatSt1DressRepair] Bone count mismatch: {corrected.bindposes.Length} vs {renderer.bones.Length}.");
                    continue;
                }
                Undo.RecordObject(renderer, "Apply Cat St1 dress repair");
                renderer.sharedMesh = corrected;
                EditorUtility.SetDirty(renderer);
                changed = true;
            }
            if (!changed) return;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[CatSt1DressRepair] Repaired dress mesh assigned to Cat St1.");
        }
    }
}
