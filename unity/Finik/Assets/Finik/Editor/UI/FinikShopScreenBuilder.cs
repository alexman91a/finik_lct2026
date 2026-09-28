using System.Collections.Generic;
using System.Linq;
using Finik.Accessories;
using Finik.Core;
using Finik.Navigation;
using Finik.UI;
using Finik.UI.Onboarding;
using Finik.UI.Shop;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static Finik.Editor.UI.FinikUiKit;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Builds the shop (Screen_Shop) over the room: the «Нужно» / «Хочу» shelf with its scrolling grid
    /// of item cards, the confirmation before every purchase, the card that explains how many coins are
    /// missing, and the result. Same column as the food, quest and piggy bank screens: beside Finik in
    /// landscape, under him in portrait.
    /// </summary>
    public static class FinikShopScreenBuilder
    {
        const string RootName = "Screen_Shop";
        const float Gap = 14f;
        const float PadX = 40f, PadY = 34f;
        const float HeaderHeight = 104f, ButtonsHeight = 112f;
        const float TabsHeight = 96f, TabHintHeight = 46f, ShelfHeight = 560f;
        const float ConfirmHeroHeight = 226f, ConfirmBodyHeight = 96f, ConfirmLineHeight = 44f;
        const float ShortBodyHeight = 92f, ShortMathHeight = 112f, ShortAdviceHeight = 88f;
        const float ResultHeroHeight = 236f, ResultBodyHeight = 104f, ResultDeltasHeight = 104f;
        const float LandscapeWidth = 860f, PortraitWidth = 1010f;

        /// <summary>Card slots built once and reused as the tabs switch; the biggest tab decides how many.</summary>
        const int MaxCards = 24;
        /// <summary>
        /// Tiles, two to a row in both orientations. A shop has to show the goods: a wide list row put
        /// a small icon in one corner and the price in the other with a dead strip between them, while
        /// a tile gives the picture the whole top and reads as merchandise.
        /// </summary>
        const float CardHeight = 330f;
        const int MaxEffectChips = 2;
        const float ChipWidth = 86f;

        static float MainHeight => 2 * PadY + HeaderHeight + TabsHeight + TabHintHeight + ShelfHeight + 3 * Gap;
        static float ConfirmHeight => 2 * PadY + HeaderHeight + ConfirmHeroHeight + ConfirmBodyHeight + 2 * ConfirmLineHeight + ButtonsHeight + 5 * Gap;
        static float ShortHeight => 2 * PadY + HeaderHeight + ShortBodyHeight + ShortMathHeight + ShortAdviceHeight + ButtonsHeight + 4 * Gap;
        static float ResultHeight => 2 * PadY + HeaderHeight + ResultHeroHeight + ResultBodyHeight + ResultDeltasHeight + ButtonsHeight + 4 * Gap;

        static readonly Color Cart = new(0.16f, 0.6f, 0.86f, 1f);
        static readonly Color Warn = new(0.86f, 0.3f, 0.2f, 1f);
        static readonly Color Good = new(0.13f, 0.62f, 0.3f, 1f);
        /// <summary>Chip plate: a pale card, not the HUD's dark glass, which reads as dirt on a light panel.</summary>
        static Color ChipPlate => FinikSdfCanvas.Hex("#EDF3FF");

        [MenuItem("Finik/UI/Rebuild Shop Screen")]
        public static void RebuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            if (!Prepare(out string message)) return message;
            var scene = EditorSceneManager.GetActiveScene();

            var finik = GameObject.Find("Finik_Root") ?? throw new System.InvalidOperationException("Finik_Root is missing from the scene.");
            var cameraGo = GameObject.Find("camera_gameplay") ?? throw new System.InvalidOperationException("camera_gameplay is missing from the scene.");
            var showcase = cameraGo.GetComponent<FinikShowcaseCamera>();
            if (!showcase) return "FinikShowcaseCamera is missing: run Finik/UI/Rebuild Onboarding first.";
            var hud = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "HUD_Home");
            if (!hud) return "Build the home HUD first (Finik/UI/Rebuild Home HUD).";

            // Same layer as the other room screens: they never open together.
            var root = ReuseOrCreateCanvas(RootName, 25);
            var screen = GetOrAdd<FinikShopScreen>(root);
            var safe = Rect("SafeArea", root.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<FinikSafeArea>();

            BuildShelf(safe, screen);
            BuildConfirm(safe, screen);
            BuildShortfall(safe, screen);
            BuildResult(safe, screen);
            WireIcons(screen);

            SetField(screen, "showcase", showcase);
            SetField(screen, "movement", finik.GetComponent<FinikMovementController>());
            SetField(screen, "activity", finik.GetComponent<FinikActivityController>());
            SetField(screen, "accessoryRig", finik.GetComponentInChildren<FinikAccessoryRig>(true));
            SetField(screen, "hudRoot", hud);
            var paused = new List<Object>();
            foreach (var behaviour in new Behaviour[] { finik.GetComponent<FinikInputController>(), finik.GetComponent<FinikWanderController>() })
                if (behaviour) paused.Add(behaviour);
            SetField(screen, "pauseWhileOpen", paused.ToArray());

            var binder = hud.GetComponent<FinikHudBinder>();
            if (binder) SetField(binder, "shopScreen", screen);

            // Every button answers at least a 48 dp tap; the component is a no-op on the big ones.
            EnsureTapTargets(root);

            EnsureEventSystem();
            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            int total = FinikShopCatalog.Items.Count;
            int art = FinikShopCatalog.Items.Count(i => SpriteOrNull(i.Icon));
            int needs = FinikShopCatalog.ItemsIn(FinikShopCategory.Need).Count;
            int biggest = Mathf.Max(needs, total - needs);
            string warning = biggest > MaxCards ? $" WARNING: a tab holds {biggest} items but only {MaxCards} cards are built." : string.Empty;
            return $"Built {RootName}: {total} items ({needs} needs / {total - needs} wants), {art} with final art.{warning}";
        }

        // ------------------------------------------------------------------ shared

        /// <summary>Full-screen panel with the card column; returns the vertical content stack and the column.</summary>
        static RectTransform Panel(Transform safe, string name, float height, out FinikScreenPanel panel, out RectTransform column)
        {
            var panelRoot = Rect(name, safe);
            Stretch(panelRoot);
            panelRoot.gameObject.AddComponent<CanvasGroup>();
            panel = panelRoot.gameObject.AddComponent<FinikScreenPanel>();

            column = Rect("Column", panelRoot);
            column.gameObject.AddComponent<FinikFitInside>();
            column.gameObject.AddComponent<FinikOrientationLayout>().Configure(
                FinikRectLayout.At(LeftMiddle, LeftMiddle, new Vector2(72, 0), new Vector2(LandscapeWidth, height)),
                FinikRectLayout.At(BottomCenter, BottomCenter, new Vector2(0, 36), new Vector2(PortraitWidth, height)));
            ShopPanelCard(column);
            var content = Rect("Content", column);
            Stretch(content, PadX, PadY, PadX, PadY);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = Gap;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panelRoot.gameObject.SetActive(false);
            return content;
        }

        /// <summary>
        /// The screen's backing card, deepened a shade and dropped on a shadow. The shelf lays pale
        /// tiles on it, and tiles the same colour as their panel read as one flat sheet — the panel has
        /// to sit back for the goods to come forward.
        /// </summary>
        static void ShopPanelCard(Transform column)
        {
            var shadow = Img("Shadow", column, "ui_card_shadow", sliced: true);
            Stretch(shadow.rectTransform, -16, -26, -16, -6);
            shadow.pixelsPerUnitMultiplier *= 1.6f;
            var card = Img("Card", column, "shop_panel", sliced: true, raycast: true);
            Stretch(card.rectTransform, -8, -14, -8, -8);
            Decor(column, "ui_decor_paw_card", BottomRight, BottomRight, new Vector2(-18, 18), new Vector2(120, 120), -14f, 0.3f);
        }

        /// <summary>Picture, small caps tag and title; the coin pill on the right when <paramref name="coins"/>.</summary>
        static TMP_Text Header(Transform content, string iconSprite, string tagText, Color tagColor, string titleText, bool coins, out FinikCounterText counter)
        {
            var row = Rect("Header", content);
            Size(row, height: HeaderHeight);
            var icon = Img("Icon", row, iconSprite, preserveAspect: true);
            Place(icon.rectTransform, LeftMiddle, LeftMiddle, Vector2.zero, new Vector2(104, 104));
            icon.gameObject.AddComponent<FinikIdleMotion>().Configure(3f, 3f, 0f, 1.8f, 0f);
            // The coin icon overhangs its pill on the left: keep the title clear of both.
            float right = coins ? 300 : 0;

            var tag = Text("Tag", row, tagText, 24, tagColor, TextAlignmentOptions.BottomLeft, outlined: false);
            tag.rectTransform.anchorMin = new Vector2(0, 0.66f);
            tag.rectTransform.anchorMax = Vector2.one;
            tag.rectTransform.offsetMin = new Vector2(124, 0);
            tag.rectTransform.offsetMax = new Vector2(-right, -2);
            tag.characterSpacing = 4;

            var title = Text("Title", row, titleText, 40, Ink, TextAlignmentOptions.TopLeft, outlined: false);
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = new Vector2(1, 0.66f);
            title.rectTransform.offsetMin = new Vector2(124, -4);
            title.rectTransform.offsetMax = new Vector2(-right, 0);
            AutoSize(title, 26, 40);

            counter = null;
            if (!coins) return title;
            var wallet = Rect("Coins", row);
            Place(wallet, RightMiddle, RightMiddle, new Vector2(0, 8), new Vector2(230, 76));
            var body = Rect("Body", wallet);
            Stretch(body);
            counter = CurrencyPill(body, "Coins", "icon_coin");
            return title;
        }

        static RectTransform Row(Transform content, string name, float height, float spacing = 18)
        {
            var row = Rect(name, content);
            Size(row, height: height);
            var group = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.childAlignment = TextAnchor.MiddleCenter;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = group.childForceExpandHeight = false;
            return row;
        }

        static TMP_Text LabelOf(Button button) => button.transform.Find("Label").GetComponent<TMP_Text>();

        static void AnchorTop(RectTransform rect, float left, float right, float top, float height)
        {
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.offsetMin = new Vector2(left, top - height);
            rect.offsetMax = new Vector2(-right, top);
        }

        /// <summary>Round close button on the card's top-right corner.</summary>
        static Button CloseButton(RectTransform column)
        {
            var closeRoot = Rect("Close", column);
            Place(closeRoot, TopRight, Center, new Vector2(-18, -18), new Vector2(84, 84));
            closeRoot.gameObject.AddComponent<FinikPressFeedback>();
            var face = Img("Face", closeRoot, "hud_btn_close", raycast: true, preserveAspect: true);
            Stretch(face.rectTransform);
            return MakeButton(closeRoot, face);
        }

        /// <summary>A big picture on a tinted plate, used at the top of the confirmation and result cards.</summary>
        static Image Hero(Transform content, string name, float height, string plate, out TMP_Text caption)
        {
            var hero = Img(name, content, plate, sliced: true);
            hero.pixelsPerUnitMultiplier *= 1.6f;
            Size(hero, height: height);

            var holder = Rect("Picture", hero.rectTransform);
            Place(holder, LeftMiddle, Center, new Vector2(140, 2), new Vector2(200, 200));
            var picture = Img("Image", holder, "shop_placeholder", preserveAspect: true);
            Stretch(picture.rectTransform);
            picture.gameObject.AddComponent<FinikIdleMotion>().Configure(2f, 4f, 0.02f, 2f, 3f);
            Decor(hero.rectTransform, "ui_spark_yellow", TopLeft, Center, new Vector2(40, -36), new Vector2(54, 54), -20f);

            caption = Text("Caption", hero.rectTransform, string.Empty, 34, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            Wrap(caption);
            AutoSize(caption, 24, 34);
            Stretch(caption.rectTransform, 262, 24, 32, 24);
            return picture;
        }

        // ------------------------------------------------------------------ the shelf

        /// <summary>
        /// The shelf's own panel: it fills the screen instead of sitting in a column beside Finik.
        /// Browsing a shop wants every tile it can get, and a narrow column could only ever show two
        /// of them. The dialogs that follow (confirmation, shortfall, result) stay centred cards.
        /// </summary>
        static RectTransform FullPanel(Transform safe, string name, out FinikScreenPanel panel, out RectTransform column)
        {
            var panelRoot = Rect(name, safe);
            Stretch(panelRoot);
            panelRoot.gameObject.AddComponent<CanvasGroup>();
            panel = panelRoot.gameObject.AddComponent<FinikScreenPanel>();

            column = Rect("Column", panelRoot);
            Orient(column, Inset(96, 46, 96, 36), Inset(28, 40, 28, 40));
            ShopPanelCard(column);
            var content = Rect("Content", column);
            Stretch(content, PadX, PadY, PadX, PadY);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = Gap;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panelRoot.gameObject.SetActive(false);
            return content;
        }

        static void BuildShelf(Transform safe, FinikShopScreen screen)
        {
            var content = FullPanel(safe, "Main", out var panel, out var column);
            SetField(screen, "main", panel);
            var title = Header(content, "icon_cart", "МАГАЗИН", Cart, "Покупки", coins: true, out var walletCoins);
            SetField(screen, "title", title);
            SetField(screen, "walletCoins", walletCoins);
            var close = CloseButton(column);
            SetField(screen, "closeButton", close);
            // A dim layer over the room: the shop is the whole screen, but the room stays alive behind
            // it — and a tap on the dim part closes the shelf.
            Dismiss(panel, close, 0.45f);

            // The two tabs the whole app teaches, as two big candy chips.
            var tabs = Row(content, "Tabs", TabsHeight, 16);
            var needs = TabChip(tabs, "Needs", "Нужно", out var needsLabel);
            var wants = TabChip(tabs, "Wants", "Хочу", out var wantsLabel);
            SetField(screen, "needsTab", needs);
            SetField(screen, "wantsTab", wants);
            SetField(screen, "needsTabLabel", needsLabel);
            SetField(screen, "wantsTabLabel", wantsLabel);

            var hint = Text("TabHint", content, "Без этого Финику не обойтись: еда, чистота, школа.", 24, InkSoft, TextAlignmentOptions.Midline, outlined: false);
            Wrap(hint);
            AutoSize(hint, 18, 24);
            Size(hint, height: TabHintHeight);
            SetField(screen, "tabHint", hint);

            BuildGrid(content, screen);
        }

        /// <summary>Scrolling grid of item cards: two per row in landscape, one wide row each in portrait.</summary>
        static void BuildGrid(Transform content, FinikShopScreen screen)
        {
            var viewport = Rect("Shelf", content);
            // Everything above it is fixed; the grid eats the rest of the screen, however tall it is.
            var element = Size(viewport, height: -1);
            element.minHeight = 300f;
            element.flexibleHeight = 1f;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.12f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.12f;
            scroll.scrollSensitivity = 40f;

            // The mask needs a graphic to clip against; a fully transparent one keeps the card visible.
            var maskImage = viewport.gameObject.AddComponent<Image>();
            maskImage.color = new Color(1, 1, 1, 0);
            maskImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = viewport;

            var grid = Rect("Content", viewport);
            grid.anchorMin = new Vector2(0, 1);
            grid.anchorMax = Vector2.one;
            grid.pivot = new Vector2(0.5f, 1);
            grid.offsetMin = new Vector2(0, 0);
            grid.offsetMax = Vector2.zero;
            scroll.content = grid;

            grid.gameObject.AddComponent<FinikFitGridLayout>()
                .Configure(4, CardHeight, 2, CardHeight, new Vector2(16f, 16f));
            var fitter = grid.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var cards = new Object[MaxCards];
            for (int i = 0; i < MaxCards; i++) cards[i] = ItemCard(grid, i);
            SetField(screen, "items", cards);
            SetField(screen, "scroll", scroll);
        }

        /// <summary>
        /// One tile: the goods big on top, then what they give Finik, the name, and the price at the
        /// foot. It sits on its own shadow, so the shelf reads as cards lying on a panel rather than
        /// as one flat sheet of blue.
        /// </summary>
        static FinikShopItemView ItemCard(Transform grid, int index)
        {
            var slot = Rect("Item" + index, grid);
            var body = Rect("Body", slot);
            Stretch(body);
            var press = body.gameObject.AddComponent<FinikPressFeedback>();

            var shadow = Img("Shadow", body, "ui_card_shadow", sliced: true);
            Stretch(shadow.rectTransform, -10, -16, -10, -4);
            shadow.pixelsPerUnitMultiplier *= 1.6f;

            var face = Img("Face", body, "shop_tile", sliced: true, raycast: true);
            Stretch(face.rectTransform);
            var button = MakeButton(body, face);
            var frame = Img("Selected", body, "shop_tile_frame", sliced: true, color: new Color(1, 1, 1, 0));
            Stretch(frame.rectTransform);

            // The picture owns the top of the tile: it is what the child is actually shopping for.
            var picture = Img("Image", body, "shop_placeholder", preserveAspect: true);
            Place(picture.rectTransform, TopCenter, TopCenter, new Vector2(0, -10), new Vector2(140, 140));
            picture.gameObject.AddComponent<FinikIdleMotion>().Configure(2f, 4f, 0.015f, 2f, index);

            // What it gives, as a centred row of chips right under the picture.
            var chipsRoot = Rect("Effects", body);
            AnchorTop(chipsRoot, 8, 8, -152, 36);
            var chipRow = chipsRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            chipRow.spacing = 10;
            chipRow.childAlignment = TextAnchor.MiddleCenter;
            chipRow.childControlWidth = chipRow.childControlHeight = false;
            chipRow.childForceExpandWidth = chipRow.childForceExpandHeight = false;

            var chips = new Object[MaxEffectChips];
            var chipIcons = new Object[MaxEffectChips];
            var chipLabels = new Object[MaxEffectChips];
            for (int i = 0; i < MaxEffectChips; i++)
            {
                var chip = Rect("Effect" + i, chipsRoot);
                Size(chip, ChipWidth, 36);
                chip.sizeDelta = new Vector2(ChipWidth, 36);
                var plate = Img("Plate", chip, "ui_card", sliced: true, color: ChipPlate);
                plate.pixelsPerUnitMultiplier *= 4f;
                Stretch(plate.rectTransform);
                var chipIcon = Img("Icon", chip, i == 0 ? "icon_food" : "icon_mood", preserveAspect: true);
                Place(chipIcon.rectTransform, LeftMiddle, Center, new Vector2(21, 0), new Vector2(30, 30));
                var chipLabel = Text("Label", chip, "+26", 20, Ink, TextAlignmentOptions.Midline, outlined: false);
                Stretch(chipLabel.rectTransform, 38, 2, 8, 0);
                chips[i] = chip.gameObject;
                chipIcons[i] = chipIcon;
                chipLabels[i] = chipLabel;
            }

            var title = Text("Title", body, "Полезный обед", 25, Ink, TextAlignmentOptions.Midline, outlined: false);
            AutoSize(title, 17, 25);
            AnchorTop(title.rectTransform, 10, 10, -190, 30);

            var subtitle = Text("Subtitle", body, "суп и второе", 17, InkSoft, TextAlignmentOptions.Midline, outlined: false);
            AutoSize(subtitle, 13, 17);
            AnchorTop(subtitle.rectTransform, 10, 10, -220, 22);

            // Price at the foot: a coin and a number, centred as one pair by the layout group.
            var priceRow = Rect("PriceTag", body);
            Place(priceRow, TopCenter, TopCenter, new Vector2(0, -252), new Vector2(150, 44));
            // The capsule hugs whatever the row ends up being: the fitter sizes the row to the coin and
            // the number, and the plate is pulled out of the layout so it can sit behind them.
            var priceFitter = priceRow.gameObject.AddComponent<ContentSizeFitter>();
            priceFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            priceFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            var priceFace = Img("Plate", priceRow, "shop_price", sliced: true);
            Stretch(priceFace.rectTransform, -18, -2, -18, -2);
            priceFace.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var priceLayout = priceRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            priceLayout.spacing = 8;
            priceLayout.childAlignment = TextAnchor.MiddleCenter;
            priceLayout.childControlWidth = true;
            priceLayout.childControlHeight = false;
            priceLayout.childForceExpandWidth = priceLayout.childForceExpandHeight = false;
            var coin = Img("Coin", priceRow, "icon_coin", preserveAspect: true);
            Size(coin, 38, 38);
            coin.rectTransform.sizeDelta = new Vector2(38, 38);
            var price = Text("Price", priceRow, "120", 28, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            Size(price, -1, 38);

            var shortfall = Text("Shortfall", body, "по карману", 17, Good, TextAlignmentOptions.Midline, outlined: false);
            AnchorTop(shortfall.rectTransform, 10, 10, -298, 22);
            AutoSize(shortfall, 13, 17);

            // «Открыть» / «Куплено» stand in for the price when there is nothing to pay.
            var owned = Rect("Owned", body);
            AnchorTop(owned, 38, 38, -252, 54);
            owned.gameObject.AddComponent<FinikPressFeedback>();
            var ownedPlate = Img("Plate", owned, "ui_btn_white", sliced: true);
            Stretch(ownedPlate.rectTransform);
            var ownedLabel = Text("Label", owned, "Открыть", 22, Color.white, TextAlignmentOptions.Midline);
            Stretch(ownedLabel.rectTransform, 16, 4, 16, 0);
            AutoSize(ownedLabel, 16, 22);
            owned.gameObject.SetActive(false);

            // Tiles spring in one after another when the shelf opens: the shop lands instead of
            // appearing. The stagger wraps every six so a long shelf never waits on a long queue.
            body.gameObject.AddComponent<CanvasGroup>();
            body.gameObject.AddComponent<FinikPopIn>().Configure(0.03f * (index % 6), 0.86f);

            var choice = slot.gameObject.AddComponent<FinikChoiceItem>();
            choice.Configure("item" + index, button, press, frame, null, null);
            SetField(choice, "selectedScale", 1.03f);

            var view = slot.gameObject.AddComponent<FinikShopItemView>();
            view.Configure(choice, picture, null, title, subtitle, price, priceRow.gameObject, shortfall, owned.gameObject, ownedLabel,
                System.Array.ConvertAll(chips, o => (GameObject)o),
                System.Array.ConvertAll(chipIcons, o => (Image)o),
                System.Array.ConvertAll(chipLabels, o => (TMP_Text)o));
            return view;
        }

        /// <summary>«Нужно» / «Хочу» chip: the tab's own card art, an orange ring when it is the open one.</summary>
        static FinikChoiceItem TabChip(Transform row, string name, string caption, out TMP_Text label)
        {
            var slot = Rect(name, row);
            // Two equal flexible tabs. Do not give them a fixed minimum width: on narrow phone
            // surfaces (Fold cover) 330 + 330 + spacing is wider than the available row.
            var tabLayout = Size(slot, width: -1, height: TabsHeight, flexibleWidth: 1);
            tabLayout.minWidth = 0f;
            tabLayout.preferredWidth = 0f;
            var body = Rect("Body", slot);
            Stretch(body);
            var press = body.gameObject.AddComponent<FinikPressFeedback>();
            var face = Img("Face", body, "shop_tab_off", sliced: true, raycast: true);
            Stretch(face.rectTransform);
            var button = MakeButton(body, face);
            var chosen = Img("Chosen", body, "shop_tab_on", sliced: true, color: new Color(1, 1, 1, 0));
            Stretch(chosen.rectTransform);

            label = Text("Label", body, caption, 34, Ink, TextAlignmentOptions.Midline, outlined: false);
            Stretch(label.rectTransform, 20, 4, 20, 0);
            AutoSize(label, 24, 34);

            var choice = slot.gameObject.AddComponent<FinikChoiceItem>();
            choice.Configure(name.ToLowerInvariant(), button, press, chosen, null, null);
            choice.SetSelectedScale(1f);
            return choice;
        }

        // ------------------------------------------------------------------ confirmation

        static void BuildConfirm(Transform safe, FinikShopScreen screen)
        {
            var content = Panel(safe, "Confirm", ConfirmHeight, out var panel, out _);
            SetField(screen, "confirm", panel);
            var title = Header(content, "icon_cart", "ПОКУПКА", Cart, "Купить полезный обед?", coins: false, out _);
            SetField(screen, "confirmTitle", title);

            var picture = Hero(content, "Hero", ConfirmHeroHeight, "ui_card_wants", out var effect);
            SetField(screen, "confirmIcon", picture);
            SetField(screen, "confirmEffect", effect);

            var body = Text("Body", content, "Это покупка из «Нужно». Спишется 120 монет.", 27, Ink, TextAlignmentOptions.TopLeft, outlined: false);
            Wrap(body);
            AutoSize(body, 20, 27);
            Size(body, height: ConfirmBodyHeight);
            SetField(screen, "confirmBody", body);

            var balance = Text("Balance", content, "Было 300, станет 180", 28, Ink, TextAlignmentOptions.Midline, outlined: false);
            Size(balance, height: ConfirmLineHeight);
            SetField(screen, "confirmBalance", balance);

            var buttons = Row(content, "Buttons", ButtonsHeight);
            var buy = WideButton(buttons, "Buy", "ui_btn_green", "Купить за 120", 32, Color.white, ButtonsHeight, 440, "ui_decor_paw_green");
            SetField(screen, "buyButton", buy);
            SetField(screen, "buyLabel", LabelOf(buy));
            SetField(screen, "buyFace", buy.targetGraphic);
            SetField(screen, "buyFaceNormal", SpriteOrNull("ui_btn_green"));
            SetField(screen, "disabledFace", SpriteOrNull("ui_btn_white"));
            var cancel = WideButton(buttons, "Cancel", "ui_btn_white", "Передумал", 30, Ink, ButtonsHeight, 300);
            SetField(screen, "cancelButton", cancel);
            // A tap beside the card buys nothing, exactly like «Передумал».
            Dismiss(panel, cancel);
        }

        // ------------------------------------------------------------------ not enough coins

        static void BuildShortfall(Transform safe, FinikShopScreen screen)
        {
            var content = Panel(safe, "Short", ShortHeight, out var panel, out _);
            SetField(screen, "shortPanel", panel);
            // The tag names the section, it does not repeat the headline: «НЕ ХВАТАЕТ МОНЕТ» over
            // «Не хватает 40 монет» was the same sentence twice.
            var title = Header(content, "icon_coin", "ПОКУПКА", Warn, "Не хватает 40 монет", coins: false, out _);
            SetField(screen, "shortTitle", title);

            var body = Text("Body", content, "«Полезный обед» стоит 120 монет, а в кошельке 80.", 27, Ink, TextAlignmentOptions.TopLeft, outlined: false);
            Wrap(body);
            AutoSize(body, 20, 27);
            Size(body, height: ShortBodyHeight);
            SetField(screen, "shortBody", body);

            // The arithmetic spelled out, with the item beside it: the gap is a number, not a locked door.
            var block = Img("Math", content, "ui_card", sliced: true, color: FinikSdfCanvas.Hex("#FFF1EE"));
            block.pixelsPerUnitMultiplier *= 1.6f;
            Size(block, height: ShortMathHeight);
            // The thing and its sum read as one group: a big picture, then the arithmetic right beside
            // it. Centred text with a small icon pinned to the far edge left the picture orphaned.
            var picture = Img("Image", block.rectTransform, "shop_placeholder", preserveAspect: true);
            Place(picture.rectTransform, LeftMiddle, LeftMiddle, new Vector2(26, 0), new Vector2(92, 92));
            SetField(screen, "shortIcon", picture);
            var math = Text("Label", block.rectTransform, "120 − 80 = 40", 38, Warn, TextAlignmentOptions.MidlineLeft, outlined: false);
            Stretch(math.rectTransform, 136, 4, 28, 0);
            AutoSize(math, 28, 38);
            SetField(screen, "shortMath", math);

            var advice = Text("Advice", content, "За задания дают монеты: ещё 40 монет — и предмет твой.", 25, InkSoft, TextAlignmentOptions.TopLeft, outlined: false);
            Wrap(advice);
            AutoSize(advice, 18, 25);
            Size(advice, height: ShortAdviceHeight);
            SetField(screen, "shortAdvice", advice);

            var buttons = Row(content, "Buttons", ButtonsHeight);
            var earn = WideButton(buttons, "Earn", "ui_btn_green", "Заработать монеты", 30, Color.white, ButtonsHeight, 460);
            SetField(screen, "earnButton", earn);
            SetField(screen, "earnLabel", LabelOf(earn));
            var back = WideButton(buttons, "Back", "ui_btn_white", "Понятно", 30, Ink, ButtonsHeight, 280);
            SetField(screen, "shortBackButton", back);
            Dismiss(panel, back);
        }

        // ------------------------------------------------------------------ result

        static void BuildResult(Transform safe, FinikShopScreen screen)
        {
            var content = Panel(safe, "Result", ResultHeight, out var panel, out var column);
            SetField(screen, "result", panel);
            var title = Header(content, "icon_star", "ГОТОВО", Good, "Полезный обед — куплено!", coins: false, out _);
            SetField(screen, "resultTitle", title);

            var picture = Hero(content, "Hero", ResultHeroHeight, "ui_card_savings", out var comment);
            SetField(screen, "resultIcon", picture);
            comment.gameObject.SetActive(false);
            HeroSparkles(picture.transform.parent, 100f);

            var body = Text("Body", content, "Полный обед — полный животик.", 27, Ink, TextAlignmentOptions.TopLeft, outlined: false);
            Wrap(body);
            AutoSize(body, 20, 27);
            Size(body, height: ResultBodyHeight);
            SetField(screen, "resultBody", body);

            // What changed, as four chips in one row: coins out, needs up, XP when it was the first time.
            var deltas = Row(content, "Deltas", ResultDeltasHeight, 14);
            SetField(screen, "resultCoins", DeltaChip(deltas, "Coins", "icon_coin", "–120"));
            SetField(screen, "resultFood", DeltaChip(deltas, "Food", "icon_food", "+26"));
            SetField(screen, "resultMood", DeltaChip(deltas, "Mood", "icon_mood", "+5"));
            var xp = DeltaChip(deltas, "Xp", "icon_star", "+6");
            SetField(screen, "resultXp", xp);
            SetField(screen, "resultXpChip", xp.transform.parent.gameObject);

            var buttons = Row(content, "Buttons", ButtonsHeight);
            var close = WideButton(buttons, "Done", "ui_btn_green", "Готово", 32, Color.white, ButtonsHeight, 420, "ui_decor_paw_green");
            SetField(screen, "resultCloseButton", close);
            Dismiss(panel, close);

            var confettiLayer = Rect("Confetti", column.parent);
            Stretch(confettiLayer);
            var confetti = confettiLayer.gameObject.AddComponent<FinikConfettiBurst>();
            SetField(confetti, "pieceSprite", SpriteOrNull("ui_piece"));
            SetField(screen, "confetti", confetti);
        }

        static TMP_Text DeltaChip(Transform row, string name, string icon, string sample)
        {
            var slot = Rect(name, row);
            Size(slot, width: 168, height: 88, flexibleWidth: 1);
            var plate = Img("Plate", slot, "ui_card", sliced: true, color: ChipPlate);
            plate.pixelsPerUnitMultiplier *= 2.6f;
            Stretch(plate.rectTransform);
            var image = Img("Icon", slot, icon, preserveAspect: true);
            Place(image.rectTransform, LeftMiddle, Center, new Vector2(38, 0), new Vector2(52, 52));
            var label = Text("Label", slot, sample, 28, Ink, TextAlignmentOptions.Midline, outlined: false);
            Stretch(label.rectTransform, 66, 2, 14, 0);
            AutoSize(label, 20, 28);
            return label;
        }

        // ------------------------------------------------------------------ icons

        static void WireIcons(FinikShopScreen screen)
        {
            var so = new SerializedObject(screen);
            var icons = so.FindProperty("icons");
            icons.arraySize = 0;

            void Add(string spriteName)
            {
                var sprite = SpriteOrNull(spriteName);
                if (!sprite) return;
                for (int i = 0; i < icons.arraySize; i++)
                    if (icons.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == spriteName) return;
                int index = icons.arraySize++;
                var element = icons.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("name").stringValue = spriteName;
                element.FindPropertyRelative("sprite").objectReferenceValue = sprite;
            }

            foreach (var item in FinikShopCatalog.Items) Add(item.Icon);
            // The effect chips look their sprites up by the same table.
            Add("icon_food");
            Add("icon_mood");
            so.FindProperty("fallbackIcon").objectReferenceValue = SpriteOrNull("shop_placeholder") ?? SpriteOrNull("icon_cart");
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
