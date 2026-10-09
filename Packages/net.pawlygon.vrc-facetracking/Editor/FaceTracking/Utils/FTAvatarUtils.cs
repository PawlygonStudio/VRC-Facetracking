using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Pawlygon.FaceTracking.Editor
{
    internal static class FTAvatarUtils
    {
        const string EditorOnlyTag = "EditorOnly";

        public static VRCAvatarDescriptor FindAvatar(Component component)
        {
            return component ? component.GetComponentInParent<VRCAvatarDescriptor>(true) : null;
        }

        /// <summary>
        /// True when the object or one of its parents up to the avatar root is tagged EditorOnly,
        /// meaning the SDK removes it before upload.
        /// </summary>
        public static bool IsEditorOnly(Transform transform, Transform avatarRoot)
        {
            for (var t = transform; t != null; t = t.parent)
            {
                if (t.CompareTag(EditorOnlyTag)) return true;
                if (t == avatarRoot) break;
            }
            return false;
        }

        /// <summary>
        /// Components on the avatar, including inactive objects, skipping anything removed by EditorOnly.
        /// </summary>
        public static List<T> GetBuildComponents<T>(GameObject avatarRoot) where T : Component
        {
            return avatarRoot.GetComponentsInChildren<T>(true)
                .Where(c => c != null && !IsEditorOnly(c.transform, avatarRoot.transform))
                .ToList();
        }

        public static bool IsPartOfAvatar(Component component, Transform avatarRoot)
        {
            return component != null && (component.transform == avatarRoot || component.transform.IsChildOf(avatarRoot));
        }

        public static string GetPath(Transform transform, Transform avatarRoot)
        {
            if (transform == null) return "(missing)";
            if (transform == avatarRoot) return avatarRoot.name;

            var parts = new List<string>();
            for (var t = transform; t != null && t != avatarRoot; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
