using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Base inspector for Pawlygon face tracking components: header, the avatar checks every
    /// component needs, and a status section refreshed while the inspector is visible.
    /// </summary>
    internal abstract class FTComponentEditor<T> : UnityEditor.Editor where T : PawlygonFTComponent
    {
        const int RefreshIntervalMs = 2000;

        VisualElement statusContainer;
        string lastStatusKey;

        protected T Component => (T)target;

        protected abstract string Title { get; }
        protected abstract string Description { get; }

        /// <summary>Adds the component's own fields.</summary>
        protected abstract void BuildFields(VisualElement root);

        /// <summary>Adds status items for a component that sits on an avatar.</summary>
        protected abstract void BuildStatus(FTStatus status, VRCAvatarDescriptor avatar);

        public sealed override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(FTInspectorUI.Header(Title, Description));

            if (Component.IsTooNew)
            {
                root.Add(new HelpBox("This component was saved by a newer version of Pawlygon Face Tracking. " +
                                     "Update the package to edit it.", HelpBoxMessageType.Error));
                return root;
            }

            BuildFields(root);

            statusContainer = new VisualElement { style = { marginTop = 8 } };
            root.Add(statusContainer);
            RefreshStatus();
            root.schedule.Execute(RefreshStatus).Every(RefreshIntervalMs);
            return root;
        }

        protected void RefreshStatus()
        {
            if (target == null || statusContainer == null) return;

            var status = new FTStatus();
            var avatar = FTAvatarUtils.FindAvatar(Component);
            if (avatar == null)
            {
                status.Error("Place this inside an avatar (an object with a VRC Avatar Descriptor).");
            }
            else if (FTAvatarUtils.IsEditorOnly(Component.transform, avatar.transform))
            {
                status.Warning("This is inside an EditorOnly object, so it's ignored when the avatar is built.");
            }
            else
            {
                BuildStatus(status, avatar);
            }

            // Only rebuild the UI when something changed, so foldouts and buttons don't flicker
            var key = status.Key;
            if (key == lastStatusKey) return;
            lastStatusKey = key;

            statusContainer.Clear();
            foreach (var item in status.Items) statusContainer.Add(item.Create());
        }
    }

    /// <summary>A list of status items, compared by content between refreshes.</summary>
    internal sealed class FTStatus
    {
        internal sealed class Item
        {
            public string Key;
            public System.Func<VisualElement> Create;
        }

        public readonly List<Item> Items = new List<Item>();

        public string Key => string.Join("\n", Items.Select(i => i.Key));

        public void Error(string message) => Box(message, HelpBoxMessageType.Error);
        public void Warning(string message) => Box(message, HelpBoxMessageType.Warning);
        public void Info(string message) => Box(message, HelpBoxMessageType.Info);

        public void Box(string message, HelpBoxMessageType type, params (string label, System.Action action)[] buttons)
        {
            Add("box:" + type + message + string.Join(",", buttons.Select(b => b.label)), () =>
            {
                var box = new HelpBox(message, type);
                if (buttons.Length == 0) return box;

                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd, marginBottom = 4 } };
                foreach (var (label, action) in buttons) row.Add(new Button(action) { text = label });
                var container = new VisualElement();
                container.Add(box);
                container.Add(row);
                return container;
            });
        }

        public void Add(string key, System.Func<VisualElement> create)
        {
            Items.Add(new Item { Key = key, Create = create });
        }
    }

    internal static class FTInspectorUI
    {
        public static VisualElement Header(string title, string description)
        {
            var header = new VisualElement { style = { marginBottom = 6 } };
            header.Add(new Label(title) { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 13, marginBottom = 2 } });
            header.Add(new HelpBox(description, HelpBoxMessageType.Info));
            return header;
        }

        public static VisualElement Row(string label, string value)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginLeft = 3, marginBottom = 1 } };
            row.Add(new Label(label) { style = { minWidth = 120, unityFontStyleAndWeight = FontStyle.Bold } });
            row.Add(new Label(value) { style = { flexShrink = 1, whiteSpace = WhiteSpace.Normal } });
            return row;
        }
    }
}
