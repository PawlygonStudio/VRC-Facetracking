using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Copies of an avatar's controllers that this package's inspector tools change instead of the originals (Bake
    /// Guards, and Remove Fake Pupils for controllers it can't edit in place). The avatar's reference switches to the
    /// copy: in the descriptor, or in the VRCFury Full Controller or Modular Avatar Merge Animator that merges it. The
    /// original files are never changed, so restoring is just switching back.
    /// </summary>
    internal static class FTControllerCopies
    {
        // Marks the copies. The importer's user data holds the original's GUID. Named after Bake Guards, the first
        // tool that made them, and kept so copies made by older versions are still recognized.
        const string Label = "PawlygonFTGuards";
        const string CopiesFolder = "Assets/Pawlygon Face Tracking";

        public static bool IsCopy(AnimatorController controller)
        {
            return controller != null && AssetDatabase.GetLabels(controller).Contains(Label);
        }

        /// <summary>The controller a copy was made from, or null if it's gone.</summary>
        public static AnimatorController OriginalOf(AnimatorController copy)
        {
            if (!IsCopy(copy)) return null;
            var guid = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(copy))?.userData;
            return string.IsNullOrEmpty(guid) ? null : AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath(guid));
        }

        /// <summary>
        /// The controllers that can be switched to a copy: the ones the descriptor or a component in the avatar references
        /// directly. One used through an override controller can't be, as the override would still point at the original.
        /// </summary>
        public static HashSet<AnimatorController> Referenced(VRCAvatarDescriptor avatar)
        {
            var result = new HashSet<AnimatorController>();
            foreach (var behaviour in Holders(avatar))
            {
                using (var serialized = new SerializedObject(behaviour))
                {
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue is AnimatorController controller)
                        {
                            result.Add(controller);
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>The copies the avatar uses, with the controllers they were made from.</summary>
        public static List<(AnimatorController copy, AnimatorController original)> Copies(VRCAvatarDescriptor avatar)
        {
            return Referenced(avatar).Where(IsCopy).Select(c => (c, OriginalOf(c))).Where(x => x.Item2 != null).ToList();
        }

        /// <summary>
        /// Copies the controller's file: next to the original, or in <c>Assets/Pawlygon Face Tracking</c> when the
        /// original is in a package, which can't be written to.
        /// </summary>
        public static AnimatorController CreateCopy(AnimatorController original, string suffix)
        {
            var originalPath = AssetDatabase.GetAssetPath(original);
            var folder = originalPath.StartsWith("Assets/") ? Path.GetDirectoryName(originalPath)?.Replace('\\', '/') : CopiesFolder;
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(CopiesFolder));

            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{original.name} ({suffix}).controller");
            if (!AssetDatabase.CopyAsset(originalPath, path)) throw new FTBuildException($"Couldn't copy '{originalPath}'.");

            var copy = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            AssetDatabase.SetLabels(copy, new[] { Label });
            var importer = AssetImporter.GetAtPath(path);
            importer.userData = AssetDatabase.AssetPathToGUID(originalPath);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        }

        /// <summary>
        /// Points every reference to <paramref name="from"/> in the avatar's descriptor and components at <paramref name="to"/>.
        /// Only this avatar changes: other avatars and prefab assets using the controller keep it.
        /// </summary>
        public static void Switch(VRCAvatarDescriptor avatar, AnimatorController from, AnimatorController to, string undoName)
        {
            Undo.SetCurrentGroupName(undoName);
            foreach (var behaviour in Holders(avatar))
            {
                using (var serialized = new SerializedObject(behaviour))
                {
                    var changed = false;
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue != from) continue;
                        property.objectReferenceValue = to;
                        // VRCFury keeps a GUID id next to the reference; empty, it's rebuilt from the reference like its own setter does
                        var cut = property.propertyPath.LastIndexOf('.');
                        var id = cut > 0 ? serialized.FindProperty(property.propertyPath.Substring(0, cut) + ".id") : null;
                        if (id != null && id.propertyType == SerializedPropertyType.String) id.stringValue = "";
                        changed = true;
                    }
                    if (!changed) continue;
                    serialized.ApplyModifiedProperties();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(behaviour);
                }
            }
        }

        /// <summary>The descriptor and the components that may reference the controllers: VRCFury, Modular Avatar and the like.</summary>
        static IEnumerable<MonoBehaviour> Holders(VRCAvatarDescriptor avatar)
        {
            return avatar.GetComponentsInChildren<MonoBehaviour>(true).Where(b => b != null && !(b is PawlygonFTComponent));
        }
    }
}
