using System.Collections.Generic;
using Finik.UI;
using Finik.UI.Onboarding;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Shared building blocks for the editor-side UI builders (HUD, onboarding): sprites, fonts and
    /// small factory helpers. Call <see cref="Prepare"/> once per build before using the factories.
    /// </summary>
    public static class FinikUiKit
    {
        const string FontFolder = "Assets/Finik/UI/Fonts";
        const string FontSource = FontFolder + "/Nunito-Black.ttf";
        const string FontAssetPath = FontFolder + "/Nunito-Black SDF.asset";
        const string TextMaterialPath = FontFolder + "/Nunito-Black SDF HUD.mat";
        const string TmpEssentialsPackage = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";
        const string Glyphs =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
            "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя«»—–…№ ";

        public static readonly Vector2 TopLeft = new(0, 1), TopRight = new(1, 1), TopCenter = new(0.5f, 1);
        public static readonly Vector2 BottomCenter = new(0.5f, 0), BottomLeft = new(0, 0), Center = new(0.5f, 0.5f);
        public static readonly Vector2 LeftMiddle = new(0, 0.5f), RightMiddle = new(1, 0.5f), BottomRight = new(1, 0);

        public static readonly Color Ink = new(0.12f, 0.15f, 0.36f, 1f);
        public static readonly Color InkSoft = new(0.36f, 0.41f, 0.62f, 1f);
        // 1080-wide reference canvases are roughly 3 UI units per phone dp. Values below this
        // become visibly tiny on narrow/foldable cover displays, even when TMP technically fits.
        public const float MinimumReadableFontSize = 24f;

        public static Dictionary<string, Sprite> Sprites { get; private set; }
        public static TMP_FontAsset Font { get; private set; }
        /// <summary>White text with navy outline + drop shadow, for text over the 3D scene.</summary>
        public static Material OutlinedMaterial { get; private set; }

        /// <summary>Loads sprites and fonts. Returns false (with a message) when TMP must import first.</summary>
        public static bool Prepare(out string message)
        {
            message = null;
            if (AssetDatabase.FindAssets("t:TMP_Settings").Length == 0)
            {
                AssetDatabase.ImportPackage(TmpEssentialsPackage, false);
                message = "TMP Essential Resources were missing and are being imported. Run the builder again once the import finishes.";
                return false;
            }
            Sprites = FinikHudSpriteBaker.BakeAll();
            Font = EnsureFont();
            OutlinedMaterial = EnsureTextMaterial(Font);
            return true;
        }

        // ------------------------------------------------------------------ factories

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public static Image Img(string name, Transform parent, string sprite, bool sliced = false, Color? color = null, bool raycast = false, bool preserveAspect = false)
        {
            var rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            if (!Sprites.TryGetValue(sprite, out var s) || !s)
                Debug.LogWarning($"[FinikUi] Sprite '{sprite}' is missing.");
            image.sprite = s;
            image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            image.pixelsPerUnitMultiplier = FinikHudSpriteBaker.PixelsPerUnitFor(sprite, s);
            image.fillCenter = true;
            image.preserveAspect = preserveAspect;
            image.color = color ?? Color.white;
            image.raycastTarget = raycast;
            return image;
        }

        public static Sprite SpriteOrNull(string name) => Sprites.TryGetValue(name, out var s) ? s : null;

        /// <param name="outlined">true: white-ish text readable over the 3D room; false: plain ink text for light panels.</param>
        public static TextMeshProUGUI Text(string name, Transform parent, string value, float size, Color color, TextAlignmentOptions alignment, bool outlined = true)
        {
            var rect = Rect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = Font;
            text.fontSharedMaterial = outlined ? OutlinedMaterial : Font.material;
            text.text = value;
            text.fontSize = Mathf.Max(size, MinimumReadableFontSize);
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// Gives every button on <paramref name="root"/> a <see cref="FinikTapTarget"/>, which widens a
        /// tap area under 48 dp without touching the art. Run at the end of a build, once the buttons
        /// have their target graphics; the component sizes itself and is a no-op on big buttons.
        /// </summary>
        public static int EnsureTapTargets(GameObject root)
        {
            int added = 0;
            foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
            {
                var graphic = selectable.targetGraphic ? selectable.targetGraphic : selectable.GetComponent<Graphic>();
                if (!graphic || !graphic.raycastTarget || graphic.GetComponent<FinikTapTarget>()) continue;
                graphic.gameObject.AddComponent<FinikTapTarget>();
                added++;
            }
            return added;
        }

        public static Button MakeButton(RectTransform on, Graphic target)
        {
            var button = on.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = target;
            var nav = button.navigation;
            nav.mode = UnityEngine.UI.Navigation.Mode.None;
            button.navigation = nav;
            return button;
        }

        /// <summary>
        /// The HUD currency pill (dark glass, overhanging icon, rolling counter) filling <paramref name="body"/>.
        /// <paramref name="rightInset"/> leaves room for a trailing button such as "+".
        /// </summary>
        public static FinikCounterText CurrencyPill(RectTransform body, string id, string icon, float rightInset = 20f) =>
            CurrencyPill(body, id, icon, out _, rightInset);

        public static FinikCounterText CurrencyPill(RectTransform body, string id, string icon, out Image pill, float rightInset = 20f)
        {
            pill = Img("Pill", body, "hud_pill_dark", sliced: true, raycast: true);
            Stretch(pill.rectTransform);

            var iconHolder = Rect("IconHolder", body);
            Place(iconHolder, LeftMiddle, Center, new Vector2(2, 2), new Vector2(96, 96));
            var iconPunch = iconHolder.gameObject.AddComponent<FinikPressFeedback>();
            var iconImage = Img("Icon", iconHolder, icon, preserveAspect: true);
            Stretch(iconImage.rectTransform);
            iconImage.gameObject.AddComponent<FinikIdleMotion>().Configure(3f, 4f, 0f, 1.5f, id.Length);

            var value = Text("Value", body, "0", 42, Color.white, TextAlignmentOptions.Center);
            Stretch(value.rectTransform, 52, 0, rightInset, 2);
            var counter = value.gameObject.AddComponent<FinikCounterText>();
            SetField(counter, "punchOnGain", iconPunch);
            return counter;
        }

        /// <summary>Glossy pill button sized for layout groups; the caption is the child named "Label".</summary>
        /// <param name="icon">Optional sticker inside the right end (ui_decor_chevron, ui_decor_paw_green, ui_decor_star_orange); the caption makes room for it.</param>
        public static Button WideButton(Transform parent, string name, string sprite, string label, float fontSize, Color labelColor, float height, float width, string icon = null)
        {
            var root = Rect(name, parent);
            Size(root, width, height);
            var body = Rect("Body", root);
            Stretch(body);
            body.gameObject.AddComponent<FinikPressFeedback>();
            var face = Img("Face", body, sprite, sliced: true, raycast: true);
            Stretch(face.rectTransform);
            // The secondary button is a blue candy now: its caption is white with the outline, like the others.
            if (sprite == "ui_btn_white") labelColor = Color.white;
            var text = Text("Label", body, label, fontSize, labelColor, TextAlignmentOptions.Midline, outlined: labelColor == Color.white);
            float iconSize = height * 0.42f;
            bool hasIcon = icon != null && SpriteOrNull(icon);
            // With an icon the caption keeps the same room on both sides, so it stays centred on the
            // button (centring it in the space left of the icon pushed every such caption off-centre).
            float side = hasIcon ? iconSize + height * 0.3f : 24;
            Stretch(text.rectTransform, side, height * 0.14f, side, 0);
            text.enableAutoSizing = true;
            text.fontSizeMin = Mathf.Max(MinimumReadableFontSize, fontSize * 0.8f);
            text.fontSizeMax = Mathf.Max(fontSize, text.fontSizeMin);
            if (hasIcon) Decor(body, icon, RightMiddle, Center, new Vector2(-height * 0.36f, height * 0.05f), Vector2.one * iconSize);
            // The screen's main action (a coloured button with an icon) gets sparkles over its top-left
            // corner, the same size and spot on every button; secondary buttons stay plain.
            if (hasIcon && sprite != "ui_btn_white")
            {
                string spark = sprite == "ui_btn_green" ? "ui_spark_green" : "ui_spark_yellow";
                Decor(body, spark, TopLeft, Center, new Vector2(4, 2), new Vector2(52, 52), 38f);
            }
            return MakeButton(body, face);
        }

        /// <summary>
        /// A small sticker (paw, star, sparkles) over a stretchable piece: it keeps its size and never
        /// stretches. Only placed when the sticker art exists; null otherwise.
        /// </summary>
        public static Image Decor(Transform parent, string sprite, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, float rotation = 0f, float alpha = 1f)
        {
            if (!SpriteOrNull(sprite)) return null;
            var image = Img("Decor_" + sprite, parent, sprite, color: new Color(1, 1, 1, alpha), preserveAspect: true);
            Place(image.rectTransform, anchor, pivot, position, size);
            image.rectTransform.localRotation = Quaternion.Euler(0, 0, rotation);
            return image;
        }

        /// <summary>A star and sparkles flanking the picture at the top of a result card (picture half-width given).</summary>
        public static void HeroSparkles(Transform hero, float halfWidth)
        {
            // Mirrored pair: a star on each side of the picture, sparkles above each star.
            foreach (int side in new[] { -1, 1 })
            {
                Decor(hero, "ui_decor_star_small", TopCenter, Center, new Vector2(side * (halfWidth + 34), -86), new Vector2(46, 46), side * 14f);
                Decor(hero, "ui_spark_yellow", TopCenter, Center, new Vector2(side * (halfWidth + 22), -34), new Vector2(54, 54), side * -38f);
            }
        }

        /// <summary>
        /// Lays a full-screen catcher behind a panel's card, under everything else the panel holds.
        /// A tap beside the card then does what <paramref name="action"/> does — close, cancel, back —
        /// and the room and the HUD under the menu stop answering taps, so no second window can open.
        /// Call it once per panel, after that panel's own dismiss button exists.
        /// </summary>
        /// <param name="alpha">0 for an invisible catcher; higher dims the room behind the card.</param>
        public static FinikScrim Dismiss(FinikScreenPanel panel, Button action, float alpha = 0f)
        {
            var rect = Rect("Scrim", panel.transform);
            rect.SetSiblingIndex(0);
            // The panel slides in from an offset, so the catcher is oversized: no strip of the room
            // is ever left tappable along an edge while the card is still on its way.
            Stretch(rect, -400, -400, -400, -400);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.04f, 0.06f, 0.2f, alpha);
            image.raycastTarget = true;
            var scrim = rect.gameObject.AddComponent<FinikScrim>();
            if (action) SetField(scrim, "action", action);
            return scrim;
        }

        /// <summary>The big card behind a screen column, with a faint paw in its bottom-right corner.</summary>
        public static Image PanelCard(Transform column)
        {
            var card = Img("Card", column, "ui_card", sliced: true, raycast: true);
            Stretch(card.rectTransform, -8, -14, -8, -8);
            Decor(column, "ui_decor_paw_card", BottomRight, BottomRight, new Vector2(-18, 18), new Vector2(120, 120), -14f, 0.4f);
            return card;
        }

        /// <summary>Stars on the speech bubble: a big one on the top-right corner, a small one bottom-left.</summary>
        public static void DecorateBubble(RectTransform bubble)
        {
            Decor(bubble, "ui_decor_star", TopRight, Center, new Vector2(-14, -12), new Vector2(60, 60), 12f);
        }

        public static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size) =>
            FinikRectLayout.At(anchor, pivot, position, size).ApplyTo(rect);

        public static void Stretch(RectTransform rect, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = Center;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        public static LayoutElement Size(Component c, float width = -1, float height = -1, float flexibleWidth = -1)
        {
            var element = c.GetComponent<LayoutElement>();
            if (!element) element = c.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = height;
            element.flexibleWidth = flexibleWidth;
            // A fixed height must not grow: nested layout groups otherwise report flexible space.
            element.flexibleHeight = height >= 0 ? 0 : -1;
            if (width >= 0) element.minWidth = width;
            if (height >= 0) element.minHeight = height;
            return element;
        }

        public static void SetField(Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(field) ?? throw new System.ArgumentException($"{target.GetType().Name} has no field '{field}'");
            switch (value)
            {
                case Object o: property.objectReferenceValue = o; break;
                case float f: property.floatValue = f; break;
                case int i: property.intValue = i; break;
                case bool b: property.boolValue = b; break;
                case string s: property.stringValue = s; break;
                case Vector2 v: property.vector2Value = v; break;
                case Color c: property.colorValue = c; break;
                case Object[] array:
                    property.arraySize = array.Length;
                    for (int k = 0; k < array.Length; k++) property.GetArrayElementAtIndex(k).objectReferenceValue = array[k];
                    break;
                case string[] strings:
                    property.arraySize = strings.Length;
                    for (int k = 0; k < strings.Length; k++) property.GetArrayElementAtIndex(k).stringValue = strings[k];
                    break;
                case int[] ints:
                    property.arraySize = ints.Length;
                    for (int k = 0; k < ints.Length; k++) property.GetArrayElementAtIndex(k).intValue = ints[k];
                    break;
                default: throw new System.ArgumentException($"Unsupported field type for {field}");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void EnsureEventSystem()
        {
            var system = Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (!system)
            {
                var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
                return;
            }
            // The project uses the Input System only; the legacy module would throw every frame.
            var legacy = system.GetComponent<StandaloneInputModule>();
            if (legacy) Object.DestroyImmediate(legacy);
            if (!system.GetComponent<InputSystemUIInputModule>()) system.gameObject.AddComponent<InputSystemUIInputModule>();
        }

        /// <summary>Screen-space canvas with the shared orientation-aware scaler.</summary>
        public static GameObject CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            ConfigureCanvas(go, sortingOrder);
            Undo.RegisterCreatedObjectUndo(go, "Build " + name);
            return go;
        }

        /// <summary>
        /// Rebuilds a screen in place: keeps the scene's root canvas <paramref name="name"/> and clears only
        /// its children, so whatever references the root or its components (the HUD binder holds the food
        /// and quest screens, the screens hold HUD_Home) survives rebuilding any single screen. The root's
        /// components are kept too; the builder re-wires their fields. Creates the canvas when it is missing.
        /// </summary>
        public static GameObject ReuseOrCreateCanvas(string name, int sortingOrder)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            GameObject root = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name != name) continue;
                if (!root) root = go;
                else Object.DestroyImmediate(go);
            }
            if (!root) return CreateCanvas(name, sortingOrder);
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            GetOrAdd<Canvas>(root);
            GetOrAdd<CanvasScaler>(root);
            GetOrAdd<GraphicRaycaster>(root);
            ConfigureCanvas(root, sortingOrder);
            return root;
        }

        public static T GetOrAdd<T>(GameObject go) where T : Component =>
            go.TryGetComponent<T>(out var component) ? component : go.AddComponent<T>();

        static void ConfigureCanvas(GameObject go, int sortingOrder)
        {
            go.layer = LayerMask.NameToLayer("UI");
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.referencePixelsPerUnit = 100;
            GetOrAdd<FinikCanvasOrientation>(go);
        }

        /// <summary>Different placement per orientation, applied by FinikOrientationLayout.</summary>
        public static void Orient(RectTransform rect, FinikRectLayout landscape, FinikRectLayout portrait) =>
            rect.gameObject.AddComponent<FinikOrientationLayout>().Configure(landscape, portrait);

        /// <summary>Stretched to the parent with fixed insets, as a placement FinikOrientationLayout can hold.</summary>
        public static FinikRectLayout Inset(float left, float bottom, float right, float top) => new()
        {
            anchorMin = Vector2.zero,
            anchorMax = Vector2.one,
            pivot = Center,
            anchoredPosition = new Vector2((left - right) / 2f, (bottom - top) / 2f),
            sizeDelta = new Vector2(-(left + right), -(bottom + top))
        };

        public static void Wrap(TMP_Text text) => text.textWrappingMode = TextWrappingModes.Normal;

        public static void AutoSize(TMP_Text text, float min, float max)
        {
            text.enableAutoSizing = true;
            text.fontSizeMin = Mathf.Max(min, MinimumReadableFontSize);
            text.fontSizeMax = Mathf.Max(max, text.fontSizeMin);
        }

        // ------------------------------------------------------------------ fonts

        static TMP_FontAsset EnsureFont()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (existing) return existing;

            var source = AssetDatabase.LoadAssetAtPath<UnityEngine.Font>(FontSource);
            if (!source) throw new System.InvalidOperationException($"Font source {FontSource} is missing.");
            var asset = TMP_FontAsset.CreateFontAsset(source, 90, 10, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            asset.name = "Nunito-Black SDF";
            AssetDatabase.CreateAsset(asset, FontAssetPath);
            asset.atlasTextures[0].name = asset.name + " Atlas";
            AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
            asset.material.name = asset.name + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            if (!asset.TryAddCharacters(Glyphs, out string missing))
                Debug.LogWarning($"[FinikUi] Font is missing glyphs: {missing}");
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        static Material EnsureTextMaterial(TMP_FontAsset fontAsset)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(TextMaterialPath);
            bool created = !material;
            if (created) material = new Material(fontAsset.material) { name = "Nunito-Black SDF HUD" };
            else material.CopyPropertiesFromMaterial(fontAsset.material);

            material.SetTexture(ShaderUtilities.ID_MainTex, fontAsset.atlasTexture);
            material.SetFloat(ShaderUtilities.ID_FaceDilate, 0.08f);
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.16f);
            material.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.07f, 0.08f, 0.25f, 1f));
            material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            material.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0.02f, 0.03f, 0.15f, 0.55f));
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.9f);
            material.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.2f);
            material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.2f);
            if (created) AssetDatabase.CreateAsset(material, TextMaterialPath);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }
    }
}
