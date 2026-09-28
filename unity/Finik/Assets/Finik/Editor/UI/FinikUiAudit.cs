using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Finik.UI;
using Finik.UI.Onboarding;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Finik.Editor.UI
{
    /// <summary>
    /// Layout checks shared by the screen audits (food, quests): text that overflows, gets cut, shrinks
    /// to its minimum size, collides with a neighbour, leaves its background or breaks lines badly
    /// (hanging prepositions, a dash opening a line). A screen audit drives its screen through every
    /// state and calls <see cref="Check"/> on the visible column after each one.
    /// </summary>
    public sealed class FinikUiAudit
    {
        const float OverlapTolerance = 2f;
        const float OverflowTolerance = 3f;

        // One- and two-letter words that must not end a line (они «висят»).
        static readonly HashSet<string> ShortWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "а", "в", "и", "к", "о", "с", "у", "я", "во", "за", "из", "ко", "на", "не", "ни", "но", "об", "от",
            "по", "со", "то", "до", "же", "ли", "бы", "да", "уж"
        };

        readonly (string a, string b)[] intendedOverlaps;
        readonly HashSet<string> contentImages;
        readonly HashSet<string> containers;
        readonly List<string> issues = new();
        readonly Dictionary<string, (float min, float max)> fontSizes = new();
        // Tap problems are a property of the button, not of the state it was seen in: keep the worst
        // measurement per button instead of repeating it for every state the audit walks through.
        readonly Dictionary<string, float> smallTaps = new();
        readonly HashSet<string> shadowedTaps = new();

        public int States { get; private set; }
        public int IssueCount => issues.Count;

        /// <param name="intended">Overlaps that are part of the design, as path fragments.</param>
        /// <param name="content">Image names that are content and must not collide (card faces and frames are backgrounds).</param>
        /// <param name="backgrounds">Background names a text must stay inside (a message box, a card face, a strip).</param>
        public FinikUiAudit((string a, string b)[] intended, IEnumerable<string> content, IEnumerable<string> backgrounds)
        {
            intendedOverlaps = intended ?? Array.Empty<(string, string)>();
            contentImages = new HashSet<string>(content, StringComparer.Ordinal);
            containers = new HashSet<string>(backgrounds, StringComparer.Ordinal);
        }

        /// <summary>
        /// Drives the Game view to a fixed resolution. Unity registers it as a custom entry in the Game
        /// view's size dropdown and selects it — and that selection is editor state, which outlives Play
        /// Mode. Whoever drives the Game view has to put the old size back with
        /// <see cref="SelectGameViewSize"/>, or the editor is left rendering the last audited phone.
        /// One name for every call, so the dropdown gains a single entry instead of one per resolution.
        /// </summary>
        public static string SetResolution(int width, int height)
        {
            PlayModeWindow.SetCustomRenderingResolution((uint)width, (uint)height, ResolutionSizeName);
            return $"Game view set to {width}x{height}";
        }

        public const string ResolutionSizeName = "Finik audit";

        /// <summary>The Game view's chosen size, to hand back to <see cref="SelectGameViewSize"/>. -1: none open.</summary>
        public static int SelectedGameViewSize()
        {
            var (window, property) = GameViewSize();
            return window != null && property != null ? (int)property.GetValue(window) : -1;
        }

        /// <summary>Puts the Game view back on the size <see cref="SelectedGameViewSize"/> returned.</summary>
        public static void SelectGameViewSize(int index)
        {
            if (index < 0) return;
            var (window, property) = GameViewSize();
            if (window == null || property == null) return;
            property.SetValue(window, index);
            ((EditorWindow)window).Repaint();
        }

        // UnityEditor.GameView and its selected size are internal; the Fold presets reach for the same
        // corner of the editor. A missing type or property only means no restore, never an exception.
        static (object window, PropertyInfo property) GameViewSize()
        {
            var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            if (type == null) return (null, null);
            var windows = Resources.FindObjectsOfTypeAll(type);
            if (windows.Length == 0) return (null, null);
            return (windows[0], type.GetProperty("selectedSizeIndex", Flags));
        }

        /// <summary>
        /// Pretends the Game view has a notch. Screen.safeArea is always the full screen there, so
        /// without this the safe-area checks below would pass on any device. Reset to None afterwards.
        /// </summary>
        public static string SetNotch(FinikNotch notch)
        {
            FinikSafeAreaSimulation.Notch = notch;
            // The components apply on their next Update; nudge the editor so edit mode follows too.
            foreach (var area in UnityEngine.Object.FindObjectsByType<FinikSafeArea>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                EditorUtility.SetDirty(area);
            SceneView.RepaintAll();
            return $"Notch simulation: {notch} -> {FinikSafeAreaSimulation.Area}";
        }

        /// <summary>
        /// "Always Start With Onboarding" wipes the profile on Play. Without one the screens will not open,
        /// so run the onboarding's own demo path; it finishes after ~4 s of celebration.
        /// </summary>
        public static string EnsureProfile()
        {
            if (!EditorApplication.isPlaying) return "Enter Play Mode first.";
            if (FinikProfileStore.TryLoad(out _)) return "profile ready";
            var flow = UnityEngine.Object.FindAnyObjectByType<FinikOnboardingFlow>();
            if (!flow) return "No profile and no active onboarding to create one.";
            Call(flow, "StartDemo");
            return "demo started";
        }

        public static string Capture(string path)
        {
            ScreenCapture.CaptureScreenshot(path);
            return path;
        }

        /// <summary>Report: header line, catalog problems, issues grouped by message, and auto-size ranges.</summary>
        public string Report(string screenName, RectTransform anyColumn, IEnumerable<string> catalogProblems)
        {
            var report = new StringBuilder();
            foreach (string problem in catalogProblems ?? Array.Empty<string>())
                report.AppendLine($"! каталог: {problem}");
            string notch = FinikSafeAreaSimulation.Notch == FinikNotch.None ? "без выреза" : FinikSafeAreaSimulation.Notch.ToString();
            report.AppendLine($"{screenName} audit at {Screen.width}x{Screen.height} ({(Screen.height > Screen.width ? "portrait" : "landscape")}, {notch}), canvas {CanvasSize(anyColumn)}: {States} states, {issues.Count + smallTaps.Count + shadowedTaps.Count} issues.");
            foreach (var group in issues.GroupBy(i => i.Substring(i.IndexOf('|') + 1)).OrderBy(g => g.Key))
            {
                var where = group.Select(i => i.Substring(0, i.IndexOf('|'))).ToList();
                report.AppendLine($"- {group.Key}  [{where.Count}x: {string.Join(", ", where.Take(4))}{(where.Count > 4 ? ", …" : "")}]");
            }
            foreach (var pair in smallTaps.OrderBy(p => p.Value))
                report.AppendLine($"- {pair.Key}: зона нажатия {pair.Value:0} юнитов, нужно {FinikTapTarget.Minimum:0} (≈48 dp)");
            foreach (string shadowed in shadowedTaps.OrderBy(s => s))
                report.AppendLine($"- {shadowed}");
            report.AppendLine("Auto-sized text, font size range over all states:");
            foreach (var pair in fontSizes.OrderBy(p => p.Key))
                if (pair.Value.max - pair.Value.min > 0.5f) report.AppendLine($"  {pair.Key}: {pair.Value.min:0.#}..{pair.Value.max:0.#}");
            return report.ToString();
        }

        // ------------------------------------------------------------------ checks

        public void Check(RectTransform root, string state)
        {
            States++;
            Canvas.ForceUpdateCanvases();
            foreach (var group in root.GetComponentsInChildren<LayoutGroup>()) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)group.transform);
            Canvas.ForceUpdateCanvases();

            // Measured against the safe area, not the screen: on a notched phone the strip under the
            // camera is screen but not usable, and a column that reaches into it is the bug we want.
            if (!Inside(WorldRect(root), SafeRect(root))) issues.Add($"{state}|карточка выходит за безопасную зону");
            CheckTaps(root);

            var items = new List<(string name, Rect rect, Transform transform)>();
            foreach (var text in root.GetComponentsInChildren<TMP_Text>())
            {
                if (!text.isActiveAndEnabled || string.IsNullOrWhiteSpace(text.text) || text.alpha <= 0.01f) continue;
                if (!Visible(text.transform)) continue;
                text.ForceMeshUpdate();
                string name = PathOf(text.transform, root);
                CheckText(text, name, state);
                if (text.enableAutoSizing)
                {
                    fontSizes.TryGetValue(name, out var range);
                    fontSizes[name] = range.max == 0 ? (text.fontSize, text.fontSize) : (Mathf.Min(range.min, text.fontSize), Mathf.Max(range.max, text.fontSize));
                }
                var bounds = RenderedRect(text);
                var shown = Clipped(bounds, text.transform);
                if (shown.width > 0 && shown.height > 0) items.Add((name, shown, text.transform));
                var container = ContainerOf(text.transform, root);
                if (container && bounds.width > 0 && !Inside(Shrink(bounds, OverflowTolerance), WorldRect(container)))
                    issues.Add($"{state}|{name}: текст выходит за подложку {container.name}");
            }
            foreach (var image in root.GetComponentsInChildren<Image>())
                if (image.isActiveAndEnabled && image.color.a > 0.01f && contentImages.Contains(image.name) && Visible(image.transform))
                {
                    var shown = Clipped(WorldRect((RectTransform)image.transform), image.transform);
                    if (shown.width > 0 && shown.height > 0) items.Add((PathOf(image.transform, root), shown, image.transform));
                }

            for (int i = 0; i < items.Count; i++)
            for (int j = i + 1; j < items.Count; j++)
            {
                if (items[i].transform.IsChildOf(items[j].transform) || items[j].transform.IsChildOf(items[i].transform)) continue;
                var a = Shrink(items[i].rect, OverlapTolerance);
                var b = Shrink(items[j].rect, OverlapTolerance);
                if (a.Overlaps(b) && !Intended(items[i].name, items[j].name)) issues.Add($"{state}|наложение: {items[i].name} и {items[j].name}");
            }
        }

        /// <summary>
        /// <see cref="Check"/> for a menu that scrolls: one pass per viewport-high page from top to
        /// bottom, so what sits below the fold is measured too. Leaves the view at the top.
        /// </summary>
        public void CheckScrolling(RectTransform root, ScrollRect scroll, string state)
        {
            if (!scroll || !scroll.content || !scroll.viewport)
            {
                Check(root, state);
                return;
            }
            Canvas.ForceUpdateCanvases();
            // A column that sizes itself to its content may not have done so yet this frame.
            var fit = root.GetComponentInChildren<FinikFitColumn>(true);
            if (fit) fit.Refresh();
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            Canvas.ForceUpdateCanvases();
            float view = scroll.viewport.rect.height;
            // A short viewport is fine while everything fits; squeezed under content it is a bug.
            if (view < 200f && scroll.content.rect.height > view + 1f)
            {
                issues.Add($"{state}|окно прокрутки высотой {view:0} px: содержимого почти не видно");
                Check(root, state);
                return;
            }
            float hidden = scroll.content.rect.height - view;
            int pages = hidden <= 1f ? 1 : Mathf.Min(12, Mathf.CeilToInt(scroll.content.rect.height / (view * 0.8f)));
            for (int page = 0; page < pages; page++)
            {
                scroll.StopMovement();
                scroll.verticalNormalizedPosition = pages == 1 ? 1f : 1f - page / (float)(pages - 1);
                Check(root, pages == 1 ? state : $"{state}, экран {page + 1}/{pages}");
            }
            scroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>
        /// Every button must answer a finger, not a stylus: at least <see cref="FinikTapTarget.Minimum"/>
        /// units on each side. Where two tap areas overlap the later one in the hierarchy wins the tap,
        /// so a button whose centre falls inside a later button's area is unreachable and reported too.
        /// </summary>
        void CheckTaps(Transform root)
        {
            var taps = new List<(string name, Rect hit, Rect visual, Transform t)>();
            foreach (var selectable in root.GetComponentsInChildren<Selectable>())
            {
                if (!selectable.isActiveAndEnabled || !selectable.interactable) continue;
                var graphic = selectable.targetGraphic ? selectable.targetGraphic : selectable.GetComponent<Graphic>();
                if (!graphic || !graphic.raycastTarget || !graphic.isActiveAndEnabled) continue;
                var group = selectable.GetComponentInParent<CanvasGroup>();
                if (group && !group.blocksRaycasts) continue;
                if (!Visible(selectable.transform)) continue;

                string name = PathOf(selectable.transform, root);
                // Scrolled out of its viewport, a button can be neither seen nor tapped.
                var hit = Clipped(WorldHitRect(graphic), graphic.transform);
                var visual = Clipped(WorldRect((RectTransform)graphic.transform), graphic.transform);
                if (visual.width <= 0 || visual.height <= 0) continue;
                taps.Add((name, hit, visual, graphic.transform));

                // Measured on the rect, not on the world corners: a button caught mid pop-in is
                // scaled down for that frame, and its designed tap area is what we are checking.
                var local = FinikTapTarget.HitRect(graphic);
                float side = Mathf.Min(local.width, local.height);
                if (side < FinikTapTarget.Minimum - 1f && (!smallTaps.TryGetValue(name, out float worst) || side < worst))
                    smallTaps[name] = side;
            }

            // Hierarchy order is raycast order: a later sibling is drawn on top and takes the tap.
            for (int i = 0; i < taps.Count; i++)
            for (int j = i + 1; j < taps.Count; j++)
            {
                if (taps[i].t.IsChildOf(taps[j].t) || taps[j].t.IsChildOf(taps[i].t)) continue;
                if (taps[j].hit.Contains(taps[i].visual.center)) shadowedTaps.Add($"{taps[i].name} перекрыт зоной {taps[j].name}");
            }
        }

        void CheckText(TMP_Text text, string name, string state)
        {
            var info = text.textInfo;
            var rect = text.rectTransform.rect;
            if (text.isTextTruncated) issues.Add($"{state}|{name}: текст обрезан многоточием");
            else
            {
                float overY = text.textBounds.size.y - rect.height, overX = text.textBounds.size.x - rect.width;
                if (overY > OverflowTolerance || overX > OverflowTolerance)
                    issues.Add($"{state}|{name}: текст не помещается в свою рамку ({Mathf.Max(overX, overY):0} px)");
            }
            if (text.enableAutoSizing && text.fontSize <= text.fontSizeMin + 0.01f && text.fontSizeMin < text.fontSizeMax)
                issues.Add($"{state}|{name}: шрифт ужат до минимума {text.fontSizeMin:0.#}");
            if (text.fontSize < FinikUiKit.MinimumReadableFontSize - 0.01f)
                issues.Add($"{state}|{name}: мелкий шрифт {text.fontSize:0.#}, минимум {FinikUiKit.MinimumReadableFontSize:0.#}");

            for (int line = 0; line < info.lineCount; line++)
            {
                string words = LineText(text, line);
                if (line > 0 && (words.StartsWith("—") || words.StartsWith("–"))) issues.Add($"{state}|{name}: строка начинается с тире «{Clip(words)}»");
                if (line == info.lineCount - 1) break;
                string last = words.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim('«', '»', ',', '.', '(', ')');
                if (last != null && ShortWords.Contains(last)) issues.Add($"{state}|{name}: висячее «{last}» в конце строки «{Clip(words)}»");
            }
        }

        /// <summary>The background the text sits on: a container-named graphic among its ancestors or their siblings.</summary>
        RectTransform ContainerOf(Transform text, Transform root)
        {
            for (var t = text.parent; t && t != root; t = t.parent)
            {
                if (containers.Contains(t.name)) return (RectTransform)t;
                if (t.name == "Content") break; // the column itself: its children are separate blocks
                foreach (Transform sibling in t)
                    if (sibling != text && containers.Contains(sibling.name) && sibling.GetComponent<Graphic>()) return (RectTransform)sibling;
            }
            return null;
        }

        bool Intended(string a, string b)
        {
            foreach (var pair in intendedOverlaps)
                if ((a.Contains(pair.a) && b.Contains(pair.b)) || (a.Contains(pair.b) && b.Contains(pair.a))) return true;
            return false;
        }

        static string LineText(TMP_Text text, int line)
        {
            var info = text.textInfo;
            var lineInfo = info.lineInfo[line];
            var chars = new StringBuilder();
            for (int c = lineInfo.firstCharacterIndex; c <= lineInfo.lastCharacterIndex && c < info.characterCount; c++)
                chars.Append(info.characterInfo[c].character);
            return chars.ToString().Trim();
        }

        static string Clip(string value) => value.Length > 40 ? "…" + value.Substring(value.Length - 40) : value;

        // ------------------------------------------------------------------ geometry

        static Rect RenderedRect(TMP_Text text)
        {
            if (text.textInfo.characterCount == 0) return default;
            var bounds = text.textBounds;
            var min = text.rectTransform.TransformPoint(bounds.min);
            var max = text.rectTransform.TransformPoint(bounds.max);
            return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
        }

        static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        /// <summary>
        /// Whether the player can see this element at all. A room hint parked off-stage, a panel that
        /// is faded out and the dock tabs before their pop-in are all held at alpha 0 by a CanvasGroup
        /// above them, and an invisible element neither collides with anything nor takes a tap.
        /// </summary>
        static bool Visible(Transform item)
        {
            float alpha = 1f;
            for (var t = item; t; t = t.parent)
            {
                if (!t.TryGetComponent<CanvasGroup>(out var group)) continue;
                alpha *= group.alpha;
                if (alpha <= 0.01f) return false;
                if (group.ignoreParentGroups) break;
            }
            return true;
        }

        /// <summary>
        /// The part of <paramref name="rect"/> left after every RectMask2D above <paramref name="item"/>
        /// has clipped it; empty when a scroll view has moved it out of sight. A clipped graphic neither
        /// draws nor takes taps outside its mask.
        /// </summary>
        static Rect Clipped(Rect rect, Transform item)
        {
            for (var t = item.parent; t; t = t.parent)
            {
                if (!t.TryGetComponent<RectMask2D>(out var mask) || !mask.isActiveAndEnabled) continue;
                var clip = WorldRect((RectTransform)t);
                float xMin = Mathf.Max(rect.xMin, clip.xMin), yMin = Mathf.Max(rect.yMin, clip.yMin);
                float xMax = Mathf.Min(rect.xMax, clip.xMax), yMax = Mathf.Min(rect.yMax, clip.yMax);
                if (xMax <= xMin || yMax <= yMin) return default;
                rect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            }
            return rect;
        }

        /// <summary>The graphic's raycast rectangle: its own rect widened by a negative raycastPadding.</summary>
        static Rect WorldHitRect(Graphic graphic)
        {
            var rect = (RectTransform)graphic.transform;
            var local = FinikTapTarget.HitRect(graphic);
            var min = rect.TransformPoint(new Vector3(local.xMin, local.yMin));
            var max = rect.TransformPoint(new Vector3(local.xMax, local.yMax));
            return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
        }

        /// <summary>The area the UI may use: the nearest FinikSafeArea above, or the whole canvas.</summary>
        static Rect SafeRect(RectTransform any)
        {
            var safe = any.GetComponentInParent<FinikSafeArea>();
            return WorldRect(safe ? (RectTransform)safe.transform : (RectTransform)any.GetComponentInParent<Canvas>().rootCanvas.transform);
        }

        static string CanvasSize(RectTransform any)
        {
            var canvas = (RectTransform)any.GetComponentInParent<Canvas>().rootCanvas.transform;
            var size = canvas.rect.size;
            // The HUD is audited from its canvas root, whose safe area is a child, not an ancestor.
            var safe = any.GetComponentInParent<FinikSafeArea>() ?? canvas.GetComponentInChildren<FinikSafeArea>(true);
            if (!safe) return $"{size.x:0}x{size.y:0}";
            var safeSize = ((RectTransform)safe.transform).rect.size;
            return $"{size.x:0}x{size.y:0}, безопасная зона {safeSize.x:0}x{safeSize.y:0}";
        }

        static bool Inside(Rect inner, Rect outer) =>
            inner.xMin >= outer.xMin - 1 && inner.yMin >= outer.yMin - 1 && inner.xMax <= outer.xMax + 1 && inner.yMax <= outer.yMax + 1;

        static Rect Shrink(Rect rect, float by) => Rect.MinMaxRect(rect.xMin + by, rect.yMin + by, rect.xMax - by, rect.yMax - by);

        static string PathOf(Transform item, Transform root)
        {
            var parts = new List<string>();
            for (var t = item; t && t != root; t = t.parent)
                if (t.name != "Body" && t.name != "Texts" && t.name != "Text") parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        // ------------------------------------------------------------------ reflection plumbing for the screen audits

        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        public static object Get(object target, string field) => target.GetType().GetField(field, Flags)!.GetValue(target);
        public static void Set(object target, string field, object value) => target.GetType().GetField(field, Flags)!.SetValue(target, value);
        public static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Flags)!.Invoke(target, args);
    }
}
