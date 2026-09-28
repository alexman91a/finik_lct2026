using UnityEngine;
using UnityEngine.AI;

namespace Finik.Navigation
{
    public sealed class FinikInteractionTarget : MonoBehaviour
    {
        [SerializeField] string interactionId;
        [SerializeField] Transform interactionPoint;
        [SerializeField, Min(0.1f)] float arrivalRadius = 0.45f;
        [SerializeField, Min(0.1f)] float approachSearchRadius = 1.25f;

        public string InteractionId => interactionId;
        public float ArrivalRadius => arrivalRadius;

        public void Configure(string id, float arrival = 0.45f, float searchRadius = 1.5f)
        {
            interactionId = id;
            arrivalRadius = arrival;
            approachSearchRadius = searchRadius;
        }

        public bool TryGetApproachPoint(Vector3 actorPosition, out Vector3 point)
        {
            if (interactionPoint &&
                NavMesh.SamplePosition(interactionPoint.position, out NavMeshHit explicitHit, approachSearchRadius, NavMesh.AllAreas))
            {
                point = explicitHit.position;
                return true;
            }
            Collider targetCollider = GetComponentInChildren<Collider>();
            Vector3 candidate = transform.position;
            if (targetCollider)
            {
                Bounds bounds = targetCollider.bounds;
                candidate = bounds.ClosestPoint(actorPosition);
                Vector3 away = actorPosition - bounds.center;
                away.y = 0f;
                if (away.sqrMagnitude > 0.001f) candidate += away.normalized * 0.25f;
            }

            candidate.y = actorPosition.y;
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, approachSearchRadius, NavMesh.AllAreas))
            {
                point = hit.position;
                return true;
            }

            if (NavMesh.SamplePosition(transform.position, out hit, approachSearchRadius * 2f, NavMesh.AllAreas))
            {
                point = hit.position;
                return true;
            }
            point = default;
            return false;
        }
    }
}
