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
    /// Bake Guards: writes the gesture and blink guards into a copy of the avatar's FX controller and switches the
    /// descriptor to it, like Unity Tools' FX Gesture Checker. The original file is never changed, so restoring it
    /// is just switching back. Face Tracking Guards then drives the guards when the avatar is built.
    /// </summary>
    internal static class FTGuardBaker
    {
        // Marks controllers made by Bake Guards. The importer's user data holds the original's GUID.
        const string Label = "PawlygonFTGuards";
        const string CopiesFolder = "Assets/Pawlygon Face Tracking";

        /// <summary>The avatar's own FX controller, which Bake Guards can copy. Null for none or an override controller.</summary>
        public static AnimatorController BakeableFX(VRCAvatarDescriptor avatar) => FTAvatarAssets.GetFX(avatar) as AnimatorController;

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

        /// <summary>The selected layers of the avatar's own FX that don't have a guard yet.</summary>
        public static List<FTGuardSelection.Layer> Pending(PawlygonFTGuards component, VRCAvatarDescriptor avatar)
        {
            var fx = BakeableFX(avatar);
            if (fx == null) return new List<FTGuardSelection.Layer>();
            var analysis = FXGuardAnalysis.Analyze(fx, FTGuardSelection.FaceMeshes(avatar));
            return FTGuardSelection.From(component, new[] { ("FX", fx, analysis) }).Unguarded.ToList();
        }

        /// <summary>
        /// Asks first, then guards the pending layers: in place when the FX is already a baked copy, otherwise in a new
        /// copy the descriptor switches to.
        /// </summary>
        public static bool Bake(PawlygonFTGuards component, VRCAvatarDescriptor avatar)
        {
            var fx = BakeableFX(avatar);
            var pending = Pending(component, avatar);
            if (fx == null || pending.Count == 0) return false;

            var gestures = pending.Where(l => l.Kind == FTGuardKind.Gesture).ToList();
            var blinks = pending.Where(l => l.Kind == FTGuardKind.Blink).ToList();
            var names = pending.Select(l => l.CleanName);
            var inPlace = IsBakedCopy(fx);
            var message = $"Adds guards to {Count(gestures.Count, "gesture layer")}{(blinks.Count > 0 ? $" and {Count(blinks.Count, "blink layer")}" : "")}:\n" +
                          string.Join(", ", names) + "\n\n" +
                          (inPlace
                              ? $"'{fx.name}' is already a copy made by Bake Guards, so the guards are added to it."
                              : $"A copy of '{fx.name}' gets the guards and becomes the avatar's FX controller. The original file isn't changed, " +
                                "and Restore Original switches back to it.");
            if (!EditorUtility.DisplayDialog("Bake Face Tracking Guards", message, "Bake Guards", "Cancel")) return false;

            Apply(component, avatar);
            return true;
        }

        /// <summary>Bakes the pending guards without asking. Returns the controller that got them.</summary>
        internal static AnimatorController Apply(PawlygonFTGuards component, VRCAvatarDescriptor avatar)
        {
            var fx = BakeableFX(avatar);
            var pending = Pending(component, avatar);
            if (fx == null || pending.Count == 0) return null;

            var inPlace = IsBakedCopy(fx);
            var target = inPlace ? fx : CreateCopy(fx);
            var path = AssetDatabase.GetAssetPath(target);
            // A copy keeps the original's layer order, so the indices still match
            var gestureIndices = pending.Where(l => l.Kind == FTGuardKind.Gesture).Select(l => l.Index).ToList();
            var blinkIndices = pending.Where(l => l.Kind == FTGuardKind.Blink).Select(l => l.Index).ToList();

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
            if (!inPlace) SetFX(avatar, target, "Bake Face Tracking Guards");
            Debug.Log($"[{FTErrorBoundary.Title}] Baked face tracking guards into '{path}'.", target);
            return target;
        }

        /// <summary>Switches the avatar back to the controller the baked copy was made from. The copy is kept.</summary>
        public static void Restore(VRCAvatarDescriptor avatar)
        {
            var original = OriginalOf(BakeableFX(avatar));
            if (original != null) SetFX(avatar, original, "Restore Original FX");
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

        static void SetFX(VRCAvatarDescriptor avatar, AnimatorController controller, string undoName)
        {
            Undo.RecordObject(avatar, undoName);
            var layers = avatar.baseAnimationLayers;
            var index = System.Array.FindIndex(layers, l => l.type == VRCAvatarDescriptor.AnimLayerType.FX);
            layers[index].animatorController = controller;
            layers[index].isDefault = false;
            avatar.baseAnimationLayers = layers;
            PrefabUtility.RecordPrefabInstancePropertyModifications(avatar);
            EditorUtility.SetDirty(avatar);
        }

        static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
    }
}
