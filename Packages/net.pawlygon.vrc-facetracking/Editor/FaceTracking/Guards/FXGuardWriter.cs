using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Adds guards to FX layers, compatible with the ones Pawlygon Unity Tools bakes: an empty state
    /// entered from Any State while the guard parameter is on, which returns to the layer's default
    /// state once it clears. Applying a guard to a layer that already has one repairs it in place.
    /// </summary>
    internal static class FXGuardWriter
    {
        public const string GestureGuardState = "FacialExpressionsDisabled_Empty";
        public const string BlinkGuardState = "EyeTrackingActive_BlinkDisabled";
        public const string GestureGuardParameter = "FacialExpressionsDisabled";
        public const string BlinkGuardParameter = "EyeTrackingActive";

        internal sealed class Spec
        {
            public string StateName;
            public string Parameter;
            public AnimatorCondition Active;
            public AnimatorCondition Clear;
            public Vector3 Position;
            // A guard state whose Any State transition this guard leaves alone
            public string ExemptState;
        }

        public static bool IsGuardState(string stateName) => stateName == GestureGuardState || stateName == BlinkGuardState;

        public static bool HasGuard(AnimatorStateMachine stateMachine, string stateName)
        {
            return stateMachine != null && stateMachine.anyStateTransitions.Any(t => t.destinationState != null && t.destinationState.name == stateName);
        }

        /// <summary>
        /// Guard conditions for a parameter, adapted to its type if the controller already has it.
        /// Adds the parameter with <paramref name="defaultType"/> when it's missing.
        /// </summary>
        public static Spec CreateSpec(AnimatorController controller, string stateName, string parameter,
            AnimatorControllerParameterType defaultType, Vector3 position, string exemptState = null)
        {
            OnOffConditions(controller, parameter, defaultType, out var active, out var clear);
            return new Spec
            {
                StateName = stateName,
                Parameter = parameter,
                Position = position,
                ExemptState = exemptState,
                Active = active,
                Clear = clear,
            };
        }

        /// <summary>
        /// Conditions for "parameter is on" and "parameter is off", fitting the parameter's type in the
        /// controller. Adds the parameter with <paramref name="defaultType"/> when it's missing.
        /// </summary>
        public static void OnOffConditions(AnimatorController controller, string parameter, AnimatorControllerParameterType defaultType,
            out AnimatorCondition on, out AnimatorCondition off)
        {
            switch (EnsureParameter(controller, parameter, defaultType))
            {
                case AnimatorControllerParameterType.Bool:
                    on = Condition(AnimatorConditionMode.If, 0, parameter);
                    off = Condition(AnimatorConditionMode.IfNot, 0, parameter);
                    break;
                case AnimatorControllerParameterType.Float:
                    on = Condition(AnimatorConditionMode.Greater, 0.5f, parameter);
                    off = Condition(AnimatorConditionMode.Less, 0.5f, parameter);
                    break;
                case AnimatorControllerParameterType.Int:
                    on = Condition(AnimatorConditionMode.Greater, 0, parameter);
                    off = Condition(AnimatorConditionMode.Less, 1, parameter);
                    break;
                default:
                    throw new FTBuildException($"The FX parameter '{parameter}' is a Trigger, so it can't be used here. Rename or remove it.");
            }
        }

        /// <summary>Adds the parameter if missing, and returns the type it ends up with.</summary>
        public static AnimatorControllerParameterType EnsureParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
        {
            var existing = controller.parameters.FirstOrDefault(p => p.name == name);
            if (existing != null) return existing.type;
            controller.AddParameter(name, type);
            return type;
        }

        public static void EnsureGuard(AnimatorController controller, int layerIndex, Spec spec)
        {
            var layers = controller.layers;
            var layer = layers[layerIndex];
            var machine = layer.stateMachine;
            var writeDefaults = DominantWriteDefaults(machine);

            var guardTransition = machine.anyStateTransitions.FirstOrDefault(t => t.destinationState != null && t.destinationState.name == spec.StateName);
            var guardState = guardTransition != null
                ? guardTransition.destinationState
                : machine.states.Select(s => s.state).FirstOrDefault(s => s.name == spec.StateName) ?? machine.AddState(spec.StateName, spec.Position);
            if (guardTransition == null) guardTransition = machine.AddAnyStateTransition(guardState);

            guardTransition.conditions = new[] { spec.Active };
            Instant(guardTransition);
            guardTransition.canTransitionToSelf = false;

            // Highest priority, so nothing else in the layer can win while the guard is on
            var anyState = machine.anyStateTransitions.Where(t => t != guardTransition).ToList();
            anyState.Insert(0, guardTransition);
            machine.anyStateTransitions = anyState.ToArray();

            guardState.writeDefaultValues = writeDefaults;
            guardState.motion = writeDefaults ? null : CreateResetClip(controller, layer.name, machine, spec);

            // Back to the layer's default state once the guard clears
            var returnState = ReturnState(machine);
            foreach (var transition in guardState.transitions) guardState.RemoveTransition(transition);
            var exit = returnState != null ? guardState.AddTransition(returnState) : guardState.AddExitTransition();
            exit.conditions = new[] { spec.Clear };
            Instant(exit);

            // Nothing else may pull the layer out of the guard state while it's on
            foreach (var transition in machine.anyStateTransitions.Where(t => t != guardTransition).ToList())
            {
                if (spec.ExemptState != null && transition.destinationState != null && transition.destinationState.name == spec.ExemptState) continue;

                var onParameter = transition.conditions.Where(c => c.parameter == spec.Parameter).ToList();

                // The controller's own version of this guard (e.g. Any State -> Idle while
                // FacialExpressionsDisabled is on). Ours replaces it; keeping it would bounce the
                // layer between both states while the guard is on.
                if (onParameter.Any(c => Holds(c, 1) && !Holds(c, 0)))
                {
                    machine.RemoveAnyStateTransition(transition);
                    continue;
                }

                // Already only taken while the guard is off
                if (onParameter.Any(c => Holds(c, 0) && !Holds(c, 1))) continue;

                transition.conditions = transition.conditions.Where(c => c.parameter != spec.Parameter).Append(spec.Clear).ToArray();
            }

            controller.layers = layers;
            EditorUtility.SetDirty(controller);
        }

        public static void Instant(AnimatorStateTransition transition)
        {
            transition.hasExitTime = false;
            transition.exitTime = 0;
            transition.hasFixedDuration = true;
            transition.duration = 0;
            transition.offset = 0;
            transition.interruptionSource = TransitionInterruptionSource.None;
        }

        /// <summary>Whether the condition passes when its parameter has this value (0 = off, 1 = on).</summary>
        static bool Holds(AnimatorCondition condition, float value)
        {
            switch (condition.mode)
            {
                case AnimatorConditionMode.If: return value != 0;
                case AnimatorConditionMode.IfNot: return value == 0;
                case AnimatorConditionMode.Greater: return value > condition.threshold;
                case AnimatorConditionMode.Less: return value < condition.threshold;
                case AnimatorConditionMode.Equals: return Mathf.Approximately(value, condition.threshold);
                case AnimatorConditionMode.NotEqual: return !Mathf.Approximately(value, condition.threshold);
                default: return false;
            }
        }

        static AnimatorCondition Condition(AnimatorConditionMode mode, float threshold, string parameter)
        {
            return new AnimatorCondition { mode = mode, threshold = threshold, parameter = parameter };
        }

        static AnimatorState ReturnState(AnimatorStateMachine machine)
        {
            if (machine.defaultState != null && !IsGuardState(machine.defaultState.name)) return machine.defaultState;
            return FXGuardAnalysis.AllStates(machine).FirstOrDefault(s => !IsGuardState(s.name));
        }

        /// <summary>Majority of the layer's states, ignoring guard states. A tie means on.</summary>
        static bool DominantWriteDefaults(AnimatorStateMachine machine)
        {
            var states = FXGuardAnalysis.AllStates(machine).Where(s => !IsGuardState(s.name)).ToList();
            var on = states.Count(s => s.writeDefaultValues);
            return on * 2 >= states.Count;
        }

        /// <summary>
        /// With Write Defaults off, an empty state would keep whatever the layer last animated. The guard
        /// state plays a clip that puts everything the layer animates back to its default value instead:
        /// the default state's value when it has one, otherwise 0 for blendshapes.
        /// </summary>
        static AnimationClip CreateResetClip(AnimatorController controller, string layerName, AnimatorStateMachine machine, Spec spec)
        {
            var clips = new HashSet<AnimationClip>();
            foreach (var state in FXGuardAnalysis.AllStates(machine))
            {
                if (!IsGuardState(state.name)) CollectClips(state.motion, clips);
            }

            var defaultClip = machine.defaultState != null && !IsGuardState(machine.defaultState.name)
                ? machine.defaultState.motion as AnimationClip
                : null;
            var clip = new AnimationClip { name = $"{spec.StateName} Reset ({FXGuardAnalysis.CleanLayerName(layerName)})" };

            var floats = clips.SelectMany(AnimationUtility.GetCurveBindings).GroupBy(Key).Select(g => g.First());
            foreach (var binding in floats)
            {
                var defaultCurve = defaultClip != null ? AnimationUtility.GetEditorCurve(defaultClip, binding) : null;
                float value;
                if (defaultCurve != null && defaultCurve.length > 0) value = defaultCurve.Evaluate(0);
                else if (binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal)) value = 0;
                else continue;
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 1f / 60f, value));
            }

            var objects = clips.SelectMany(AnimationUtility.GetObjectReferenceCurveBindings).GroupBy(Key).Select(g => g.First());
            foreach (var binding in objects)
            {
                var keys = defaultClip != null ? AnimationUtility.GetObjectReferenceCurve(defaultClip, binding) : null;
                if (keys == null || keys.Length == 0) continue;
                AnimationUtility.SetObjectReferenceCurve(clip, binding, new[] { new ObjectReferenceKeyframe { time = 0, value = keys[0].value } });
            }

            if (AssetDatabase.Contains(controller)) AssetDatabase.AddObjectToAsset(clip, controller);
            return clip;
        }

        static string Key(EditorCurveBinding binding) => $"{binding.path}|{binding.type?.FullName}|{binding.propertyName}";

        static void CollectClips(Motion motion, HashSet<AnimationClip> clips)
        {
            if (motion is AnimationClip clip) clips.Add(clip);
            else if (motion is BlendTree tree)
            {
                foreach (var child in tree.children) CollectClips(child.motion, clips);
            }
        }
    }
}
