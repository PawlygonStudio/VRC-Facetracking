using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Base inspector for Pawlygon face tracking components: branded header, issues at the top,
    /// the component's own body, and a refresh while the inspector is visible.
    /// </summary>
    internal abstract class FTComponentEditor<T> : UnityEditor.Editor where T : PawlygonFTComponent
    {
        const int RefreshIntervalMs = 1500;

        readonly FTDynamic issuesView = new FTDynamic();
        VisualElement body;

        protected T Component => (T)target;

        protected abstract string Title { get; }
        protected abstract string Subtitle { get; }
        protected abstract string Details { get; }

        /// <summary>Builds the fields and the dynamic parts of the inspector, once.</summary>
        protected abstract void BuildBody(VisualElement body);

        /// <summary>Updates the dynamic parts for the avatar, and adds any issues.</summary>
        protected abstract void Refresh(VRCAvatarDescriptor avatar, FTIssues issues);

        public sealed override VisualElement CreateInspectorGUI()
        {
            var root = FTUI.Root();
            root.Add(FTTitleBar.Create(Title));
            root.Add(FTUI.Intro(Subtitle, Details));

            if (Component.IsTooNew)
            {
                root.Add(FTUI.Issue(FTKind.Error, "Saved by a newer version of Pawlygon Face Tracking.", "Update the package to edit this component.", null));
                return root;
            }

            root.Add(issuesView.Element);
            body = new VisualElement();
            BuildBody(body);
            root.Add(body);

            RefreshNow();
            root.schedule.Execute(RefreshNow).Every(RefreshIntervalMs);
            root.TrackSerializedObjectValue(serializedObject, _ => RefreshNow());
            return root;
        }

        protected void RefreshNow()
        {
            if (target == null || body == null) return;

            var issues = new FTIssues();
            var avatar = FTAvatarUtils.FindAvatar(Component);
            var onAvatar = avatar != null && !FTAvatarUtils.IsEditorOnly(Component.transform, avatar.transform);

            if (avatar == null)
            {
                issues.Error("Not on an avatar.", "Place this inside an object with a VRC Avatar Descriptor.");
            }
            else if (!onAvatar)
            {
                issues.Warning("Inside an EditorOnly object, ignored at build.", "EditorOnly objects are removed before the avatar is built.");
            }

            body.SetEnabled(onAvatar);
            if (onAvatar) Refresh(avatar, issues);

            issuesView.Update(issues.Key, issues.Render);
        }

        protected static void Select(UnityEngine.Object target)
        {
            if (target == null) return;
            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }
    }
}
