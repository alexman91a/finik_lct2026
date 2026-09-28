using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Finik.Navigation
{
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class FinikMovementController : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField] Transform visual;
        [SerializeField] float movementSpeed = 1.55f;
        [SerializeField] float runMovementSpeed = 2.42f;
        [SerializeField] float movementAcceleration = 6.5f;
        [SerializeField] float walkAnimationSpeed = 1.3f;
        [SerializeField] float runAnimationSpeed = 1.3f;
        [SerializeField, Range(0.35f, 1f)] float minimumLocomotionPlayback = 0.5f;
        [SerializeField] float runDistanceThreshold = 2.2f;
        [SerializeField] float movingThreshold = 0.04f;
        [SerializeField] float animatorDampTime = 0.12f;
        [SerializeField] float rotationSharpness = 8f;
        [SerializeField] float turnBeforeMoveAngle = 28f;
        [SerializeField] float turnReleaseAngle = 6f;
        [SerializeField] float resumeAutonomousDelay = 4f;
        [SerializeField] float navMeshStartSnapRadius = 0.6f;
        [SerializeField] float runtimeRecoverySnapRadius = 0.12f;
        [SerializeField] float destinationSnapRadius = 0.5f;
        [SerializeField] float minimumPathDistance = 0.18f;
        [SerializeField] bool debugDraw;
        NavMeshAgent agent;
        NavMeshPath path;
        float autonomousAllowedAt;
        int activityLocks;
        // Menus hold their lock by name: one per menu however often it opens, and the menu gives it
        // back when it closes even if another menu has already taken the room over. A bare counter
        // leaked one lock per handover, and Finik stopped walking on his own for the rest of the session.
        readonly HashSet<Object> activityOwners = new();
        bool recoveryAttempted;
        bool turningIntoPath;
        float activeWalkStrideScale = 1f;
        float activeRunStrideScale = 1f;
        Vector3 lastManualTarget;
        Vector3 lastAutonomousTarget;
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int MoveModeId = Animator.StringToHash("MoveMode");
        public FinikState State { get; private set; } = FinikState.Idle;
        public bool CanStartAutonomous => State == FinikState.Idle && !Busy && Time.time >= autonomousAllowedAt && AgentSettled;
        public float RemainingDistance => agent != null && agent.hasPath ? agent.remainingDistance : 0f;
        public Vector3 Destination => agent != null && agent.hasPath ? agent.destination : transform.position;
        bool AgentSettled => agent == null || !agent.isOnNavMesh || (!agent.pathPending && agent.velocity.sqrMagnitude <= movingThreshold * movingThreshold);
        float WalkMovementScale => Mathf.Clamp(Mathf.Sqrt(Mathf.Max(.1f, activeWalkStrideScale)), .85f, 1.35f);
        float RunMovementScale => Mathf.Clamp(Mathf.Sqrt(Mathf.Max(.1f, activeRunStrideScale)), .85f, 1.35f);
        float WalkPlaybackScale => Mathf.Clamp(WalkMovementScale / Mathf.Max(.1f, activeWalkStrideScale), .5f, 1.2f);
        float RunPlaybackScale => Mathf.Clamp(RunMovementScale / Mathf.Max(.1f, activeRunStrideScale), .5f, 1.2f);
        float EffectiveWalkSpeed => movementSpeed * WalkMovementScale;
        float EffectiveRunSpeed => runMovementSpeed * RunMovementScale;
        float EffectiveWalkAnimationSpeed => walkAnimationSpeed * WalkPlaybackScale;
        float EffectiveRunAnimationSpeed => runAnimationSpeed * RunPlaybackScale;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>(); path = new NavMeshPath();
            if (!animator) animator = GetComponentInChildren<Animator>();
            if (!visual && animator) visual = animator.transform;
            if (animator) animator.applyRootMotion = false;
            agent.speed = EffectiveWalkSpeed;
            agent.acceleration = movementAcceleration * Mathf.Max(WalkMovementScale, RunMovementScale);
            agent.updatePosition = true;
            agent.updateRotation = false;
        }

        void Start() => RecoverAgentToNearestNavMesh(true);

        void Update()
        {
            if (!agent.isOnNavMesh)
            {
                if (animator) { animator.SetFloat(SpeedId, 0f); animator.SetInteger(MoveModeId, 0); }
                RecoverAgentToNearestNavMesh(false);
                return;
            }
            recoveryAttempted = false;

            if (turningIntoPath && State != FinikState.Idle && agent.hasPath)
            {
                Vector3 toSteeringTarget = agent.steeringTarget - transform.position;
                toSteeringTarget.y = 0f;

                if (toSteeringTarget.sqrMagnitude > .001f)
                {
                    Vector3 direction = toSteeringTarget.normalized;
                    Quaternion wanted = Quaternion.LookRotation(direction, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, wanted, 1f - Mathf.Exp(-rotationSharpness * Time.deltaTime));

                    if (Vector3.Angle(transform.forward, direction) <= turnReleaseAngle)
                    {
                        turningIntoPath = false;
                        agent.isStopped = false;
                    }
                }
                else
                {
                    turningIntoPath = false;
                    agent.isStopped = false;
                }
            }

            int moveMode = ResolveMoveMode();
            float targetMovementSpeed = moveMode == 3 ? EffectiveRunSpeed : EffectiveWalkSpeed;
            if (agent.speed != targetMovementSpeed)
                agent.speed = targetMovementSpeed;

            float speed = agent.velocity.magnitude;
            FinikAudioManager.Instance.TickFootsteps(speed, Time.deltaTime);
            if (animator)
            {
                animator.SetFloat(SpeedId, speed, animatorDampTime, Time.deltaTime);
                animator.SetInteger(MoveModeId, moveMode);

                if (speed > movingThreshold && moveMode != 0)
                {
                    float referenceSpeed = moveMode == 3 ? EffectiveRunSpeed : EffectiveWalkSpeed;
                    float basePlayback = moveMode == 3 ? EffectiveRunAnimationSpeed : EffectiveWalkAnimationSpeed;
                    float speedRatio = referenceSpeed > 0.001f ? speed / referenceSpeed : 1f;
                    animator.speed = basePlayback * Mathf.Clamp(speedRatio, minimumLocomotionPlayback, 1.15f);
                }
                else animator.speed = 1f;
            }
            if (!turningIntoPath && speed > movingThreshold && agent.desiredVelocity.sqrMagnitude > .001f)
            {
                Quaternion wanted = Quaternion.LookRotation(agent.desiredVelocity.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, wanted, 1f - Mathf.Exp(-rotationSharpness * Time.deltaTime));
            }
            if (State != FinikState.Idle && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + .03f)
            {
                FinikState completed = State;
                turningIntoPath = false;
                agent.ResetPath();
                State = FinikState.Idle;
                autonomousAllowedAt = Time.time + (completed == FinikState.UserMove ? resumeAutonomousDelay : 0f);
            }
        }

        int ResolveMoveMode()
        {
            if (agent == null || !agent.isOnNavMesh || State == FinikState.Idle)
                return 0;

            if (State == FinikState.AutonomousMove)
                return agent.velocity.magnitude > movingThreshold || agent.hasPath ? 1 : 0;

            if (State == FinikState.UserMove)
            {
                if (agent.velocity.magnitude <= movingThreshold && !agent.hasPath)
                    return 0;

                if (agent.remainingDistance > runDistanceThreshold)
                    return 3;

                // Keep the run cycle during deceleration. Switching to walk while the
                // body is still travelling at run speed produces a visible foot slide.
                if (agent.velocity.magnitude > EffectiveWalkSpeed * 1.08f)
                    return 3;

                return 2;
            }

            return 0;
        }

        bool TryDestination(Vector3 target, FinikState nextState)
        {
            if (!agent.isOnNavMesh || !TryProjectDestination(target, out Vector3 projectedTarget)) return false;
            if (!NavMesh.CalculatePath(agent.nextPosition, projectedTarget, agent.areaMask, path) || path.status != NavMeshPathStatus.PathComplete) return false;
            if (path.corners.Length < 2) return false;
            if ((projectedTarget - transform.position).sqrMagnitude < minimumPathDistance * minimumPathDistance) return false;

            agent.isStopped = false;
            if (!agent.SetPath(path)) return false;

            State = nextState;
            agent.speed = nextState == FinikState.UserMove && PathLength(path) > runDistanceThreshold
                ? EffectiveRunSpeed
                : EffectiveWalkSpeed;
            Vector3 initialDirection = path.corners[1] - transform.position;
            initialDirection.y = 0f;
            turningIntoPath = initialDirection.sqrMagnitude > .001f && Vector3.Angle(transform.forward, initialDirection.normalized) >= turnBeforeMoveAngle;
            agent.isStopped = turningIntoPath;

            if (nextState == FinikState.UserMove)
            {
                lastManualTarget = projectedTarget;
                autonomousAllowedAt = float.PositiveInfinity;
            }
            else lastAutonomousTarget = projectedTarget;
            return true;
        }

        static float PathLength(NavMeshPath navPath)
        {
            if (navPath == null || navPath.corners == null || navPath.corners.Length < 2) return 0f;
            float total = 0f;
            for (int i = 1; i < navPath.corners.Length; i++)
                total += Vector3.Distance(navPath.corners[i - 1], navPath.corners[i]);
            return total;
        }

        bool TryProjectDestination(Vector3 target, out Vector3 projectedTarget)
        {
            if (NavMesh.SamplePosition(target, out NavMeshHit hit, destinationSnapRadius, agent.areaMask))
            {
                projectedTarget = hit.position;
                return true;
            }
            projectedTarget = target;
            return false;
        }

        void RecoverAgentToNearestNavMesh(bool initialization)
        {
            if (agent.isOnNavMesh || recoveryAttempted) return;
            recoveryAttempted = true;
            float radius = initialization ? navMeshStartSnapRadius : runtimeRecoverySnapRadius;
            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, radius, agent.areaMask)) return;

            // A wider Warp is allowed only when the scene starts. During gameplay we only
            // accept a tiny corrective snap, preventing visible teleports across the room.
            if (!initialization && Vector3.Distance(transform.position, hit.position) > runtimeRecoverySnapRadius) return;
            agent.Warp(hit.position);
            turningIntoPath = false;
            agent.ResetPath();
            State = FinikState.Idle;
            autonomousAllowedAt = Time.time + resumeAutonomousDelay;
        }

        public bool TrySetAutonomousDestination(Vector3 target)
        {
            if (!CanStartAutonomous) return false;
            return TryDestination(target, FinikState.AutonomousMove);
        }

        public bool TrySetUserDestination(Vector3 target)
        {
            // Validate the replacement path before touching the current one. An invalid tap
            // must not cancel a valid in-progress user move or accidentally hand control back
            // to autonomous roaming.
            return TryDestination(target, FinikState.UserMove);
        }

        public void SetLocomotionStrideScales(float walkStrideScale, float runStrideScale)
        {
            activeWalkStrideScale = Mathf.Max(.1f, walkStrideScale);
            activeRunStrideScale = Mathf.Max(.1f, runStrideScale);

            if (agent != null)
            {
                agent.acceleration = movementAcceleration * Mathf.Max(WalkMovementScale, RunMovementScale);
                if (State == FinikState.Idle)
                    agent.speed = EffectiveWalkSpeed;
            }
        }

        public void SetAnimator(Animator nextAnimator, Transform nextVisual = null)
        {
            animator = nextAnimator;
            if (nextVisual) visual = nextVisual;
            else if (animator) visual = animator.transform;
            if (!animator) return;
            animator.applyRootMotion = false;
            animator.speed = 1f;
            if (agent != null) agent.speed = EffectiveWalkSpeed;
            animator.SetFloat(SpeedId, 0f);
            animator.SetInteger(MoveModeId, 0);
        }

        public void NotifyUserMoveCompleted() { autonomousAllowedAt = Time.time + resumeAutonomousDelay; }

        /// <summary>Turns the character toward the gameplay camera on the horizontal plane.</summary>
        public void FaceCamera()
        {
            var camera = Camera.main;
            if (!camera) return;

            Vector3 toCamera = camera.transform.position - transform.position;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude <= 0.0001f) return;

            transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
        }

        static readonly System.Predicate<Object> Destroyed = owner => !owner;

        bool Busy
        {
            get
            {
                // An owner destroyed while holding its lock (a rebuilt screen) must not freeze Finik.
                if (activityOwners.Count > 0) activityOwners.RemoveWhere(Destroyed);
                return activityLocks > 0 || activityOwners.Count > 0;
            }
        }

        /// <summary>Stops Finik for an activity that pairs every Begin with exactly one End.</summary>
        public void BeginActivity()
        {
            activityLocks++;
            Halt();
        }

        public void EndActivity()
        {
            if (activityLocks == 0) return;
            activityLocks--;
            ResumeIfFree();
        }

        /// <summary>
        /// Stops Finik on behalf of <paramref name="owner"/> (a menu). Repeated calls from the same owner
        /// hold one lock, and <see cref="EndActivity(Object)"/> from that owner always releases it.
        /// </summary>
        public void BeginActivity(Object owner)
        {
            if (!owner)
            {
                BeginActivity();
                return;
            }
            activityOwners.Add(owner);
            Halt();
        }

        public void EndActivity(Object owner)
        {
            if (!owner || !activityOwners.Remove(owner)) return;
            ResumeIfFree();
        }

        void Halt()
        {
            turningIntoPath = false;
            if (animator) { animator.SetFloat(SpeedId, 0f); animator.SetInteger(MoveModeId, 0); }
            if (agent.isOnNavMesh) { agent.ResetPath(); agent.isStopped = true; }
            State = FinikState.Idle;
        }

        void ResumeIfFree()
        {
            if (Busy) return;
            if (agent.isOnNavMesh) agent.isStopped = false;
            autonomousAllowedAt = Time.time + 1.5f;
        }

        void OnDrawGizmosSelected()
        {
            if (!debugDraw) return;
            Gizmos.color = State == FinikState.UserMove ? Color.cyan : State == FinikState.AutonomousMove ? Color.yellow : Color.green;
            Gizmos.DrawWireSphere(transform.position, .12f); Gizmos.DrawSphere(Destination, .08f);
            if (agent != null && agent.hasPath) { var c=agent.path.corners; for(int i=1;i<c.Length;i++) Gizmos.DrawLine(c[i-1],c[i]); }
            Gizmos.color=Color.cyan; Gizmos.DrawWireSphere(lastManualTarget,.1f); Gizmos.color=Color.yellow; Gizmos.DrawWireSphere(lastAutonomousTarget,.1f);
        }
    }
}
