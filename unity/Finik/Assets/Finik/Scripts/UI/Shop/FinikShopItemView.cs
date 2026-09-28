using Finik.Core;
using Finik.UI.Onboarding;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Shop
{
    /// <summary>
    /// One thing on the shelf: picture, what it is, what it gives Finik and what it costs.
    ///
    /// A card the wallet cannot cover is never switched off — it says «не хватает N» in red and still
    /// opens, because the point of the screen is to explain the gap, not to hide it. An accessory
    /// already bought shows «Куплено» instead of the price and offers to put it back on, and a
    /// signpost card (the «Еда» one) shows «Открыть» and leads to the screen that owns that flow.
    /// </summary>
    public sealed class FinikShopItemView : MonoBehaviour
    {
        [SerializeField] FinikChoiceItem choice;
        [SerializeField] Image icon;
        [SerializeField] TMP_Text groupTag;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text subtitle;
        [SerializeField] TMP_Text price;
        [Tooltip("The gold price pill as a whole; hidden when the item is already owned.")]
        [SerializeField] GameObject priceRoot;
        [SerializeField] TMP_Text shortfall;
        [SerializeField] GameObject ownedBadge;
        [SerializeField] TMP_Text ownedLabel;

        [Header("Effects")]
        [SerializeField] GameObject[] effectChips = System.Array.Empty<GameObject>();
        [SerializeField] Image[] effectIcons = System.Array.Empty<Image>();
        [SerializeField] TMP_Text[] effectLabels = System.Array.Empty<TMP_Text>();

        [Header("Colors")]
        [SerializeField] Color shortColor = new(0.86f, 0.24f, 0.28f, 1f);
        [SerializeField] Color affordColor = new(0.13f, 0.62f, 0.3f, 1f);
        [Tooltip("Faded while the wallet cannot cover the price. Left empty the row never fades (V1).")]
        [SerializeField] CanvasGroup outOfReach;
        [SerializeField, Range(0.3f, 1f)] float outOfReachAlpha = 0.55f;

        public FinikChoiceItem Choice => choice;
        public string ItemId { get; private set; }

        public void Configure(FinikChoiceItem item, Image iconImage, TMP_Text tag, TMP_Text titleText, TMP_Text subtitleText,
            TMP_Text priceText, GameObject priceTag, TMP_Text shortfallText, GameObject owned, TMP_Text ownedText,
            GameObject[] chips, Image[] chipIcons, TMP_Text[] chipLabels)
        {
            choice = item;
            icon = iconImage;
            groupTag = tag;
            title = titleText;
            subtitle = subtitleText;
            price = priceText;
            priceRoot = priceTag;
            shortfall = shortfallText;
            ownedBadge = owned;
            ownedLabel = ownedText;
            effectChips = chips;
            effectIcons = chipIcons;
            effectLabels = chipLabels;
        }

        /// <summary>Fills the card from the catalog and the wallet. <paramref name="sprite"/> may be the fallback.</summary>
        public void Show(FinikShopItem item, Sprite sprite, System.Func<string, Sprite> effectSprite, int balance, bool owned, bool worn)
        {
            ItemId = item.Id;
            if (icon) icon.sprite = sprite;
            if (groupTag) groupTag.text = item.Group.ToUpperInvariant();
            if (title) title.text = FinikTypography.Fix(item.Title);
            if (subtitle) subtitle.text = FinikTypography.Fix(item.Subtitle);

            for (int i = 0; i < effectChips.Length; i++)
            {
                bool shown = i < item.Effects.Count;
                if (effectChips[i]) effectChips[i].SetActive(shown);
                if (!shown) continue;
                var effect = item.Effects[i];
                if (i < effectIcons.Length && effectIcons[i] && effectSprite != null)
                {
                    var chipSprite = effectSprite(effect.icon);
                    if (chipSprite) effectIcons[i].sprite = chipSprite;
                }
                if (i < effectLabels.Length && effectLabels[i]) effectLabels[i].text = effect.Chip;
            }

            // A signpost card buys nothing, so it carries no price, no effects and no shortfall — just
            // an invitation to the screen it points at. Owned accessories are worn again for free, so
            // the price would be a lie on those too.
            bool wearable = owned && item.IsAccessory;
            bool badge = item.IsLink || wearable;
            if (item.IsLink)
                foreach (var chip in effectChips)
                    if (chip) chip.SetActive(false);

            if (ownedBadge) ownedBadge.SetActive(badge);
            // Both states sit on the same candy face, so the caption stays white and outlined; the
            // word itself says which one it is.
            if (ownedLabel) ownedLabel.text = item.IsLink ? "Открыть" : worn ? "Снять" : "Надеть";
            if (priceRoot) priceRoot.SetActive(!badge);

            int deficit = item.IsLink ? 0 : Mathf.Max(0, item.Price - Mathf.Max(0, balance));
            // The pill keeps the price white on gold; a wallet that cannot cover it says so in the
            // red line underneath instead, so the number stays readable either way.
            if (price) price.text = item.Price.ToString();
            // Only the gap is worth a line. «По карману» under every affordable tile was the same
            // sentence twenty times over, and it is already implied by the coins in the header.
            // Out of reach is shown, not hidden: the row goes quiet and says by how much, because
            // the point of the screen is to explain the gap rather than pretend it is not there.
            if (outOfReach) outOfReach.alpha = !badge && deficit > 0 ? outOfReachAlpha : 1f;
            if (shortfall)
            {
                shortfall.gameObject.SetActive(!badge && deficit > 0);
                shortfall.text = $"не хватает {deficit}";
                shortfall.color = shortColor;
            }
        }
    }
}
