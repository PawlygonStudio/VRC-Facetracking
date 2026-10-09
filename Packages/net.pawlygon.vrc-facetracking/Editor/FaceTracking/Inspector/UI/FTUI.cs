using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Pawlygon.FaceTracking.Editor
{
    internal enum FTKind
    {
        Ok,
        Info,
        Warning,
        Error,
        Muted,
    }

    /// <summary>
    /// Building blocks for the face tracking inspectors. Styles live in FTInspector.uss and match
    /// the look of Pawlygon Unity Tools.
    /// </summary>
    internal static class FTUI
    {
        const string UiFolder = "Packages/net.pawlygon.vrc-facetracking/Editor/FaceTracking/Inspector/UI/";

        static StyleSheet styleSheet;
        static Texture2D logo;

        public static VisualElement Root()
        {
            var root = new VisualElement();
            if (styleSheet == null) styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UiFolder + "FTInspector.uss");
            if (styleSheet != null) root.styleSheets.Add(styleSheet);
            root.AddToClassList(EditorGUIUtility.isProSkin ? "ft-dark" : "ft-light");
            return root;
        }

        public static Texture2D Logo()
        {
            if (logo == null) logo = AssetDatabase.LoadAssetAtPath<Texture2D>(UiFolder + "pawlygon_logo.png");
            return logo;
        }

        /// <summary>
        /// One-line summary under the title bar. The "?" button toggles the longer explanation.
        /// The component name and branding are drawn in the title bar by <see cref="FTTitleBar"/>.
        /// </summary>
        public static VisualElement Intro(string summary, string details)
        {
            var container = new VisualElement();
            var row = new VisualElement();
            row.AddToClassList("ft-intro");
            row.Add(Text(summary, "ft-intro__text", "ft-muted"));
            container.Add(row);

            if (!string.IsNullOrEmpty(details))
            {
                var detailsLabel = Text(details, "ft-details");
                detailsLabel.style.display = DisplayStyle.None;
                var help = new Button(() =>
                {
                    var visible = detailsLabel.style.display == DisplayStyle.Flex;
                    detailsLabel.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
                }) { text = "?", tooltip = "What does this do?" };
                help.AddToClassList("ft-intro__help");
                row.Add(help);
                container.Add(detailsLabel);
            }
            return container;
        }

        /// <summary>A titled card. Put badges or toggles in <paramref name="extras"/>, content in the returned body.</summary>
        public static VisualElement Card(VisualElement parent, string title, out VisualElement extras)
        {
            var card = new VisualElement();
            card.AddToClassList("ft-card");

            var header = new VisualElement();
            header.AddToClassList("ft-card__header");
            header.Add(Text(title, "ft-card__title"));
            extras = new VisualElement();
            extras.AddToClassList("ft-card__extras");
            header.Add(extras);
            card.Add(header);

            var body = new VisualElement();
            card.Add(body);
            parent.Add(card);
            return body;
        }

        public static Label Badge(string text, FTKind kind, string tooltip = null)
        {
            var badge = Text(text, "ft-badge", "ft-badge--" + KindName(kind));
            badge.tooltip = tooltip;
            return badge;
        }

        /// <summary>A one-line message with an icon and optional buttons. Longer details go in the tooltip.</summary>
        public static VisualElement Issue(FTKind kind, string text, string tooltip, IEnumerable<(string label, Action action)> actions)
        {
            var issue = new VisualElement { tooltip = tooltip };
            issue.AddToClassList("ft-issue");
            issue.AddToClassList("ft-issue--" + KindName(kind));

            var icon = new Image { image = Icon(kind) };
            icon.AddToClassList("ft-issue__icon");
            issue.Add(icon);
            issue.Add(Text(text, "ft-issue__text"));

            if (actions != null)
            {
                foreach (var (label, action) in actions)
                {
                    var button = new Button(action) { text = label };
                    button.AddToClassList("ft-issue__button");
                    issue.Add(button);
                }
            }
            return issue;
        }

        public static VisualElement Row(string label, string value, string tooltip = null)
        {
            var row = new VisualElement { tooltip = tooltip };
            row.AddToClassList("ft-row");
            row.Add(Text(label, "ft-row__label"));
            row.Add(Text(value, "ft-row__value"));
            return row;
        }

        public static VisualElement Chips(IEnumerable<string> items, Action<string> onClick = null, string tooltip = null)
        {
            var chips = new VisualElement();
            chips.AddToClassList("ft-chips");
            foreach (var item in items)
            {
                var chip = Text(item, "ft-chip");
                chip.tooltip = tooltip;
                if (onClick != null)
                {
                    var value = item;
                    chip.AddToClassList("ft-chip--clickable");
                    chip.RegisterCallback<ClickEvent>(_ => onClick(value));
                }
                chips.Add(chip);
            }
            return chips;
        }

        public static VisualElement Progress(float fraction, FTKind kind)
        {
            var bar = new VisualElement();
            bar.AddToClassList("ft-progress");
            var fill = new VisualElement { style = { width = Length.Percent(Mathf.Clamp01(fraction) * 100f) } };
            fill.AddToClassList("ft-progress__fill");
            fill.AddToClassList("ft-progress__fill--" + KindName(kind));
            bar.Add(fill);
            return bar;
        }

        /// <summary>A row of buttons bound to an enum property, for short choices like Auto / Manual.</summary>
        public static VisualElement Segmented(SerializedProperty property, string tooltip = null)
        {
            var container = new VisualElement { tooltip = tooltip };
            container.AddToClassList("ft-segmented");
            var names = property.enumDisplayNames;
            var buttons = new List<Button>();
            var serializedObject = property.serializedObject;
            var path = property.propertyPath;

            void Select(int index)
            {
                var prop = serializedObject.FindProperty(path);
                if (prop.enumValueIndex == index) return;
                prop.enumValueIndex = index;
                serializedObject.ApplyModifiedProperties();
            }

            void Sync()
            {
                var prop = serializedObject.FindProperty(path);
                for (var i = 0; i < buttons.Count; i++)
                {
                    buttons[i].EnableInClassList("ft-segmented__button--selected", prop.enumValueIndex == i);
                }
            }

            for (var i = 0; i < names.Length; i++)
            {
                var index = i;
                var button = new Button(() => Select(index)) { text = names[i] };
                button.AddToClassList("ft-segmented__button");
                if (i == 0) button.AddToClassList("ft-segmented__button--first");
                if (i == names.Length - 1) button.AddToClassList("ft-segmented__button--last");
                buttons.Add(button);
                container.Add(button);
            }
            Sync();
            container.TrackPropertyValue(property, _ => Sync());
            return container;
        }

        public static Label Text(string text, params string[] classes)
        {
            var label = new Label(text);
            foreach (var c in classes) label.AddToClassList(c);
            return label;
        }

        public static Texture Icon(FTKind kind)
        {
            switch (kind)
            {
                case FTKind.Error: return EditorGUIUtility.IconContent("console.erroricon.sml").image;
                case FTKind.Warning: return EditorGUIUtility.IconContent("console.warnicon.sml").image;
                case FTKind.Ok: return EditorGUIUtility.IconContent("TestPassed").image;
                default: return EditorGUIUtility.IconContent("console.infoicon.sml").image;
            }
        }

        static string KindName(FTKind kind) => kind.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// A part of the inspector rebuilt on refresh, only when its content key changed. Keeps foldouts,
    /// focus and buttons stable while the inspector polls the avatar.
    /// </summary>
    internal sealed class FTDynamic
    {
        // Created on first use: editors are ScriptableObjects, which can't create UI in field initializers
        VisualElement element;
        string key;

        public VisualElement Element => element ?? (element = new VisualElement());

        public void Update(string newKey, Action<VisualElement> render)
        {
            if (newKey == key) return;
            key = newKey;
            Element.Clear();
            render(Element);
        }

        public void Hide()
        {
            key = null;
            Element.Clear();
        }
    }

    /// <summary>Messages collected during a refresh and shown at the top of the inspector.</summary>
    internal sealed class FTIssues
    {
        readonly List<(FTKind kind, string text, string tooltip, (string label, Action action)[] actions)> items =
            new List<(FTKind, string, string, (string, Action)[])>();

        public int Count => items.Count;

        public void Add(FTKind kind, string text, string tooltip = null, params (string label, Action action)[] actions)
        {
            items.Add((kind, text, tooltip, actions));
        }

        public void Error(string text, string tooltip = null, params (string label, Action action)[] actions) => Add(FTKind.Error, text, tooltip, actions);
        public void Warning(string text, string tooltip = null, params (string label, Action action)[] actions) => Add(FTKind.Warning, text, tooltip, actions);
        public void Info(string text, string tooltip = null, params (string label, Action action)[] actions) => Add(FTKind.Info, text, tooltip, actions);

        public string Key
        {
            get
            {
                var parts = new List<string>();
                foreach (var item in items)
                {
                    parts.Add($"{item.kind}|{item.text}|{item.tooltip}|{item.actions?.Length}");
                }
                return string.Join("\n", parts);
            }
        }

        public void Render(VisualElement container)
        {
            foreach (var item in items)
            {
                container.Add(FTUI.Issue(item.kind, item.text, item.tooltip, item.actions));
            }
        }
    }
}
