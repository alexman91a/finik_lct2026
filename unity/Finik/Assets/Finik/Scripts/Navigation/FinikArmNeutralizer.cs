using System;
using UnityEngine;

namespace Finik.Navigation
{
    /// <summary>
    /// Stage 2 Finik imports with wrists twisted away from the body in its idle pose.
    /// Restore only the hand twist to the mesh bind pose after animation; arm motion is kept intact.
    /// </summary>
    [DefaultExecutionOrder(105)]
    public sealed class FinikArmNeutralizer : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] float weight = 0.8f;

        static readonly int MoveModeId = Animator.StringToHash("MoveMode");

        Animator animator;
        SkinnedMeshRenderer body;
        Transform leftHand, rightHand;
        Quaternion leftRest, rightRest;
        bool apply;

        public void ConfigureCharacter(string id, int stage)
        {
            apply = id == "fox" && stage == 2;
            RefreshBindings();
        }

        public void RefreshBindings()
        {
            animator = GetComponentInChildren<Animator>();
            body = FindBody();
            if (!body)
            {
                leftHand = rightHand = null;
                return;
            }

            leftHand = Bone("LeftHand");
            rightHand = Bone("RightHand");
            leftRest = BindLocalRotation(leftHand);
            rightRest = BindLocalRotation(rightHand);
        }

        void LateUpdate()
        {
            if (!apply) return;
            if (!body || !body.gameObject.activeInHierarchy) RefreshBindings();
            if (!leftHand || !rightHand) return;

            int moveMode = animator ? animator.GetInteger(MoveModeId) : 0;
            float appliedWeight = moveMode == 1 || moveMode == 2 ? 0f : weight;
            leftHand.localRotation = Quaternion.Slerp(leftHand.localRotation, leftRest, appliedWeight);
            rightHand.localRotation = Quaternion.Slerp(rightHand.localRotation, rightRest, appliedWeight);
        }

        Transform Bone(string suffix)
        {
            foreach (var bone in body.bones)
                if (bone && (bone.name == suffix || bone.name.EndsWith(":" + suffix)))
                    return bone;
            return null;
        }

        SkinnedMeshRenderer FindBody()
        {
            foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(false))
                if (HasBone(renderer, "LeftHand") && HasBone(renderer, "RightHand"))
                    return renderer;
            return null;
        }

        static bool HasBone(SkinnedMeshRenderer renderer, string suffix)
        {
            foreach (var bone in renderer.bones)
                if (bone && (bone.name == suffix || bone.name.EndsWith(":" + suffix)))
                    return true;
            return false;
        }

        Quaternion BindLocalRotation(Transform bone)
        {
            if (!bone) return Quaternion.identity;
            int boneIndex = Array.IndexOf(body.bones, bone);
            int parentIndex = Array.IndexOf(body.bones, bone.parent);
            if (boneIndex < 0 || parentIndex < 0) return bone.localRotation;

            var bindposes = body.sharedMesh.bindposes;
            Matrix4x4 toWorld = body.transform.localToWorldMatrix;
            Quaternion boneWorld = (toWorld * bindposes[boneIndex].inverse).rotation;
            Quaternion parentWorld = (toWorld * bindposes[parentIndex].inverse).rotation;
            return Quaternion.Inverse(parentWorld) * boneWorld;
        }
    }
}
