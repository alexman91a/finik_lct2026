using Finik.Navigation;
using UnityEngine;

namespace Finik.UI.Onboarding
{
    /// <summary>
    /// Takes the room camera over for onboarding: a close-up of Finik that leaves room for panels
    /// (Finik on the right in landscape, in the upper half in portrait). <see cref="Release"/> flies
    /// back to the gameplay pose and hands control back to <see cref="FinikRoomCameraController"/>.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class FinikShowcaseCamera : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] float lookHeight = 1.05f;
        [SerializeField] float landscapeDistance = 4.1f;
        [SerializeField] float portraitDistance = 5.2f;
        [SerializeField] float cameraHeight = 1.5f;
        [Tooltip("Camera-space shift that pushes Finik aside: x < 0 moves him right on screen, y < 0 moves him up.")]
        [SerializeField] Vector2 landscapeShift = new(-1.2f, 0.1f);
        [SerializeField] Vector2 portraitShift = new(0f, -1.25f);
        [SerializeField] float landscapeFov = 28.5f;
        [Tooltip("Tall screens need a wider vertical angle to keep Finik large without leaving the room.")]
        [SerializeField] float portraitFov = 50f;
        [SerializeField] float blendSeconds = 1.1f;

        Camera cam;
        FinikRoomCameraController room;
        Vector3 releasePosition;
        Quaternion releaseRotation;
        float releaseFov;
        bool releasePhysical;
        float releaseVerticalFov;
        Vector3 releaseFromPosition;
        Quaternion releaseFromRotation;
        float releaseFromFov;
        Vector3 viewDirection;
        bool active;
        bool releasing;
        float blend;
        System.Action onReleased;

        public bool IsActive => active;

        void Awake()
        {
            cam = GetComponent<Camera>();
            room = GetComponent<FinikRoomCameraController>();
            if (!target) target = GameObject.Find("Finik_Root")?.transform;
        }

        /// <summary>Start the close-up. Finik is turned to face the gameplay camera.</summary>
        public void Engage()
        {
            if (!target) return;
            // One menu can take over from another while the close-up is already running. Re-reading the
            // pose here would capture the close-up itself as the pose to fly back to, and hand the room
            // camera a spot right in front of Finik's face on release — so only the first engage records it.
            if (!active)
            {
                releasePosition = transform.position;
                releaseRotation = transform.rotation;
                releaseFov = cam.fieldOfView;
                // The room camera is a physical camera with horizontal gate fit, which inflates the vertical
                // angle on tall screens. The close-up works in plain vertical FOV and restores it on release.
                releasePhysical = cam.usePhysicalProperties;
            }
            float effective = EffectiveVerticalFov(cam, cam.fieldOfView);
            cam.usePhysicalProperties = false;
            cam.fieldOfView = effective;
            Vector3 toCamera = transform.position - target.position;
            toCamera.y = 0f;
            viewDirection = toCamera.sqrMagnitude > 0.001f ? toCamera.normalized : Vector3.forward;
            target.rotation = Quaternion.LookRotation(viewDirection, Vector3.up);
            if (room) room.enabled = false;
            // Taking over cancels a fly-back in progress. Whoever was waiting for it has to be let go,
            // or its screen would sit half-closed for ever.
            var interrupted = releasing ? onReleased : null;
            onReleased = null;
            active = true;
            releasing = false;
            blend = 0f;
            interrupted?.Invoke();
        }

        public void Release(System.Action done = null)
        {
            if (!active)
            {
                done?.Invoke();
                return;
            }
            onReleased = done;
            // Start from exactly where the camera is now: recomputing the close-up pose here would
            // jump if Finik moved (e.g. his celebration dance) since the camera last converged.
            releaseFromPosition = transform.position;
            releaseFromRotation = transform.rotation;
            releaseFromFov = cam.fieldOfView;
            // Fly to the pose the room camera will hold right now (not the raw scene pose captured at
            // start, which the controller had not framed yet), so the hand-off has no jump.
            if (room) room.GetRestPose(out releasePosition, out releaseRotation, out releaseFov);
            bool wasPhysical = cam.usePhysicalProperties;
            cam.usePhysicalProperties = releasePhysical;
            releaseVerticalFov = EffectiveVerticalFov(cam, releaseFov);
            cam.usePhysicalProperties = wasPhysical;
            releasing = true;
            blend = 0f;
        }

