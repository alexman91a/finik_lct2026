using System;
using Finik.Accessories;
using Finik.Core;
using Finik.UI.Onboarding;
using UnityEngine;

namespace Finik.Navigation
{
    /// <summary>
    /// Swaps the visible character while gameplay logic stays on Finik_Root.
    /// Character transforms are authored and saved in the scene; this component
    /// never changes their position, rotation or scale at runtime.
    /// </summary>
    public sealed class FinikCharacterSwitcher : MonoBehaviour
    {
        [Serializable]
        public sealed class CharacterVisual
        {
            public string id;
            [Min(0)] public int stage;
            public GameObject root;
            public Animator animator;
            public Transform visual;
            [Min(.1f)] public float walkStrideScale = 1f;
            [Min(.1f)] public float runStrideScale = 1f;
        }

        [Serializable]
        sealed class PetIdentityPayload
        {
            public string petId;
            public int stage = 1;
        }

        [SerializeField] CharacterVisual[] characters = Array.Empty<CharacterVisual>();
        [SerializeField] FinikMovementController movement;
        [SerializeField] FinikActivityController activity;

        public string ActiveId { get; private set; }
        public int ActiveStage { get; private set; }

        public bool HasCharacter(string id, int stage)
        {
            foreach (var item in characters)
            {
                if (item?.root && item.id == id && item.stage == stage)
                    return true;
            }
            return false;
        }

        void Awake()
        {
            if (!movement) movement = GetComponent<FinikMovementController>();
            if (!activity) activity = GetComponent<FinikActivityController>();
        }

        void OnEnable()
        {
            FinikGame.Changed += SyncGrowthStage;
            SyncGrowthStage();
        }

        void OnDisable() => FinikGame.Changed -= SyncGrowthStage;

        void SyncGrowthStage()
        {
            if (!FinikProfileStore.TryLoad(out var profile) || profile.demo || !FinikGame.HasJourney) return;
            int stage = FinikGame.CharacterStage;
            if (ActiveId == profile.petId && ActiveStage != stage && HasCharacter(profile.petId, stage))
                Show(profile.petId, stage);
        }

        public bool Show(string id) => Show(id, 0);

        public bool Show(string id, int stage)
        {
            if (stage > 0 && FinikProfileStore.TryLoad(out var profile) && !profile.demo && FinikGame.HasJourney)
                stage = FinikGame.CharacterStage;
            CharacterVisual selected = null;

            if (stage > 0)
            {
                foreach (var item in characters)
                {
                    if (item != null && item.id == id && item.stage == stage)
                    {
                        selected = item;
                        break;
                    }
                }
            }

            if (selected == null)
            {
                foreach (var item in characters)
                {
                    if (item != null && item.id == id && item.stage == 0)
                    {
                        selected = item;
                        break;
                    }
                }
            }

            if (selected == null)
            {
                foreach (var item in characters)
                {
                    if (item != null && item.id == id)
                    {
                        selected = item;
                        break;
                    }
                }
            }

            if (selected == null || !selected.root) return false;
            int selectedStage = Math.Max(1, selected.stage);

            var nextAnimator = selected.animator
                ? selected.animator
                : selected.root.GetComponentInChildren<Animator>(true);
            if (!nextAnimator) return false;


            foreach (var item in characters)
            {
                if (item?.root)
                    item.root.SetActive(item == selected);
            }

            if (movement)
            {
                movement.SetLocomotionStrideScales(
                    selected.walkStrideScale > 0f ? selected.walkStrideScale : 1f,
                    selected.runStrideScale > 0f ? selected.runStrideScale : 1f
                );
                movement.SetAnimator(nextAnimator, selected.visual ? selected.visual : nextAnimator.transform);
            }
            if (activity)
                activity.SetAnimator(nextAnimator);

            var footGrounding = GetComponent<FinikFootGrounding>();
            if (footGrounding)
            {
                footGrounding.ConfigureCharacter(id, selectedStage);
                footGrounding.RefreshBindings();
            }

            // Older scenes have this correction component disabled. Keep it active for
            // every character variant so the face does not drift down after a switch.
            var headLevel = GetComponent<FinikHeadLevel>();
            if (headLevel)
            {
                headLevel.enabled = true;
                headLevel.ConfigureCharacter(id, selectedStage);
                headLevel.RefreshBindings();
            }

            var armNeutralizer = GetComponent<FinikArmNeutralizer>();
            if (!armNeutralizer)
                armNeutralizer = gameObject.AddComponent<FinikArmNeutralizer>();
            armNeutralizer.ConfigureCharacter(id, selectedStage);

            var armClearance = GetComponent<FinikArmHeadClearance>();
            if (!armClearance)
                armClearance = gameObject.AddComponent<FinikArmHeadClearance>();
            armClearance.ConfigureCharacter(id, selectedStage);

            ActiveId = id;
            ActiveStage = selectedStage;
            var accessories = GetComponent<FinikAccessoryRig>();
            if (accessories) accessories.RefreshForActiveCharacter();
            return true;
        }

        public void SetPetIdentity(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            try
            {
                var payload = JsonUtility.FromJson<PetIdentityPayload>(json);
                if (payload == null || string.IsNullOrWhiteSpace(payload.petId)) return;
                Show(payload.petId, Mathf.Clamp(payload.stage, 1, 3));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[FinikCharacterSwitcher] Invalid pet identity: {exception.Message}");
            }
        }

        public void HideAll()
        {
            foreach (var item in characters)
            {
                if (item?.root)
                    item.root.SetActive(false);
            }

            ActiveId = null;
            ActiveStage = 0;
        }
    }
}
