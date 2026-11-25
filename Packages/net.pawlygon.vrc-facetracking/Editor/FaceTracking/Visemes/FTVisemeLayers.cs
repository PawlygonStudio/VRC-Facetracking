using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// The FX layers behind the Visemes menu. One runs on the avatar's owner and works out the level in effect (the one
    /// picked in the menu while face tracking is active, Full otherwise), which is synced as two bits. The other runs
    /// for everyone and sets the viseme sets' weight for that level with VRChat's blendshapes set behaviour.
    /// </summary>
    internal static class FTVisemeLayers
    {
        public const string DriverLayer = "Pawlygon FT - Visemes Level";
        public const string WeightLayer = "Pawlygon FT - Visemes";

        // Weight change per second: a tenth of a second from full to off, so visemes don't pop
        const float FadeSpeed = 10f;

        public static string StateName(FTVisemesLevel level) => $"Visemes {level}";

        /// <summary>The synced bits follow the level picked in the menu while face tracking is active, and say Full otherwise.</summary>
        public static void AddVisemesDriver(AnimatorController controller)
        {
            FXGuardWriter.OnOffConditions(controller, FTGuardDriver.UserFaceTracking, AnimatorControllerParameterType.Bool, out var tracking, out var notTracking);
            FXGuardWriter.EnsureParameter(controller, FTVisemesPlan.VisemesLevelParameter, AnimatorControllerParameterType.Int);
            FXGuardWriter.EnsureParameter(controller, FTVisemesPlan.VisemesBitAParameter, AnimatorControllerParameterType.Bool);
            FXGuardWriter.EnsureParameter(controller, FTVisemesPlan.VisemesBitBParameter, AnimatorControllerParameterType.Bool);

            var machine = FTGuardDriver.AddLayer(controller, DriverLayer);
            var states = new Dictionary<FTVisemesLevel, AnimatorState>();
            foreach (var level in FTVisemesPlan.Levels)
            {
                states[level] = FTGuardDriver.DriverState(machine, StateName(level), new Vector3(300, 100 * states.Count),
                    (FTVisemesPlan.VisemesBitAParameter, FTVisemesPlan.BitA(level) ? 1f : 0f),
                    (FTVisemesPlan.VisemesBitBParameter, FTVisemesPlan.BitB(level) ? 1f : 0f));
            }
            machine.defaultState = states[FTVisemesLevel.Full];

            foreach (var from in FTVisemesPlan.Levels)
            {
                // A lower level needs face tracking and that level picked
                foreach (var to in FTVisemesPlan.Levels.Where(l => l != from && l != FTVisemesLevel.Full))
                {
                    FTGuardDriver.Transition(states[from], states[to], tracking, Picked(to));
                }
                if (from == FTVisemesLevel.Full) continue;

                // Back to full as soon as either Full is picked or face tracking stops
                FTGuardDriver.Transition(states[from], states[FTVisemesLevel.Full], Picked(FTVisemesLevel.Full));
                FTGuardDriver.Transition(states[from], states[FTVisemesLevel.Full], notTracking);
            }
        }

        static AnimatorCondition Picked(FTVisemesLevel level)
        {
            return new AnimatorCondition { mode = AnimatorConditionMode.Equals, parameter = FTVisemesPlan.VisemesLevelParameter, threshold = (int)level };
        }

        /// <summary>The viseme sets play at their own weight, scaled by the level the synced bits say.</summary>
        public static void AddVisemesWeightLayer(AnimatorController controller, IReadOnlyList<FTVisemesPlan.VisemeSet> sets)
        {
            FXGuardWriter.OnOffConditions(controller, FTVisemesPlan.VisemesBitAParameter, AnimatorControllerParameterType.Bool, out var aOn, out var aOff);
            FXGuardWriter.OnOffConditions(controller, FTVisemesPlan.VisemesBitBParameter, AnimatorControllerParameterType.Bool, out var bOn, out var bOff);

            var machine = FTGuardDriver.AddLayer(controller, WeightLayer);
            var states = new Dictionary<FTVisemesLevel, AnimatorState>();
            foreach (var level in FTVisemesPlan.Levels)
            {
                states[level] = WeightState(machine, StateName(level), new Vector3(300, 100 * states.Count), sets, FTVisemesPlan.LevelWeight(level));
            }
            machine.defaultState = states[FTVisemesLevel.Full];

            foreach (var from in FTVisemesPlan.Levels)
            {
                foreach (var to in FTVisemesPlan.Levels.Where(l => l != from))
                {
                    FTGuardDriver.Transition(states[from], states[to], FTVisemesPlan.BitA(to) ? aOn : aOff, FTVisemesPlan.BitB(to) ? bOn : bOff);
                }
            }
        }

        static AnimatorState WeightState(AnimatorStateMachine machine, string name, Vector3 position, IReadOnlyList<FTVisemesPlan.VisemeSet> sets, float scale)
        {
            var state = machine.AddState(name, position);
            state.writeDefaultValues = false;

            var behaviour = state.AddStateMachineBehaviour<VRCAnimatorFaceTrackingBlendshapesSet>();
            behaviour.blendSets = sets.Select(set => new VRC_AnimatorFaceTrackingBlendshapesSet.BlendSet
            {
                type = VRC_AnimatorFaceTrackingBlendshapesSet.SourceType.Value,
                blendshapesSetName = set.Name,
                weight = set.Weight * scale,
                lerpSpeed = FadeSpeed,
                sourceParameter = "",
                convertRange = false,
                sourceMin = 0f,
                sourceMax = 1f,
                destMin = 0f,
                destMax = 1f,
            }).ToList();
            return state;
        }
    }
}
