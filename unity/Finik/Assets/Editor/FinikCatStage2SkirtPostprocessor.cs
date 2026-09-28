using UnityEditor;
using UnityEngine;

public sealed class FinikCatStage2SkirtPostprocessor : AssetPostprocessor
{
    const string Target = "Assets/Characters/Cat/St2/Models/Cat.fbx";

    void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.Equals(Target, System.StringComparison.OrdinalIgnoreCase)) return;

        int fixedVertices = 0;
        foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = renderer.sharedMesh;
            if (!mesh) continue;
            // This repair was authored for the previous Cat ST2 mesh only.
            // The replacement rig has corrected weights and must pass through untouched.
            if (mesh.vertexCount != 100665) continue;
            var vertices = mesh.vertices;
            var weights = mesh.boneWeights;
            if (vertices.Length != weights.Length) continue;

            var bones = renderer.bones;
            for (int i = 0; i < weights.Length; i++)
            {
                Vector3 v = vertices[i];
                // Cat St2 skirt/hip shell occupies this central lower-torso band.
                if (Mathf.Abs(v.x) >= 0.17f || v.z <= 0.64f || v.z >= 1.04f) continue;

                BoneWeight bw = weights[i];
                float pelvis = WeightIfPelvis(bw.boneIndex0, bw.weight0, bones)
                             + WeightIfPelvis(bw.boneIndex1, bw.weight1, bones)
                             + WeightIfPelvis(bw.boneIndex2, bw.weight2, bones)
                             + WeightIfPelvis(bw.boneIndex3, bw.weight3, bones);
                if (pelvis < 0.08f) continue;

                bool changed = false;
                if (IsArm(bw.boneIndex0, bw.weight0, bones)) { bw.weight0 = 0f; changed = true; }
                if (IsArm(bw.boneIndex1, bw.weight1, bones)) { bw.weight1 = 0f; changed = true; }
                if (IsArm(bw.boneIndex2, bw.weight2, bones)) { bw.weight2 = 0f; changed = true; }
                if (IsArm(bw.boneIndex3, bw.weight3, bones)) { bw.weight3 = 0f; changed = true; }
                if (!changed) continue;

                bw = CompactAndNormalize(bw);
                if (bw.weight0 + bw.weight1 <= 0.00001f) continue;
                weights[i] = bw;
                fixedVertices++;
            }

            if (fixedVertices > 0)
                mesh.boneWeights = weights;
        }

        Debug.Log($"[FinikCatStage2SkirtPostprocessor] Removed hand/arm weights from {fixedVertices} Cat St2 skirt vertices.");
    }

    static BoneWeight CompactAndNormalize(BoneWeight bw)
    {
        int[] indices = { bw.boneIndex0, bw.boneIndex1, bw.boneIndex2, bw.boneIndex3 };
        float[] values = { bw.weight0, bw.weight1, bw.weight2, bw.weight3 };
        for (int a = 0; a < 3; a++)
            for (int b = a + 1; b < 4; b++)
                if (values[b] > values[a])
                {
                    float valueTmp = values[a];
                    values[a] = values[b];
                    values[b] = valueTmp;

                    int indexTmp = indices[a];
                    indices[a] = indices[b];
                    indices[b] = indexTmp;
                }

        float total = values[0] + values[1] + values[2] + values[3];
        if (total <= 0.00001f) return bw;
        return new BoneWeight
        {
            boneIndex0 = indices[0], weight0 = values[0] / total,
            boneIndex1 = indices[1], weight1 = values[1] / total,
            boneIndex2 = indices[2], weight2 = values[2] / total,
            boneIndex3 = indices[3], weight3 = values[3] / total
        };
    }

    static bool IsArm(int boneIndex, float weight, Transform[] bones)
    {
        return weight > 0f && boneIndex >= 0 && boneIndex < bones.Length && bones[boneIndex] &&
               IsArmBone(bones[boneIndex].name);
    }

    static float WeightIfPelvis(int boneIndex, float weight, Transform[] bones)
    {
        if (weight <= 0f || boneIndex < 0 || boneIndex >= bones.Length || !bones[boneIndex]) return 0f;
        string n = bones[boneIndex].name;
        return n.EndsWith("Hips") || n.Contains("Spine") || n.EndsWith("UpLeg") ? weight : 0f;
    }

    static bool IsArmBone(string n)
    {
        return n.Contains("Shoulder") || n.Contains("Arm") || n.Contains("Hand");
    }
}
