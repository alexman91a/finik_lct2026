using UnityEngine;

namespace Finik.Navigation
{
    /// <summary>
    /// Post-animation leg fix for the Stage 1 rig. Its leg bones are not aligned with the leg mesh:
    /// a visually straight leg has ~38° between thigh and shin bones. The clips were retargeted as
    /// if that were bent, so they over-straighten knees (the leg looks broken backwards) and twist
    /// the feet outward or into the floor.
    ///
    /// For every grounded foot this re-solves the leg with two-bone IK that never straightens past
    /// the rest angle and always bends forward, stands the sole on the surface below (rug, floor),
    /// restores the rest foot pitch/toe-out and the rest toe bend. Rest values are read from the
    /// mesh bind poses, so nothing is hard-coded per model. Airborne feet keep the clip's motion.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class FinikFootGrounding : MonoBehaviour
    {
        sealed class Leg
        {
            public Transform upLeg, leg, foot, toe;
            public float straightKneeAngle;   // thigh→shin angle of a visually straight leg
            public float ankleHeight;         // ankle above the sole, per unit of body scale
            public float solePitch;           // ankle→toe pitch with the sole flat (deg, <0 = down)
            public float toeOut;              // rest toe-out (deg, outward positive)
            public Quaternion toeLocalRest;
            public Quaternion upLegLocalRest, legLocalRest, footLocalRest;
            public Vector3 plantedAnkle;
            public float plantWeight;
            public bool planted;
        }

        [SerializeField] SkinnedMeshRenderer body;
        [SerializeField] LayerMask groundMask = ~0;
        [Tooltip("Extra gap between sole and ground, metres.")]
        [SerializeField] float soleClearance = 0.004f;
        [SerializeField, Range(0f, 20f)] float preferredToeOut = 7f;
        [SerializeField, Range(0f, 12f)] float toeOutTolerance = 3.5f;
        [Tooltip("Foot lift (metres above its grounded height) over which the correction fades out.")]
        [SerializeField] Vector2 groundedBlend = new(0.03f, 0.10f);
        [SerializeField, Range(0f, 1f)] float weight = 1f;
        [Tooltip("How fast the body follows the ground height and the leg-reach correction (1/s).")]
        [SerializeField] float pelvisSharpness = 10f;

        Leg left, right;
        Transform model;          // top of the character visual, moved up/down as a whole
        Vector3 modelBaseLocal;
        float pelvisOffset;       // metres, applied to the model this frame

        float profileToeOut;
        float profileToeOutTolerance;
        float profileStanceOutset;
        float profileKneeOutset;
        bool profileEnabled;
        bool levelWalkingFeet;
        bool profileForceFlatFeet;
        bool profileStaticHeight;
        float profileAnkleHeightWorld;
        float profileFootPitchOffset;
        Vector3 plantedRootPosition;
        Quaternion plantedRootRotation;
        bool plantedRoot;

        void Awake()
        {
            ResetProfile();
            RefreshBindings();
        }

        void OnEnable() => RefreshBindings();

        /// <summary>Applies small corrective offsets for a character source with a known bad bind pose.</summary>
        public void ConfigureCharacter(string id, int stage)
        {
            ResetProfile();

            if (id == "cat" && stage == 2)
            {
                // Replacement Cat St2 has a taller ankle-to-sole offset than the legacy mesh.
                // Use the measured sole distance so the whole character actually reaches the rug.
                profileAnkleHeightWorld = 0.158f;
                profileStaticHeight = true;
                return;
            }

            if (id != "fox") return;

            switch (stage)
            {
                case 1:
                    profileToeOut = 4f;
                    profileToeOutTolerance = 2f;
                    levelWalkingFeet = false;
                    profileForceFlatFeet = false;
                    // The replacement FBX retargets with both shoes pitched upward.
                    // A -20° local-X correction makes heel and toe contact the same plane.
                    profileFootPitchOffset = -17f;
                    profileAnkleHeightWorld = 0.116f;
                    profileStaticHeight = true;
                    break;
                case 2:
                    // St2's source rig has a narrow stance. Keep its validated foot rotations,
                    // but enforce a small lateral safety gap so the shoes never overlap/cross.
                    profileStanceOutset = 0.05f;
                    profileKneeOutset = 0.03f;
                    profileEnabled = false;
                    break;
                case 3:
                    profileToeOut = 4f;
                    profileToeOutTolerance = 1.5f;
                    break;
            }
        }

        void ResetProfile()
        {
            profileToeOut = preferredToeOut;
            profileToeOutTolerance = toeOutTolerance;
            profileStanceOutset = 0f;
            profileKneeOutset = 0f;
            profileEnabled = true;
            levelWalkingFeet = false;
            profileForceFlatFeet = false;
            profileStaticHeight = false;
            profileAnkleHeightWorld = -1f;
            profileFootPitchOffset = 0f;
        }

        /// <summary>Rebuilds bind-pose data for the currently visible character.</summary>
        public void RefreshBindings()
        {
            if (model) model.localPosition = modelBaseLocal;
            pelvisOffset = 0f;
            plantedRoot = false;

            body = FindBody();
            if (!body)
            {
                left = right = null;
                return;
            }

            left = Build("Left");
            right = Build("Right");
            if (left == null || right == null) return;

            model = body.transform;
            while (model.parent && model.parent != transform) model = model.parent;
            modelBaseLocal = model.localPosition;
        }

        /// <summary>Reads the rest (bind) pose of one leg from the mesh bind poses, in world orientation.</summary>
        Leg Build(string side)
        {
            int up = Index(side + "UpLeg"), kn = Index(side + "Leg"), ft = Index(side + "Foot"), to = Index(side + "ToeBase");
            int hipL = Index("LeftUpLeg"), hipR = Index("RightUpLeg");
            if (up < 0 || kn < 0 || ft < 0 || to < 0 || hipL < 0 || hipR < 0) return null;
            int pelvis = System.Array.IndexOf(body.bones, body.bones[up].parent);
            if (pelvis < 0) return null;
            var mesh = body.sharedMesh;
            var bindposes = mesh.bindposes;
            Matrix4x4 toWorld = body.transform.localToWorldMatrix;
            Matrix4x4 W(int i) => toWorld * bindposes[i].inverse;
            Vector3 P(int i) => W(i).MultiplyPoint3x4(Vector3.zero);

            Vector3 hip = P(up), knee = P(kn), ankle = P(ft), toe = P(to);
            // Renderer bounds include tails and clothing, so their minimum is not the shoe sole.
            float ankleToSole = Mathf.Clamp(ankle.y - toe.y + 0.04f, 0.04f, 0.16f);
            Vector3 foot = toe - ankle;
            Vector3 flat = new Vector3(foot.x, 0f, foot.z);
            Vector3 across = P(hipR) - P(hipL);
            across.y = 0f;
            Vector3 forward = Vector3.Cross(across.normalized, Vector3.up);
            float yaw = Vector3.SignedAngle(forward, flat, Vector3.up);
            return new Leg
            {
                upLeg = body.bones[up], leg = body.bones[kn], foot = body.bones[ft], toe = body.bones[to],
                straightKneeAngle = Vector3.Angle(knee - hip, ankle - knee),
                ankleHeight = ankleToSole / Mathf.Max(1e-4f, Mathf.Abs(body.transform.lossyScale.y)),
                solePitch = -Vector3.Angle(flat, foot),
                toeOut = side == "Left" ? -yaw : yaw,
                toeLocalRest = Quaternion.Inverse(W(ft).rotation) * W(to).rotation,
                upLegLocalRest = Quaternion.Inverse(W(pelvis).rotation) * W(up).rotation,
                legLocalRest = Quaternion.Inverse(W(up).rotation) * W(kn).rotation,
                footLocalRest = Quaternion.Inverse(W(kn).rotation) * W(ft).rotation
            };
        }

        int Index(string suffix)
        {
            var bones = body.bones;
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] && (bones[i].name == suffix || bones[i].name.EndsWith(":" + suffix)))
                    return i;
            return -1;
        }

        SkinnedMeshRenderer FindBody()
        {
            foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (HasBone(renderer, "LeftUpLeg") && HasBone(renderer, "RightUpLeg") &&
                    HasBone(renderer, "LeftFoot") && HasBone(renderer, "RightFoot"))
                    return renderer;
            }
            return null;
        }

        static bool HasBone(SkinnedMeshRenderer renderer, string suffix)
        {
            foreach (var bone in renderer.bones)
                if (bone && (bone.name == suffix || bone.name.EndsWith(":" + suffix)))
                    return true;
            return false;
        }

        void LateUpdate()
        {
            if (!body || !body.gameObject.activeInHierarchy) RefreshBindings();
            if (left == null || right == null) return;
            if (profileStanceOutset > 0f) ApplyStanceOutset();
            CorrectIdleHeight();
            var movement = GetComponent<FinikMovementController>();
            var activity = GetComponent<FinikActivityController>();
            bool standing = (!movement || movement.State == FinikState.Idle) && (!activity || !activity.IsBusy);

            if (profileStaticHeight)
            {
                if (Mathf.Abs(profileFootPitchOffset) > 0.01f)
                {
                    ApplyFixedFootPitch(left);
                    ApplyFixedFootPitch(right);
                }
                ApplyIdleFootLocks(standing);
                return;
            }

            if (!profileEnabled)
            {
                ApplyIdleFootLocks(standing);
                return;
            }
            // This correction is a standing-pose fix. Applying it over a retargeted
            // walk cycle distorts the stride, most visibly on Finik Stage 2.
            if (movement && movement.State != FinikState.Idle)
            {
                ApplyIdleFootLocks(false);
                if (levelWalkingFeet)
                {
                    LevelWalkingFoot(left);
                    LevelWalkingFoot(right);
                }
                return;
            }
            if (weight <= 0f) { ApplyIdleFootLocks(false); return; }
            Vector3 across = right.upLeg.position - left.upLeg.position;
            across.y = 0f;
            if (across.sqrMagnitude < 1e-8f) return;
            Vector3 forward = Vector3.Cross(across.normalized, Vector3.up);
            float scale = body.transform.lossyScale.y;

            // Keep the clip's leg motion; only the grounded foot orientation is corrected here.
            // Correct the planted foot orientation after the body height has been solved.
            Solve(left, forward, +1f, scale);
            Solve(right, forward, -1f, scale);
            ApplyIdleFootLocks(standing);
        }

        void ApplyIdleFootLocks(bool standing)
        {
            if (!standing || weight <= 0f)
            {
                plantedRoot = false;
                left.planted = right.planted = false;
                left.plantWeight = right.plantWeight = 0f;
                return;
            }
            Vector3 rootDelta = transform.position - plantedRootPosition;
            rootDelta.y = 0f;
            if (!plantedRoot || rootDelta.sqrMagnitude > 0.12f * 0.12f ||
                Quaternion.Angle(transform.rotation, plantedRootRotation) > 15f)
            {
                left.planted = right.planted = false;
                plantedRootPosition = transform.position;
                plantedRootRotation = transform.rotation;
                plantedRoot = true;
            }
            PlantFoot(left);
            PlantFoot(right);
        }

        void PlantFoot(Leg limb)
        {
            float scale = body.transform.lossyScale.y;
            float ankleHeight = profileAnkleHeightWorld > 0f ? profileAnkleHeightWorld : limb.ankleHeight * scale;
            float ground = GroundY(limb.foot.position);
            Vector3 drift = limb.foot.position - limb.plantedAnkle;
            drift.y = 0f;
            if (!limb.planted || drift.sqrMagnitude > 0.22f * 0.22f * scale * scale)
            {
                limb.plantedAnkle = limb.foot.position;
                limb.planted = true;
                limb.plantWeight = 0f;
            }
            limb.plantedAnkle.y = GroundY(limb.plantedAnkle) + soleClearance + ankleHeight;
            limb.plantWeight = Mathf.MoveTowards(limb.plantWeight, weight, Time.deltaTime * 5f);
            Vector3 target = Vector3.Lerp(limb.foot.position, limb.plantedAnkle, limb.plantWeight);
            if (target.y < ground + soleClearance + ankleHeight)
                target.y = ground + soleClearance + ankleHeight;
            PositionFootWithLeg(limb, target);
        }

        void PositionFootWithLeg(Leg limb, Vector3 target)
        {
            Vector3 hip = limb.upLeg.position, knee = limb.leg.position, ankle = limb.foot.position;
            float upper = Vector3.Distance(hip, knee), lower = Vector3.Distance(knee, ankle);
            Vector3 toTarget = target - hip;
            if (upper < 1e-5f || lower < 1e-5f || toTarget.sqrMagnitude < 1e-8f) return;

            float maxReach = Mathf.Max(Vector3.Distance(hip, ankle), Mathf.Sqrt(upper * upper + lower * lower +
                2f * upper * lower * Mathf.Cos(Mathf.Max(10f, limb.straightKneeAngle) * Mathf.Deg2Rad)));
            float distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upper - lower) + 1e-4f, maxReach);
            Vector3 direction = toTarget.normalized;
            Vector3 pole = Vector3.ProjectOnPlane(knee - hip, direction);
            if (pole.sqrMagnitude < 1e-8f) pole = Vector3.ProjectOnPlane(transform.forward, direction);
            if (pole.sqrMagnitude < 1e-8f) return;
            pole.Normalize();

            float cosine = Mathf.Clamp((upper * upper + distance * distance - lower * lower) /
                (2f * upper * distance), -1f, 1f);
            Vector3 desiredKnee = hip + direction * (upper * cosine) + pole * (upper * Mathf.Sqrt(1f - cosine * cosine));
            Vector3 desiredAnkle = hip + direction * distance;
            Quaternion footRotation = limb.foot.rotation;
            Quaternion toeRotation = limb.toe.rotation;

            limb.upLeg.rotation = Quaternion.FromToRotation(knee - hip, desiredKnee - hip) * limb.upLeg.rotation;
            Vector3 kneeNow = limb.leg.position, ankleNow = limb.foot.position;
            if ((ankleNow - kneeNow).sqrMagnitude > 1e-8f && (desiredAnkle - kneeNow).sqrMagnitude > 1e-8f)
                limb.leg.rotation = Quaternion.FromToRotation(ankleNow - kneeNow, desiredAnkle - kneeNow) * limb.leg.rotation;
            limb.foot.rotation = footRotation;
            limb.toe.rotation = toeRotation;
        }

        void CorrectIdleHeight()
        {
            var movement = GetComponent<FinikMovementController>();
            var activity = GetComponent<FinikActivityController>();
            bool idle = (!movement || movement.State == FinikState.Idle) && (!activity || !activity.IsBusy);
            float target = 0f;
            if (idle)
            {
                float scale = body.transform.lossyScale.y;
                float leftHeight = profileAnkleHeightWorld > 0f ? profileAnkleHeightWorld : left.ankleHeight * scale;
                float rightHeight = profileAnkleHeightWorld > 0f ? profileAnkleHeightWorld : right.ankleHeight * scale;
                float leftError = GroundY(left.foot.position) + soleClearance + leftHeight - left.foot.position.y;
                float rightError = GroundY(right.foot.position) + soleClearance + rightHeight - right.foot.position.y;
                target = Mathf.Clamp(pelvisOffset + Mathf.Max(leftError, rightError), -0.35f, 0.12f);
            }
            float blend = 1f - Mathf.Exp(-pelvisSharpness * Time.deltaTime);
            pelvisOffset = Mathf.Lerp(pelvisOffset, target, blend);
            float parentY = model.parent ? model.parent.lossyScale.y : 1f;
            model.localPosition = modelBaseLocal + Vector3.up * (weight * pelvisOffset / Mathf.Max(1e-4f, parentY));
        }

        void ApplyStanceOutset()
        {
            Vector3 across = right.upLeg.position - left.upLeg.position;
            across.y = 0f;
            if (across.sqrMagnitude < 1e-8f) return;
            across.Normalize();

            Vector3 center = (left.upLeg.position + right.upLeg.position) * 0.5f;
            float scale = Mathf.Max(0.001f, (Mathf.Abs(body.transform.lossyScale.x) + Mathf.Abs(body.transform.lossyScale.z)) * 0.5f);
            float halfHipWidth = Mathf.Abs(Vector3.Dot(right.upLeg.position - center, across));
            float minHalfStance = halfHipWidth + profileStanceOutset * scale;
            float minHalfKnee = halfHipWidth + profileKneeOutset * scale;

            KeepLegOnSide(left, center, across, -1f, minHalfStance, minHalfKnee);
            KeepLegOnSide(right, center, across, +1f, minHalfStance, minHalfKnee);
        }

        static void KeepLegOnSide(Leg limb, Vector3 center, Vector3 across, float side,
            float minHalfStance, float minHalfKnee)
        {
            if (!limb.upLeg || !limb.leg || !limb.foot) return;

            Vector3 hip = limb.upLeg.position;
            Vector3 knee = limb.leg.position;
            Vector3 ankle = limb.foot.position;
            float ankleLateral = Vector3.Dot(ankle - center, across);
            float kneeLateral = Vector3.Dot(knee - center, across);
            bool fixAnkle = side * ankleLateral < minHalfStance;
            bool fixKnee = side * kneeLateral < minHalfKnee;
            if (!fixAnkle && !fixKnee) return;

            Vector3 target = fixAnkle
                ? ankle + across * (side * minHalfStance - ankleLateral)
                : ankle;
            float upperLength = Vector3.Distance(hip, knee);
            float lowerLength = Vector3.Distance(knee, ankle);
            Vector3 toTarget = target - hip;
            if (upperLength < 1e-5f || lowerLength < 1e-5f || toTarget.sqrMagnitude < 1e-8f) return;

            float distance = Mathf.Clamp(toTarget.magnitude,
                Mathf.Abs(upperLength - lowerLength) + 1e-4f,
                upperLength + lowerLength - 1e-4f);
            Vector3 direction = toTarget.normalized;
            Vector3 pole = Vector3.ProjectOnPlane(knee - hip, direction);
            if (pole.sqrMagnitude < 1e-8f)
                pole = Vector3.ProjectOnPlane(across * side, direction);
            if (pole.sqrMagnitude < 1e-8f)
                pole = Vector3.ProjectOnPlane(Vector3.forward, direction);
            pole.Normalize();

            float cosAngle = Mathf.Clamp(
                (upperLength * upperLength + distance * distance - lowerLength * lowerLength) /
                (2f * upperLength * distance), -1f, 1f);
            float sinAngle = Mathf.Sqrt(Mathf.Max(0f, 1f - cosAngle * cosAngle));
            Vector3 desiredKnee = hip + direction * (upperLength * cosAngle) + pole * (upperLength * sinAngle);

            float desiredKneeLateral = Vector3.Dot(desiredKnee - center, across);
            if (side * desiredKneeLateral < minHalfKnee)
            {
                desiredKnee += across * (side * minHalfKnee - desiredKneeLateral);
                Vector3 fromHip = desiredKnee - hip;
                if (fromHip.sqrMagnitude > 1e-8f)
                    desiredKnee = hip + fromHip.normalized * upperLength;
            }

            Vector3 desiredAnkle = hip + direction * distance;
            Quaternion footRotation = limb.foot.rotation;
            Quaternion toeRotation = limb.toe ? limb.toe.rotation : Quaternion.identity;

            limb.upLeg.rotation = Quaternion.FromToRotation(knee - hip, desiredKnee - hip) * limb.upLeg.rotation;
            Vector3 kneeNow = limb.leg.position;
            Vector3 ankleNow = limb.foot.position;
            if ((ankleNow - kneeNow).sqrMagnitude > 1e-8f && (desiredAnkle - kneeNow).sqrMagnitude > 1e-8f)
                limb.leg.rotation = Quaternion.FromToRotation(ankleNow - kneeNow, desiredAnkle - kneeNow) * limb.leg.rotation;

            limb.foot.rotation = footRotation;
            if (limb.toe) limb.toe.rotation = toeRotation;
        }

        void ApplyFixedFootPitch(Leg limb)
        {
            if (limb == null || !limb.foot) return;
            limb.foot.localRotation =
                limb.foot.localRotation * Quaternion.AngleAxis(profileFootPitchOffset, Vector3.right);
        }

        void LevelWalkingFoot(Leg limb)
        {
            if (!limb.foot || !limb.toe) return;
            float ground = GroundY(limb.foot.position);
            // Do not flatten a foot in the air; only remove the raised-toe bind-pose bias near contact.
            if (limb.foot.position.y - ground > 0.16f * body.transform.lossyScale.y) return;
            Vector3 direction = limb.toe.position - limb.foot.position;
            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (flat.sqrMagnitude < 1e-6f) return;
            if (profileForceFlatFeet)
            {
                Quaternion flatten = Quaternion.FromToRotation(direction.normalized, flat.normalized);
                limb.foot.rotation = flatten * limb.foot.rotation;
                return;
            }

            float pitch = Mathf.Atan2(direction.y, flat.magnitude) * Mathf.Rad2Deg;
            float correction = Mathf.Clamp(pitch, -20f, 35f);
            Vector3 hinge = Vector3.Cross(Vector3.up, flat).normalized;
            limb.foot.rotation = Quaternion.AngleAxis(correction, hinge) * limb.foot.rotation;
        }

        void OnDisable()
        {
            if (model) model.localPosition = modelBaseLocal;
            pelvisOffset = 0f;
            plantedRoot = false;
        }

        float GroundY(Vector3 above)
        {
            var hits = Physics.RaycastAll(above + Vector3.up * 0.6f, Vector3.down, 2f, groundMask, QueryTriggerInteraction.Ignore);
            float best = transform.position.y;
            float bestDistance = float.MaxValue;
            foreach (var hit in hits)
            {
                // Skip the character's own colliders (tap targets, accessories).
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.distance < bestDistance) { bestDistance = hit.distance; best = hit.point.y; }
            }
            return best;
        }

        /// <summary>
        /// Solves one leg. Returns how much longer (m, may be negative) the leg would need to be to
        /// reach its grounded target when straight, or -∞ if the foot is not planted.
        /// </summary>
        float Solve(Leg leg, Vector3 forward, float outwardSign, float scale)
        {
            Vector3 hip = leg.upLeg.position, knee = leg.leg.position, ankle = leg.foot.position;
            float ankleHeight = profileAnkleHeightWorld > 0f
                ? profileAnkleHeightWorld
                : leg.ankleHeight * scale;
            float restAnkleY = GroundY(ankle) + soleClearance + ankleHeight;

            float lift = ankle.y - restAnkleY;
            float grounded = 1f - Mathf.InverseLerp(groundedBlend.x, groundedBlend.y, lift);
            float w = weight * Mathf.SmoothStep(0f, 1f, grounded);
            if (w <= 0.001f) return float.NegativeInfinity;

            // Keep the clip's thigh and shin pose. Different source rigs have incompatible
            // bind-pose knee angles, so a shared two-bone IK bends otherwise straight legs.
            // Only the foot orientation is corrected below.

            // Toes back to their rest bend, then the foot to a neutral pitch and toe-out.
            leg.toe.localRotation = Quaternion.Slerp(leg.toe.localRotation, leg.toeLocalRest, w);
            Vector3 footDir = leg.toe.position - leg.foot.position;
            Vector3 flat = new Vector3(footDir.x, 0f, footDir.z);
            if (flat.sqrMagnitude < 1e-8f) flat = forward;
            float yaw = Vector3.SignedAngle(forward, flat, Vector3.up);
            float outward = -outwardSign * yaw;
            // Imported variants have incompatible rest-foot yaw. Preserve a subtle
            // outward stance instead of inheriting toe-in or extreme toe-out angles.
            float clampedOutward = Mathf.Clamp(
                outward,
                profileToeOut - profileToeOutTolerance,
                profileToeOut + profileToeOutTolerance);
            Vector3 heading = Quaternion.AngleAxis(-outwardSign * clampedOutward, Vector3.up) * forward;
            Vector3 right = Vector3.Cross(Vector3.up, heading);
            Vector3 desired = profileForceFlatFeet
                ? heading
                : Quaternion.AngleAxis(-leg.solePitch, right) * heading;
            Quaternion fix = Quaternion.FromToRotation(footDir.normalized, desired.normalized);
            leg.foot.rotation = Quaternion.Slerp(Quaternion.identity, fix, w) * leg.foot.rotation;
            return float.NegativeInfinity;
        }
    }
}
