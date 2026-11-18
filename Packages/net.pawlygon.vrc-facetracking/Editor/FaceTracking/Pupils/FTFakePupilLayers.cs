using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Fake pupil layers avatars bring from Pawlygon VRCFT 1.x: the one Face Tracking Extras generated, or one made by
    /// hand. They fight Pupil Emulation over the pupil blendshapes, so its inspector lists them with Remove Fake Pupils,
    /// and the build warns about the ones still there. A layer counts when it animates nothing but pupil blendshapes,
    /// reads face or eye tracking parameters, and doesn't react to gestures (pupil effects on hand gestures are left alone).
    /// </summary>
    internal static class FTFakePupilLayers
    {
        // The fake pupil layers Pawlygon's own 1.x content brings: Face Tracking Extras (Pawlygon Unity Tools) generates
        // the first, the Custom Animations prefabs of Pawlygon avatars have the second
        const string ExtrasLayerName = "Fake Pupil Dilation";
        const string CustomAnimationsLayerName = "Fake Pupil";
        const string CustomAnimationsController = "Custom Animations";

        // VRCFury prefixes parameters it makes local to one merged controller
        static readonly Regex MergedParameter = new Regex(@"^VF\d+_");

        static readonly HashSet<string> TrackingParameters = new HashSet<string>
        {
            "EyeTrackingActive", "EyeDilationEnable", "LipTrackingActive", "UserEyeTracking", "UserFaceTracking",
        };

        static readonly HashSet<string> GestureParameters = new HashSet<string>
        {
            "GestureLeft", "GestureRight", "GestureLeftWeight", "GestureRightWeight",
        };

        /// <summary>Indices of the controller's fake pupil layers.</summary>
        public static List<int> Find(AnimatorController controller)
        {
            var result = new List<int>();
            if (controller == null) return result;

            var layers = controller.layers;
            for (var i = 0; i < layers.Length; i++)
            {
                if (IsFakePupilLayer(layers[i])) result.Add(i);
            }
            return result;
        }

        public static bool IsFakePupilLayer(AnimatorControllerLayer layer)
        {
            if (layer.stateMachine == null || FXGuardAnalysis.IsOwnLayer(layer.name)) return false;

            var states = FXGuardAnalysis.AllStates(layer.stateMachine).ToList();
            var clips = states.SelectMany(s => Clips(s.motion)).Where(c => c != null).Distinct().ToList();
            if (clips.Count == 0 || clips.Any(c => AnimationUtility.GetObjectReferenceCurveBindings(c).Length > 0)) return false;

            var bindings = clips.SelectMany(AnimationUtility.GetCurveBindings).ToList();
            if (bindings.Count == 0 || !bindings.All(IsPupilBinding)) return false;

            var parameters = FTAnimatorReads.ReadParameters(layer).Select(p => MergedParameter.Replace(p, "")).ToList();
            if (parameters.Any(GestureParameters.Contains)) return false;
            return parameters.Any(IsTrackingParameter);
        }

        /// <summary>
        /// A fake pupil layer from Pawlygon's own 1.x content (Face Tracking Extras or a Custom Animations prefab).
        /// Only meaningful for layers <see cref="Find"/> returned.
        /// </summary>
        public static bool IsPawlygonLayer(AnimatorController controller, AnimatorControllerLayer layer)
        {
            var name = FXGuardAnalysis.CleanLayerName(layer.name);
            return name == ExtrasLayerName
                   || (name == CustomAnimationsLayerName && controller.name.Contains(CustomAnimationsController));
        }

        /// <summary>
        /// Whether Remove Fake Pupils can change the controller: in place when it's in Assets, or through a copy the
        /// avatar switches to when the avatar references it directly (<paramref name="referenced"/>, from
        /// <see cref="FTControllerCopies.Referenced"/>). A package controller behind an override controller can't be.
        /// </summary>
        public static bool CanRemove(AnimatorController controller, HashSet<AnimatorController> referenced)
        {
            return InAssets(controller) || referenced.Contains(controller);
        }

        /// <summary>
        /// Empties the fake pupil layers of the controllers, after asking. Controllers in Assets are changed in place,
        /// with undo. Controllers in packages can't be written to, so a copy gets the change and the avatar's reference
        /// switches to it, like Bake Guards does.
        /// </summary>
        public static bool Remove(VRCAvatarDescriptor avatar, IEnumerable<AnimatorController> controllers)
        {
            var referenced = FTControllerCopies.Referenced(avatar);
            var targets = controllers.Distinct()
                .Select(controller => (controller, indices: Find(controller)))
                .Where(t => t.indices.Count > 0 && CanRemove(t.controller, referenced))
                .ToList();
            if (targets.Count == 0) return false;

            var list = string.Join("\n", targets.Select(t =>
                $"{t.controller.name}: {string.Join(", ", t.indices.Select(i => FXGuardAnalysis.CleanLayerName(t.controller.layers[i].name)))}"));
            var copies = targets.Any(t => !InAssets(t.controller));
            var fromExtras = targets.Any(t => t.indices.Any(i => FXGuardAnalysis.CleanLayerName(t.controller.layers[i].name) == ExtrasLayerName));
            var message = "Empties these fake pupil layers, which Pupil Emulation replaces:\n\n" + list +
                          "\n\nThe layers stay, empty, so the layers after them keep their place." +
                          (copies ? "\n\nControllers in packages can't be changed, so a copy of each gets the change and the avatar uses the copy instead." : "") +
                          (fromExtras ? "\n\nUntick Fake Pupil Dilation in Face Tracking Extras before generating again, or it comes back." : "");
            if (!EditorUtility.DisplayDialog("Remove Fake Pupils", message, "Remove", "Cancel")) return false;

            foreach (var (controller, indices) in targets)
            {
                var target = controller;
                if (InAssets(controller))
                {
                    Undo.RecordObject(controller, "Remove Fake Pupils");
                }
                else
                {
                    // A copy keeps the original's layer order, so the indices still match
                    target = FTControllerCopies.CreateCopy(controller, "No Fake Pupils");
                }

                Empty(target, indices);
                AssetDatabase.SaveAssets();
                if (target != controller) FTControllerCopies.Switch(avatar, controller, target, "Remove Fake Pupils");
                Debug.Log($"[{FTErrorBoundary.Title}] Removed the fake pupils from '{AssetDatabase.GetAssetPath(target)}'.", target);
            }
            return true;
        }

        static bool InAssets(AnimatorController controller) => AssetDatabase.GetAssetPath(controller).StartsWith("Assets/");

        /// <summary>
        /// Replaces each layer's state machine with an empty one at weight 0. The layers stay where they are, so
        /// Animator Layer Control behaviours elsewhere keep pointing at the right layers.
        /// </summary>
        public static void Empty(AnimatorController controller, IEnumerable<int> indices)
        {
            var layers = controller.layers;
            foreach (var index in indices)
            {
                var machine = new AnimatorStateMachine { name = layers[index].name, hideFlags = HideFlags.HideInHierarchy };
                if (AssetDatabase.Contains(controller)) AssetDatabase.AddObjectToAsset(machine, controller);
                var state = machine.AddState("Replaced by Pupil Emulation");
                state.writeDefaultValues = false;
                layers[index].stateMachine = machine;
                layers[index].defaultWeight = 0f;
            }
            controller.layers = layers;
            EditorUtility.SetDirty(controller);
        }

        static bool IsPupilBinding(EditorCurveBinding binding)
        {
            const string prefix = "blendShape.";
            return binding.type == typeof(SkinnedMeshRenderer)
                   && binding.propertyName.StartsWith(prefix)
                   && FTKeys.KeysForName(binding.propertyName.Substring(prefix.Length)).Any(FTKeys.IsPupilKey);
        }

        static bool IsTrackingParameter(string parameter)
        {
            return TrackingParameters.Contains(parameter) || FTParameterCatalog.GetExpression(parameter) != null;
        }

        static IEnumerable<AnimationClip> Clips(Motion motion)
        {
            if (motion is AnimationClip clip) return new[] { clip };
            if (motion is BlendTree tree) return tree.children.SelectMany(c => Clips(c.motion));
            return Enumerable.Empty<AnimationClip>();
        }
    }
}
