using System.Collections.Generic;
using System.Linq;
using UnityEditor;
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

        /// <summary>
        /// Animator controllers referenced anywhere in a component's serialized data, such as the
        /// controllers a VRCFury Full Controller or a Modular Avatar Merge Animator will merge.
        /// </summary>
        public static List<RuntimeAnimatorController> GetReferencedControllers(MonoBehaviour behaviour)
        {
            var controllers = new List<RuntimeAnimatorController>();
            using (var serialized = new SerializedObject(behaviour))
            {
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType == SerializedPropertyType.ObjectReference
                        && property.objectReferenceValue is RuntimeAnimatorController controller)
                    {
                        controllers.Add(controller);
                    }
                }
            }
            return controllers;
        }

        /// <summary>
        /// Like <see cref="GetReferencedControllers"/>, but only controllers merged into the given playable layer.
        /// The target is read from a "type" or "layerType" field next to the reference (VRCFury Full Controller,
        /// Modular Avatar Merge Animator). References without one are kept.
        /// </summary>
        public static List<RuntimeAnimatorController> GetReferencedControllers(MonoBehaviour behaviour, int playableLayerType)
        {
            var controllers = new List<RuntimeAnimatorController>();
            using (var serialized = new SerializedObject(behaviour))
            {
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference
                        || !(property.objectReferenceValue is RuntimeAnimatorController controller)) continue;

                    var layerType = FindSiblingLayerType(serialized, property.propertyPath);
                    if (layerType == null || layerType == playableLayerType) controllers.Add(controller);
                }
            }
            return controllers;
        }

        static int? FindSiblingLayerType(SerializedObject serialized, string path)
        {
            // Walk up from the reference, e.g. content.controllers.Array.data[0].controller.objRef
            for (var cut = path.LastIndexOf('.'); cut > 0; cut = path.LastIndexOf('.', cut - 1))
            {
                var parent = path.Substring(0, cut);
                foreach (var name in new[] { "type", "layerType" })
                {
                    var sibling = serialized.FindProperty(parent + "." + name);
                    if (sibling != null && (sibling.propertyType == SerializedPropertyType.Enum || sibling.propertyType == SerializedPropertyType.Integer))
                    {
                        return sibling.intValue;
                    }
                }
            }
            var top = serialized.FindProperty("layerType");
            return top != null && (top.propertyType == SerializedPropertyType.Enum || top.propertyType == SerializedPropertyType.Integer) ? top.intValue : (int?)null;
        }

        public static RuntimeAnimatorController BaseController(RuntimeAnimatorController controller)
        {
            return controller is AnimatorOverrideController overrideController ? overrideController.runtimeAnimatorController : controller;
        }

        /// <summary>A component's name as shown in the Add Component menu.</summary>
        public static string DisplayName(System.Type type)
        {
            var menu = (AddComponentMenu)System.Attribute.GetCustomAttribute(type, typeof(AddComponentMenu));
            var path = menu?.componentMenu;
            return string.IsNullOrEmpty(path) ? ObjectNames.NicifyVariableName(type.Name) : path.Substring(path.LastIndexOf('/') + 1);
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
