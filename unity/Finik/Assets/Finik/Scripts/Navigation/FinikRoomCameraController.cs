using Finik.DebugTools;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Finik.Navigation
{
    [RequireComponent(typeof(Camera))]
    public sealed class FinikRoomCameraController : MonoBehaviour
    {
        public enum CameraMode { LandscapeOverview, PortraitFollow, PortraitManualPan, ReturningToFollow }

        [Header("References")]
        [SerializeField] Transform finik;
        [SerializeField] BoxCollider cameraBounds;
        [Header("Framing")]
        [SerializeField, Range(20f, 70f)] float landscapeFov = 34f;
        [Tooltip("Vertical field of view the portrait screen actually renders. Match it to the landscape view so portrait is a vertical crop of the same room, not a closer shot.")]
        [SerializeField, Range(20f, 70f)] float portraitFov = 30.5f;
        [Tooltip("Ultra-wide screens zoom in to this reference aspect instead of exposing the technical area beyond the room walls.")]
        [SerializeField, Min(1f)] float maximumOverviewAspect = 1.777778f;
        [Tooltip("Screens narrower than this reference aspect zoom in as well. With horizontal gate fit a narrow screen stretches the view vertically, which would show past the floor edge and above the walls.")]
        [SerializeField, Min(1f)] float minimumOverviewAspect = 1.777778f;
        [SerializeField, Min(0f)] float landscapeDistance = 0f;
        [Tooltip("0 keeps the landscape distance, so portrait frames the same depth of room.")]
        [SerializeField, Min(0f)] float portraitDistance = 0f;
        [SerializeField, Range(-15f, 25f)] float portraitPitchOffset = 0f;
        [SerializeField, Range(-0.4f, 0.4f)] float landscapeFinikScreenOffset = 0f;
        [SerializeField, Range(-0.4f, 0.4f)] float portraitFinikScreenOffset = 0f;
        [SerializeField] float optionalYOffset = 0f;
        [Header("Portrait follow and touch")]
        [SerializeField, Min(0.01f)] float followSmoothing = 0.32f;
        [SerializeField, Min(0.01f)] float dragSensitivity = 2.2f;
        [SerializeField, Min(0f)] float followResumeDelay = 1.75f;
        [SerializeField, Min(0f)] float followDeadZone = 0.25f;
        [SerializeField, Min(1f)] float dragThresholdPixels = 12f;
        [SerializeField, Range(1f, 2.4f)] float portraitAspectThreshold = 1.05f;
        [SerializeField] bool drawGizmos = true;

        Camera roomCamera;
        bool usesPhysicalCamera;
        Vector3 basePosition;
        Vector3 baseForward;
        Quaternion baseRotation;
        float baseDistance;
        float spawnX;
        float cameraX;
        float velocityX;
        float manualX;
        float lastDragAt;
        Vector2 pressStart;
        Vector2 lastPointer;
        bool pointerHeld;
        bool pressStartedOverUi;
        bool draggedThisPress;
        CameraMode mode;

        public CameraMode Mode => mode;
        public bool WasPointerDraggedThisPress => draggedThisPress;
        public bool IsPortrait => Screen.width < Screen.height || (float)Screen.width / Mathf.Max(1, Screen.height) < portraitAspectThreshold;

        void Awake()
        {
            roomCamera = GetComponent<Camera>();
            usesPhysicalCamera = roomCamera.usePhysicalProperties;
            basePosition = transform.position;
            baseForward = transform.forward;
            baseRotation = transform.rotation;
            if (!finik) finik = GameObject.Find("Finik_Root")?.transform;
            if (!cameraBounds) cameraBounds = GameObject.Find("CameraBounds")?.GetComponent<BoxCollider>();
            baseDistance = finik ? Vector3.Distance(basePosition, finik.position + Vector3.up * 0.65f) : 12f;
            spawnX = finik ? finik.position.x : 0f;
            cameraX = basePosition.x;
            manualX = cameraX;
            mode = CameraMode.LandscapeOverview;
        }

        void Update()
        {
            ReadPointer();
            if (!IsPortrait)
            {
                // A narrow landscape crop can hide either the backpack or sofa. Let the player
                // browse horizontally, while ClampCameraX still keeps the camera within the room.
                if (mode != CameraMode.PortraitManualPan) mode = CameraMode.LandscapeOverview;
                return;
            }
            if (mode == CameraMode.LandscapeOverview) mode = CameraMode.PortraitFollow;
            if (mode == CameraMode.PortraitManualPan && !pointerHeld)
            {
                if (Time.unscaledTime - lastDragAt >= followResumeDelay || FinikMoving()) mode = CameraMode.ReturningToFollow;
            }
        }

        Vector3 ReferencePosition(bool portrait)
        {
            float distance = portrait ? portraitDistance : landscapeDistance;
            float actualDistance = distance > 0f ? distance : baseDistance * (portrait ? 0.94f : 1f);
            Vector3 distanceOffset = -baseForward * (actualDistance - baseDistance);
            return basePosition + distanceOffset + Vector3.up * (portrait ? optionalYOffset : 0f);
        }

        /// <summary>
        /// The settled gameplay pose for the current orientation and Finik position (no smoothing),
        /// so another camera driver can fly back to exactly where this controller will take over.
        /// </summary>
        public void GetRestPose(out Vector3 position, out Quaternion rotation, out float fieldOfView)
        {
            bool portrait = IsPortrait;
            fieldOfView = CameraFieldOfView(portrait);
            Vector3 referencePosition = ReferencePosition(portrait);
            float x = referencePosition.x;
            if (portrait && finik) x += finik.position.x - (spawnX + portraitFinikScreenOffset);
            else if (!portrait) x += landscapeFinikScreenOffset;
            x = ClampCameraX(x, portrait);
            position = referencePosition + Vector3.right * (x - referencePosition.x);
            rotation = portrait ? baseRotation * Quaternion.Euler(portraitPitchOffset, 0f, 0f) : baseRotation;
        }

        /// <summary>Adopt the camera's current pose as the follow state, e.g. after an external fly-in.</summary>
        public void SyncToCurrentPose()
        {
            cameraX = transform.position.x;
            manualX = cameraX;
            velocityX = 0f;
            mode = IsPortrait ? CameraMode.PortraitFollow : CameraMode.LandscapeOverview;
        }

        void LateUpdate()
        {
            bool portrait = IsPortrait;
            float fov = CameraFieldOfView(portrait);
            Vector3 referencePosition = ReferencePosition(portrait);

            // Translation along world X preserves the locked camera rotation and room perspective.
            float desiredX = referencePosition.x;
            if (mode == CameraMode.PortraitManualPan)
            {
                desiredX = manualX;
            }
            else if (portrait && finik)
            {
                float followed = referencePosition.x + finik.position.x - (spawnX + portraitFinikScreenOffset);
                if (Mathf.Abs(followed - cameraX) > followDeadZone) desiredX = followed;
                else desiredX = cameraX;
            }
            else desiredX += landscapeFinikScreenOffset;

            desiredX = ClampCameraX(desiredX, portrait);
            if (!portrait && mode != CameraMode.PortraitManualPan) cameraX = desiredX;
            else cameraX = Mathf.SmoothDamp(cameraX, desiredX, ref velocityX, followSmoothing, Mathf.Infinity, Time.unscaledDeltaTime);
            cameraX = ClampCameraX(cameraX, portrait);
            transform.position = referencePosition + Vector3.right * (cameraX - referencePosition.x);
            Quaternion desiredRotation = portrait ? baseRotation * Quaternion.Euler(portraitPitchOffset, 0f, 0f) : baseRotation;
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
            roomCamera.fieldOfView = Mathf.Lerp(roomCamera.fieldOfView, fov, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
            if (!portrait) mode = CameraMode.LandscapeOverview;
            else if (mode == CameraMode.ReturningToFollow && Mathf.Abs(cameraX - desiredX) < 0.04f) mode = CameraMode.PortraitFollow;
        }

        /// <summary>
        /// True when the projection keeps the horizontal field of view fixed and lets the
        /// vertical one follow the screen aspect. In that mode a narrow screen shows *more*
        /// vertically, which is what can reveal the technical area around the room.
        /// </summary>
        bool FitsHorizontally(float screenAspect)
        {
            if (!roomCamera || !usesPhysicalCamera) return false;
            float sensorAspect = roomCamera.sensorSize.x / Mathf.Max(0.001f, roomCamera.sensorSize.y);
            return roomCamera.gateFit switch
            {
                Camera.GateFitMode.Horizontal => true,
                Camera.GateFitMode.Fill => screenAspect > sensorAspect,
                Camera.GateFitMode.Overscan => screenAspect < sensorAspect,
                _ => false
            };
        }

        float CameraFieldOfView(bool portrait)
        {
            float target = portrait ? portraitFov : landscapeFov;
            if (!roomCamera) return target;

            float screenAspect = Mathf.Max(0.001f, roomCamera.aspect);
            float sensorAspect = roomCamera.sensorSize.x / Mathf.Max(0.001f, roomCamera.sensorSize.y);
            bool fitHorizontal = FitsHorizontally(screenAspect);

            if (!portrait)
            {
                float halfTan = Mathf.Tan(target * 0.5f * Mathf.Deg2Rad);
                if (screenAspect > maximumOverviewAspect)
                {
                    target = 2f * Mathf.Atan(halfTan * maximumOverviewAspect / screenAspect) * Mathf.Rad2Deg;
                }
                else if (fitHorizontal && screenAspect < minimumOverviewAspect)
                {
                    // Tablets and unfolded foldables are landscape but narrower than 16:9.
                    // Holding the horizontal framing there would stretch the view past the
                    // floor edge and above the walls, so zoom in to keep the reference
                    // vertical framing of a 16:9 screen instead.
                    target = 2f * Mathf.Atan(halfTan * screenAspect / minimumOverviewAspect) * Mathf.Rad2Deg;
                }
                return target;
            }

            if (!fitHorizontal) return target;

            float portraitHalfTan = Mathf.Tan(target * 0.5f * Mathf.Deg2Rad) * screenAspect / sensorAspect;
            return 2f * Mathf.Atan(portraitHalfTan) * Mathf.Rad2Deg;
        }

        void ReadPointer()
        {
            Pointer pointer = Pointer.current;
            if (pointer == null) return;
            Vector2 position = pointer.position.ReadValue();
            if (pointer.press.wasPressedThisFrame)
            {
                pointerHeld = true;
                pressStart = lastPointer = position;
                draggedThisPress = false;
                pressStartedOverUi = IsPointerOverUi();
            }
            if (pointerHeld && pointer.press.isPressed)
            {
                if (!pressStartedOverUi && !IsPointerOverUi() && (position - pressStart).sqrMagnitude >= dragThresholdPixels * dragThresholdPixels)
                {
                    if (!draggedThisPress) manualX = cameraX;
                    draggedThisPress = true;
                    mode = CameraMode.PortraitManualPan;
                    // Dragging left moves the camera to the right, and dragging right moves it left.
                    float zoomFactor = Mathf.Clamp(portraitFov / Mathf.Max(1f, roomCamera.fieldOfView), 1f, 2f);
                    manualX = ClampCameraX(manualX + (position.x - lastPointer.x) / Mathf.Max(1, Screen.width) * dragSensitivity * 1.6f * zoomFactor, true);
                    lastDragAt = Time.unscaledTime;
                }
                lastPointer = position;
            }
            if (pointer.press.wasReleasedThisFrame)
            {
                pointerHeld = false;
                if (draggedThisPress) lastDragAt = Time.unscaledTime;
            }
        }

        bool FinikMoving()
        {
            var agent = finik ? finik.GetComponent<UnityEngine.AI.NavMeshAgent>() : null;
            return agent && agent.velocity.sqrMagnitude > 0.09f;
        }

        bool IsPointerOverUi()
        {
            var pointer = Pointer.current;
            if (pointer != null && FinikDemoCharacterPanel.BlocksWorldPointer(pointer.position.ReadValue()))
                return true;
            if (!EventSystem.current) return false;
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
                return EventSystem.current.IsPointerOverGameObject(Touchscreen.current.primaryTouch.touchId.ReadValue());
            return EventSystem.current.IsPointerOverGameObject();
        }

        float ClampCameraX(float value, bool portrait)
        {
            if (!cameraBounds) return Mathf.Clamp(value, basePosition.x - 1.4f, basePosition.x + 1.4f);
            Bounds bounds = cameraBounds.bounds;
            float margin = Mathf.Min(1.6f, bounds.extents.x * 0.45f);
            float minimum = basePosition.x + bounds.min.x + margin;
            float maximum = basePosition.x + bounds.max.x - margin;
            return minimum <= maximum ? Mathf.Clamp(value, minimum, maximum) : basePosition.x;
        }

        void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            Camera previewCamera = GetComponent<Camera>();
            if (previewCamera)
            {
                Gizmos.color = Color.red;
                Gizmos.matrix = Matrix4x4.TRS(Application.isPlaying ? basePosition : transform.position,
                    Application.isPlaying ? baseRotation : transform.rotation, Vector3.one);
                Gizmos.DrawFrustum(Vector3.zero, landscapeFov, 12f, previewCamera.nearClipPlane, 16f / 9f);
                Gizmos.matrix = Matrix4x4.identity;
            }
            Gizmos.color = Color.cyan;
            if (cameraBounds) Gizmos.DrawWireCube(cameraBounds.bounds.center, cameraBounds.bounds.size);
            if (finik)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(finik.position + Vector3.up * 0.8f, new Vector3(followDeadZone * 2f, 1.6f, 0.05f));
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, finik.position + Vector3.up * 0.8f);
            }
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position + transform.forward * 5f, 0.12f);
        }
    }
}
