using Finik.Core;
using UnityEngine;

namespace Finik.Navigation
{
    /// <summary>Persists pet-need decay in small batches while the application is actually in use.</summary>
    public sealed class FinikActiveNeedDecay : MonoBehaviour
    {
        const float CommitIntervalSeconds = 5f;
        float elapsed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindFirstObjectByType<FinikActiveNeedDecay>()) return;
            new GameObject("Finik_ActiveNeedDecay").AddComponent<FinikActiveNeedDecay>();
        }

        void Update()
        {
            if (!Application.isFocused) return;
            elapsed += Time.unscaledDeltaTime;
            if (elapsed < CommitIntervalSeconds) return;
            FinikGame.AdvanceActiveNeedDecay(elapsed);
            elapsed = 0f;
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused) elapsed = 0f;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) elapsed = 0f;
        }
    }
}
