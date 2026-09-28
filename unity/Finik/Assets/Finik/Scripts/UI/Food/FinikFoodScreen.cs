using System;
using System.Collections;
using System.Collections.Generic;
using Finik.Core;
using Finik.Navigation;
using Finik.UI.Onboarding;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Food
{
    /// <summary>
    /// "What does Finik eat tonight?" (port of app/pet-food.tsx). A life scene offers four ways to eat
    /// with different prices and trade-offs; the player picks one, pays from the wallet and learns
    /// whether it was smart, a treat or a trap. Opens when Finik walks to the fridge.
    ///
    /// The price lives on the action: the orange button reads "Выбрать за 150" with the split between
    /// «Нужно» and «Хочу» under it. An option the wallet cannot cover says so on its card, and picking
    /// it turns the button into a disabled "Не хватает N монет" with a cheaper option suggested below
    /// the scene text.
    /// </summary>
    public sealed class FinikFoodScreen : MonoBehaviour, IFinikScreen
    {
        public const string FridgeInteraction = "fridge";

        enum Stage { Choosing, Preview, Result }

        [Serializable]
        public struct IconEntry
        {
            public string name;
            public Sprite sprite;
        }

        [Header("Panel")]
        [SerializeField] FinikScreenPanel panel;
        [SerializeField] Image sceneIcon;
        [SerializeField] TMP_Text sceneTitle;
        [SerializeField] TMP_Text sceneSetup;
        [Tooltip("One quiet line under the scene text: the scene's advice, or why the choice cannot be paid.")]
        [SerializeField] TMP_Text sceneHint;
        [SerializeField] FinikCounterText coins;
        [SerializeField] FinikFoodOptionView[] options = Array.Empty<FinikFoodOptionView>();

        [SerializeField] FinikFoodResultView result;

        [Header("Buttons")]
        [SerializeField] Button closeButton;
        [SerializeField] TMP_Text closeLabel;
        [SerializeField] Button primaryButton;
        [Tooltip("Layout slot of the primary button; hidden as a whole so the close button re-centres.")]
        [SerializeField] GameObject primaryRoot;
        [SerializeField] Image primaryFace;
        [SerializeField] TMP_Text primaryLabel;
        [Tooltip("Second, smaller line of the primary button: where the coins come from.")]
        [SerializeField] TMP_Text primarySplit;

        [Header("Colors")]
        [SerializeField] Color hintColor = new(0.36f, 0.41f, 0.62f, 1f);
        [SerializeField] Color warningColor = new(0.86f, 0.24f, 0.28f, 1f);
        [Tooltip("Face of the primary button while the wallet cannot cover the choice (a tinted orange reads muddy).")]
        [SerializeField] Sprite disabledFace;
        [Tooltip("Caption material on that quiet face. Left empty it borrows the close button's.")]
        [SerializeField] Material disabledLabelMaterial;

        [Header("Icons")]
        [SerializeField] IconEntry[] icons = Array.Empty<IconEntry>();
        [SerializeField] Sprite fallbackFoodIcon;
        [SerializeField] Sprite fallbackSceneIcon;

        [Header("World")]
        [SerializeField] FinikShowcaseCamera showcase;
        [SerializeField] FinikMovementController movement;
        [SerializeField] FinikActivityController activity;
        [SerializeField] Behaviour[] pauseWhileOpen = Array.Empty<Behaviour>();
        [SerializeField] GameObject hudRoot;

        // Label placement inside the button: centred when it is alone, raised when the split line shows
        // under it. The split lives inside the button, so the label has to move; give all four the same
        // insets and it stays put.
        [Header("Primary label insets")]
        [SerializeField] Vector2 labelAloneMin = new(24f, 15.7f);
        [SerializeField] Vector2 labelAloneMax = new(-24f, 0f);
        [SerializeField] Vector2 labelRaisedMin = new(24f, 42f);
        [SerializeField] Vector2 labelRaisedMax = new(-24f, -6f);

        readonly Dictionary<string, Sprite> iconsByName = new(StringComparer.Ordinal);
        FinikFoodScene scene;
        FinikFoodPlan selected;
        Stage stage;
        bool open;
        bool closing;
        FinikInteractionController pendingInteraction;
        string petName = "Финик";
        Func<FinikInteractionController, string, bool> interactionHandler;
        // Test seam for the screen audit: pretend the wallet holds this much.
        int? balanceOverride;

        public bool IsOpen => open;

        /// <summary>This menu takes the room over: the HUD goes away and Finik stands still.</summary>
        public bool HoldsRoom => true;

        void Awake()
        {
            foreach (var text in GetComponentsInChildren<TMP_Text>(true))
                if (text && text.name == "Tag") text.text = "ЕДА · ЖИЗНЕННАЯ СИТУАЦИЯ";

            foreach (var entry in icons)
                if (!string.IsNullOrEmpty(entry.name) && entry.sprite) iconsByName[entry.name] = entry.sprite;
            foreach (var option in options)
            {
                if (!option || !option.Choice) continue;
                var view = option;
                option.Choice.Clicked += _ => Select(view.PlanId);
            }
            if (closeButton) closeButton.onClick.AddListener(Close);
            if (result && result.CloseButton) result.CloseButton.onClick.AddListener(Close);
            if (result && result.Panel) result.Panel.Hide(instant: true);
            if (primaryButton) primaryButton.onClick.AddListener(Choose);
            interactionHandler = HandleInteraction;
            FinikInteractionController.RegisterHandler(interactionHandler);
            if (panel) panel.Hide(instant: true);
        }

        void OnDestroy()
        {
            FinikScreens.Unregister(this);
            FinikInteractionController.UnregisterHandler(interactionHandler);
        }

        void OnEnable() => FinikScreens.Register(this);

        void OnDisable() => FinikScreens.Unregister(this);

        bool HandleInteraction(FinikInteractionController controller, string interactionId)
        {
            if (interactionId != FridgeInteraction || open) return false;
            if (!Open()) return false;
            pendingInteraction = controller;
            return true;
        }

        /// <summary>Lets the fridge Finik walked to know the visit is over, wherever the close ended.</summary>
        void HandBackInteraction()
        {
            if (!pendingInteraction) return;
            pendingInteraction.CompleteInteraction(FridgeInteraction);
            pendingInteraction = null;
        }

        // ------------------------------------------------------------------ open / close

        /// <summary>Opens the screen on <paramref name="sceneId"/>, or today's scene. False when there is no profile yet.</summary>
        public bool Open(string sceneId = null)
        {
            if (open) return true;
            if (!FinikProfileStore.TryLoad(out var profile)) return false;
            FinikGame.EnsureJourney();
            petName = string.IsNullOrWhiteSpace(profile.petName) ? "Финик" : profile.petName;
            scene = sceneId != null && FinikFoodCatalog.TryGetScene(sceneId, out var requested)
                ? requested
                : FinikFoodCatalog.SceneForDate(DateTime.Now);

            open = true;
            closing = false;
            // One menu at a time: whatever else is up closes before this one takes the room.
            FinikScreens.CloseOthers(this);
            if (hudRoot) hudRoot.SetActive(false);
            foreach (var behaviour in pauseWhileOpen) if (behaviour) behaviour.enabled = false;
            if (movement) movement.BeginActivity(this);
            if (showcase) showcase.Engage();

            selected = null;
            stage = Stage.Choosing;
            RenderScene();
            Render();
            panel.Show();
            FinikAudioManager.Instance.PlayAssistant(FinikAudioManager.VoiceShopFridge, interruptCurrent: true);
            return true;
        }

        public void Close()
        {
            if (!open || closing) return;
            closing = true;
            panel.Hide();
            if (result && result.Panel) result.Panel.Hide();
            StartCoroutine(CloseRoutine());
        }

        IEnumerator CloseRoutine()
        {
            // A menu takes a moment to close — the camera fly-back alone is about a second — and
            // another one can take over at any point during it. Whoever is open owns the HUD, the
            // camera and Finik, so this screen re-checks after every wait and hands back only what
            // is still its own.
            if (TakenOver()) yield break;

            bool released = false;
            if (showcase) showcase.Release(() => released = true);
            else released = true;
            while (!released) yield return null;
            if (TakenOver()) yield break;

            if (hudRoot) hudRoot.SetActive(true);
            HandBackInteraction();
            if (TakenOver()) yield break;

            foreach (var behaviour in pauseWhileOpen) if (behaviour) behaviour.enabled = true;
            if (movement) movement.EndActivity(this);
            open = false;
            closing = false;
        }

        /// <summary>
        /// True once another menu holds the room. The close then stops where it is: this screen is no
        /// longer open, but the HUD, the camera and Finik stay exactly as the new menu set them.
        /// </summary>
        bool TakenOver()
        {
            if (!FinikScreens.RoomTakenOver(this)) return false;
            // The HUD and the camera belong to the new menu now; this menu's own hold on Finik does not.
            if (movement) movement.EndActivity(this);
            HandBackInteraction();
            open = false;
            closing = false;
            return true;
        }

        // ------------------------------------------------------------------ flow

        void Select(string planId)
        {
            if (stage == Stage.Result || closing || !FinikFoodCatalog.TryGetPlan(planId, out var plan)) return;
            selected = plan;
            stage = Stage.Preview;
            Render();
        }

        void Choose()
        {
            if (stage != Stage.Preview || selected == null || !CanAfford(selected)) return;
            int balanceBefore = FinikGame.Balance;
            int xpBefore = FinikGame.Growth.xp;
            var needsBefore = FinikGame.NeedsNow;
            var outcome = FinikGame.PerformFoodPlan(selected.Id);
            switch (outcome.failure)
            {
                case FinikCareFailure.None:
                    stage = Stage.Result;
                    var needsAfter = FinikGame.NeedsNow;
                    var gained = new FinikNeeds(needsAfter.food - needsBefore.food, needsAfter.mood - needsBefore.mood);
                    Render();
                    panel.Hide();
                    if (result)
                    {
                        result.Show(new FinikFoodResultView.Outcome(selected, outcome.message, balanceBefore - FinikGame.Balance,
                                gained, FinikGame.Growth.xp - xpBefore),
                            IconFor(selected.Icon, fallbackFoodIcon),
                            hudRoot && hudRoot.TryGetComponent<FinikHudView>(out var hud) ? hud.AvatarSprite : null);
                    }

                    FinikAudioManager.Instance.PlayAssistant(FinikAudioManager.VoicePurchaseFood, interruptCurrent: true);

                    int foodGain = Mathf.RoundToInt(gained.food);
                    int moodGain = Mathf.RoundToInt(gained.mood);
                    if (foodGain > 0) FinikPetPopup.ShowDelta("icon_food", foodGain, "Сыт!");
                    if (moodGain > 0) FinikPetPopup.ShowDelta("icon_mood", moodGain, "Стало веселее");
                    break;
                case FinikCareFailure.InsufficientFunds:
                    // The wallet changed under us: the card and the button now say it does not fit.
                    Render();
                    break;
                default:
                    // Cooldown and the like: explain under the scene text and stay on the preview.
                    Render(note: outcome.message);
                    break;
            }
        }

        int Balance => balanceOverride ?? FinikGame.Balance;
        bool CanAfford(FinikFoodPlan plan) => plan != null && plan.TotalCost <= Balance;

        FinikFoodPlan CheaperOption()
        {
            FinikFoodPlan best = null;
            foreach (string id in scene.OptionIds)
            {
                if (selected != null && id == selected.Id) continue;
                if (!FinikFoodCatalog.TryGetPlan(id, out var plan) || !CanAfford(plan)) continue;
                if (best == null || plan.TotalCost < best.TotalCost) best = plan;
            }
            return best;
        }

        // ------------------------------------------------------------------ rendering

        void RenderScene()
        {
            if (sceneIcon) sceneIcon.sprite = IconFor(scene.Icon, fallbackSceneIcon);
            if (sceneTitle) sceneTitle.text = FinikTypography.Fix(scene.Title);
            if (sceneSetup) sceneSetup.text = FinikTypography.Fix(scene.Setup.Replace("Финик", petName));
            for (int i = 0; i < options.Length; i++)
            {
                if (!options[i]) continue;
                bool used = i < scene.OptionIds.Count && FinikFoodCatalog.TryGetPlan(scene.OptionIds[i], out _);
                options[i].gameObject.SetActive(used);
                if (!used) continue;
                FinikFoodCatalog.TryGetPlan(scene.OptionIds[i], out var plan);
                options[i].Show(plan, IconFor(plan.Icon, fallbackFoodIcon), CanAfford(plan));
            }
        }

        void Render(string note = null)
        {
            // Snap on open, roll when the balance changes while the screen is up (paying).
            if (coins) coins.SetValue(Balance, animate: panel && panel.IsVisible);
            foreach (var option in options)
            {
                if (!option || !option.gameObject.activeSelf) continue;
                option.Choice.SetSelected(selected != null && option.PlanId == selected.Id);
                option.SetInteractable(stage != Stage.Result);
                if (FinikFoodCatalog.TryGetPlan(option.PlanId, out var plan)) option.SetAffordable(CanAfford(plan));
            }

            bool preview = stage == Stage.Preview && selected != null;
            bool affordable = preview && CanAfford(selected);

            string hint = scene.Hint;
            bool warning = false;
            if (note != null)
            {
                hint = note;
                warning = true;
            }
            else if (preview && !affordable)
            {
                var cheaper = CheaperOption();
                hint = cheaper != null
                    ? cheaper.TotalCost == 0
                        ? $"Можно бесплатно: «{cheaper.Title}»."
                        : $"Можно выбрать дешевле: «{cheaper.Title}» за {Coins(cheaper.TotalCost)}."
                    : "Сейчас все варианты дороже, чем есть в кошельке. Можно вернуться позже.";
                warning = true;
            }
            if (sceneHint)
            {
                sceneHint.text = FinikTypography.Fix(hint ?? string.Empty);
                sceneHint.color = warning ? warningColor : hintColor;
                sceneHint.gameObject.SetActive(!string.IsNullOrEmpty(hint));
            }

            if (closeLabel) closeLabel.text = "Не сейчас";
            var slot = primaryRoot ? primaryRoot : primaryButton ? primaryButton.gameObject : null;
            if (slot) slot.SetActive(preview);
            if (!preview) return;

            string label = !affordable ? $"Не хватает {Coins(selected.TotalCost - Balance)}"
                : selected.TotalCost == 0 ? "Выбрать" : $"Выбрать за {selected.TotalCost}";
            string split = affordable ? DescribeCharges(selected) : null;
            if (primaryLabel)
            {
                primaryLabel.text = label;
                bool raised = !string.IsNullOrEmpty(split);
                primaryLabel.rectTransform.offsetMin = raised ? labelRaisedMin : labelAloneMin;
                primaryLabel.rectTransform.offsetMax = raised ? labelRaisedMax : labelAloneMax;
            }
            if (primarySplit)
            {
                primarySplit.text = split ?? string.Empty;
                primarySplit.gameObject.SetActive(!string.IsNullOrEmpty(split));
            }
            if (primaryButton) primaryButton.interactable = affordable;
            SetPrimaryLook(affordable);
        }

        static string Coins(int amount) => $"{amount} {FinikTypography.Plural(amount, "монета", "монеты", "монет")}";

        Sprite enabledFace;
        Material enabledLabelMaterial;
        Color enabledLabelColor;

        /// <summary>Orange with a white outlined caption when it can be paid, the quiet white button otherwise.</summary>
        void SetPrimaryLook(bool enabled)
        {
            if (!primaryFace || !primaryLabel) return;
            if (!enabledFace)
            {
                enabledFace = primaryFace.sprite;
                enabledLabelMaterial = primaryLabel.fontSharedMaterial;
                enabledLabelColor = primaryLabel.color;
            }
            var quiet = disabledLabelMaterial ? disabledLabelMaterial
                : closeLabel ? closeLabel.fontSharedMaterial : null;
            primaryFace.sprite = enabled || !disabledFace ? enabledFace : disabledFace;
            primaryLabel.fontSharedMaterial = enabled || !quiet ? enabledLabelMaterial : quiet;
            primaryLabel.color = enabled ? enabledLabelColor : warningColor;
        }

        /// <summary>Where the coins come from: "Нужно 80 · Хочу 70", or "без новых трат".</summary>
        static string DescribeCharges(FinikFoodPlan plan)
        {
            if (plan.Charges.Count == 0) return "без новых трат";
            var parts = new List<string>(plan.Charges.Count);
            foreach (var charge in plan.Charges) parts.Add($"{FinikFoodCatalog.BucketLabel(charge.bucket)} {charge.amount}");
            return string.Join(" · ", parts);
        }

        /// <summary>
        /// Fills the icon lookup the first time it is needed. Not left to Awake: the editor runs
        /// with domain and scene reload disabled, and a screen that was already in the scene can
        /// reach Play without its Awake — which showed every item on this screen as the fallback
        /// placeholder instead of its own picture.
        /// </summary>
        void EnsureIcons()
        {
            if (iconsByName.Count > 0 || icons.Length == 0) return;
            foreach (var entry in icons)
                if (!string.IsNullOrEmpty(entry.name) && entry.sprite) iconsByName[entry.name] = entry.sprite;
        }

        Sprite IconFor(string name, Sprite fallback)
        {
            EnsureIcons();
            return iconsByName.TryGetValue(name, out var sprite) ? sprite : fallback;
        }
    }
}
