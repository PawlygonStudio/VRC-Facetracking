using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Fake pupil layers avatars bring from Pawlygon VRCFT 1.x: the one Face Tracking Extras generated, or one made by
    /// hand. They would fight Pupil Emulation over the pupil blendshapes, so the build empties them. A layer counts
    /// when it animates nothing but pupil blendshapes, reads face or eye tracking parameters, and doesn't react to
    /// gestures (pupil effects on hand gestures are left alone).
    /// </summary>
    internal static class FTFakePupilLayers
    {
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

            var parameters = ReadParameters(layer).Select(p => MergedParameter.Replace(p, "")).ToList();
            if (parameters.Any(GestureParameters.Contains)) return false;
            return parameters.Any(IsTrackingParameter);
        }

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

        /// <summary>Parameters a layer reads: transition conditions and blend tree parameters.</summary>
        public static IEnumerable<string> ReadParameters(AnimatorControllerLayer layer)
        {
            if (layer.stateMachine == null) return Enumerable.Empty<string>();
            var conditions = FXGuardAnalysis.AllTransitions(layer.stateMachine).SelectMany(t => t.conditions).Select(c => c.parameter);
            var trees = FXGuardAnalysis.AllStates(layer.stateMachine).SelectMany(s => Trees(s.motion))
                .SelectMany(t => new[] { t.blendParameter, t.blendParameterY }.Concat(t.children.Select(c => c.directBlendParameter)));
            var stateParameters = FXGuardAnalysis.AllStates(layer.stateMachine)
                .SelectMany(s => new[] { s.speedParameterActive ? s.speedParameter : null, s.timeParameterActive ? s.timeParameter : null });
            return conditions.Concat(trees).Concat(stateParameters).Where(p => !string.IsNullOrEmpty(p)).Distinct();
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

        static IEnumerable<BlendTree> Trees(Motion motion)
        {
            if (!(motion is BlendTree tree)) return Enumerable.Empty<BlendTree>();
            return new[] { tree }.Concat(tree.children.SelectMany(c => Trees(c.motion)));
        }
    }
}
