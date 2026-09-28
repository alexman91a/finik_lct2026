using System.Collections.Generic;
using System.Linq;
using Finik.Accessories;
using Finik.Navigation;
using Finik.UI;
using Finik.UI.Onboarding;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static Finik.Editor.UI.FinikUiKit;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Builds the first-run onboarding (title, money basics, pets, pet name, finale) as an
    /// overlay canvas on the room scene and wires it to Finik, the camera and the home HUD.
    /// </summary>
    public static class FinikOnboardingBuilder
    {
        const string RootName = "UI_Onboarding";
        const string CatalogPath = "Assets/Finik/Accessories/FinikAccessoryCatalog.asset";
        const float Gap = 18f;


        // Column that holds each screen: left of Finik in landscape, under him in portrait.
        static readonly FinikRectLayout LandscapeColumn = FinikRectLayout.At(LeftMiddle, LeftMiddle, new Vector2(72, 0), new Vector2(860, 960));
        static readonly FinikRectLayout PortraitColumn = FinikRectLayout.At(BottomCenter, BottomCenter, new Vector2(0, 36), new Vector2(1010, 1080));

        [MenuItem("Finik/UI/Rebuild Onboarding")]
        public static void RebuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            if (!Prepare(out string message)) return message;
            var scene = EditorSceneManager.GetActiveScene();

            var finik = GameObject.Find("Finik_Root") ?? throw new System.InvalidOperationException("Finik_Root is missing from the scene.");
            var cameraGo = GameObject.Find("camera_gameplay") ?? throw new System.InvalidOperationException("camera_gameplay is missing from the scene.");
            var hud = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "HUD_Home");
            if (!hud) return "Build the home HUD first (Finik/UI/Rebuild Home HUD).";

            var catalog = AssetDatabase.LoadAssetAtPath<FinikAccessoryCatalog>(CatalogPath);
            var rig = finik.GetComponent<FinikAccessoryRig>();
            if (!rig) rig = Undo.AddComponent<FinikAccessoryRig>(finik);
            if (!rig.Catalog) rig.Catalog = catalog;
            EditorUtility.SetDirty(rig);
            var showcase = cameraGo.GetComponent<FinikShowcaseCamera>();
            if (!showcase) showcase = Undo.AddComponent<FinikShowcaseCamera>(cameraGo);
            // Framed for the Stage 1 model (~1.55 m after its 0.8 scale).
            SetField(showcase, "lookHeight", 0.82f);
            SetField(showcase, "landscapeDistance", 4.0f);
            SetField(showcase, "landscapeShift", new Vector2(-0.95f, 0.05f));
            // Portrait: Finik fills the upper half above the card; stay inside the room (no sky).
            SetField(showcase, "portraitDistance", 3.6f);
            SetField(showcase, "portraitShift", new Vector2(0f, -0.62f));
            SetField(showcase, "portraitFov", 50f);
            SetField(showcase, "landscapeFov", 28.5f);
            SetField(showcase, "cameraHeight", 1.1f);

            var root = ReuseOrCreateCanvas(RootName, 30);
            var flow = GetOrAdd<FinikOnboardingFlow>(root);
            var safe = Rect("SafeArea", root.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<FinikSafeArea>();

            BuildStage(safe, flow);
            var title = BuildTitle(safe, flow);
            var basics = BuildBasics(safe, flow);
            var pets = BuildPets(safe, flow);
            var customize = BuildCustomize(safe, flow);
            var finale = BuildFinale(safe, flow);
            var tutorial = BuildTutorial(safe, flow);

            var confettiLayer = Rect("Confetti", safe);
            Stretch(confettiLayer);
            var confetti = confettiLayer.gameObject.AddComponent<FinikConfettiBurst>();
            SetField(confetti, "pieceSprite", SpriteOrNull("ui_piece"));
            SetField(flow, "confetti", confetti);

            SetField(flow, "titleScreen", title);
            SetField(flow, "basicsScreen", basics);
            SetField(flow, "petsScreen", pets);
            SetField(flow, "customizeScreen", customize);
            SetField(flow, "finaleScreen", finale);
            SetField(flow, "tutorialScreen", tutorial);

            WireWorld(flow, finik, showcase, rig, hud);

            // Every button answers at least a 48 dp tap; the component is a no-op on the big ones.
            EnsureTapTargets(root);

            EnsureEventSystem();
            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"Built {RootName}.";
        }

        // ------------------------------------------------------------------ screens

        static (FinikScreenPanel panel, RectTransform column) BuildScreen(string name, Transform parent, bool card, float spacing = Gap)
        {
            var root = Rect(name, parent);
            Stretch(root);
            root.gameObject.AddComponent<CanvasGroup>();
            var panel = root.gameObject.AddComponent<FinikScreenPanel>();

            var column = Rect("Column", root);
            column.gameObject.AddComponent<FinikFitInside>();
            column.gameObject.AddComponent<FinikOrientationLayout>().Configure(LandscapeColumn, PortraitColumn);
            if (card) PanelCard(column);

            var content = Rect("Content", column);
            Stretch(content, card ? 44 : 0, card ? 40 : 0, card ? 44 : 0, card ? 40 : 0);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return (panel, content);
        }

        static FinikScreenPanel BuildTitle(Transform parent, FinikOnboardingFlow flow)
        {
            var (panel, column) = BuildScreen("Screen_Title", parent, card: false);
            var logoSprite = SpriteOrNull("ui_logo");
            if (logoSprite)
            {
                var holder = Rect("Logo", column);
                Size(holder, height: 250);
                var logo = Img("Image", holder, "ui_logo", preserveAspect: true);
                Stretch(logo.rectTransform);
                logo.gameObject.AddComponent<FinikIdleMotion>().Configure(6f, 1.5f, 0f, 1.2f, 0f);
                // Sparkles around the logo (about 675 wide in its 260-high holder).
                foreach (int side in new[] { -1, 1 })
                    Decor(holder, "ui_spark_yellow", Center, Center, new Vector2(side * 300, 96), new Vector2(68, 68), side * -38f);
            }
            else
            {
                var holder = Rect("Logo", column);
                Size(holder, height: 260);
                var logo = Text("Text", holder, "FINIK", 190, FinikSdfCanvas.Hex("#FFB321"), TextAlignmentOptions.Center);
                Stretch(logo.rectTransform);
                logo.enableVertexGradient = true;
                logo.colorGradient = new VertexGradient(FinikSdfCanvas.Hex("#FFE066"), FinikSdfCanvas.Hex("#FFE066"), FinikSdfCanvas.Hex("#FF8A1F"), FinikSdfCanvas.Hex("#FF8A1F"));
                logo.color = Color.white;
                logo.gameObject.AddComponent<FinikIdleMotion>().Configure(6f, 1.5f, 0f, 1.2f, 0f);
            }
            // The pitch names the money loop (care, choose, save) in words a 7-11 year old reads at a glance;
            // each tile pairs a picture with its words, so the icons explain the text instead of repeating it.
            Pitch(column, ("icon_food", "Заботься\nо питомце"), ("icon_cart", "Решай,\nна что тратить"), ("icon_piggy_coin", "Копи\nна мечту!"));
            // Room for the Play button's sparkles above its corner.
            Spacer(column, 10);
            var play = WideButton(ButtonRow(column, 160), "Play", "ui_btn_green", "Играть", 64, Color.white, 160, 600, "ui_decor_chevron");
            // Breathe on the root: the body's scale belongs to FinikPressFeedback.
            play.transform.parent.gameObject.AddComponent<FinikIdleMotion>().Configure(0f, 0f, 0.025f, 2.2f, 0f);
            var demo = WideButton(ButtonRow(column, 118), "Demo", "ui_btn_white", "Посмотреть готовый мир", 34, Ink, 118, 600);
            SetField(flow, "playButton", play);
            SetField(flow, "demoButton", demo);
            return panel;
        }

        /// <summary>
        /// The title pitch as a row of light tiles, a big picture over a two-line caption each. They are
        /// not buttons (no action); a tap only springs the tile, since kids will poke them anyway.
        /// </summary>
        static void Pitch(Transform column, params (string icon, string caption)[] tiles)
        {
            const float tileWidth = 270f, tileHeight = 272f, gap = 18f, iconSize = 132f, padX = 14f, maxFont = 34f;
            var row = Rect("Pitch", column);
            Size(row, height: tileHeight);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = gap;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            var captions = new List<TextMeshProUGUI>();
            for (int i = 0; i < tiles.Length; i++)
            {
                var tile = Rect("Tile_" + tiles[i].icon, row);
                Size(tile, tileWidth, tileHeight);
                var body = Rect("Body", tile);
                Stretch(body);
                var feedback = body.gameObject.AddComponent<FinikPressFeedback>();
                SetField(feedback, "pressedScale", 0.94f);
                var card = Img("Card", body, "ui_card", sliced: true, raycast: true);
                Stretch(card.rectTransform);

                var icon = Img("Icon", body, tiles[i].icon, preserveAspect: true);
                Place(icon.rectTransform, TopCenter, TopCenter, new Vector2(0, -20), Vector2.one * iconSize);
                // Out of phase, so the pictures don't bob in lockstep.
                icon.gameObject.AddComponent<FinikIdleMotion>().Configure(4f, 3f, 0f, 1.4f, i * 0.8f);

                var caption = Text("Caption", body, tiles[i].caption, maxFont, Ink, TextAlignmentOptions.Center, outlined: false);
                Stretch(caption.rectTransform, padX, 18, padX, 20 + iconSize + 4);
                captions.Add(caption);
            }
            // One size for every caption (per-caption auto-size would shrink only the longest): the largest
            // at which the widest caption line still fits the tile.
            float widest = captions.Max(t => t.GetPreferredValues(t.text).x);
            float font = Mathf.Clamp(Mathf.Floor(maxFont * (tileWidth - padX * 2) / widest), 26f, maxFont);
            foreach (var caption in captions) caption.fontSize = font;
        }

        // Нужно / Хочу / Коплю: all three money choices at once, a softly tinted card each, plus what Finik
        // says when the card is tapped.
        static readonly (string id, string card, string icon, string fallbackIcon, string title, string accent, string lead, string body, string reaction)[] BasicCards =
        {
            ("Needs", "ui_card_needs", "ill_needs", "icon_food", "НУЖНО", "#3570D4",
                "То, без чего Финику трудно.", "Еда, уход, важные вещи.",
                "Без еды и заботы мне грустно. Это — в первую очередь!"),
            ("Wants", "ui_card_wants", "ill_wants", "icon_star", "ХОЧУ", "#E0703A",
                "То, что радует Финика.", "Игрушки, одежда, развлечения.",
                "Ура, игрушки! Только всё сразу купить не получится."),
            ("Savings", "ui_card_savings", "ill_savings", "icon_piggy_coin", "КОПЛЮ", "#2E9460",
                "Монеты для большой цели.", "Откладывай понемногу — и мечта станет ближе.",
                "Моё любимое! Понемногу — и мечта уже близко.")
        };

        static FinikScreenPanel BuildBasics(Transform parent, FinikOnboardingFlow flow)
        {
            var (panel, column) = BuildScreen("Screen_Basics", parent, card: false, spacing: 12);

            var bubble = Img("Speech", column, "ui_bubble", sliced: true);
            DecorateBubble(bubble.rectTransform);
            Size(bubble, height: 188);
            var speech = Text("Text", bubble.transform, "", 36, Ink, TextAlignmentOptions.TopLeft, outlined: false);
            speech.textWrappingMode = TextWrappingModes.Normal;
            // The intro's first line is a touch wider than the landscape bubble at full size.
            AutoSize(speech, 30, 36);
            Stretch(speech.rectTransform, 44, 64, 40, 34);
            var typewriter = speech.gameObject.AddComponent<FinikTypewriter>();

            var so = new SerializedObject(flow);
            var array = so.FindProperty("basics");
            array.arraySize = BasicCards.Length;
            var bodies = new List<TextMeshProUGUI>();
            for (int i = 0; i < BasicCards.Length; i++)
            {
                var (button, feedback, body) = BasicCard(column, BasicCards[i], i);
                bodies.Add(body);
                var element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("button").objectReferenceValue = button;
                element.FindPropertyRelative("feedback").objectReferenceValue = feedback;
                element.FindPropertyRelative("reaction").stringValue = BasicCards[i].reaction;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            // One size for every second line, the largest at which the longest still fits on one line of
            // the narrower (landscape) column; per-text auto-size would shrink only the longest.
            float widest = bodies.Max(t => t.GetPreferredValues(t.text).x);
            float font = Mathf.Clamp(Mathf.Floor(BasicBodyFont * BasicTextWidth / widest), MinimumReadableFontSize, BasicBodyFont);
            foreach (var body in bodies) body.fontSize = font;

            var motto = Text("Motto", column, FinikTypography.Fix("Главное — не потратить всё сразу!"), 38, Color.white, TextAlignmentOptions.Center);
            Size(motto, height: 54);
            // Room for the button's sparkles above its corner.
            Spacer(column, 6);

            var row = ButtonRow(column, 118);
            var back = WideButton(row, "Back", "ui_btn_white", "Назад", 34, Ink, 118, 220);
            var next = WideButton(row, "Next", "ui_btn_orange", "Понятно!", 50, Color.white, 118, 480, "ui_decor_chevron");

            // Over the column, so the hand draws on top of every card.
            SetField(flow, "basicsHint", TapHint(panel.transform));
            SetField(flow, "speech", typewriter);
            // Explicit break, and TMP would otherwise wrap at the hyphen of "по-разному".
            SetField(flow, "basicsIntro", "Монеты можно использовать <nobr>по-разному.</nobr>\nВыбирай сам!");
            SetField(flow, "basicsNext", next);
            SetField(flow, "basicsBack", back);
            return panel;
        }

        /// <summary>A tapping hand with a ripple under its fingertip (see <see cref="FinikTapHint"/>); never takes taps.</summary>
        static FinikTapHint TapHint(Transform parent)
        {
            var root = Rect("TapHint", parent);
            Stretch(root);
            root.gameObject.AddComponent<CanvasGroup>();
            var hint = root.gameObject.AddComponent<FinikTapHint>();
            var tip = Rect("Tip", root);
            Place(tip, Center, Center, Vector2.zero, Vector2.zero);
            var ripple = Img("Ripple", tip, "hud_tap_ring", color: FinikSdfCanvas.Hex("#FF8A1F"), preserveAspect: true);
            Place(ripple.rectTransform, Center, Center, Vector2.zero, new Vector2(150, 150));
            ripple.gameObject.AddComponent<CanvasGroup>();
            var hand = Img("Hand", tip, "hud_hand", preserveAspect: true);
            // Pivot on the fingertip, whatever art hud_hand is; tilted so the hand comes in from the lower right.
            Place(hand.rectTransform, Center, Fingertip(hand.sprite), Vector2.zero, new Vector2(150, 150));
            hand.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 22f);
            SetField(hint, "tip", tip);
            SetField(hint, "hand", hand.rectTransform);
            SetField(hint, "ripple", ripple.rectTransform);
            return hint;
        }

        /// <summary>
        /// Fingertip of an upward-pointing hand as a fraction of its sprite: the middle of the topmost
        /// opaque pixels, nudged a little into the finger so the tap lands on the pad, not on the outline.
        /// Read from the PNG itself, so generated and hand-drawn art both work without a tuned constant.
        /// </summary>
        static Vector2 Fingertip(Sprite sprite)
        {
            var fallback = new Vector2(0.5f, 0.95f);
            string path = sprite ? AssetDatabase.GetAssetPath(sprite) : null;
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return fallback;
            var texture = new Texture2D(2, 2);
            try
            {
                if (!texture.LoadImage(System.IO.File.ReadAllBytes(path))) return fallback;
                var pixels = texture.GetPixels32();
                int width = texture.width, height = texture.height;
                // The finger's top few rows: at the very top only a sliver of the rounded tip is opaque.
                int band = Mathf.Max(2, height / 40);
                for (int y = height - 1; y >= 0; y--)
                {
                    int top = -1;
                    for (int x = 0; x < width && top < 0; x++) if (pixels[y * width + x].a > 128) top = y;
                    if (top < 0) continue;
                    long sum = 0;
                    int count = 0;
                    for (int row = top; row > top - band && row >= 0; row--)
                        for (int x = 0; x < width; x++)
                            if (pixels[row * width + x].a > 128) { sum += x; count++; }
                    return new Vector2((sum / (float)count + 0.5f) / width, (top + 1 - band) / (float)height);
                }
                return fallback;
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// One money-choice card: picture on the left, a coloured title and two short lines. Tapping it is
        /// optional; it only grows the card and lets Finik react, so the whole card is the hit area.
        /// </summary>
        const float BasicCardHeight = 196f, BasicIconSize = 146f, BasicPadLeft = 22f, BasicPadRight = 30f, BasicBodyFont = 30f;
        const float BasicTextLeft = BasicPadLeft + BasicIconSize + 18f;
        static readonly float BasicTextWidth = LandscapeColumn.sizeDelta.x - BasicTextLeft - BasicPadRight;

        static (Button button, FinikPressFeedback feedback, TextMeshProUGUI body) BasicCard(Transform column,
            (string id, string card, string icon, string fallbackIcon, string title, string accent, string lead, string body, string reaction) spec, int index)
        {
            var slot = Rect("Card_" + spec.id, column);
            Size(slot, height: BasicCardHeight);
            // The body scales (press spring, selected size); the slot keeps its place in the layout.
            var body = Rect("Body", slot);
            Stretch(body);
            var feedback = body.gameObject.AddComponent<FinikPressFeedback>();
            SetField(feedback, "pressedScale", 0.95f);
            var background = Img("Background", body, SpriteOrNull(spec.card) ? spec.card : "ui_card", sliced: true, raycast: true);
            Stretch(background.rectTransform);
            var button = MakeButton(body, background);

            var icon = Img("Icon", body, SpriteOrNull(spec.icon) ? spec.icon : spec.fallbackIcon, preserveAspect: true);
            Place(icon.rectTransform, LeftMiddle, LeftMiddle, new Vector2(BasicPadLeft, 2), Vector2.one * BasicIconSize);
            // Out of phase, so the pictures don't bob in lockstep.
            icon.gameObject.AddComponent<FinikIdleMotion>().Configure(5f, 3f, 0f, 1.5f, index * 0.7f);

            var texts = Rect("Texts", body);
            Stretch(texts, BasicTextLeft, 22, BasicPadRight, 20);
            var layout = texts.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 2;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            Text("Title", texts, spec.title, 50, FinikSdfCanvas.Hex(spec.accent), TextAlignmentOptions.Left, outlined: false);
            var lead = Text("Lead", texts, FinikTypography.Fix(spec.lead), 30, Ink, TextAlignmentOptions.Left, outlined: false);
            Wrap(lead);
            var text = Text("Body", texts, FinikTypography.Fix(spec.body), BasicBodyFont, InkSoft, TextAlignmentOptions.Left, outlined: false);
            return (button, feedback, text);
        }

        static FinikScreenPanel BuildPets(Transform parent, FinikOnboardingFlow flow)
        {
            var (panel, column) = BuildScreen("Screen_Pets", parent, card: true, spacing: 14);

            // Everything about the current pet re-pops when the player flips to another one.
            // The panel animates its own anchoredPosition, so it must not be a layout child itself.
            var slot = Rect("PetInfoSlot", column);
            Size(slot, height: 560);
            var info = Rect("PetInfo", slot);
            Stretch(info);
            info.gameObject.AddComponent<CanvasGroup>();
            var infoPanel = info.gameObject.AddComponent<FinikScreenPanel>();
            SetField(infoPanel, "slide", Vector2.zero);
            var swipeTarget = info.gameObject.AddComponent<Image>();
            swipeTarget.color = new Color(1, 1, 1, 0);
            var swipe = info.gameObject.AddComponent<FinikSwipeArea>();
            var infoLayout = info.gameObject.AddComponent<VerticalLayoutGroup>();
            infoLayout.spacing = 12;
            infoLayout.childAlignment = TextAnchor.UpperCenter;
            infoLayout.childControlWidth = infoLayout.childControlHeight = true;
            infoLayout.childForceExpandWidth = true;
            infoLayout.childForceExpandHeight = false;

            var top = Row(info, "Top", 200, 48);
            var prev = ArrowButton(top, "Prev", mirrored: true);
            var avatarHolder = Rect("Avatar", top);
            Size(avatarHolder, 200, 200);
            var avatar = Img("Image", avatarHolder, "avatar_finik", preserveAspect: true);
            Stretch(avatar.rectTransform);
            avatar.gameObject.AddComponent<FinikIdleMotion>().Configure(5f, 2f, 0f, 1.5f, 0f);
            var next = ArrowButton(top, "Next", mirrored: false);

            var name = Text("Name", info, "Финик", 72, Ink, TextAlignmentOptions.Center, outlined: false);
            Size(name, height: 100);
            var roleRow = Row(info, "Role", 48, 0);
            var pill = Rect("Pill", roleRow);
            Size(pill, 380, 48);
            Stretch(Img("Background", pill, "ui_btn_orange", sliced: true).rectTransform);
            var role = Text("Label", pill, "Исследователь", 28, Color.white, TextAlignmentOptions.Midline);
            Stretch(role.rectTransform, 12, 6, 12, 0);

            var bubble = Img("Speech", info, "ui_bubble", sliced: true);
            DecorateBubble(bubble.rectTransform);
            // Every pet's line wraps to two rows; the bubble holds them with room to spare, and the
            // text shrinks a little rather than spilling over the traits if a line ever grows.
            Size(bubble, height: 176);
            var speech = Text("Text", bubble.transform, "", 31, Ink, TextAlignmentOptions.TopLeft, outlined: false);
            speech.textWrappingMode = TextWrappingModes.Normal;
            AutoSize(speech, 26, 31);
            Stretch(speech.rectTransform, 40, 54, 36, 26);
            var typewriter = speech.gameObject.AddComponent<FinikTypewriter>();

            var pageDots = DotsRow(column, 3, out var pageDotGraphics);
            var buttons = ButtonRow(column);
            var back = WideButton(buttons, "Back", "ui_btn_white", "Назад", 34, Ink, 124, 220);
            var choose = WideButton(buttons, "Choose", "ui_btn_green", "Выбрать Финика", 42, Color.white, 124, 500, "ui_decor_paw_green");

            SetField(flow, "petCard", infoPanel);
            SetField(flow, "petSwipe", swipe);
            SetField(flow, "petAvatar", avatar);
            SetField(flow, "petTitle", name);
            SetField(flow, "petRole", role);
            SetField(flow, "petSpeech", typewriter);
            SetField(flow, "traitDots", System.Array.Empty<Graphic>());
            SetField(flow, "petDots", pageDotGraphics);
            SetField(flow, "prevPet", prev);
            SetField(flow, "nextPet", next);
            SetField(flow, "choosePet", choose);
            SetField(flow, "choosePetLabel", choose.GetComponentInChildren<TMP_Text>());
            SetField(flow, "petsBack", back);
            return panel;
        }

        static void BuildStage(Transform parent, FinikOnboardingFlow flow)
        {
            var root = Rect("PetStage", parent);
            Stretch(root);
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            var panel = root.gameObject.AddComponent<FinikScreenPanel>();
            var stage = Rect("Stage", root);
            stage.gameObject.AddComponent<FinikOrientationLayout>().Configure(
                FinikRectLayout.At(RightMiddle, RightMiddle, new Vector2(-110, -30), new Vector2(760, 960)),
                FinikRectLayout.At(TopCenter, TopCenter, new Vector2(0, -60), new Vector2(900, 760)));
            var shadow = Img("Shadow", stage, "ui_dot", color: new Color(0.05f, 0.04f, 0.2f, 0.25f));
            Place(shadow.rectTransform, BottomCenter, Center, new Vector2(0, 40), new Vector2(420, 70));
            var art = Img("Art", stage, "avatar_cat", preserveAspect: true);
            Stretch(art.rectTransform, 0, 40, 0, 0);
            art.gameObject.AddComponent<FinikIdleMotion>().Configure(10f, 1.5f, 0.012f, 1.3f, 0f);
            var soon = Rect("Soon", stage);
            Place(soon, BottomCenter, BottomCenter, new Vector2(0, 0), new Vector2(300, 56));
            Stretch(Img("Background", soon, "ui_btn_white", sliced: true).rectTransform);
            var label = Text("Label", soon, "3D скоро!", 30, Ink, TextAlignmentOptions.Center, outlined: false);
            Stretch(label.rectTransform, 8, 8, 8, 0);
            SetField(flow, "petStage", panel);
            SetField(flow, "petStageImage", art);
        }

        static RectTransform Row(Transform parent, string name, float height, float spacing)
        {
            var row = Rect(name, parent);
            Size(row, height: height);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            return row;
        }

        static Button ArrowButton(Transform parent, string name, bool mirrored)
        {
            var root = Rect(name, parent);
            Size(root, 104, 104);
            var body = Rect("Body", root);
            Stretch(body);
            body.gameObject.AddComponent<FinikPressFeedback>();
            var bg = Img("Background", body, "hud_btn_round_light", raycast: true);
            Stretch(bg.rectTransform);
            var chevron = Img("Chevron", body, "ui_chevron", preserveAspect: true);
            Stretch(chevron.rectTransform, 20, 22, 20, 18);
            if (mirrored) chevron.rectTransform.localScale = new Vector3(-1, 1, 1);
            return MakeButton(body, bg);
        }

        static RectTransform DotsRow(Transform column, int count, out Object[] graphics)
        {
            var dots = Rect("Dots", column);
            Size(dots, height: 26);
            var layout = dots.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 22;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            graphics = new Object[count];
            for (int i = 0; i < count; i++)
            {
                var dot = Img("Dot" + i, dots, "ui_dot", color: FinikSdfCanvas.Hex("#FF9A2E"));
                dot.rectTransform.sizeDelta = new Vector2(22, 22);
                graphics[i] = dot;
            }
            return dots;
        }

        // Only the pet's name: no outfit at the start, accessories are bought in the shop later.
        static FinikScreenPanel BuildCustomize(Transform parent, FinikOnboardingFlow flow)
        {
            var (panel, column) = BuildScreen("Screen_Customize", parent, card: true);
            // A name alone fills far less than the shared column: a card of its height, not a half-empty one.
            column.parent.GetComponent<FinikOrientationLayout>().Configure(
                FinikRectLayout.At(LeftMiddle, LeftMiddle, LandscapeColumn.anchoredPosition, new Vector2(LandscapeColumn.sizeDelta.x, 560)),
                FinikRectLayout.At(BottomCenter, BottomCenter, PortraitColumn.anchoredPosition, new Vector2(PortraitColumn.sizeDelta.x, 600)));
            Heading(column, "Как зовут питомца?");
            var input = Input(column, "PetName", "Имя питомца");
            var chips = ChipRow(column, "PetNameIdeas", new[] { "Финик", "Пиксель", "Флэш", "Локки" });

            var row = ButtonRow(column);
            var back = WideButton(row, "Back", "ui_btn_white", "Назад", 34, Ink, 124, 220);
            var done = WideButton(row, "Done", "ui_btn_green", "Готово!", 50, Color.white, 124, 480, "ui_decor_paw_green");

            SetField(flow, "petNameInput", input);
            SetField(flow, "petNameChips", chips);
            SetField(flow, "customizeDone", done);
            SetField(flow, "customizeDoneGroup", done.gameObject.AddComponent<CanvasGroup>());
            SetField(flow, "customizeBack", back);
            return panel;
        }

        static FinikScreenPanel BuildFinale(Transform parent, FinikOnboardingFlow flow)
        {
            var (panel, column) = BuildScreen("Screen_Finale", parent, card: false);
            var party = SpriteOrNull("ill_party");
            if (party)
            {
                var holder = Rect("Party", column);
                Size(holder, height: 260);
                var image = Img("Image", holder, "ill_party", preserveAspect: true);
                Stretch(image.rectTransform);
                image.gameObject.AddComponent<FinikIdleMotion>().Configure(8f, 6f, 0.04f, 2.4f, 0f);
                foreach (int side in new[] { -1, 1 })
                {
                    Decor(holder, "ui_decor_star", Center, Center, new Vector2(side * 190, 60), new Vector2(70, 70), side * 14f);
                    Decor(holder, "ui_spark_yellow", Center, Center, new Vector2(side * 180, 140), new Vector2(64, 64), side * -38f);
                }
            }
            var title = Text("Title", column, "Ура!", 72, Color.white, TextAlignmentOptions.Center);
            title.textWrappingMode = TextWrappingModes.Normal;
            Size(title, height: 240);
            SetField(flow, "finaleTitle", title);
            return panel;
        }

        static FinikScreenPanel BuildTutorial(Transform parent, FinikOnboardingFlow flow)
        {
            var (panel, column) = BuildScreen("Screen_Tutorial", parent, card: true, spacing: 14);

            var dim = Rect("Dimmer", panel.transform);
            Stretch(dim);
            var dimImage = dim.gameObject.AddComponent<Image>();
            dimImage.color = new Color(0.04f, 0.05f, 0.16f, 0.38f);
            dimImage.raycastTarget = true;
            dim.SetAsFirstSibling();

            column.parent.GetComponent<FinikOrientationLayout>().Configure(
                FinikRectLayout.At(LeftMiddle, LeftMiddle, new Vector2(72, 0), new Vector2(820, 820)),
                FinikRectLayout.At(BottomCenter, BottomCenter, new Vector2(0, 34), new Vector2(940, 900)));

            var top = Row(column, "Top", 48, 14);
            var tag = Rect("Tag", top);
            Size(tag, 250, 48);
            Stretch(Img("Background", tag, "ui_btn_orange", sliced: true).rectTransform);
            var tagLabel = Text("Label", tag, "КАК ИГРАТЬ", 25, Color.white, TextAlignmentOptions.Center, outlined: false);
            Stretch(tagLabel.rectTransform, 12, 6, 12, 2);

            var grow = Rect("Grow", top);
            var growLayout = grow.gameObject.AddComponent<LayoutElement>();
            growLayout.flexibleWidth = 1f;
            growLayout.minWidth = 24f;

            var step = Text("Step", top, "1 из 4", 28, InkSoft, TextAlignmentOptions.Center, outlined: false);
            Size(step, 130, 48);

            var hero = Rect("Hero", column);
            Size(hero, height: 178);
            var icon = Img("Icon", hero, "icon_piggy_coin", preserveAspect: true);
            Place(icon.rectTransform, Center, Center, Vector2.zero, new Vector2(172, 172));
            icon.gameObject.AddComponent<FinikIdleMotion>().Configure(5f, 3f, 0.02f, 1.7f, 0f);

            var title = Text("Title", column, "Учимся обращаться с деньгами", 50, Ink,
                TextAlignmentOptions.Center, outlined: false);
            title.textWrappingMode = TextWrappingModes.Normal;
            AutoSize(title, 38, 50);
            Size(title, height: 76);

            var bubble = Img("Message", column, "ui_bubble", sliced: true);
            DecorateBubble(bubble.rectTransform);
            Size(bubble, height: 230);
            var body = Text("Body", bubble.transform,
                "Здесь ты будешь учиться обращаться с деньгами.", 31, Ink,
                TextAlignmentOptions.TopLeft, outlined: false);
            body.textWrappingMode = TextWrappingModes.Normal;
            AutoSize(body, 26, 31);
            Stretch(body.rectTransform, 42, 52, 38, 28);

            DotsRow(column, 4, out var dots);

            var buttons = ButtonRow(column, 112);
            var skip = WideButton(buttons, "Skip", "ui_btn_white", "Пропустить", 30, Ink, 112, 215);
            var replay = WideButton(buttons, "Replay", "ui_btn_white", "Ещё раз", 30, Ink, 112, 180);
            var next = WideButton(buttons, "Next", "ui_btn_orange", "Дальше", 38, Color.white, 112, 290, "ui_decor_chevron");

            SetField(flow, "tutorialStep", step);
            SetField(flow, "tutorialTitle", title);
            SetField(flow, "tutorialBody", body);
            SetField(flow, "tutorialIcon", icon);
            SetField(flow, "tutorialDots", dots);
            SetField(flow, "tutorialSkip", skip);
            SetField(flow, "tutorialReplay", replay);
            SetField(flow, "tutorialNext", next);
            SetField(flow, "tutorialNextLabel", next.GetComponentInChildren<TMP_Text>());

            var serialized = new SerializedObject(flow);
            var icons = serialized.FindProperty("tutorialIcons");
            string[] names = { "icon_piggy_coin", "icon_star", "icon_food", "icon_cart" };
            icons.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++)
                icons.GetArrayElementAtIndex(i).objectReferenceValue = SpriteOrNull(names[i]);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return panel;
        }

        // ------------------------------------------------------------------ widgets

        static RectTransform ButtonRow(Transform column, float height = 130)
        {
            var row = Rect("Buttons", column);
            Size(row, height: height);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 20;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            return row;
        }

        static void Heading(Transform column, string value)
        {
            var text = Text("Heading", column, value, 46, Ink, TextAlignmentOptions.Center, outlined: false);
            Size(text, height: 66);
        }

        static void Spacer(Transform column, float height) => Size(Rect("Spacer", column), height: height);

        static TMP_InputField Input(Transform column, string name, string placeholder)
        {
            var go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources { inputField = SpriteOrNull("ui_input") });
            go.name = name;
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(column, false);
            Size(go.GetComponent<RectTransform>(), height: 108);
            var image = go.GetComponent<Image>();
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = FinikHudSpriteBaker.PixelsPerUnitFor("ui_input", image.sprite);
            var field = go.GetComponent<TMP_InputField>();
            field.transition = Selectable.Transition.None;
            field.fontAsset = FinikUiKit.Font;
            field.pointSize = 44;
            field.characterLimit = FinikProfile.MaxNameLength;
            field.shouldHideMobileInput = false;
            field.caretColor = Ink;
            field.caretWidth = 3;
            field.selectionColor = new Color(1f, 0.6f, 0.2f, 0.35f);
            foreach (var text in go.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                text.font = FinikUiKit.Font;
                text.fontSharedMaterial = FinikUiKit.Font.material;
                text.fontSize = 44;
                text.alignment = TextAlignmentOptions.Center;
                text.color = text == field.placeholder ? new Color(Ink.r, Ink.g, Ink.b, 0.35f) : Ink;
                if (text == field.placeholder) text.text = placeholder;
            }
            var area = go.transform.Find("Text Area") as RectTransform;
            bool paw = Decor(go.transform, "ui_decor_paw_input", LeftMiddle, Center, new Vector2(52, 0), new Vector2(52, 52), -10f);
            // Balanced insets keep the centred text in the middle of the field.
            if (area) Stretch(area, paw ? 92 : 28, 12, paw ? 92 : 28, 18);
            return field;
        }

        static Object[] ChipRow(Transform column, string name, string[] labels)
        {
            var row = Rect(name, column);
            Size(row, height: 76);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 14;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            return labels.Select(label => (Object)Chip(row, label)).ToArray();
        }

        static FinikChoiceItem Chip(Transform parent, string label)
        {
            var chip = Rect("Chip_" + label, parent);
            var body = Rect("Body", chip);
            Stretch(body);
            var press = body.gameObject.AddComponent<FinikPressFeedback>();
            var face = Img("Face", body, "ui_btn_white", sliced: true, raycast: true);
            Stretch(face.rectTransform);
            var highlight = Img("Selected", body, "ui_btn_orange", sliced: true, color: new Color(1, 1, 1, 0));
            Stretch(highlight.rectTransform);
            var text = Text("Label", body, label, 30, Ink, TextAlignmentOptions.Midline, outlined: false);
            Stretch(text.rectTransform, 10, 12, 10, 0);
            var item = chip.gameObject.AddComponent<FinikChoiceItem>();
            item.Configure(label, MakeButton(body, face), press, highlight, null, text);
            return item;
        }

        // ------------------------------------------------------------------ world wiring

        static List<FinikOnboardingFlow.PetOption> Pets() => new()
        {
            new()
            {
                id = "fox", name = "Финик", accusative = "Финика", role = "Исследователь",
                description = "Любопытный и внимательный к решениям",
                quote = "Обожаю узнавать, откуда берутся монетки и куда они убегают!",
                traits = new[] { 3, 2, 2 }, avatar = SpriteOrNull("avatar_finik"), fullBody = null,
                nameIdeas = new[] { "Финик", "Пиксель", "Флэш", "Локки" }, has3DModel = true
            },
            new()
            {
                id = "cat", name = "Копейка", accusative = "Копейку", role = "Планировщица",
                description = "Спокойная, любит порядок и планы",
                quote = "Люблю, когда всё по полочкам: сначала план, потом покупки.",
                traits = new[] { 1, 3, 2 }, avatar = SpriteOrNull("avatar_cat"), fullBody = SpriteOrNull("full_cat"),
                nameIdeas = new[] { "Копейка", "Мята", "Пикси", "Луна" }, has3DModel = true
            },
            new()
            {
                id = "raccoon", name = "Рик", accusative = "Рика", role = "Охотник за выгодой",
                description = "Обожает выгодные и безопасные решения",
                quote = "Найду, где дешевле и безопаснее. Ни одна монетка не пропадёт зря!",
                traits = new[] { 2, 1, 3 }, avatar = SpriteOrNull("avatar_raccoon"), fullBody = SpriteOrNull("full_raccoon"),
                nameIdeas = new[] { "Рик", "Байт", "Рокки", "Спарк" }, has3DModel = true
            }
        };

        static void WireWorld(FinikOnboardingFlow flow, GameObject finik, FinikShowcaseCamera showcase, FinikAccessoryRig rig, GameObject hud)
        {
            var so = new SerializedObject(flow);
            var pets = so.FindProperty("pets");
            var options = Pets();
            pets.arraySize = options.Count;
            for (int i = 0; i < options.Count; i++)
            {
                var element = pets.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("id").stringValue = options[i].id;
                element.FindPropertyRelative("name").stringValue = options[i].name;
                element.FindPropertyRelative("description").stringValue = options[i].description;
                element.FindPropertyRelative("avatar").objectReferenceValue = options[i].avatar;
                element.FindPropertyRelative("has3DModel").boolValue = options[i].has3DModel;
                element.FindPropertyRelative("accusative").stringValue = options[i].accusative;
                element.FindPropertyRelative("role").stringValue = options[i].role;
                element.FindPropertyRelative("quote").stringValue = options[i].quote;
                element.FindPropertyRelative("fullBody").objectReferenceValue = options[i].fullBody;
                var traits = element.FindPropertyRelative("traits");
                traits.arraySize = options[i].traits.Length;
                for (int k = 0; k < options[i].traits.Length; k++) traits.GetArrayElementAtIndex(k).intValue = options[i].traits[k];
                var ideas = element.FindPropertyRelative("nameIdeas");
                ideas.arraySize = options[i].nameIdeas.Length;
                for (int k = 0; k < options[i].nameIdeas.Length; k++) ideas.GetArrayElementAtIndex(k).stringValue = options[i].nameIdeas[k];
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            SetField(flow, "showcase", showcase);
            SetField(flow, "rig", rig);
            SetField(flow, "movement", finik.GetComponent<FinikMovementController>());
            SetField(flow, "activity", finik.GetComponent<FinikActivityController>());
            SetField(flow, "characterSwitcher", finik.GetComponent<FinikCharacterSwitcher>());
            var paused = new List<Object>();
            foreach (var behaviour in new Behaviour[]
                     {
                         finik.GetComponent<FinikInputController>(),
                         finik.GetComponent<FinikWanderController>(),
                         finik.GetComponent<FinikActivityController>()
                     })
                if (behaviour) paused.Add(behaviour);
            SetField(flow, "pauseDuringOnboarding", paused.ToArray());
            SetField(flow, "hudRoot", hud);
            SetField(flow, "hudView", hud.GetComponent<FinikHudView>());
        }

        [MenuItem("Finik/Profile/Reset Local Profile")]
        static void ResetProfile()
        {
            FinikProfileStore.Clear();
            Finik.Core.FinikGame.Clear();
            Debug.Log("[FinikProfile] Local profile and game progress cleared: onboarding will run on next Play.");
        }
    }
}
