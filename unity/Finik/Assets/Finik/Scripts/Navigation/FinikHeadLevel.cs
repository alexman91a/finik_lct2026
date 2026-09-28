using UnityEngine;

namespace Finik.Navigation
{
    /// <summary>
    /// Keeps every character looking towards the gameplay camera after animation has evaluated.
    /// The correction is shared between neck and head, preserving the source clip while avoiding
    /// a visible kink at the base of the head.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public sealed class FinikHeadLevel : MonoBehaviour
    {
        [SerializeField] Transform neck;
        [SerializeField] Transform head;
        [Tooltip("Child of the head pointing out of the face (Stage 1: 'headfront').")]
        [SerializeField] Transform faceMarker;
        [Tooltip("Degrees added to the clip's face pitch (positive = look up).")]
        [SerializeField] float pitchBias = 22f;
        [SerializeField] Vector2 pitchRange = new(-22f, 14f);
        [Tooltip("Small artistic offset above the camera target, in degrees.")]
        [SerializeField] float cameraPitchOffset = 2f;
        [Tooltip("Maximum pitch correction applied on top of the animation.")]
        [SerializeField, Min(0f)] float maxPitchCorrection = 55f;
        [Tooltip("Maximum horizontal turn towards the camera. Keeps locomotion readable.")]
        [SerializeField, Min(0f)] float maxYawCorrection = 50f;
        [SerializeField, Range(0f, 1f)] float neckShare = 0.35f;
        [SerializeField, Range(0f, 1f)] float weight = 1f;

        float profilePitchBias;
        float profilePitchMax;
        float profileMaxPitchCorrection;
        Camera gameplayCamera;

        void Awake()
        {
            ResetProfile();
            RefreshBindings();
        }

        void OnEnable() => RefreshBindings();

        /// <summary>Uses a stronger neutral pose only for Finik Stage 1's downward-facing rig.</summary>
        public void ConfigureCharacter(string id, int stage)
        {
            ResetProfile();
            if (id == "fox" && stage == 1)
            {
                profilePitchBias = 36f;
                profilePitchMax = 26f;
                profileMaxPitchCorrection = 85f;
            }
            else if (id == "fox" && stage == 2)
            {
                // Meshy's native walking_2_inplace keeps the face roughly 25-30 degrees down.
                // Lift St2 to a near-horizontal gaze while preserving small nods.
                profilePitchBias = 0f;
                profilePitchMax = 20f;
                profileMaxPitchCorrection = 85f;
            }
            else if (id == "fox") profileMaxPitchCorrection = 75f;
        }

        void ResetProfile()
        {
            profilePitchBias = pitchBias;
            profilePitchMax = pitchRange.y;
            profileMaxPitchCorrection = maxPitchCorrection;
        }

        /// <summary>Finds the head chain on the currently visible character.</summary>
        public void RefreshBindings()
        {
            var animator = GetComponentInChildren<Animator>();
            var root = animator ? animator.transform : transform;

            // Bone names differ between the imported character sources. Humanoid
            // mappings are stable across all variants and take precedence.
            neck = animator && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.Neck)
                : Deep(root, "Neck");
            head = animator && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.Head)
                : Deep(root, "Head");
            faceMarker = Deep(root, "headfront");
            gameplayCamera = Camera.main;
        }

        static Transform Deep(Transform root, string suffix)
        {
            if (root.name == suffix || root.name.EndsWith(":" + suffix)) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = Deep(root.GetChild(i), suffix);
                if (found) return found;
            }
            return null;
        }

        void LateUpdate()
        {
            if (!head || !head.gameObject.activeInHierarchy) RefreshBindings();
            if (!neck || !head) return;
            if (weight <= 0f) return;
            // Some imported rigs do not include the optional headfront marker.
            // The head bone's forward axis is a stable fallback for those variants.
            Vector3 face = faceMarker ? faceMarker.position - head.position : head.forward;
            if (face.sqrMagnitude < 1e-6f) return;

            if (!gameplayCamera || !gameplayCamera.isActiveAndEnabled)
                gameplayCamera = Camera.main;

            Vector3 target = gameplayCamera
                ? gameplayCamera.transform.position - head.position
                : Vector3.zero;

            // In previews without a gameplay camera, keep the previous constant-bias behaviour.
            if (target.sqrMagnitude < 1e-6f)
            {
                float currentPitch = Pitch(face);
                float desiredPitch = Mathf.Clamp(currentPitch + profilePitchBias, pitchRange.x, profilePitchMax);
                ApplyPitch(desiredPitch - currentPitch, face);
                return;
            }

            Vector3 flatFace = Vector3.ProjectOnPlane(face, Vector3.up);
            Vector3 flatTarget = Vector3.ProjectOnPlane(target, Vector3.up);
            float yawDelta = 0f;
            if (flatFace.sqrMagnitude > 1e-6f && flatTarget.sqrMagnitude > 1e-6f)
                yawDelta = Mathf.Clamp(
                    Vector3.SignedAngle(flatFace, flatTarget, Vector3.up),
                    -maxYawCorrection,
                    maxYawCorrection);

            float pitchDelta = Mathf.Clamp(
                Pitch(target) + cameraPitchOffset - Pitch(face),
                -profileMaxPitchCorrection,
                profileMaxPitchCorrection);

            ApplyYaw(yawDelta);
            // Re-read the face axis after yaw so the pitch hinge stays perpendicular to it.
            face = faceMarker ? faceMarker.position - head.position : head.forward;
            ApplyPitch(pitchDelta, face);
        }

        static float Pitch(Vector3 direction)
        {
            float horizontal = new Vector2(direction.x, direction.z).magnitude;
            return Mathf.Atan2(direction.y, horizontal) * Mathf.Rad2Deg;
        }

        void ApplyYaw(float delta)
        {
            delta *= weight;
            if (Mathf.Abs(delta) < 0.01f) return;
            neck.rotation = Quaternion.AngleAxis(delta * neckShare, Vector3.up) * neck.rotation;
            head.rotation = Quaternion.AngleAxis(delta * (1f - neckShare), Vector3.up) * head.rotation;
        }

        void ApplyPitch(float delta, Vector3 face)
        {
            delta *= weight;
            if (Mathf.Abs(delta) < 0.01f) return;
            Vector3 flatFace = Vector3.ProjectOnPlane(face, Vector3.up);
            if (flatFace.sqrMagnitude < 1e-6f) return;
            // Positive rotation about the right/across axis tips the face down, hence the minus.
            Vector3 across = Vector3.Cross(Vector3.up, flatFace).normalized;
            neck.rotation = Quaternion.AngleAxis(-delta * neckShare, across) * neck.rotation;
            head.rotation = Quaternion.AngleAxis(-delta * (1f - neckShare), across) * head.rotation;
        }
    }
}
