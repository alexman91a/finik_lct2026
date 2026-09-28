using System;
using Finik.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.UI.Onboarding
{
    /// <summary>Builds the first-run how-to card from the onboarding's existing visual presets.</summary>
    internal static class FinikTutorialRuntimeUi
    {
        internal sealed class Result
        {
            public FinikScreenPanel panel;
            public TMP_Text step;
            public TMP_Text title;
            public TMP_Text body;
            public Image icon;
            public Sprite[] icons = Array.Empty<Sprite>();
            public Graphic[] dots = Array.Empty<Graphic>();
            public Button skip;
            public Button replay;
            public Button next;
            public TMP_Text nextLabel;
        }

        static readonly Vector2 LeftMiddle = new(0f, 0.5f);
        static readonly Vector2 BottomCenter = new(0.5f, 0f);
        static readonly Vector2 Center = new(0.5f, 0.5f);

        internal static Result Build(
            FinikScreenPanel titleScreen,
            FinikScreenPanel customizeScreen,
            FinikOnboardingFlow.BasicCard[] basics,
            FinikTypewriter speech,
            TMP_Text petTitle,
            TMP_Text petRole,
            Graphic[] petDots,
            Button basicsBack,
            Button basicsNext)
        {
            if (!titleScreen || !customizeScreen || !speech || !petTitle || !basicsBack || !basicsNext)
                return null;

            var parent = titleScreen.transform.parent;
            var root = Rect("Screen_Tutorial", parent);
            Stretch(root);
            root.gameObject.AddComponent<CanvasGroup>();
            var panel = root.gameObject.AddComponent<FinikScreenPanel>();

            var dimmer = Rect("Dimmer", root);
            Stretch(dimmer);
            var dim = dimmer.gameObject.AddComponent<Image>();
            dim.color = new Color(0.04f, 0.05f, 0.16f, 0.38f);
            dim.raycastTarget = true;

            var card = Rect("Card", root);
            CopyCardStyle(customizeScreen, card);
            card.gameObject.AddComponent<FinikFitInside>();
            card.gameObject.AddComponent<FinikOrientationLayout>().Configure(
                FinikRectLayout.At(LeftMiddle, LeftMiddle, new Vector2(72f, 0f), new Vector2(820f, 820f)),
                FinikRectLayout.At(BottomCenter, BottomCenter, new Vector2(0f, 34f), new Vector2(940f, 900f)));

            var content = Rect("Content", card);
            Stretch(content, 44f, 40f, 44f, 40f);
            var vertical = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vertical.spacing = 14f;
            vertical.padding = new RectOffset(0, 0, 0, 0);
            vertical.childAlignment = TextAnchor.MiddleCenter;
            vertical.childControlWidth = vertical.childControlHeight = true;
            vertical.childForceExpandWidth = true;
            vertical.childForceExpandHeight = false;

            var result = new Result { panel = panel };
            BuildHeader(content, petRole ? petRole : petTitle, basicsNext, result);
            BuildHero(content, basics, titleScreen, result);
            BuildCopy(content, petTitle, speech, result);
            BuildDots(content, petDots, result);
            BuildButtons(content, basicsBack, basicsNext, result);

            panel.Hide(instant: true);
            return result;
        }
        static void BuildHeader(Transform parent, TMP_Text textPreset, Button orangePreset, Result result)
        {
            var row = Rect("Top", parent);
            Size(row, 48f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var tag = Rect("Tag", row);
            Size(tag, 250f, 48f);
            var orange = orangePreset.targetGraphic as Image;
            var tagImage = tag.gameObject.AddComponent<Image>();
            CopyImage(orange, tagImage);
            tagImage.raycastTarget = false;
            var tagText = NewText("Label", tag, textPreset, "КАК ИГРАТЬ", 25f, TextAlignmentOptions.Center);
            Stretch(tagText.rectTransform, 12f, 6f, 12f, 2f);
            tagText.color = Color.white;

            var grow = Rect("Grow", row);
            var growLayout = grow.gameObject.AddComponent<LayoutElement>();
            growLayout.flexibleWidth = 1f;
            growLayout.minWidth = 24f;

            result.step = NewText("Step", row, textPreset, "1 из 4", 28f, TextAlignmentOptions.Center);
            result.step.color = new Color(0.36f, 0.41f, 0.62f, 1f);
            Size(result.step.rectTransform, 130f, 48f);
        }

        static void BuildHero(Transform parent, FinikOnboardingFlow.BasicCard[] basics,
            FinikScreenPanel titleScreen, Result result)
        {
            var hero = Rect("Hero", parent);
            Size(hero, 178f);

            result.icons = new[]
            {
                BasicIcon(basics, 2),
                BasicIcon(basics, 1),
                BasicIcon(basics, 0),
                FindSprite(titleScreen.transform, "Column/Content/Pitch/Tile_icon_cart/Body/Icon")
            };

            // The icon is a child of the hero row, not the row itself: the column's layout group owns
            // the row's rect, while FinikIdleMotion rewrites anchoredPosition every frame. On the row
            // the bob pinned the icon to the position it had before the first layout pass — the
            // card's top-left corner.
            result.icon = Rect("Icon", hero).gameObject.AddComponent<Image>();
            result.icon.preserveAspect = true;
            result.icon.raycastTarget = false;
            result.icon.sprite = FirstSprite(result.icons);
            var iconRect = result.icon.rectTransform;
            iconRect.anchorMin = iconRect.anchorMax = Center;
            iconRect.pivot = Center;
            iconRect.sizeDelta = new Vector2(172f, 172f);
            iconRect.anchoredPosition = Vector2.zero;
            result.icon.gameObject.AddComponent<FinikIdleMotion>().Configure(5f, 3f, 0.02f, 1.7f, 0f);
        }
        static void BuildCopy(Transform parent, TMP_Text titlePreset, FinikTypewriter speech, Result result)
        {
            result.title = NewText("Title", parent, titlePreset, "Учимся обращаться с деньгами",
                50f, TextAlignmentOptions.Center);
            result.title.enableAutoSizing = true;
            result.title.fontSizeMin = 38f;
            result.title.fontSizeMax = 50f;
            result.title.textWrappingMode = TextWrappingModes.Normal;
            Size(result.title.rectTransform, 76f);

            var sourceBubble = speech.transform.parent.GetComponent<Image>();
            var bubble = Rect("Message", parent);
            var bubbleImage = bubble.gameObject.AddComponent<Image>();
            CopyImage(sourceBubble, bubbleImage);
            bubbleImage.raycastTarget = false;
            Size(bubble, 230f);

            result.body = NewText("Body", bubble, speech.GetComponent<TMP_Text>(),
                "Здесь ты будешь учиться обращаться с деньгами.", 31f, TextAlignmentOptions.TopLeft);
            result.body.enableAutoSizing = true;
            result.body.fontSizeMin = 26f;
            result.body.fontSizeMax = 31f;
            result.body.textWrappingMode = TextWrappingModes.Normal;
            Stretch(result.body.rectTransform, 42f, 52f, 38f, 28f);
        }

        static void BuildDots(Transform parent, Graphic[] presets, Result result)
        {
            var row = Rect("Dots", parent);
            Size(row, 26f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 22f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            result.dots = new Graphic[4];
            for (int i = 0; i < result.dots.Length; i++)
            {
                var go = Rect("Dot" + i, row);
                var image = go.gameObject.AddComponent<Image>();
                if (presets != null && presets.Length > 0 && presets[0] is Image source)
                    CopyImage(source, image);
                else
                    image.color = new Color(1f, 0.6f, 0.18f, 1f);
                image.raycastTarget = false;
                go.sizeDelta = new Vector2(22f, 22f);
                result.dots[i] = image;
            }
        }

        static void BuildButtons(Transform parent, Button whitePreset, Button orangePreset, Result result)
        {
            var row = Rect("Buttons", parent);
            Size(row, 112f);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 20f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            result.skip = CloneButton(whitePreset, row, "Skip", "Пропустить", 215f, 112f, out var skipLabel);
            result.replay = CloneButton(whitePreset, row, "Replay", "Ещё раз", 180f, 112f, out var replayLabel);
            result.next = CloneButton(orangePreset, row, "Next", "Дальше", 290f, 112f, out result.nextLabel);
            TuneButtonLabel(skipLabel, 28f, 30f);
            TuneButtonLabel(replayLabel, 28f, 30f);
            TuneButtonLabel(result.nextLabel, 32f, 38f);
        }
        static void TuneButtonLabel(TMP_Text text, float min, float max)
        {
            if (!text) return;
            text.enableAutoSizing = true;
            text.fontSizeMin = min;
            text.fontSizeMax = max;
            text.textWrappingMode = TextWrappingModes.NoWrap;
        }

        static Button CloneButton(Button source, Transform parent, string name, string label,
            float width, float height, out TMP_Text text)
        {
            var sourceSlot = source.transform.parent;
            var clone = UnityEngine.Object.Instantiate(sourceSlot.gameObject, parent, false);
            clone.name = name;
            var rect = clone.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, height);

            var element = clone.GetComponent<LayoutElement>();
            if (!element) element = clone.AddComponent<LayoutElement>();
            element.minWidth = element.preferredWidth = width;
            element.minHeight = element.preferredHeight = height;
            element.flexibleWidth = element.flexibleHeight = 0f;

            var button = clone.GetComponentInChildren<Button>(true);
            button.onClick.RemoveAllListeners();
            text = clone.GetComponentInChildren<TMP_Text>(true);
            if (text) text.text = label;
            return button;
        }

        static void CopyCardStyle(FinikScreenPanel customizeScreen, RectTransform target)
        {
            var source = customizeScreen.transform.Find("Column")?.GetComponent<Image>();
            var image = target.gameObject.AddComponent<Image>();
            if (source) CopyImage(source, image);
            else image.color = new Color(1f, 0.97f, 0.9f, 0.98f);
            image.raycastTarget = false;

            var shadowSource = customizeScreen.transform.Find("Column")?.GetComponent<Shadow>();
            if (shadowSource)
            {
                var shadow = target.gameObject.AddComponent<Shadow>();
                shadow.effectColor = shadowSource.effectColor;
                shadow.effectDistance = shadowSource.effectDistance;
                shadow.useGraphicAlpha = shadowSource.useGraphicAlpha;
            }
        }

        static TMP_Text NewText(string name, Transform parent, TMP_Text source, string value,
            float fontSize, TextAlignmentOptions alignment)
        {
            var rect = Rect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = source.font;
            text.fontSharedMaterial = source.fontSharedMaterial;
            text.fontStyle = source.fontStyle;
            text.color = source.color;
            text.text = value;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }
        static Sprite BasicIcon(FinikOnboardingFlow.BasicCard[] basics, int index)
        {
            if (basics == null || index < 0 || index >= basics.Length || basics[index] == null || !basics[index].button)
                return null;
            return basics[index].button.transform.Find("Icon")?.GetComponent<Image>()?.sprite;
        }

        static Sprite FindSprite(Transform root, string path) =>
            root.Find(path)?.GetComponent<Image>()?.sprite;

        static Sprite FirstSprite(Sprite[] sprites)
        {
            foreach (var sprite in sprites)
                if (sprite) return sprite;
            return null;
        }

        static void CopyImage(Image source, Image target)
        {
            if (!source || !target) return;
            target.sprite = source.sprite;
            target.type = source.type;
            target.color = source.color;
            target.preserveAspect = source.preserveAspect;
            target.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
        }

        static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        static void Stretch(RectTransform rect, float left = 0f, float bottom = 0f,
            float right = 0f, float top = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        static void Size(RectTransform rect, float height)
        {
            var element = rect.GetComponent<LayoutElement>();
            if (!element) element = rect.gameObject.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = height;
            element.flexibleHeight = 0f;
        }

        static void Size(RectTransform rect, float width, float height)
        {
            rect.sizeDelta = new Vector2(width, height);
            var element = rect.GetComponent<LayoutElement>();
            if (!element) element = rect.gameObject.AddComponent<LayoutElement>();
            element.minWidth = element.preferredWidth = width;
            element.minHeight = element.preferredHeight = height;
            element.flexibleWidth = element.flexibleHeight = 0f;
        }
    }
}
