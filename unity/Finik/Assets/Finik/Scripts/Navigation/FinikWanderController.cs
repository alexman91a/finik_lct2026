using System;
using UnityEngine;
using UnityEngine.AI;

namespace Finik.Navigation
{
    [RequireComponent(typeof(FinikMovementController))]
    public sealed class FinikWanderController : MonoBehaviour
    {
        [SerializeField] float minimumIdleSeconds = 2f;
        [SerializeField] float maximumIdleSeconds = 6f;
        [SerializeField] float minimumWanderDistance = 1f;
        [SerializeField] float maximumWanderDistance = 4f;
        [SerializeField] int candidateAttempts = 12;
        [SerializeField, Range(0f, 1f)] float ambientIdleChance = .45f;
        [SerializeField] bool useFixedRandomSeed = true;
        [SerializeField] int randomSeed = 19091;
        FinikMovementController movement;
        FinikActivityController activity;
        System.Random random;
        float nextAttemptAt;

        void Awake()
        {
            movement = GetComponent<FinikMovementController>();
            activity = GetComponent<FinikActivityController>();
            random = useFixedRandomSeed ? new System.Random(randomSeed) : new System.Random();
            Schedule();
        }
        void Schedule() => nextAttemptAt=Time.time+Range(minimumIdleSeconds,maximumIdleSeconds);
        float Range(float a,float b)=>(float)(a+(b-a)*random.NextDouble());
        void Update()
        {
            // Recompiling during Play Mode reloads the domain without calling Awake again, which
            // leaves the cached components null; bail out instead of throwing every frame.
            if (!movement) { movement = GetComponent<FinikMovementController>(); if (!movement) return; }
            if (!activity) activity = GetComponent<FinikActivityController>();
            if (random == null) random = useFixedRandomSeed ? new System.Random(randomSeed) : new System.Random();
            if (!movement.CanStartAutonomous || Time.time < nextAttemptAt) return;
            if (activity && activity.TryPlayNeedReminder()) { Schedule(); return; }
            if (activity && random.NextDouble() < ambientIdleChance && activity.TryPlayAmbientIdle()) { Schedule(); return; }
            for(int i=0;i<candidateAttempts;i++)
            {
                float angle=Range(0f,Mathf.PI*2f), distance=Range(minimumWanderDistance,maximumWanderDistance);
                Vector3 candidate=transform.position+new Vector3(Mathf.Cos(angle),0f,Mathf.Sin(angle))*distance;
                if (!NavMesh.SamplePosition(candidate,out NavMeshHit hit,.45f,NavMesh.AllAreas)) continue;
                if ((hit.position-transform.position).sqrMagnitude < minimumWanderDistance*minimumWanderDistance) continue;
                var path=new NavMeshPath();
                if (!NavMesh.CalculatePath(transform.position,hit.position,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete) continue;
                if (movement.TrySetAutonomousDestination(hit.position)) { Schedule(); return; }
            }
            Schedule();
        }
    }
}
