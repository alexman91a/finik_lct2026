using System;
using Finik.DebugTools;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Finik.Navigation
{
    [RequireComponent(typeof(FinikMovementController))]
    public sealed class FinikInputController : MonoBehaviour
    {
        [SerializeField] Camera gameplayCamera;
        [SerializeField] LayerMask interactionMask;
        [SerializeField] LayerMask groundMask;
        [SerializeField] float navMeshSnapRadius=.35f;
        [SerializeField, Min(1f)] float tapThresholdPixels = 12f;
        FinikMovementController movement;
        FinikActivityController activity;
        FinikInteractionController interactionController;
        FinikRoomCameraController roomCameraController;
        Vector2 pressPosition;
        bool pressStartedOverUi;
        void Awake()
        {
            movement=GetComponent<FinikMovementController>();
            activity=GetComponent<FinikActivityController>();
            interactionController=GetComponent<FinikInteractionController>();
            if(!gameplayCamera) gameplayCamera=Camera.main;
            if(gameplayCamera) roomCameraController=gameplayCamera.GetComponent<FinikRoomCameraController>();
        }
        void Update()
        {
            var pointer=Pointer.current; if(pointer==null) return;
            if(pointer.press.wasPressedThisFrame) { pressPosition=pointer.position.ReadValue(); pressStartedOverUi=IsPointerOverUi(); }
            if(!pointer.press.wasReleasedThisFrame || pressStartedOverUi) return;
            if(IsPointerOverUi()) return;
            if((pointer.position.ReadValue()-pressPosition).sqrMagnitude>tapThresholdPixels*tapThresholdPixels) return;
            if(roomCameraController && roomCameraController.WasPointerDraggedThisPress) return;

            Ray ray=gameplayCamera.ScreenPointToRay(pointer.position.ReadValue());
            if(TryInteraction(ray)) return;
            interactionController?.CancelPendingInteraction();
            if(!TryResolveGroundPoint(ray,out Vector3 groundPoint)) return;
            if(!NavMesh.SamplePosition(groundPoint,out NavMeshHit navHit,navMeshSnapRadius,NavMesh.AllAreas)) return;
            if (movement.TrySetUserDestination(navHit.position))
                activity?.InterruptForUserMovement();
        }

        bool TryInteraction(Ray ray)
        {
            RaycastHit[] hits=Physics.RaycastAll(ray,100f,~0,QueryTriggerInteraction.Collide);
            if(hits.Length==0) return false;
            Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
            foreach(var hit in hits)
            {
                var target=hit.collider.GetComponentInParent<FinikInteractionTarget>();
                if(target)
                {
                    interactionController?.RequestInteraction(target);
                    return true;
                }

                bool reservedInteractionLayer = interactionMask.value != 0 &&
                    (interactionMask.value & (1 << hit.collider.gameObject.layer)) != 0;
                if(reservedInteractionLayer) return true;
            }
            return false;
        }

        bool TryResolveGroundPoint(Ray ray, out Vector3 point)
        {
            if(Physics.Raycast(ray,out RaycastHit hit,100f,groundMask,QueryTriggerInteraction.Ignore))
            {
                point=hit.point;
                return true;
            }

            Plane floorPlane=new Plane(Vector3.up,new Vector3(0f,transform.position.y,0f));
            if(floorPlane.Raycast(ray,out float enter) && enter>0f)
            {
                point=ray.GetPoint(enter);
                return true;
            }

            point=default;
            return false;
        }

        static bool IsPointerOverUi()
        {
            var pointer = Pointer.current;
            if (pointer != null && FinikDemoCharacterPanel.BlocksWorldPointer(pointer.position.ReadValue()))
                return true;
            if(EventSystem.current==null) return false;
            if(Touchscreen.current!=null && Touchscreen.current.primaryTouch.press.isPressed)
                return EventSystem.current.IsPointerOverGameObject(Touchscreen.current.primaryTouch.touchId.ReadValue());
            return EventSystem.current.IsPointerOverGameObject();
        }
    }
}
