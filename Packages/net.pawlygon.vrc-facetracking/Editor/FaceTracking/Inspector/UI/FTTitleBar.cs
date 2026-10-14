using System.Linq;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Draws the Pawlygon tag and the component name over Unity's component title bar.
    /// The overlay ignores the mouse, so folding, the help/preset/menu icons and the context menu keep working.
    /// </summary>
    internal static class FTTitleBar
    {
        const string OverlayClass = "ft-titlebar";
        // Width of the tag's pointed right end, matching .ft-tag's right padding in FTInspector.uss
        const float TagPoint = 7;
        static readonly Color TagColor = new Color32(75, 166, 173, 255); // #4BA6AD

        /// <summary>
        /// Add the returned element to the inspector. Once attached it finds the title bar and puts the
        /// overlay on it, or falls back to a plain title when there's no title bar (e.g. nested inspectors).
        /// </summary>
        public static VisualElement Create(string title)
        {
            var anchor = new VisualElement();
            anchor.RegisterCallback<AttachToPanelEvent>(_ => Attach(anchor, title));
            return anchor;
        }

        static void Attach(VisualElement anchor, string title)
        {
            var editorElement = FindEditorElement(anchor);
            var header = editorElement?.Children().FirstOrDefault(c => c.name != null && c.name.EndsWith("Header"));
            if (header == null)
            {
                anchor.Clear();
                anchor.Add(FTUI.Text(title, "ft-titlebar__fallback"));
                return;
            }

            foreach (var old in editorElement.Children().Where(c => c.ClassListContains(OverlayClass)).ToList())
            {
                old.RemoveFromHierarchy();
            }

            var overlay = Build(title, out var row);
            editorElement.Insert(editorElement.IndexOf(header) + 1, overlay);

            // The title bar lightens on hover; follow it since the overlay covers it
            EventCallback<MouseEnterEvent> enter = _ => row.AddToClassList("ft-titlebar__row--hover");
            EventCallback<MouseLeaveEvent> leave = _ => row.RemoveFromClassList("ft-titlebar__row--hover");
            header.RegisterCallback(enter);
            header.RegisterCallback(leave);

            anchor.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                header.UnregisterCallback(enter);
                header.UnregisterCallback(leave);
                overlay.RemoveFromHierarchy();
            });
        }

        static VisualElement Build(string title, out VisualElement row)
        {
            // Zero height, so the inspector layout doesn't move. The area is pulled up over the title bar.
            var overlay = FTUI.Root();
            overlay.AddToClassList(OverlayClass);
            overlay.pickingMode = PickingMode.Ignore;

            var area = new VisualElement { pickingMode = PickingMode.Ignore };
            area.AddToClassList("ft-titlebar__area");
            overlay.Add(area);

            row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("ft-titlebar__row");
            area.Add(row);

            var tag = new VisualElement { pickingMode = PickingMode.Ignore };
            tag.AddToClassList("ft-tag");
            tag.generateVisualContent += DrawTag;
            var brand = FTUI.Text("Pawlygon", "ft-tag__text");
            brand.pickingMode = PickingMode.Ignore;
            tag.Add(brand);
            row.Add(tag);

            var name = FTUI.Text(title, "ft-titlebar__title");
            name.pickingMode = PickingMode.Ignore;
            row.Add(name);
            return overlay;
        }

        /// <summary>
        /// The tag's background: flat on the left, pointed on the right. Drawn as one antialiased shape,
        /// so the point stays smooth at any display scale.
        /// </summary>
        static void DrawTag(MeshGenerationContext context)
        {
            var size = context.visualElement.layout.size;
            var painter = context.painter2D;
            painter.fillColor = TagColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0, 0));
            painter.LineTo(new Vector2(size.x - TagPoint, 0));
            painter.LineTo(new Vector2(size.x, size.y / 2));
            painter.LineTo(new Vector2(size.x - TagPoint, size.y));
            painter.LineTo(new Vector2(0, size.y));
            painter.ClosePath();
            painter.Fill();
        }

        /// <summary>The element holding one component's title bar and its InspectorElement.</summary>
        static VisualElement FindEditorElement(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent)
            {
                if (e is InspectorElement) return e.parent;
            }
            return null;
        }
    }
}
