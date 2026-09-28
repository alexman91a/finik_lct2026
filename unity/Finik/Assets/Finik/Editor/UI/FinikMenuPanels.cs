using Finik.UI;
using Finik.UI.Onboarding;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Finik.Editor.UI.FinikUiKit;

namespace Finik.Editor.UI
{
    /// <summary>
    /// The settings-style menu card shared by «Настройки» and «Для взрослых»: a fixed header, a
    /// scrolling middle made of section cards, a fixed footer with the screen's buttons, and a «×».
    ///
    /// The column is exactly as tall as its content (<see cref="FinikFitColumn"/>) and scrolls only
    /// when the screen is shorter than that, so the footer's «Готово» is always in reach.
    /// </summary>
    public static class FinikMenuPanels
    {
        public const float ColumnWidth = 1010f;
        /// <summary>A menu that can spread into two columns takes this much on a landscape screen.</summary>
        public const float WideColumnWidth = 1860f;
        /// <summary>Narrowest a column of sections may get before the page falls back to one column.</summary>
        public const float MinSectionWidth = 820f;
        public const float StripHeight = 80f;
        public const float PadX = 40f, PadY = 34f;
        public const float HeaderHeight = 104f, FooterHeight = 112f;
        public const float Gap = 16f;
        /// <summary>Inner padding of a section card.</summary>
        public const float CardPad = 28f;
        /// <summary>Room around the section cards inside the mask, so their rims and shadows are not cut.</summary>
        const float MaskBleed = 10f;

        /// <summary>Width a section card's children get.</summary>
        public const float CardInner = ColumnWidth - 2 * PadX - 2 * CardPad;

        public static readonly Color Accent = new(0.95f, 0.5f, 0.1f, 1f);
        public static readonly Color Violet = new(0.55f, 0.3f, 0.95f, 1f);
        public static readonly Color Danger = new(0.86f, 0.2f, 0.27f, 1f);
        public static readonly Color Good = new(0.13f, 0.58f, 0.3f, 1f);

        public sealed class Menu
        {
            public FinikScreenPanel panel;
            public RectTransform column;
            public RectTransform header;
            public RectTransform content;
            public RectTransform footer;
            /// <summary>A fixed band under the header (tabs), or null.</summary>
            public RectTransform strip;
            public ScrollRect scroll;
        }

        /// <summary>
        /// A hidden panel holding one menu card. No footer when <paramref name="footer"/> is false;
        /// <paramref name="wide"/> lets the card spread over a landscape screen for two columns of
        /// sections; <paramref name="strip"/> reserves a band under the header for tabs.
        /// </summary>
        public static Menu Build(Transform safe, string name, bool footer, bool wide = false, bool strip = false)
        {
            var menu = new Menu();
            var panelRoot = Rect(name, safe);
            Stretch(panelRoot);
            panelRoot.gameObject.AddComponent<CanvasGroup>();
            menu.panel = panelRoot.gameObject.AddComponent<FinikScreenPanel>();

            var column = Rect("Column", panelRoot);
            menu.column = column;
            PanelCard(column);

            menu.header = Rect("Header", column);
            Place(menu.header, TopCenter, TopCenter, new Vector2(0, -PadY), new Vector2(ColumnWidth - 2 * PadX, HeaderHeight));
            menu.header.anchorMin = new Vector2(0, 1);
            menu.header.anchorMax = new Vector2(1, 1);
            menu.header.sizeDelta = new Vector2(-2 * PadX, HeaderHeight);

            float bottom = PadY;
            if (footer)
            {
                menu.footer = Rect("Footer", column);
                menu.footer.anchorMin = new Vector2(0, 0);
                menu.footer.anchorMax = new Vector2(1, 0);
                menu.footer.pivot = new Vector2(0.5f, 0);
                menu.footer.anchoredPosition = new Vector2(0, PadY);
                menu.footer.sizeDelta = new Vector2(-2 * PadX, FooterHeight);
                var group = menu.footer.gameObject.AddComponent<HorizontalLayoutGroup>();
                group.spacing = 20;
                group.childAlignment = TextAnchor.MiddleCenter;
                group.childControlWidth = group.childControlHeight = true;
                group.childForceExpandWidth = group.childForceExpandHeight = false;
                bottom += FooterHeight + Gap;
            }

            float top = PadY + HeaderHeight + Gap;
            if (strip)
            {
                menu.strip = Rect("Tabs", column);
                menu.strip.anchorMin = new Vector2(0, 1);
                menu.strip.anchorMax = new Vector2(1, 1);
                menu.strip.pivot = new Vector2(0.5f, 1);
                menu.strip.anchoredPosition = new Vector2(0, -top);
                menu.strip.sizeDelta = new Vector2(-2 * PadX, StripHeight);
                top += StripHeight + Gap;
            }

            var viewport = Rect("Viewport", column);
            Stretch(viewport, PadX - MaskBleed, bottom - MaskBleed, PadX - MaskBleed, top - MaskBleed);
            // RectMask2D clips by rect and needs no graphic of its own.
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = Gap;
            int bleed = (int)MaskBleed;
            layout.padding = new RectOffset(bleed, bleed, bleed, bleed);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            menu.content = content;

            var scroll = column.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            // Clamped: a menu that fits stays put instead of wobbling under the finger.
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            scroll.inertia = true;
            menu.scroll = scroll;

            column.gameObject.AddComponent<FinikFitColumn>().Configure(
                content, top + bottom - 2 * MaskBleed, ColumnWidth, wide ? WideColumnWidth : ColumnWidth, new Vector2(32f, 56f));

            panelRoot.gameObject.SetActive(false);
            return menu;
        }

