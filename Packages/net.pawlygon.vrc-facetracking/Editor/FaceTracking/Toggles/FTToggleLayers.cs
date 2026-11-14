using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// The FX layers behind the Visemes toggle. One runs on the avatar's owner and works out whether visemes are muted
    /// (the toggle is off and face tracking is active), which is synced. The other runs for everyone and fades the
    /// viseme sets' weight with VRChat's blendshapes set behaviour.
    /// </summary>
    internal static class FTToggleLayers
    {
        public const string DriverLayer = "Pawlygon FT - Visemes Toggle";
        public const string WeightLayer = "Pawlygon FT - Visemes";

        // Weight change per second: a tenth of a second from full to muted, so visemes don't pop
        const float FadeSpeed = 10f;

        /// <summary>VisemesMuted is on while the Visemes toggle is off and face tracking is on.</summary>
        public static void AddVisemesDriver(AnimatorController controller)
        {
            FXGuardWriter.OnOffConditions(controller, FTGuardDriver.UserFaceTracking, AnimatorControllerParameterType.Bool, out var tracking, out var notTracking);
            FXGuardWriter.OnOffConditions(controller, FTTogglesPlan.VisemesParameter, AnimatorControllerParameterType.Bool, out var toggled, out var notToggled);
            FXGuardWriter.EnsureParameter(controller, FTTogglesPlan.VisemesMutedParameter, AnimatorControllerParameterType.Bool);

            var machine = FTGuardDriver.AddLayer(controller, DriverLayer);
            var playing = FTGuardDriver.DriverState(machine, "Visemes Playing", new Vector3(300, 0), FTTogglesPlan.VisemesMutedParameter, 0);
            var muted = FTGuardDriver.DriverState(machine, "Visemes Muted", new Vector3(300, 100), FTTogglesPlan.VisemesMutedParameter, 1);
            machine.defaultState = playing;

            FTGuardDriver.Transition(playing, muted, tracking, notToggled);
            // Back as soon as either the toggle is turned on or face tracking stops
            FTGuardDriver.Transition(muted, playing, toggled);
            FTGuardDriver.Transition(muted, playing, notTracking);
        }

        /// <summary>The viseme sets play at their own weight, or at 0 while VisemesMuted is on.</summary>
        public static void AddVisemesWeightLayer(AnimatorController controller, IReadOnlyList<FTTogglesPlan.VisemeSet> sets)
        {
            FXGuardWriter.OnOffConditions(controller, FTTogglesPlan.VisemesMutedParameter, AnimatorControllerParameterType.Bool, out var isMuted, out var notMuted);

            var machine = FTGuardDriver.AddLayer(controller, WeightLayer);
            var playing = WeightState(machine, "Visemes Playing", new Vector3(300, 0), sets, false);
            var muted = WeightState(machine, "Visemes Muted", new Vector3(300, 100), sets, true);
            machine.defaultState = playing;

            FTGuardDriver.Transition(playing, muted, isMuted);
            FTGuardDriver.Transition(muted, playing, notMuted);
        }

        static AnimatorState WeightState(AnimatorStateMachine machine, string name, Vector3 position, IReadOnlyList<FTTogglesPlan.VisemeSet> sets, bool muted)
        {
            var state = machine.AddState(name, position);
            state.writeDefaultValues = false;

            var behaviour = state.AddStateMachineBehaviour<VRCAnimatorFaceTrackingBlendshapesSet>();
            behaviour.blendSets = sets.Select(set => new VRC_AnimatorFaceTrackingBlendshapesSet.BlendSet
            {
                type = VRC_AnimatorFaceTrackingBlendshapesSet.SourceType.Value,
                blendshapesSetName = set.Name,
                weight = muted ? 0f : set.Weight,
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
