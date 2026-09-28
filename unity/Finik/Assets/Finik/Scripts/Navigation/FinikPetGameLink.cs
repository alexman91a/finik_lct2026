using Finik.Core;
using UnityEngine;

namespace Finik.Navigation
{
    /// <summary>
    /// Connects the 3D Finik to <see cref="FinikGame"/>: keeps his need gestures in sync with the
    /// decaying needs and plays reactions (fed, played, …) that game actions request.
    /// </summary>
    [RequireComponent(typeof(FinikActivityController))]
    public sealed class FinikPetGameLink : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] float syncSeconds = 2f;
        [SerializeField, Min(1f)] float needDeltaPopupStep = 5f;

        FinikActivityController activity;
        float nextSync;
        bool hasObservedNeeds;
        FinikNeeds observedNeeds;
        float pendingFoodLoss;
        float pendingMoodLoss;

        void Awake() => activity = GetComponent<FinikActivityController>();

        void OnEnable()
        {
            FinikGame.Changed += Sync;
            FinikGame.ReactionRequested += OnReaction;
            Sync();
        }

        void OnDisable()
        {
            FinikGame.Changed -= Sync;
            FinikGame.ReactionRequested -= OnReaction;
        }

        void Update()
        {
            if (Time.unscaledTime >= nextSync) Sync();
        }

        void Sync()
        {
            nextSync = Time.unscaledTime + syncSeconds;
            var current = FinikGame.NeedsNow;

            if (hasObservedNeeds)
            {
                float foodDelta = current.food - observedNeeds.food;
                float moodDelta = current.mood - observedNeeds.mood;

                if (foodDelta < -0.01f) pendingFoodLoss += -foodDelta;
                else if (foodDelta > 0.01f) pendingFoodLoss = 0f;

                if (moodDelta < -0.01f) pendingMoodLoss += -moodDelta;
                else if (moodDelta > 0.01f) pendingMoodLoss = 0f;

                if (pendingFoodLoss >= needDeltaPopupStep)
                {
                    int lost = Mathf.Max(1, Mathf.RoundToInt(pendingFoodLoss));
                    pendingFoodLoss = 0f;
                    FinikPetPopup.ShowDelta("icon_food", -lost,
                        current.food <= 26f ? "Очень проголодался" : "Проголодался");
                }

                if (pendingMoodLoss >= needDeltaPopupStep)
                {
                    int lost = Mathf.Max(1, Mathf.RoundToInt(pendingMoodLoss));
                    pendingMoodLoss = 0f;
                    FinikPetPopup.ShowDelta("icon_mood", -lost,
                        current.mood <= 26f ? "Очень грустно" : "Настроение снизилось");
                }
            }

            observedNeeds = current;
            hasObservedNeeds = true;
            activity.SetNeeds(current);
        }

        void OnReaction(string type, string intensity)
        {
            // Plays at once even while a full-screen flow pauses gameplay; if a clip is already
            // running, queue it like reactions from the mobile host.
            if (!activity.PlayReactionNow(type, intensity))
                activity.PlayReaction(JsonUtility.ToJson(new ReactionMessage { eventId = System.Guid.NewGuid().ToString("N"), type = type, intensity = intensity }));
        }

        [System.Serializable]
        sealed class ReactionMessage
        {
            public string eventId;
            public string type;
            public string intensity;
        }
    }
}
