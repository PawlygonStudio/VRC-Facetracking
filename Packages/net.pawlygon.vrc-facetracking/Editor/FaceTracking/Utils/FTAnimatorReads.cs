using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Which parameters an animator actually reads. Controllers made for OSC face tracking often declare far more
    /// parameters than their layers use, and driving or forwarding one costs synced bits or performance rank, so
    /// only the ones that are read count.
    /// </summary>
    internal static class FTAnimatorReads
    {
        /// <summary>
        /// Parameters a layer reads: transition conditions, blend tree parameters, state speed / time parameters, the
        /// sources parameter drivers copy from, and the ones blendshapes set weights are taken from.
        /// </summary>
        public static IEnumerable<string> ReadParameters(AnimatorControllerLayer layer)
        {
            if (layer.stateMachine == null) return Enumerable.Empty<string>();
            var states = FXGuardAnalysis.AllStates(layer.stateMachine).ToList();
            var conditions = FXGuardAnalysis.AllTransitions(layer.stateMachine).SelectMany(t => t.conditions).Select(c => c.parameter);
            var trees = states.SelectMany(s => Trees(s.motion))
                .SelectMany(t => new[] { t.blendParameter, t.blendParameterY }.Concat(t.children.Select(c => c.directBlendParameter)));
            var stateParameters = states
                .SelectMany(s => new[] { s.speedParameterActive ? s.speedParameter : null, s.timeParameterActive ? s.timeParameter : null });
            var behaviours = states.SelectMany(s => s.behaviours).Concat(StateMachines(layer.stateMachine).SelectMany(m => m.behaviours)).ToList();
            var copied = behaviours
                .OfType<VRCAvatarParameterDriver>()
                .SelectMany(d => d.parameters)
                .Where(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy)
                .Select(p => p.source);
            var setWeights = behaviours
                .OfType<VRC_AnimatorFaceTrackingBlendshapesSet>()
                .SelectMany(b => b.blendSets ?? new List<VRC_AnimatorFaceTrackingBlendshapesSet.BlendSet>())
                .Where(s => s != null && s.type == VRC_AnimatorFaceTrackingBlendshapesSet.SourceType.Parameter)
                .Select(s => s.sourceParameter);
            return conditions.Concat(trees).Concat(stateParameters).Concat(copied).Concat(setWeights).Where(p => !string.IsNullOrEmpty(p)).Distinct();
        }

        /// <summary>Whether any layer of the controller reads the parameter. Declaring it isn't enough.</summary>
        public static bool Reads(RuntimeAnimatorController controller, string parameter)
        {
            return FTAvatarUtils.BaseController(controller) is AnimatorController animator
                   && animator.layers.Any(layer => ReadParameters(layer).Contains(parameter));
        }

        static IEnumerable<AnimatorStateMachine> StateMachines(AnimatorStateMachine machine)
        {
            yield return machine;
            foreach (var child in machine.stateMachines)
            {
                foreach (var nested in StateMachines(child.stateMachine)) yield return nested;
            }
        }

        static IEnumerable<BlendTree> Trees(Motion motion)
        {
            if (!(motion is BlendTree tree)) return Enumerable.Empty<BlendTree>();
            return new[] { tree }.Concat(tree.children.SelectMany(c => Trees(c.motion)));
        }
    }
}