        /// <summary>Icon, a small coloured tag over the title. Returns the title.</summary>
        public static TMP_Text Header(Menu menu, string icon, string tagText, Color tagColor, string titleText)
        {
            var row = menu.header;
            var image = Img("Icon", row, icon, preserveAspect: true);
            Place(image.rectTransform, LeftMiddle, LeftMiddle, new Vector2(0, 0), new Vector2(84, 84));

            var tag = Text("Tag", row, tagText, 24, tagColor, TextAlignmentOptions.TopLeft, outlined: false);
            tag.characterSpacing = 6;
            Place(tag.rectTransform, TopLeft, TopLeft, new Vector2(104, -6), new Vector2(640, 32));

            var title = Text("Title", row, titleText, 42, Ink, TextAlignmentOptions.BottomLeft);
            AutoSize(title, 30, 42);
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = Vector2.one;
            title.rectTransform.offsetMin = new Vector2(104, 4);
            // Leaves room for the «×» in the corner.
            title.rectTransform.offsetMax = new Vector2(-96, -34);
            return title;
        }

        public static Button CloseCorner(Menu menu)
        {
            var face = Img("Close", menu.column, "ui_btn_white", sliced: true, color: Color.white, raycast: true);
            Place(face.rectTransform, TopRight, TopRight, new Vector2(-18, -18), new Vector2(76, 76));
            var label = Text("Label", face.transform, "×", 46, Color.white, TextAlignmentOptions.Center);
            Stretch(label.rectTransform, 2, 8, 2, 0);
            return MakeButton(face.rectTransform, face);
        }

        /// <summary>
        /// A page of sections: one column on an upright phone, two where the card is wide. Sections
        /// alternate between the columns in the order they are added.
        /// </summary>
        public static RectTransform Page(Transform content, string name)
        {
            var page = Rect(name, content);
            page.gameObject.AddComponent<FinikFlowColumns>().Configure(MinSectionWidth, 2, Gap);
            return page;
        }

        /// <summary>
        /// A section card: a small coloured caption and a column of rows under it. The card is as tall
        /// as what it holds; fixed heights only live on the rows.
        /// </summary>
        public static RectTransform Section(Transform content, string name, string caption, Color captionColor, string sprite = "ui_card")
        {
            // The glass is a child that stays out of the layout: a sliced Image reports its 9-slice
            // borders as a minimum height, which left a slab of empty card under short sections.
            var card = Rect(name, content);
            var glass = Img("Glass", card, sprite, sliced: true, color: Color.white);
            Stretch(glass.rectTransform);
            glass.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            int pad = (int)CardPad;
            layout.padding = new RectOffset(pad, pad, pad - 6, pad);
            layout.spacing = 14;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            if (!string.IsNullOrEmpty(caption))
            {
                var label = Text("Caption", card.transform, caption, 24, captionColor, TextAlignmentOptions.MidlineLeft, outlined: false);
                label.characterSpacing = 4;
                Size(label, height: 34);
            }
            return card;
        }

        /// <summary>Wrapping text whose height follows its lines; the parent layout reads it from TMP.</summary>
        public static TMP_Text Paragraph(Transform parent, string name, string value, float size, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            var text = Text(name, parent, FinikTypography.Fix(value), size, color, alignment, outlined: false);
            Wrap(text);
            text.lineSpacing = -2;
            return text;
        }

        public static RectTransform Row(Transform parent, string name, float height, float spacing = 20f, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var row = Rect(name, parent);
            if (height > 0) Size(row, height: height);
            var group = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.childAlignment = align;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            return row;
        }

