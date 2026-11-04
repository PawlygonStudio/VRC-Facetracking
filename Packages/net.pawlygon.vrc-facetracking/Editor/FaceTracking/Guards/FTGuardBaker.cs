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
    /// Bake Guards: writes the gesture and blink guards into copies of the controllers that have the layers, like Unity
    /// Tools' FX Gesture Checker, and switches the avatar to them: the descriptor's FX or Gesture, or the reference in the
    /// VRCFury Full Controller or Modular Avatar Merge Animator that merges it. The original files are never changed, so
    /// restoring is just switching back. Face Tracking Guards then drives the guards when the avatar is built.
    /// </summary>
    internal static class FTGuardBaker
    {
        // Marks controllers made by Bake Guards. The importer's user data holds the original's GUID.
        const string Label = "PawlygonFTGuards";
        const string CopiesFolder = "Assets/Pawlygon Face Tracking";

        public static bool IsBakedCopy(AnimatorController controller)
        {
            return controller != null && AssetDatabase.GetLabels(controller).Contains(Label);
        }

        /// <summary>The controller a baked copy was made from, or null if it's gone.</summary>
        public static AnimatorController OriginalOf(AnimatorController copy)
        {
            if (!IsBakedCopy(copy)) return null;
            var guid = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(copy))?.userData;
            return string.IsNullOrEmpty(guid) ? null : AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath(guid));
        }

        /// <summary>
        /// The controllers Bake Guards can switch to a copy: the ones the descriptor or a component in the avatar references
        /// directly. One used through an override controller can't be, as the override would still point at the original.
        /// </summary>
        public static HashSet<AnimatorController> Bakeable(VRCAvatarDescriptor avatar)
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

        /// <summary>The baked copies the avatar uses, with the controllers they were made from.</summary>
        public static List<(AnimatorController copy, AnimatorController original)> BakedCopies(VRCAvatarDescriptor avatar)
        {
            return Bakeable(avatar).Where(IsBakedCopy).Select(c => (c, OriginalOf(c))).Where(x => x.Item2 != null).ToList();
        }

        /// <summary>The selected layers without a guard yet, in controllers Bake Guards can copy.</summary>
        public static List<FTGuardSelection.Layer> Pending(PawlygonFTGuards component, VRCAvatarDescriptor avatar)
        {
            var bakeable = Bakeable(avatar);
            return FTGuardSelection.Resolve(component, avatar).Unguarded.Where(l => bakeable.Contains(l.Controller)).ToList();
        }

        /// <summary>
        /// Asks first, then guards the pending layers: in place in controllers that are already baked copies, otherwise in
        /// new copies the avatar switches to.
        /// </summary>
        public static bool Bake(PawlygonFTGuards component, VRCAvatarDescriptor avatar)
        {
            var pending = Pending(component, avatar);
            if (pending.Count == 0) return false;

            var gestures = pending.Count(l => l.Kind == FTGuardKind.Gesture);
            var blinks = pending.Count(l => l.Kind == FTGuardKind.Blink);
            var controllers = pending.GroupBy(l => l.Controller).ToList();
            var message = $"Adds guards to {Count(gestures, "gesture layer")}{(blinks > 0 ? $" and {Count(blinks, "blink layer")}" : "")}:\n" +
                          string.Join("\n", controllers.Select(g => $"{g.Key.name}: {string.Join(", ", g.Select(l => l.CleanName))}")) + "\n\n" +
                          (controllers.All(g => IsBakedCopy(g.Key))
                              ? "They're already copies made by Bake Guards, so the guards are added to them."
                              : "A copy of each controller gets the guards, and the avatar uses the copy instead. The original files aren't " +
                                "changed, and Restore Originals switches back to them.");
            if (!EditorUtility.DisplayDialog("Bake Face Tracking Guards", message, "Bake Guards", "Cancel")) return false;

            Apply(component, avatar);
            return true;
        }

        /// <summary>Bakes the pending guards without asking. Returns the controllers that got them.</summary>
        internal static List<AnimatorController> Apply(PawlygonFTGuards component, VRCAvatarDescriptor avatar)
        {
            var result = new List<AnimatorController>();
            foreach (var group in Pending(component, avatar).GroupBy(l => l.Controller))
            {
                var original = group.Key;
                var inPlace = IsBakedCopy(original);
                var target = inPlace ? original : CreateCopy(original);
                var path = AssetDatabase.GetAssetPath(target);
                // A copy keeps the original's layer order, so the indices still match
                var gestureIndices = group.Where(l => l.Kind == FTGuardKind.Gesture).Select(l => l.Index).ToList();
                var blinkIndices = group.Where(l => l.Kind == FTGuardKind.Blink).Select(l => l.Index).ToList();

                // A layer whose objects live in another file would change that file too
                foreach (var index in gestureIndices.Concat(blinkIndices).Distinct())
                {
                    if (!FTLayerCloner.IsStoredIn(target.layers[index].stateMachine, path)) FTLayerCloner.CloneStateMachine(target, index);
                }

                // Blink guards first: the gesture guard then gates their Any State transitions, so it wins on layers
                // that have both, like the Unity Tools FX Check does
                if (blinkIndices.Count > 0)
                {
                    var spec = FXGuardWriter.CreateSpec(target, FXGuardWriter.BlinkGuardState, FXGuardWriter.BlinkGuardParameter,
                        AnimatorControllerParameterType.Float, new Vector3(30, -140), FXGuardWriter.GestureGuardState);
                    foreach (var index in blinkIndices) FXGuardWriter.EnsureGuard(target, index, spec);
                }
                if (gestureIndices.Count > 0)
                {
                    var spec = FXGuardWriter.CreateSpec(target, FXGuardWriter.GestureGuardState, FXGuardWriter.GestureGuardParameter,
                        AnimatorControllerParameterType.Bool, new Vector3(30, -80));
                    foreach (var index in gestureIndices) FXGuardWriter.EnsureGuard(target, index, spec);
                }

                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssets();
                if (!inPlace) Switch(avatar, original, target, "Bake Face Tracking Guards");
                Debug.Log($"[{FTErrorBoundary.Title}] Baked face tracking guards into '{path}'.", target);
                result.Add(target);
            }
            return result;
        }

        /// <summary>Switches the avatar back to the controllers the baked copies were made from. The copies are kept.</summary>
        public static void Restore(VRCAvatarDescriptor avatar)
        {
            foreach (var (copy, original) in BakedCopies(avatar)) Switch(avatar, copy, original, "Restore Original Controllers");
        }

        static AnimatorController CreateCopy(AnimatorController original)
        {
            var originalPath = AssetDatabase.GetAssetPath(original);
            // Next to the original, unless it's in a package, which can't be written to
            var folder = originalPath.StartsWith("Assets/") ? Path.GetDirectoryName(originalPath)?.Replace('\\', '/') : CopiesFolder;
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(CopiesFolder));

            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{original.name} (FT Guards).controller");
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
        static void Switch(VRCAvatarDescriptor avatar, AnimatorController from, AnimatorController to, string undoName)
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

        static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
    }
}
