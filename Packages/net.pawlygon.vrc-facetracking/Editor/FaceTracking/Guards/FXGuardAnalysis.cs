using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Pawlygon.FaceTracking.Editor
{
    internal enum FXBlinkConfidence
    {
        Low,
        Medium,
        High,
    }

    /// <summary>
    /// Finds FX layers to guard: layers driven by GestureLeft / GestureRight, and blink layers.
    /// Same detection as the FX Gesture Checker in Pawlygon Unity Tools.
    /// </summary>
    internal sealed class FXGuardAnalysis
    {
        internal sealed class GestureLayer
        {
            public int Index;
            public string Name;
            public int GestureTransitions;
            public bool AlreadyGuarded;
            // Face blendshapes set by the states gestures lead to. Empty for layers that only check
            // gestures, like a blink layer that pauses during gestures.
            public readonly List<string> ExpressionBlendshapes = new List<string>();
            public bool DrivesExpressions => ExpressionBlendshapes.Count > 0;
        }

        internal sealed class BlinkLayer
        {
            public int Index;
            public string Name;
            public int Score;
            public FXBlinkConfidence Confidence;
            public readonly List<string> Reasons = new List<string>();
            public bool AlreadyGuarded;
        }

        public const string GestureLeft = "GestureLeft";
        public const string GestureRight = "GestureRight";

        const string OwnLayerPrefix = "Pawlygon FT - ";
        const int HighConfidence = 5;
        const int MediumConfidence = 3;

        static readonly string[] StrongLayerKeywords = { "blink", "blinking", "まばたき", "瞬き", "瞬目", "マバタキ" };
        static readonly string[] WeakLayerKeywords = { "eye_close", "eyeclose", "eyes close", "eye close", "目閉じ", "目を閉じ" };

        static readonly HashSet<string> BlinkBlendshapes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "blink", "blink_l", "blink_r", "blink_left", "blink_right",
            "eye_close", "eyeclosed", "eyeclosedleft", "eyeclosedright", "eye_close_l", "eye_close_r",
            "まばたき", "まばたき左", "まばたき右", "瞬き", "瞬き左", "瞬き右",
            "目閉じ", "目閉じ左", "目閉じ右", "目を閉じ", "目を閉じ左", "目を閉じ右",
            "ウィンク", "ウィンク左", "ウィンク右", "ウインク", "ウインク左", "ウインク右",
            "mabataki",
        };

        // VRCFury prefixes the layers it merges with [VF{n}]
        static readonly Regex MergePrefix = new Regex(@"^\[VF\d+\]\s*");

        public readonly List<GestureLayer> GestureLayers = new List<GestureLayer>();
        public readonly List<BlinkLayer> BlinkLayers = new List<BlinkLayer>();

        /// <param name="faceMeshes">Meshes whose blendshapes count as facial expressions.</param>
        public static FXGuardAnalysis Analyze(AnimatorController controller, FXFaceMeshes faceMeshes)
        {
            var analysis = new FXGuardAnalysis();
            if (controller == null) return analysis;

            var layers = controller.layers;
            for (var i = 0; i < layers.Length; i++)
            {
                var stateMachine = layers[i].stateMachine;
                if (stateMachine == null) continue;
                // Layers added by this package's own builders (drivers, Pupil Emulation) are never guarded
                if (IsOwnLayer(layers[i].name)) continue;

                var gestureTransitions = AllTransitions(stateMachine).Where(UsesGesture).ToList();
                if (gestureTransitions.Count > 0)
                {
                    var layer = new GestureLayer
                    {
                        Index = i,
                        Name = layers[i].name,
                        GestureTransitions = gestureTransitions.Count,
                        AlreadyGuarded = FXGuardWriter.HasGuard(stateMachine, FXGuardWriter.GestureGuardState),
                    };
                    var reached = gestureTransitions.Where(t => !IsReturnToNeutral(t, stateMachine)).SelectMany(Destinations).Distinct();
                    layer.ExpressionBlendshapes.AddRange(reached
                        .Where(st => !FXGuardWriter.IsGuardState(st.name))
                        .SelectMany(st => FaceBlendshapes(st.motion, faceMeshes))
                        .Distinct()
                        .OrderBy(n => n));
                    analysis.GestureLayers.Add(layer);
                }

                var blink = ScoreBlinkLayer(i, layers[i]);
                if (blink.Score > 0) analysis.BlinkLayers.Add(blink);
            }

            analysis.BlinkLayers.Sort((a, b) => b.Score.CompareTo(a.Score));
            return analysis;
        }

        /// <summary>Layer name without the prefix VRCFury adds when merging, for matching user settings.</summary>
        public static string CleanLayerName(string name) => MergePrefix.Replace(name ?? "", "");

        /// <summary>A layer this package added, also after VRCFury prefixed its name when merging it.</summary>
        public static bool IsOwnLayer(string name) => CleanLayerName(name).StartsWith(OwnLayerPrefix, StringComparison.Ordinal);

        static BlinkLayer ScoreBlinkLayer(int index, AnimatorControllerLayer layer)
        {
            var result = new BlinkLayer { Index = index, Name = layer.name };
            var name = CleanLayerName(layer.name).ToLowerInvariant();

            if (StrongLayerKeywords.Any(k => name.Contains(k)))
            {
                result.Score += 3;
                result.Reasons.Add("name");
            }
            else if (WeakLayerKeywords.Any(k => name.Contains(k)))
            {
                result.Score += 1;
                result.Reasons.Add("name");
            }

            var states = AllStates(layer.stateMachine).Where(s => !FXGuardWriter.IsGuardState(s.name)).ToList();
            if (states.Any(s => AnimatesBlinkBlendshape(s.motion)))
            {
                result.Score += 3;
                result.Reasons.Add("blink blendshape");
            }
            if (states.Count >= 1 && states.Count <= 4)
            {
                result.Score += 1;
                result.Reasons.Add("simple layer");
            }

            result.Confidence = result.Score >= HighConfidence ? FXBlinkConfidence.High
                : result.Score >= MediumConfidence ? FXBlinkConfidence.Medium
                : FXBlinkConfidence.Low;
            result.AlreadyGuarded = FXGuardWriter.HasGuard(layer.stateMachine, FXGuardWriter.BlinkGuardState);
            return result;
        }

        static bool AnimatesBlinkBlendshape(Motion motion)
        {
            switch (motion)
            {
                case AnimationClip clip:
                    return AnimationUtility.GetCurveBindings(clip).Any(b =>
                        b.propertyName.StartsWith("blendShape.", StringComparison.Ordinal)
                        && BlinkBlendshapes.Contains(b.propertyName.Substring("blendShape.".Length)));
                case BlendTree tree:
                    return tree.children.Any(c => AnimatesBlinkBlendshape(c.motion));
                default:
                    return false;
            }
        }

        /// <summary>
        /// A transition back to the neutral face: an exit, a transition to the default state, or one whose
        /// gesture conditions all hold for gesture 0. Same rules as the Unity Tools FX Check.
        /// </summary>
        static bool IsReturnToNeutral(AnimatorTransitionBase transition, AnimatorStateMachine root)
        {
            if (transition.isExit) return true;
            if (transition.destinationState != null && transition.destinationState == root.defaultState) return true;
            return transition.conditions.Where(c => c.parameter == GestureLeft || c.parameter == GestureRight).All(AdmitsNeutral);
        }

        static bool AdmitsNeutral(AnimatorCondition condition)
        {
            switch (condition.mode)
            {
                case AnimatorConditionMode.Equals: return Mathf.RoundToInt(condition.threshold) == 0;
                case AnimatorConditionMode.NotEqual: return Mathf.RoundToInt(condition.threshold) != 0;
                case AnimatorConditionMode.Greater: return condition.threshold < 0;
                case AnimatorConditionMode.Less: return condition.threshold > 0;
                default: return false;
            }
        }

        static IEnumerable<AnimatorState> Destinations(AnimatorTransitionBase transition)
        {
            if (transition.destinationState != null) return new[] { transition.destinationState };
            return AllStates(transition.destinationStateMachine);
        }

        /// <summary>Blendshapes on face meshes that the motion sets above zero.</summary>
        static IEnumerable<string> FaceBlendshapes(Motion motion, FXFaceMeshes faceMeshes)
        {
            switch (motion)
            {
                case AnimationClip clip:
                    return AnimationUtility.GetCurveBindings(clip)
                        .Where(b => b.type == typeof(SkinnedMeshRenderer)
                                    && b.propertyName.StartsWith("blendShape.", StringComparison.Ordinal)
                                    && faceMeshes.Matches(b.path)
                                    && AnimationUtility.GetEditorCurve(clip, b).keys.Any(k => Mathf.Abs(k.value) > 0.001f))
                        .Select(b => b.propertyName.Substring("blendShape.".Length));
                case BlendTree tree:
                    return tree.children.SelectMany(c => FaceBlendshapes(c.motion, faceMeshes));
                default:
                    return Enumerable.Empty<string>();
            }
        }

        static bool UsesGesture(AnimatorTransitionBase transition)
        {
            return transition.conditions.Any(c => c.parameter == GestureLeft || c.parameter == GestureRight);
        }

        /// <summary>
        /// Every transition Unity can take in a layer: root Any State transitions (Unity only evaluates
        /// those on the root), and Entry, state and sub-state machine exit transitions at every level.
        /// </summary>
        public static IEnumerable<AnimatorTransitionBase> AllTransitions(AnimatorStateMachine root)
        {
            foreach (var transition in root.anyStateTransitions) yield return transition;
            foreach (var transition in StateMachineTransitions(root, root, new HashSet<AnimatorStateMachine>())) yield return transition;
        }

        static IEnumerable<AnimatorTransitionBase> StateMachineTransitions(AnimatorStateMachine root, AnimatorStateMachine machine, HashSet<AnimatorStateMachine> visited)
        {
            if (machine == null || !visited.Add(machine)) yield break;

            foreach (var transition in machine.entryTransitions) yield return transition;
            foreach (var child in machine.states)
            {
                foreach (var transition in child.state.transitions) yield return transition;
            }
            foreach (var child in machine.stateMachines)
            {
                foreach (var transition in machine.GetStateMachineTransitions(child.stateMachine)) yield return transition;
                foreach (var transition in StateMachineTransitions(root, child.stateMachine, visited)) yield return transition;
            }
        }

        public static IEnumerable<AnimatorState> AllStates(AnimatorStateMachine machine)
        {
            if (machine == null) yield break;
            foreach (var child in machine.states) yield return child.state;
            foreach (var child in machine.stateMachines)
            {
                foreach (var state in AllStates(child.stateMachine)) yield return state;
            }
        }
    }

    /// <summary>
    /// The meshes face tracking drives. Animation paths are matched exactly, or by mesh name for
    /// controllers whose paths are rewritten when merged. With no meshes, every mesh counts.
    /// </summary>
    internal sealed class FXFaceMeshes
    {
        readonly HashSet<string> paths = new HashSet<string>();
        readonly HashSet<string> names = new HashSet<string>();

        public FXFaceMeshes(IEnumerable<SkinnedMeshRenderer> meshes, Transform avatarRoot)
        {
            foreach (var mesh in meshes.Where(m => m != null))
            {
                paths.Add(FTAvatarUtils.GetPath(mesh.transform, avatarRoot));
                names.Add(mesh.name);
            }
        }

        public bool Matches(string bindingPath)
        {
            if (paths.Count == 0) return true;
            var name = bindingPath.Substring(bindingPath.LastIndexOf('/') + 1);
            return paths.Contains(bindingPath) || names.Contains(name);
        }
    }
}
