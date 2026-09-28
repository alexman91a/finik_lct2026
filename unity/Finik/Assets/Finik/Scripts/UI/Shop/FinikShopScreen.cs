using System;
using System.Collections;
using System.Collections.Generic;
using Finik.Accessories;
using Finik.Core;
using Finik.Navigation;
using Finik.UI.Onboarding;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Shop
{
    /// <summary>
    /// «Покупки»: the shelf of things the child can buy for Finik, split into the two tabs the whole
    /// app teaches — «Нужно» (food, care, school) and «Хочу» (treats, toys, clothes, trinkets).
    ///
    /// Every card shows the item, its price and what it gives («+сытость», «+настроение»), and nothing
    /// is ever charged without a confirmation. When the wallet cannot cover the price the card is not
    /// switched off: tapping it opens an explanation of exactly how many coins are missing and what
    /// can be done about it — earn them on quests, take them out of the piggy bank, or pick the
    /// cheapest thing that does fit. Clothes and trinkets are worn by the 3D Finik and bought once.
    /// </summary>
    public sealed class FinikShopScreen : MonoBehaviour, IFinikScreen
    {
        /// <summary>Finik walks to the toy shelf and the shop opens there, like the fridge opens food.</summary>
        public const string ShelfInteraction = "shelf";

        [Serializable]
        public struct IconEntry
        {
            public string name;
            public Sprite sprite;
        }

        [Header("Shelf")]
        [SerializeField] FinikScreenPanel main;
        [SerializeField] TMP_Text title;
        [SerializeField] FinikCounterText walletCoins;
        [SerializeField] FinikChoiceItem needsTab;
        [SerializeField] FinikChoiceItem wantsTab;
        [SerializeField] TMP_Text needsTabLabel;
        [SerializeField] TMP_Text wantsTabLabel;
        [Tooltip("One quiet line under the tabs: what this tab is for.")]
        [SerializeField] TMP_Text tabHint;
        [SerializeField] FinikShopItemView[] items = Array.Empty<FinikShopItemView>();
        [SerializeField] ScrollRect scroll;
        [SerializeField] Button closeButton;

        [Header("Confirmation")]
        [SerializeField] FinikScreenPanel confirm;
        [SerializeField] Image confirmIcon;
        [SerializeField] TMP_Text confirmTitle;
        [SerializeField] TMP_Text confirmBody;
        [SerializeField] TMP_Text confirmEffect;
        [SerializeField] TMP_Text confirmBalance;
        [SerializeField] Button buyButton;
        [SerializeField] TMP_Text buyLabel;
        [SerializeField] Button cancelButton;

        [Header("Not enough coins")]
        [SerializeField] FinikScreenPanel shortPanel;
        [SerializeField] Image shortIcon;
        [SerializeField] TMP_Text shortTitle;
        [SerializeField] TMP_Text shortBody;
        [SerializeField] TMP_Text shortMath;
        [SerializeField] TMP_Text shortAdvice;
        [SerializeField] Button earnButton;
        [SerializeField] TMP_Text earnLabel;
        [SerializeField] Button shortBackButton;

        [Header("Result")]
        [SerializeField] FinikScreenPanel result;
        [SerializeField] Image resultIcon;
        [SerializeField] TMP_Text resultTitle;
        [SerializeField] TMP_Text resultBody;
        [SerializeField] TMP_Text resultCoins;
        [SerializeField] TMP_Text resultFood;
        [SerializeField] TMP_Text resultMood;
        [SerializeField] GameObject resultXpChip;
        [SerializeField] TMP_Text resultXp;
        [SerializeField] Button resultCloseButton;
        [SerializeField] FinikConfettiBurst confetti;

        [Header("Colors")]
        [SerializeField] Color gainColor = new(0.13f, 0.62f, 0.3f, 1f);
        [SerializeField] Color lossColor = new(0.86f, 0.24f, 0.28f, 1f);
        [SerializeField] Color neutralColor = new(0.36f, 0.41f, 0.62f, 1f);
        [Tooltip("Face of the buy button while the wallet cannot cover the item.")]
        [SerializeField] Sprite disabledFace;
        [SerializeField] Image buyFace;
        [SerializeField] Sprite buyFaceNormal;

        [Header("Icons")]
        [SerializeField] IconEntry[] icons = Array.Empty<IconEntry>();
        [SerializeField] Sprite fallbackIcon;

        [Header("World")]
        [SerializeField] FinikShowcaseCamera showcase;
        [SerializeField] FinikMovementController movement;
        [SerializeField] FinikActivityController activity;
        [SerializeField] FinikAccessoryRig accessoryRig;
        [SerializeField] Behaviour[] pauseWhileOpen = Array.Empty<Behaviour>();
        [SerializeField] GameObject hudRoot;

        readonly Dictionary<string, Sprite> iconsByName = new(StringComparer.Ordinal);
        readonly List<FinikShopItem> shown = new();
        FinikShopCategory tab = FinikShopCategory.Need;
        FinikShopItem pending;
        string previewSlot;
        string previewAccessoryId;
        string previewPreviousId;
        bool previewActive;
        bool open;
        bool closing;
        FinikInteractionController pendingInteraction;
        Func<FinikInteractionController, string, bool> interactionHandler;

        public bool IsOpen => open;

        /// <summary>This menu takes the room over: the HUD goes away and Finik stands still.</summary>
        public bool HoldsRoom => true;

        /// <summary>Raised when the child taps «Заработать» on the not-enough card; the HUD opens quests.</summary>
        public event Action EarnRequested;

        /// <summary>
        /// Raised when the child taps the «Еда» card: the shop does not sell meals, it sends them to
        /// the fridge, where choosing dinner is a scene with a real trade-off.
        /// </summary>
        public event Action FoodRequested;
        bool openFoodAfterClose;

        void Awake()
        {
            foreach (var entry in icons)
                if (!string.IsNullOrEmpty(entry.name) && entry.sprite) iconsByName[entry.name] = entry.sprite;

            foreach (var view in items)
            {
                if (!view || !view.Choice) continue;
                var card = view;
                view.Choice.Clicked += _ => Pick(card.ItemId);
            }
            if (needsTab) needsTab.Clicked += _ => SelectTab(FinikShopCategory.Need);
            if (wantsTab) wantsTab.Clicked += _ => SelectTab(FinikShopCategory.Want);
            EnsureTabLayout();
            if (closeButton) closeButton.onClick.AddListener(Close);
            if (buyButton) buyButton.onClick.AddListener(Confirm);
            if (cancelButton) cancelButton.onClick.AddListener(ShowMain);
            if (shortBackButton) shortBackButton.onClick.AddListener(() =>
            {
                if (!FinikGame.AllDailyQuestsAnswered) Close();
                else ShowMain();
            });
            if (earnButton) earnButton.onClick.AddListener(Earn);
            if (resultCloseButton) resultCloseButton.onClick.AddListener(ShowMain);

            interactionHandler = HandleInteraction;
            FinikInteractionController.RegisterHandler(interactionHandler);

            if (main) main.Hide(instant: true);
            if (confirm) confirm.Hide(instant: true);
            if (shortPanel) shortPanel.Hide(instant: true);
            if (result) result.Hide(instant: true);
        }

        void EnsureTabLayout()
        {
            ConfigureFlexibleTab(needsTab);
            ConfigureFlexibleTab(wantsTab);

            var row = needsTab ? needsTab.transform.parent as RectTransform : null;
            if (!row) return;

            var group = row.GetComponent<HorizontalLayoutGroup>();
            if (group)
            {
                group.spacing = Mathf.Max(group.spacing, 16f);
                group.childControlWidth = true;
                group.childForceExpandWidth = false;
            }
            LayoutRebuilder.MarkLayoutForRebuild(row);
        }

        static void ConfigureFlexibleTab(FinikChoiceItem tab)
        {
            if (!tab) return;
            tab.SetSelectedScale(1f);
            var element = tab.GetComponent<LayoutElement>();
            if (!element) element = tab.gameObject.AddComponent<LayoutElement>();
            element.minWidth = 0f;
            element.preferredWidth = 0f;
            element.flexibleWidth = 1f;
        }

        void OnDestroy()
        {
            FinikScreens.Unregister(this);
            FinikInteractionController.UnregisterHandler(interactionHandler);
        }

        void OnEnable()
        {
            FinikScreens.Register(this);
            FinikGame.Changed += OnGameChanged;
            EnsureTabLayout();
        }

        void OnDisable()
        {
            EndAccessoryPreview(commit: false);
            FinikScreens.Unregister(this);
            FinikGame.Changed -= OnGameChanged;
        }

        // Coins can arrive while the shelf is open (a quest reward, the debug menu): keep prices honest.
        void OnGameChanged()
        {
            if (open && !closing && main && main.IsVisible) RenderShelf(animate: true);
        }

        bool HandleInteraction(FinikInteractionController controller, string interactionId)
        {
            if (interactionId != ShelfInteraction || open) return false;
            if (!Open()) return false;
            pendingInteraction = controller;
            return true;
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

        public Sprite IconFor(string spriteName)
        {
            EnsureIcons();
            return !string.IsNullOrEmpty(spriteName) && iconsByName.TryGetValue(spriteName, out var sprite)
                ? sprite
                : fallbackIcon;
        }

        Sprite EffectSprite(string spriteName)
        {
            EnsureIcons();
            return !string.IsNullOrEmpty(spriteName) && iconsByName.TryGetValue(spriteName, out var sprite)
                ? sprite
                : null;
        }

        // ------------------------------------------------------------------ open / close

        /// <summary>Opens the shop on «Нужно». False when there is no profile yet.</summary>
        public bool Open() => Open(FinikShopCategory.Need);

        /// <summary>Opens the shop on the requested category.</summary>
        public bool Open(FinikShopCategory initialTab)
        {
            if (open) return true;
            if (!FinikProfileStore.TryLoad(out _)) return false;
            FinikGame.EnsureJourney();

            open = true;
            closing = false;
            // One menu at a time: whatever else is up closes before this one takes the room.
            FinikScreens.CloseOthers(this);
            pending = null;
            tab = initialTab;
            if (hudRoot) hudRoot.SetActive(false);
            foreach (var behaviour in pauseWhileOpen) if (behaviour) behaviour.enabled = false;
            if (movement) movement.BeginActivity(this);
            if (showcase) showcase.Engage();
            RenderShelf(animate: false);
            ShowMain();
            if (FinikGame.AllDailyQuestsAnswered)
                FinikAudioManager.Instance.PlayAssistant(
                    tab == FinikShopCategory.Need ? FinikAudioManager.VoiceShopNeed : FinikAudioManager.VoiceShopWant,
                    interruptCurrent: true);
            return true;
        }

        public void Close()
        {
            if (!open || closing) return;
            EndAccessoryPreview(commit: false);
            closing = true;
            if (main) main.Hide();
            if (confirm) confirm.Hide();
            if (shortPanel) shortPanel.Hide();
            if (result) result.Hide();
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
            if (TakenOver()) yield break;

            foreach (var behaviour in pauseWhileOpen) if (behaviour) behaviour.enabled = true;
            if (movement) movement.EndActivity(this);
            HandBackInteraction();
            open = false;
            closing = false;
            if (openFoodAfterClose)
            {
                openFoodAfterClose = false;
                FoodRequested?.Invoke();
            }
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

        /// <summary>Lets the shelf Finik walked to know the visit is over, wherever the close ended.</summary>
        void HandBackInteraction()
        {
            if (!pendingInteraction) return;
            pendingInteraction.CompleteInteraction(ShelfInteraction);
            pendingInteraction = null;
        }

        void ShowMain()
        {
            if (closing) return;
            EndAccessoryPreview(commit: false);
            pending = null;
            if (confirm) confirm.Hide();
            if (shortPanel) shortPanel.Hide();
            if (result) result.Hide();
            if (!FinikGame.AllDailyQuestsAnswered)
            {
                ShowQuestGate();
                return;
            }
            RenderShelf(animate: true);
            if (main) main.Show();
        }

        void ShowQuestGate()
        {
            pending = null;
            if (main) main.Hide();
            if (confirm) confirm.Hide();
            if (result) result.Hide();
            if (shortIcon) shortIcon.sprite = IconFor("icon_task");
            if (shortTitle) shortTitle.text = "Сначала пройди задания";
            if (shortBody) shortBody.text = "Ответь на все задания на сегодня. После этого можно отправляться за покупками.";
            if (shortMath) shortMath.text = string.Empty;
            if (shortAdvice) shortAdvice.text = "Задания помогут решить, на что потратить монеты.";
            if (earnLabel) earnLabel.text = "К заданиям";
            if (shortPanel) shortPanel.Show();
        }

        // ------------------------------------------------------------------ the shelf

        public void SelectTab(FinikShopCategory category)
        {
            if (tab == category && shown.Count > 0) return;
            tab = category;
            RenderShelf(animate: true);
            FinikAudioManager.Instance.PlayAssistant(
                tab == FinikShopCategory.Need ? FinikAudioManager.VoiceShopNeed : FinikAudioManager.VoiceShopWant,
                interruptCurrent: true);
            if (scroll) scroll.verticalNormalizedPosition = 1f;
        }

        void RenderShelf(bool animate)
        {
            int balance = FinikGame.Balance;
            if (walletCoins) walletCoins.SetValue(balance, animate);
            if (title) title.text = "Покупки";

            bool needs = tab == FinikShopCategory.Need;
            if (needsTab) needsTab.SetSelected(needs);
            if (wantsTab) wantsTab.SetSelected(!needs);
            if (needsTabLabel) needsTabLabel.text = "Нужно";
            if (wantsTabLabel) wantsTabLabel.text = "Хочу";
            if (tabHint)
                tabHint.text = FinikTypography.Fix(needs
                    ? "Без этого Финику не обойтись: еда, чистота, школа."
                    : "Приятное, но не обязательное. Сначала — то, что нужно." + WantsEnvelopeLine());

            shown.Clear();
            shown.AddRange(FinikShopCatalog.ItemsIn(tab));
            for (int i = 0; i < items.Length; i++)
            {
                bool visible = i < shown.Count;
                if (!items[i]) continue;
                items[i].gameObject.SetActive(visible);
                if (!visible) continue;
                var item = shown[i];
                items[i].Show(item, IconFor(item.Icon), EffectSprite, balance, FinikGame.Owns(item.Id), IsWorn(item));
                items[i].Choice.SetSelected(false);
            }
        }

        // ------------------------------------------------------------------ confirmation

        void Pick(string itemId)
        {
            if (!FinikGame.AllDailyQuestsAnswered) { ShowQuestGate(); return; }
            if (closing || !FinikShopCatalog.TryGet(itemId, out var item)) return;
            pending = item;
            if (item.IsLink)
            {
                openFoodAfterClose = true;
                Close();
                return;
            }
            var preview = FinikGame.PreviewPurchase(itemId);

            // Something already owned is put straight back on: there is nothing to pay or confirm.
            if (preview.owned)
            {
                ToggleOwnedAccessory(item);
                return;
            }
            if (preview.affordable) ShowConfirm(preview);
            else ShowShortfall(preview);
        }

        void ShowConfirm(FinikPurchasePreview preview)
        {
            var item = preview.item;
            BeginAccessoryPreview(item);
            if (confirmIcon) confirmIcon.sprite = IconFor(item.Icon);
            if (confirmTitle) confirmTitle.text = FinikTypography.Fix($"Купить {item.Accusative.ToLowerInvariant()}?");
            if (confirmBody)
                confirmBody.text = FinikTypography.Fix(
                    $"Это покупка из «{item.CategoryLabel}». Спишется {FinikCoins.Amount(item.Price)}.");
            if (confirmEffect) confirmEffect.text = FinikTypography.Fix($"Финику: {item.EffectSentence().ToLowerInvariant()}");
            if (confirmBalance)
                // No arrow: the UI font has no glyph for one, and it came out as an empty box.
                confirmBalance.text = FinikTypography.Fix($"Было {preview.balanceBefore}, станет <b>{preview.balanceAfter}</b>" + OverEnvelopeLine(item));
            if (buyLabel) buyLabel.text = $"Купить за {item.Price}";
            if (buyButton) buyButton.interactable = true;
            if (buyFace && buyFaceNormal) buyFace.sprite = buyFaceNormal;

            if (main) main.Hide();
            if (shortPanel) shortPanel.Hide();
            if (confirm) confirm.Show();
        }

        /// <summary>
        /// The card the whole screen exists for: not «нельзя», but how much is missing, in numbers a
        /// child can check, plus the two honest ways out and the best thing that does fit right now.
        /// </summary>
        void ShowShortfall(FinikPurchasePreview preview)
        {
            EndAccessoryPreview(commit: false);
            var item = preview.item;
            if (shortIcon) shortIcon.sprite = IconFor(item.Icon);
            if (shortTitle) shortTitle.text = preview.DeficitTitle;
            // The numbers live in the sum below; the sentence only has to take the sting out.
            if (shortBody)
                shortBody.text = FinikTypography.Fix(
                    $"{item.Title} пока дороже, чем есть в кошельке. Ничего страшного — покупка никуда не денется.");
            if (shortMath)
                shortMath.text = FinikTypography.Fix($"{item.Price} − {preview.balanceBefore} = <b>{preview.deficit}</b>");
            if (shortAdvice) shortAdvice.text = FinikTypography.Fix(AdviceFor(preview));
            if (earnLabel) earnLabel.text = "Заработать монеты";

            if (main) main.Hide();
            if (confirm) confirm.Hide();
            if (shortPanel) shortPanel.Show();
        }

        /// <summary>
        /// What to do about the gap, in at most two short lines. The missing amount is already the
        /// headline and the sum, so it is never repeated here — a child reads the number once.
        /// </summary>
        string AdviceFor(FinikPurchasePreview preview)
        {
            var lines = new List<string>(2);
            int savings = FinikGame.Savings;
            if (savings >= preview.deficit && savings > 0)
                lines.Add("В копилке монеты есть, но она копится на цель.");
            var cheaper = FinikShopCatalog.CheapestAffordable(tab, preview.balanceBefore);
            if (cheaper != null && cheaper.Id != preview.item.Id)
                lines.Add($"Сейчас по карману «{cheaper.Title}» за {cheaper.Price}.");
            if (lines.Count < 2)
                lines.Add(cheaper == null ? "Монеты дают за задания." : "Ещё монет дают за задания.");
            return string.Join(" ", lines);
        }

        void Earn()
        {
            EarnRequested?.Invoke();
            Close();
        }

        // ------------------------------------------------------------------ paying

        void Confirm()
        {
            if (!FinikGame.AllDailyQuestsAnswered) { ShowQuestGate(); return; }
            if (pending == null) return;
            var item = pending;
            var outcome = FinikGame.Buy(item.Id);
            if (!outcome.Ok)
            {
                // The wallet moved between opening the card and confirming (a purchase in another
                // screen, a rolled-back reward): show the gap instead of a silent dead button.
                if (outcome.failure == FinikPurchaseFailure.InsufficientFunds)
                {
                    ShowShortfall(FinikGame.PreviewPurchase(item.Id));
                    return;
                }
                if (outcome.failure == FinikPurchaseFailure.AlreadyOwned)
                {
                    EndAccessoryPreview(commit: false);
                    ToggleOwnedAccessory(item);
                    return;
                }
                ShowMain();
                return;
            }

            if (item.IsAccessory)
            {
                // The preview is already fitted and worn. Re-equipping here recalculates its
                // placement against a later animation pose and makes it jump on purchase.
                if (previewActive && string.Equals(previewAccessoryId, item.AccessoryId, StringComparison.OrdinalIgnoreCase))
                    RememberAccessory(item.AccessoryId);
                else
                    Equip(item);
                EndAccessoryPreview(commit: true);
            }
            ShowResult(outcome);
        }

        void ShowResult(FinikPurchaseResult outcome)
        {
            var item = outcome.item;
            if (resultIcon) resultIcon.sprite = IconFor(item.Icon);
            if (resultTitle) resultTitle.text = FinikTypography.Fix($"Куплено: {item.Title}");
            if (resultBody) resultBody.text = FinikTypography.Fix(item.Result);
            SetDelta(resultCoins, -outcome.coinsSpent);
            SetDelta(resultFood, outcome.gained.food);
            SetDelta(resultMood, outcome.gained.mood);
            if (resultXpChip) resultXpChip.SetActive(outcome.xpGained > 0);
            SetDelta(resultXp, outcome.xpGained);

            if (main) main.Hide();
            if (confirm) confirm.Hide();
            if (shortPanel) shortPanel.Hide();
            if (result) result.Show();
            if (confetti) confetti.Burst(Vector2.zero);
            FinikAudioManager.Instance.PlayAssistant(
                item.Category == FinikShopCategory.Need ? FinikAudioManager.VoicePurchaseNeed : FinikAudioManager.VoicePurchaseWant,
                interruptCurrent: true);

            int foodGain = Mathf.RoundToInt(outcome.gained.food);
            int moodGain = Mathf.RoundToInt(outcome.gained.mood);
            if (foodGain > 0)
                FinikPetPopup.ShowDelta("icon_food", foodGain, "Сытость повысилась");
            if (moodGain > 0)
            {
                FinikPetPopup.ShowDelta("icon_mood", moodGain,
                    item.Category == FinikShopCategory.Want ? "Покупка порадовала" : "Стало веселее");
                if (item.Category == FinikShopCategory.Want)
                    FinikPetPopup.ShowMessage(UnityEngine.Random.value < .5f
                        ? "Ух ты! Как классно!"
                        : "Вот это мне нравится!");
            }
        }

        void SetDelta(TMP_Text text, float value)
        {
            if (!text) return;
            int rounded = Mathf.RoundToInt(value);
            text.text = rounded > 0 ? $"+{rounded}" : rounded < 0 ? $"–{-rounded}" : "0";
            text.color = rounded > 0 ? gainColor : rounded < 0 ? lossColor : neutralColor;
        }

        // ------------------------------------------------------------------ wearing

        /// <summary>Toggles an owned accessory without charging again.</summary>
        /// <summary>What is left of today's «Хочу» envelope, when the child has a plan.</summary>
        static string WantsEnvelopeLine() => FinikGame.HasBudgetPlan
            ? $"\nВ конверте «Хочу» осталось {FinikCoins.Amount(FinikGame.BudgetLeft(FinikBudgetBucket.Wants))}."
            : string.Empty;

        /// <summary>
        /// A want that does not fit the envelope is still allowed — the plan is the child's own —
        /// but it is said out loud before buying, so plan and fact never drift apart by surprise.
        /// </summary>
        static string OverEnvelopeLine(FinikShopItem item)
        {
            if (item == null || item.Category != FinikShopCategory.Want || !FinikGame.HasBudgetPlan) return string.Empty;
            int left = FinikGame.BudgetLeft(FinikBudgetBucket.Wants);
            return item.Price > left ? $"\nЭто больше, чем осталось в конверте «Хочу» ({left})." : string.Empty;
        }

        void ToggleOwnedAccessory(FinikShopItem item)
        {
            if (!item.IsAccessory)
            {
                ShowMain();
                return;
            }
            if (!accessoryRig) accessoryRig = FindAnyObjectByType<FinikAccessoryRig>(FindObjectsInactive.Include);
            bool wasWorn = IsWorn(item);
            if (wasWorn)
            {
                var entry = accessoryRig && accessoryRig.Catalog ? accessoryRig.Catalog.Find(item.AccessoryId) : null;
                if (entry != null) accessoryRig.Unequip(entry.slot);
                ForgetAccessory(item.AccessoryId);
            }
            else
            {
                Equip(item);
            }
            if (resultIcon) resultIcon.sprite = IconFor(item.Icon);
            bool worn = !wasWorn && accessoryRig && accessoryRig.AccessoriesEnabled;
            if (resultTitle) resultTitle.text = FinikTypography.Fix(worn ? $"Надето: {item.Title}" : $"Снято: {item.Title}");
            if (resultBody)
                resultBody.text = FinikTypography.Fix(worn
                    ? "Вещь надета. Купленные вещи можно менять бесплатно."
                    : "Вещь осталась в гардеробе — её можно снова надеть в любой момент.");
            SetDelta(resultCoins, 0);
            SetDelta(resultFood, 0);
            SetDelta(resultMood, 0);
            if (resultXpChip) resultXpChip.SetActive(false);

            if (main) main.Hide();
            if (confirm) confirm.Hide();
            if (shortPanel) shortPanel.Hide();
            if (result) result.Show();
        }

        /// <summary>
        /// Puts the accessory on Finik and writes it into the profile, which is what the onboarding
        /// re-equips from on the next launch — without this the clothes would vanish on restart.
        /// Demo profiles use the same compatible character skeletons, so purchases are equipped there too.
        /// </summary>
        void Equip(FinikShopItem item)
        {
            if (string.IsNullOrEmpty(item.AccessoryId)) return;
            if (!accessoryRig) accessoryRig = FindAnyObjectByType<FinikAccessoryRig>(FindObjectsInactive.Include);
            if (!accessoryRig || !accessoryRig.AccessoriesEnabled) return;
            try
            {
                accessoryRig.Equip(item.AccessoryId);
                RememberAccessory(item.AccessoryId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FinikShop] Accessory '{item.AccessoryId}' could not be worn: {e.Message}");
            }
        }

        void BeginAccessoryPreview(FinikShopItem item)
        {
            EndAccessoryPreview(commit: false);
            if (!item.IsAccessory || !accessoryRig || !accessoryRig.AccessoriesEnabled || !accessoryRig.Catalog) return;
            var entry = accessoryRig.Catalog.Find(item.AccessoryId);
            if (entry == null) return;
            previewSlot = entry.slot;
            previewAccessoryId = item.AccessoryId;
            accessoryRig.EquippedBySlot.TryGetValue(previewSlot, out previewPreviousId);
            try
            {
                accessoryRig.Equip(previewAccessoryId);
                previewActive = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FinikShop] Preview '{item.AccessoryId}' could not be shown: {e.Message}");
            }
        }

        void EndAccessoryPreview(bool commit)
        {
            if (!previewActive) return;
            if (!commit && accessoryRig)
            {
                accessoryRig.Unequip(previewSlot);
                if (!string.IsNullOrEmpty(previewPreviousId))
                {
                    try { accessoryRig.Equip(previewPreviousId); }
                    catch (Exception e) { Debug.LogWarning($"[FinikShop] Could not restore '{previewPreviousId}': {e.Message}"); }
                }
            }
            previewActive = false;
            previewSlot = previewAccessoryId = previewPreviousId = null;
        }

        bool IsWorn(FinikShopItem item)
        {
            if (!accessoryRig || !accessoryRig.Catalog) return false;
            var entry = accessoryRig.Catalog.Find(item.AccessoryId);
            return entry != null && accessoryRig.EquippedBySlot.TryGetValue(entry.slot, out var id) &&
                   string.Equals(id, item.AccessoryId, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Stores exactly one worn accessory per slot; ownership remains in game state.</summary>
        void RememberAccessory(string accessoryId)
        {
            if (!FinikProfileStore.TryLoad(out var profile)) return;
            var worn = new List<string>(profile.accessories ?? Array.Empty<string>());
            var entry = accessoryRig && accessoryRig.Catalog ? accessoryRig.Catalog.Find(accessoryId) : null;
            if (entry != null)
                worn.RemoveAll(id =>
                {
                    var other = accessoryRig.Catalog.Find(id);
                    return other != null && string.Equals(other.slot, entry.slot, StringComparison.OrdinalIgnoreCase);
                });
            if (worn.Contains(accessoryId)) return;
            worn.Add(accessoryId);
            profile.accessories = worn.ToArray();
            FinikProfileStore.Save(profile);
        }

        static void ForgetAccessory(string accessoryId)
        {
            if (!FinikProfileStore.TryLoad(out var profile)) return;
            var worn = new List<string>(profile.accessories ?? Array.Empty<string>());
            worn.RemoveAll(id => string.Equals(id, accessoryId, StringComparison.OrdinalIgnoreCase));
            profile.accessories = worn.ToArray();
            FinikProfileStore.Save(profile);
        }
    }
}