        public static RectTransform Stack(Transform parent, string name, float spacing = 4f)
        {
            var stack = Rect(name, parent);
            var group = stack.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            return stack;
        }

        /// <summary>
        /// A setting line: title and a short explanation on the left, the control on the right. The
        /// returned slot is sized for the control; the text column takes the rest.
        /// </summary>
        public static RectTransform SettingRow(Transform section, string name, string title, string description,
            float slotWidth, float slotHeight, out TMP_Text descriptionText, string icon = null)
        {
            var row = Row(section, name, -1, 20);
            if (icon != null)
            {
                var holder = Rect("Icon", row);
                Size(holder, 72, 72);
                Stretch(Img("Image", holder, icon, preserveAspect: true).rectTransform);
            }
            var copy = Stack(row, "Copy");
            Size(copy, flexibleWidth: 1);
            var titleText = Text("Title", copy, title, 30, Ink, TextAlignmentOptions.MidlineLeft, outlined: false);
            Wrap(titleText);
            descriptionText = Paragraph(copy, "Note", description, 24, InkSoft);
            var slot = Rect("Control", row);
            Size(slot, slotWidth, slotHeight);
            return slot;
        }

        /// <summary>An on/off pill: light track, green fill when on, a white knob and the state word.</summary>
        public static FinikToggleSwitch ToggleSwitch(RectTransform slot, float height)
        {
            var track = Img("Track", slot, "ui_track_light", sliced: true, raycast: true);
            Stretch(track.rectTransform);
            // The track's rounded ends must be half the switch's height, like the green pill over it:
            // at the sprite's default scale its corners came out small and the track read as a grey
            // rounded box sticking out from under the pill.
            if (track.sprite && track.sprite.border.x > 0f)
                track.pixelsPerUnitMultiplier = track.sprite.border.x / (height * 0.5f);
            var fill = Img("On", track.transform, "ui_btn_green", sliced: true, color: new Color(1, 1, 1, 0));
            Stretch(fill.rectTransform);
            var label = Text("State", track.transform, "Выкл", 26, InkSoft, TextAlignmentOptions.Center, outlined: false);
            Stretch(label.rectTransform, 0, 0, 0, 4);
            float knobSize = height - 16;
            // The knob is a holder: the shadow under it, the white disc on top, moving together.
            var knob = Rect("Knob", track.transform);
            knob.sizeDelta = Vector2.one * knobSize;
            var shadow = Img("Shadow", knob, "ui_dot", color: new Color(0.05f, 0.08f, 0.25f, 0.25f));
            Place(shadow.rectTransform, Center, Center, new Vector2(0, -5), Vector2.one * knobSize);
            var disc = Img("Disc", knob, "ui_dot", color: Color.white);
            Stretch(disc.rectTransform);
            var toggle = slot.gameObject.AddComponent<FinikToggleSwitch>();
            SetField(toggle, "button", MakeButton(track.rectTransform, track));
            SetField(toggle, "onFill", fill);
            SetField(toggle, "knob", knob);
            SetField(toggle, "stateLabel", label);
            SetField(toggle, "offLabelColor", InkSoft);
            return toggle;
        }

        /// <summary>A selectable chip in the look of the piggy bank's amounts (blue candy, orange when chosen).</summary>
        public static FinikChoiceItem Chip(Transform row, string id, string label)
        {
            var slot = Rect("Chip_" + id, row);
            var body = Rect("Body", slot);
            Stretch(body);
            var press = body.gameObject.AddComponent<FinikPressFeedback>();
            var face = Img("Face", body, "ui_btn_white", sliced: true, raycast: true);
            Stretch(face.rectTransform);
            var button = MakeButton(body, face);
            var chosen = Img("Chosen", body, "ui_btn_orange", sliced: true, color: new Color(1, 1, 1, 0));
            Stretch(chosen.rectTransform);
            var text = Text("Label", body, label, 28, Color.white, TextAlignmentOptions.Midline);
            Wrap(text);
            AutoSize(text, 24, 28);
            Stretch(text.rectTransform, 12, 8, 12, 2);
            var choice = slot.gameObject.AddComponent<FinikChoiceItem>();
            choice.Configure(id, button, press, chosen, null, text);
            SetField(choice, "selectedScale", 1.04f);
            return choice;
        }

        /// <summary>Makes a WideButton share its row's width with its siblings.</summary>
        public static Button Flexible(Button button)
        {
            var element = button.transform.parent.GetComponent<LayoutElement>();
            element.flexibleWidth = 1;
            element.minWidth = 0;
            return button;
        }
    }
}
