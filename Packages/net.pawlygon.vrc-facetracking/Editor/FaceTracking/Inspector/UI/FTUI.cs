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

        public static VisualElement Root()
        {
            var root = new VisualElement();
            if (styleSheet == null) styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UiFolder + "FTInspector.uss");
            if (styleSheet != null) root.styleSheets.Add(styleSheet);
            root.AddToClassList(EditorGUIUtility.isProSkin ? "ft-dark" : "ft-light");
            return root;
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

        /// <summary>
        /// A foldout whose arrow lines up with the card content. The inspector's default style pulls
        /// foldout arrows left into the gutter, which puts them outside our cards.
        /// </summary>
        public static Foldout Collapsible(string text, bool open = false, string tooltip = null)
        {
            var foldout = new Foldout { text = text, value = open, tooltip = tooltip };
            foldout.AddToClassList("ft-foldout");
            var toggle = foldout.Q<Toggle>(className: UnityEngine.UIElements.Foldout.toggleUssClassName);
            if (toggle != null) toggle.style.marginLeft = 0;
            return foldout;
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

        /// <summary>Chips with a tooltip each.</summary>
        public static VisualElement Chips(IEnumerable<(string text, string tooltip)> items)
        {
            var chips = new VisualElement();
            chips.AddToClassList("ft-chips");
            foreach (var (text, tooltip) in items)
            {
                var chip = Text(text, "ft-chip");
                chip.tooltip = tooltip;
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
    /// A small icon next to a setting that has a problem, with the problem in its tooltip. Hidden while there's
    /// none. Set through <see cref="FTIssues.Mark"/>, which clears it again on the next refresh.
    /// </summary>
    internal sealed class FTMark : Image
    {
        FTKind? kind;

        public FTMark()
        {
            AddToClassList("ft-mark");
            Hide();
        }

        public void Hide()
        {
            kind = null;
            image = null;
            tooltip = null;
            style.display = DisplayStyle.None;
            RemoveFromClassList("ft-mark--warning");
            RemoveFromClassList("ft-mark--error");
        }

        public void Set(FTKind newKind, string text)
        {
            tooltip = string.IsNullOrEmpty(tooltip) ? text : tooltip + "\n\n" + text;
            // The worst one decides the icon
            if (kind.HasValue && Rank(kind.Value) >= Rank(newKind)) return;
            kind = newKind;
            image = FTUI.Icon(newKind);
            style.display = DisplayStyle.Flex;
            EnableInClassList("ft-mark--warning", newKind == FTKind.Warning);
            EnableInClassList("ft-mark--error", newKind == FTKind.Error);
        }

        static int Rank(FTKind value) => value == FTKind.Error ? 3 : value == FTKind.Warning ? 2 : 1;
    }

    /// <summary>
    /// Tabs: one page of the inspector at a time. A tab can show a count next to its name, and shows a warning or
    /// error icon while its page has a problem, so problems on the pages that aren't open stay visible. The open tab
    /// is remembered for the session.
    /// </summary>
    internal sealed class FTTabs
    {
        sealed class Tab
        {
            public Button Button;
            public Label Count;
            public Image Status;
            public VisualElement Page;
        }

        readonly List<Tab> tabs = new List<Tab>();
        readonly VisualElement bar = new VisualElement();
        readonly VisualElement pages = new VisualElement();
        readonly string stateKey;
        readonly int defaultIndex;

        public VisualElement Element { get; } = new VisualElement();

        public FTTabs(string stateKey, int defaultIndex = 0)
        {
            this.stateKey = stateKey;
            this.defaultIndex = defaultIndex;
            bar.AddToClassList("ft-tabs");
            Element.Add(bar);
            Element.Add(pages);
        }

        /// <summary>Adds a tab and returns its page.</summary>
        public VisualElement Add(string title, string tooltip = null)
        {
            var index = tabs.Count;
            var tab = new Tab { Button = new Button(() => Select(index)) { tooltip = tooltip }, Page = new VisualElement() };
            tab.Button.AddToClassList("ft-tab");
            tab.Button.Add(FTUI.Text(title, "ft-tab__title"));
            tab.Count = FTUI.Text("", "ft-tab__count");
            tab.Button.Add(tab.Count);
            tab.Status = new Image();
            tab.Status.AddToClassList("ft-tab__status");
            tab.Button.Add(tab.Status);
            tab.Page.AddToClassList("ft-page");
            bar.Add(tab.Button);
            pages.Add(tab.Page);
            tabs.Add(tab);

            Show(SessionState.GetInt(stateKey, defaultIndex));
            return tab.Page;
        }

        public void SetCount(VisualElement page, string text)
        {
            var tab = tabs.Find(t => t.Page == page);
            if (tab != null) tab.Count.text = text ?? "";
        }

        public void Select(int index)
        {
            SessionState.SetInt(stateKey, index);
            Show(index);
        }

        void Show(int index)
        {
            index = Mathf.Clamp(index, 0, tabs.Count - 1);
            for (var i = 0; i < tabs.Count; i++)
            {
                tabs[i].Page.style.display = i == index ? DisplayStyle.Flex : DisplayStyle.None;
                tabs[i].Button.EnableInClassList("ft-tab--selected", i == index);
            }
        }

        /// <summary>Shows on each tab the worst problem its page has: a mark, a message or a badge.</summary>
        public void RefreshStatus()
        {
            foreach (var tab in tabs)
            {
                var worst = Has(tab.Page, "error") ? FTKind.Error : Has(tab.Page, "warning") ? FTKind.Warning : (FTKind?)null;
                tab.Status.image = worst.HasValue ? FTUI.Icon(worst.Value) : null;
                tab.Status.style.display = worst.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        static bool Has(VisualElement page, string kind)
        {
            return page.Q(className: "ft-mark--" + kind) != null || page.Q(className: "ft-issue--" + kind) != null
                   || page.Q(className: "ft-badge--" + kind) != null;
        }
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

    /// <summary>
    /// Messages collected during a refresh. The ones that stop a build or concern the whole avatar are shown at the top
    /// of the inspector. One about a single setting goes next to it with <see cref="Mark"/>, and one about a page can
    /// be rendered inside that page.
    /// </summary>
    internal sealed class FTIssues
    {
        /// <summary>The marks set in this refresh. The editor clears the ones set before that aren't among them.</summary>
        public readonly HashSet<FTMark> Marks = new HashSet<FTMark>();

        /// <summary>Shows the message as an icon next to a setting.</summary>
        public void Mark(FTMark mark, FTKind kind, string text, string tooltip = null)
        {
            if (mark == null) return;
            if (Marks.Add(mark)) mark.Hide();
            mark.Set(kind, string.IsNullOrEmpty(tooltip) ? text : text + "\n" + tooltip);
        }

        readonly List<(FTKind kind, string text, string tooltip, (string label, Action action)[] actions)> items =
            new List<(FTKind, string, string, (string, Action)[])>();

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
