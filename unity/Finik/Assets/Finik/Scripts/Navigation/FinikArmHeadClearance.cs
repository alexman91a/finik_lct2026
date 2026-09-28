using UnityEngine;

namespace Finik.Navigation
{
    /// <summary>
    /// Keeps celebratory hand poses from clipping through the face after humanoid retargeting.
    /// It is reactive: locomotion and gestures outside the head clearance remain untouched.
    /// </summary>
    [DefaultExecutionOrder(120)]
    public sealed class FinikArmHeadClearance : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] float weight = 0.75f;
        [SerializeField, Range(0.45f, 1.2f)] float clearanceByShoulderWidth = 0.72f;

        Animator animator;
        Transform head, neck, leftShoulder, rightShoulder, leftUpperArm, rightUpperArm;
        Transform leftForeArm, rightForeArm, leftHand, rightHand;
        float profileClearance;
        float headCenterOffset;
        float profileSafetyMultiplier = 1.08f;
        float profileHeadHalfWidth;
        float profileHeadHalfHeight;
        float profileHeadHalfDepth;
        bool largeHeadProfile;
        bool raccoonHeadProfile;
        bool measuredHeadProfile;
        bool bypassArmClearance;
        float profileHandMargin;

        public void ConfigureCharacter(string id, int stage)
        {
            bool foxStage1 = id == "fox" && stage == 1;
            bool raccoonStage1 = id == "raccoon" && stage == 1;
            bool catStage2 = id == "cat" && stage == 2;

            // Finik St1 and Raccoon St1 use their authored humanoid arm animations
            // without procedural head-clearance corrections. Those corrections distort
            // the gesture/idle arm poses on both characters.
            bypassArmClearance = foxStage1 || raccoonStage1;
            measuredHeadProfile = catStage2;
            largeHeadProfile = catStage2;
            raccoonHeadProfile = false;
            profileClearance = foxStage1 ? 1.4f : clearanceByShoulderWidth;
            headCenterOffset = raccoonStage1 ? 2.15f : 0f;
            profileSafetyMultiplier = raccoonStage1 ? 1.10f : 1.03f;

            // World-space silhouette sizes measured from the replacement meshes.
            // These avoid using shoulder width for stylised characters whose heads are
            // several times wider than their shoulders.
            if (foxStage1)
            {
                // Use the central face/cheek volume rather than the full head bounds
                // (which include the huge ears). This keeps the idle pose natural.
                profileHeadHalfWidth = 0.360f;
                profileHeadHalfHeight = 0.320f;
                profileHeadHalfDepth = 0.280f;
                profileHandMargin = 0.065f;
            }
            else if (catStage2)
            {
                profileHeadHalfWidth = 0.220f;
                profileHeadHalfHeight = 0.240f;
                profileHeadHalfDepth = 0.200f;
                profileHandMargin = 0.035f;
            }
            else
            {
                profileHeadHalfWidth = raccoonStage1 ? 0.43f : 0f;
                profileHeadHalfHeight = raccoonStage1 ? 0.40f : 0f;
                profileHeadHalfDepth = raccoonStage1 ? 0.33f : 0f;
                profileHandMargin = 0f;
            }
            RefreshBindings();
        }

        void OnEnable()
        {
            if (profileClearance <= 0f) profileClearance = clearanceByShoulderWidth;
            RefreshBindings();
        }

        public void RefreshBindings()
        {
            animator = GetComponentInChildren<Animator>();
            if (!animator || !animator.isHuman)
            {
                animator = null;
                head = neck = leftShoulder = rightShoulder = leftUpperArm = rightUpperArm = null;
                leftForeArm = rightForeArm = leftHand = rightHand = null;
                return;
            }

            head = animator.GetBoneTransform(HumanBodyBones.Head);
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            leftShoulder = animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
            rightShoulder = animator.GetBoneTransform(HumanBodyBones.RightShoulder);
            leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            leftForeArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            rightForeArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        }

        void LateUpdate()
        {
            if (bypassArmClearance) return;

            if (!head || !head.gameObject.activeInHierarchy) RefreshBindings();
            if (!head || !leftShoulder || !rightShoulder) return;

            // Preserve authored locomotion/neutral-idle arm poses. The clearance correction is
            // only needed for gesture states that can raise a paw beside the oversized head.
            if (measuredHeadProfile && animator)
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                bool neutralOrLocomotion =
                    state.IsName("Idle_Base") ||
                    state.IsName("Idle3") ||
                    state.IsName("Idle12") ||
                    state.IsName("Idle4") ||
                    state.IsName("Idle9") ||
                    state.IsName("LookAround") ||
                    state.IsName("TurnLeft") ||
                    state.IsName("TurnRight") ||
                    state.IsName("Walk_Autonomous") ||
                    state.IsName("Walk_User") ||
                    state.IsName("Run_User");
                if (neutralOrLocomotion) return;
            }

            float shoulderWidth = Vector3.Distance(leftShoulder.position, rightShoulder.position);
            float clearance = shoulderWidth * profileClearance;
            if (clearance <= 0.001f) return;

            Vector3 center = largeHeadProfile && neck
                ? head.position + (head.position - neck.position) * headCenterOffset
                : head.position;
            if (measuredHeadProfile)
            {
                // Replacement fox/cat meshes have heads far wider than their shoulder spacing.
                // Use the measured world-space head silhouette and include the paw radius.
                for (int pass = 0; pass < 3; pass++)
                {
                    KeepMeasuredArmOutsideHead(leftUpperArm, leftForeArm, leftShoulder, leftHand, -1f, center);
                    KeepMeasuredArmOutsideHead(rightUpperArm, rightForeArm, rightShoulder, rightHand, +1f, center);
                }
            }
            else if (raccoonHeadProfile)
            {
                // The raccoon's head is much wider than its shoulders, so a shoulder-width sphere
                // underestimates the real silhouette. Use an ellipsoid measured from the source mesh.
                for (int pass = 0; pass < 3; pass++)
                {
                    KeepRaccoonArmOutsideHead(leftUpperArm, leftForeArm, leftShoulder, leftHand, -1f, center);
                    KeepRaccoonArmOutsideHead(rightUpperArm, rightForeArm, rightShoulder, rightHand, +1f, center);
                }
            }
            else if (largeHeadProfile)
            {
                // Two passes are intentional: rotating the upper arm changes the wrist/forearm
                // position, so a second pass removes residual clipping on very large heads.
                for (int pass = 0; pass < 2; pass++)
                {
                    KeepLargeHeadArmClear(leftUpperArm, leftForeArm, leftShoulder, leftHand, -1f, center, clearance);
                    KeepLargeHeadArmClear(rightUpperArm, rightForeArm, rightShoulder, rightHand, +1f, center, clearance);
                }
            }
            else
            {
                KeepClear(leftUpperArm, leftShoulder, leftHand, -1f, clearance);
                KeepClear(rightUpperArm, rightShoulder, rightHand, +1f, clearance);
            }
        }

        void KeepMeasuredArmOutsideHead(Transform upperArm, Transform foreArm, Transform shoulder,
            Transform hand, float side, Vector3 center)
        {
            if (!upperArm || !foreArm || !shoulder || !hand || !head) return;

            Vector3 lateralAxis = rightShoulder.position - leftShoulder.position;
            lateralAxis.y = 0f;
            if (lateralAxis.sqrMagnitude < 1e-8f) return;
            lateralAxis.Normalize();
            Vector3 lateral = lateralAxis * side;
            Vector3 forward = Vector3.Cross(lateralAxis, Vector3.up).normalized;

            float rx = profileHeadHalfWidth * profileSafetyMultiplier;
            float ry = profileHeadHalfHeight * profileSafetyMultiplier;
            float rz = profileHeadHalfDepth * profileSafetyMultiplier;
            float margin = profileHandMargin;

            Vector3 worst = hand.position;
            float penetration = 0f;
            // Only the distal half of the forearm can visually enter the face.
            // Sampling from the elbow made the whole arm swing outward in idle poses.
            for (int i = 0; i <= 3; i++)
            {
                float t = 0.55f + i * 0.15f;
                Vector3 p = Vector3.Lerp(foreArm.position, hand.position, t);
                Vector3 d = p - center;
                float y = Vector3.Dot(d, Vector3.up);
                float z = Vector3.Dot(d, forward);
                float yz = (y * y) / Mathf.Max(1e-8f, ry * ry) +
                           (z * z) / Mathf.Max(1e-8f, rz * rz);
                if (yz >= 1f) continue;

                float requiredLateral = rx * Mathf.Sqrt(1f - yz) + margin;
                float outward = Vector3.Dot(d, lateral);
                float depth = requiredLateral - outward;
                if (depth > penetration)
                {
                    penetration = depth;
                    worst = p;
                }
            }

            if (penetration > 0f)
            {
                // Keep the upper arm from being pulled into an unnatural T-like pose.
                // Correct only at the elbow so the paw/forearm moves away from the face.
                Vector3 wristTarget = hand.position + lateral * penetration;
                Vector3 current = hand.position - foreArm.position;
                Vector3 desired = wristTarget - foreArm.position;
                if (current.sqrMagnitude > 1e-6f && desired.sqrMagnitude > 1e-6f)
                    foreArm.rotation = Quaternion.FromToRotation(current, desired) * foreArm.rotation;
            }

            Vector3 wrist = hand.position - center;
            float wristY = Vector3.Dot(wrist, Vector3.up);
            float wristZ = Vector3.Dot(wrist, forward);
            float wristYZ = (wristY * wristY) / Mathf.Max(1e-8f, ry * ry) +
                            (wristZ * wristZ) / Mathf.Max(1e-8f, rz * rz);

            if (wristYZ < 1f)
            {
                float required = rx * Mathf.Sqrt(1f - wristYZ) + margin;
                float outwardWrist = Vector3.Dot(wrist, lateral);
                if (outwardWrist < required)
                {
                    Vector3 wristTarget = hand.position + lateral * (required - outwardWrist);
                    Vector3 currentWrist = hand.position - foreArm.position;
                    Vector3 desiredWrist = wristTarget - foreArm.position;
                    if (currentWrist.sqrMagnitude > 1e-6f && desiredWrist.sqrMagnitude > 1e-6f)
                        foreArm.rotation = Quaternion.FromToRotation(currentWrist, desiredWrist) * foreArm.rotation;
                }
            }

            // Final wrist safety radius. AABB overlap can remain even when the wrist is
            // technically outside the face ellipsoid because the paw itself has volume.
            Vector3 radial = hand.position - center;
            float minDistance = rx + margin;
            if (radial.magnitude < minDistance)
            {
                Vector3 direction = radial.sqrMagnitude > 1e-6f
                    ? (radial.normalized + lateral * 0.25f).normalized
                    : lateral;
                Vector3 safeTarget = center + direction * minDistance;
                Vector3 current = hand.position - foreArm.position;
                Vector3 desired = safeTarget - foreArm.position;
                if (current.sqrMagnitude > 1e-6f && desired.sqrMagnitude > 1e-6f)
                    foreArm.rotation = Quaternion.FromToRotation(current, desired) * foreArm.rotation;

                // If the elbow alone cannot provide enough clearance, add only a small
                // upper-arm correction. The blend cap prevents the old T-like pose.
                radial = hand.position - center;
                if (radial.magnitude < minDistance)
                {
                    Vector3 fromShoulder = hand.position - shoulder.position;
                    Vector3 toShoulder = safeTarget - shoulder.position;
                    if (fromShoulder.sqrMagnitude > 1e-6f && toShoulder.sqrMagnitude > 1e-6f)
                    {
                        Quaternion delta = Quaternion.FromToRotation(fromShoulder, toShoulder);
                        float upperBlend = 0.35f;
                        if (animator)
                        {
                            var state = animator.GetCurrentAnimatorStateInfo(0);
                            if (state.IsName("Hello") || state.IsName("Celebrate"))
                                upperBlend = 0.80f;
                        }
                        upperArm.rotation = Quaternion.Slerp(
                            upperArm.rotation,
                            delta * upperArm.rotation,
                            upperBlend);
                    }
                }

                // Gesture clips intentionally raise the paw beside the face. If retargeting
                // still leaves it inside the head volume, place the paw beside the cheek
                // rather than pulling the whole arm into a T-pose.
                bool gestureState = false;
                if (animator)
                {
                    var state = animator.GetCurrentAnimatorStateInfo(0);
                    gestureState = state.IsName("Hello") || state.IsName("Celebrate");
                }

                radial = hand.position - center;
                if (gestureState && radial.magnitude < minDistance)
                {
                    float y = Mathf.Clamp(Vector3.Dot(radial, Vector3.up), -ry * 0.55f, ry * 0.85f);
                    float z = Mathf.Clamp(Vector3.Dot(radial, forward), -rz * 0.65f, rz * 0.65f);
                    Vector3 gestureTarget = center + lateral * minDistance + Vector3.up * y + forward * z;

                    Vector3 fromShoulder = hand.position - shoulder.position;
                    Vector3 toShoulder = gestureTarget - shoulder.position;
                    if (fromShoulder.sqrMagnitude > 1e-6f && toShoulder.sqrMagnitude > 1e-6f)
                        upperArm.rotation = Quaternion.FromToRotation(fromShoulder, toShoulder) * upperArm.rotation;

                    Vector3 fromElbow = hand.position - foreArm.position;
                    Vector3 toElbow = gestureTarget - foreArm.position;
                    if (fromElbow.sqrMagnitude > 1e-6f && toElbow.sqrMagnitude > 1e-6f)
                        foreArm.rotation = Quaternion.FromToRotation(fromElbow, toElbow) * foreArm.rotation;
                }
            }
        }

        void KeepRaccoonArmOutsideHead(Transform upperArm, Transform foreArm, Transform shoulder,
            Transform hand, float side, Vector3 center)
        {
            if (!upperArm || !foreArm || !shoulder || !hand || !head) return;

            Vector3 lateralAxis = rightShoulder.position - leftShoulder.position;
            lateralAxis.y = 0f;
            if (lateralAxis.sqrMagnitude < 1e-8f) return;
            lateralAxis.Normalize();
            Vector3 lateral = lateralAxis * side;
            Vector3 forward = Vector3.Cross(lateralAxis, Vector3.up).normalized;

            float modelScale = (Mathf.Abs(head.lossyScale.x) + Mathf.Abs(head.lossyScale.y) + Mathf.Abs(head.lossyScale.z)) / 3f;
            float rx = profileHeadHalfWidth * modelScale * profileSafetyMultiplier;
            float ry = profileHeadHalfHeight * modelScale * profileSafetyMultiplier;
            float rz = profileHeadHalfDepth * modelScale * profileSafetyMultiplier;
            // Keep the wrist outside by more than the paw radius, not merely outside the head mesh.
            float margin = 0.03f * modelScale;

            Vector3 worst = hand.position;
            float penetration = 0f;
            for (int i = 0; i <= 4; i++)
            {
                Vector3 p = Vector3.Lerp(foreArm.position, hand.position, i * 0.25f);
                float y = Vector3.Dot(p - center, Vector3.up);
                float z = Vector3.Dot(p - center, forward);
                float yz = (y * y) / Mathf.Max(1e-8f, ry * ry) + (z * z) / Mathf.Max(1e-8f, rz * rz);
                if (yz >= 1f) continue;

                float requiredLateral = rx * Mathf.Sqrt(1f - yz) + margin;
                float outward = Vector3.Dot(p - center, lateral);
                float depth = requiredLateral - outward;
                if (depth > penetration)
                {
                    penetration = depth;
                    worst = p;
                }
            }

            if (penetration > 0f)
            {
                Vector3 target = worst + lateral * penetration;
                Vector3 from = worst - shoulder.position;
                Vector3 to = target - shoulder.position;
                if (from.sqrMagnitude > 1e-6f && to.sqrMagnitude > 1e-6f)
                    upperArm.rotation = Quaternion.FromToRotation(from, to) * upperArm.rotation;
            }

            // A bent elbow can keep the wrist inside even after moving the whole arm.
            Vector3 wrist = hand.position - center;
            float wristY = Vector3.Dot(wrist, Vector3.up);
            float wristZ = Vector3.Dot(wrist, forward);
            float wristYZ = (wristY * wristY) / Mathf.Max(1e-8f, ry * ry) +
                            (wristZ * wristZ) / Mathf.Max(1e-8f, rz * rz);
            if (wristYZ >= 1f) return;

            float wristRequired = rx * Mathf.Sqrt(1f - wristYZ) + margin;
            float wristOutward = Vector3.Dot(wrist, lateral);
            if (wristOutward >= wristRequired) return;

            Vector3 wristTarget = hand.position + lateral * (wristRequired - wristOutward);
            Vector3 current = hand.position - foreArm.position;
            Vector3 desired = wristTarget - foreArm.position;
            if (current.sqrMagnitude > 1e-6f && desired.sqrMagnitude > 1e-6f)
                foreArm.rotation = Quaternion.FromToRotation(current, desired) * foreArm.rotation;
        }

        void KeepLargeHeadArmClear(Transform upperArm, Transform foreArm, Transform shoulder,
            Transform hand, float side, Vector3 center, float clearance)
        {
            if (!upperArm || !foreArm || !shoulder || !hand) return;

            Vector3 lateralAxis = (rightShoulder.position - leftShoulder.position).normalized;
            Vector3 lateral = lateralAxis * side;
            float safeClearance = clearance * profileSafetyMultiplier;

            // Large stylized heads can be intersected by the forearm even when the wrist itself is outside.
            // Check both arm segments and rotate the upper arm toward the character's natural side.
            Vector3 lowerClosest = ClosestPoint(foreArm.position, hand.position, center);
            Vector3 upperClosest = ClosestPoint(upperArm.position, foreArm.position, center);
            Vector3 closest = Vector3.SqrMagnitude(lowerClosest - center) < Vector3.SqrMagnitude(upperClosest - center)
                ? lowerClosest
                : upperClosest;

            if (Vector3.Distance(closest, center) < safeClearance)
            {
                float verticalOffset = Vector3.Dot(closest - center, Vector3.up);
                Vector3 target = center + lateral * safeClearance + Vector3.up * verticalOffset;
                Vector3 from = closest - shoulder.position;
                Vector3 to = target - shoulder.position;
                if (from.sqrMagnitude > 1e-6f && to.sqrMagnitude > 1e-6f)
                    upperArm.rotation = Quaternion.FromToRotation(from, to) * upperArm.rotation;
            }

            // Finish with a wrist correction so hands cannot sink into cheeks/temples during Celebrate.
            Vector3 wrist = hand.position - center;
            if (wrist.magnitude >= safeClearance) return;

            float wristVertical = Vector3.Dot(wrist, Vector3.up);
            Vector3 wristTarget = center + lateral * safeClearance + Vector3.up * wristVertical;
            Vector3 current = hand.position - foreArm.position;
            Vector3 desired = wristTarget - foreArm.position;
            if (current.sqrMagnitude > 1e-6f && desired.sqrMagnitude > 1e-6f)
                foreArm.rotation = Quaternion.FromToRotation(current, desired) * foreArm.rotation;
        }

        static Vector3 ClosestPoint(Vector3 start, Vector3 end, Vector3 point)
        {
            Vector3 segment = end - start;
            if (segment.sqrMagnitude < 1e-6f) return start;
            float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / segment.sqrMagnitude);
            return start + segment * t;
        }

        void KeepClear(Transform upperArm, Transform shoulder, Transform hand, float side, float clearance)
        {
            if (!upperArm || !shoulder || !hand) return;
            Vector3 fromHead = hand.position - head.position;
            float distance = fromHead.magnitude;
            if (distance >= clearance) return;

            // Always bias the escape vector toward the hand's natural side of the body.
            Vector3 lateral = (rightShoulder.position - leftShoulder.position).normalized * side;
            if (distance < 0.001f) fromHead = lateral;
            Vector3 direction = (fromHead.normalized + lateral * 0.55f).normalized;
            Vector3 target = head.position + direction * clearance;
            Vector3 currentArm = hand.position - shoulder.position;
            Vector3 desiredArm = target - shoulder.position;
            if (currentArm.sqrMagnitude < 1e-6f || desiredArm.sqrMagnitude < 1e-6f) return;

            Quaternion correction = Quaternion.FromToRotation(currentArm, desiredArm) * upperArm.rotation;
            upperArm.rotation = Quaternion.Slerp(upperArm.rotation, correction, profileClearance > clearanceByShoulderWidth ? 1f : weight);
        }
    }
}