        void LateUpdate()
        {
            if (!active || !target) return;
            float dt = Time.unscaledDeltaTime;
            blend = Mathf.Min(1f, blend + dt / blendSeconds);
            float k = FinikUiMotion.EaseOutCubic(blend);

            if (releasing)
            {
                // Finik keeps moving during the fly-back (he is celebrating), so re-read the pose the
                // room camera will hold. Aiming at a pose captured once meant landing beside him and
                // then sliding sideways after the hand-off — two separate movements instead of one.
                if (room)
                {
                    room.GetRestPose(out releasePosition, out releaseRotation, out releaseFov);
                    bool physical = cam.usePhysicalProperties;
                    cam.usePhysicalProperties = releasePhysical;
                    releaseVerticalFov = EffectiveVerticalFov(cam, releaseFov);
                    cam.usePhysicalProperties = physical;
                }
                transform.position = Vector3.Lerp(releaseFromPosition, releasePosition, k);
                transform.rotation = Quaternion.Slerp(releaseFromRotation, releaseRotation, k);
                cam.fieldOfView = Mathf.Lerp(releaseFromFov, releaseVerticalFov, k);
                if (blend >= 1f)
                {
                    active = false;
                    releasing = false;
                    cam.usePhysicalProperties = releasePhysical;
                    cam.fieldOfView = releaseFov;
                    transform.SetPositionAndRotation(releasePosition, releaseRotation);
                    if (room)
                    {
                        room.SyncToCurrentPose();
                        room.enabled = true;
                    }
                    var done = onReleased;
                    onReleased = null;
                    done?.Invoke();
                }
                return;
            }

            ShowcasePose(out Vector3 position, out Quaternion rotation);
            float fov = Screen.width < Screen.height ? portraitFov : landscapeFov;
            // Exponential approach: smooth fly-in, then tracks orientation changes without snapping.
            float follow = 1f - Mathf.Exp(-5f * dt);
            transform.position = Vector3.Lerp(transform.position, position, blend < 1f ? Mathf.Max(k, follow) : follow);
            transform.rotation = Quaternion.Slerp(transform.rotation, rotation, blend < 1f ? Mathf.Max(k, follow) : follow);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, follow);
        }

        /// <summary>Vertical FOV actually seen on screen for a camera's fieldOfView, honouring gate fit.</summary>
        static float EffectiveVerticalFov(Camera camera, float fieldOfView)
        {
            if (!camera.usePhysicalProperties) return fieldOfView;
            float sensorAspect = camera.sensorSize.x / Mathf.Max(0.001f, camera.sensorSize.y);
            float screenAspect = Mathf.Max(0.001f, camera.aspect);
            bool fitHorizontal = camera.gateFit switch
            {
                Camera.GateFitMode.Horizontal => true,
                Camera.GateFitMode.Fill => screenAspect > sensorAspect,
                Camera.GateFitMode.Overscan => screenAspect < sensorAspect,
                _ => false
            };
            if (!fitHorizontal) return fieldOfView;
            float halfTan = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad) * sensorAspect / screenAspect;
            return 2f * Mathf.Atan(halfTan) * Mathf.Rad2Deg;
        }

        void ShowcasePose(out Vector3 position, out Quaternion rotation)
        {
            bool portrait = Screen.width < Screen.height;
            float distance = portrait ? portraitDistance : landscapeDistance;
            Vector3 focus = target.position + Vector3.up * lookHeight;
            Vector3 eye = target.position + viewDirection * distance + Vector3.up * cameraHeight;
            rotation = Quaternion.LookRotation(focus - eye, Vector3.up);
            Vector2 shift = portrait ? portraitShift : landscapeShift;
            position = eye + rotation * new Vector3(shift.x, shift.y, 0f);
        }
    }
}
