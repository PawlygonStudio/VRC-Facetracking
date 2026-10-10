using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// FX layers that set the guard parameters from VRChat's face and eye tracking parameters.
    /// They run on the avatar's owner only, and the guard parameters are synced to everyone else.
    /// </summary>
    internal static class FTGuardDriver
    {
        public const string UserFaceTracking = "UserFaceTracking";
        public const string UserEyeTracking = "UserEyeTracking";
        public const string DisableGesturesParameter = "PawlygonFT/DisableGestures";
        // Pawlygon VRCFT 1.x lip tracking toggle, still read by animators like Custom Animations
        public const string LipTrackingParameter = "LipTrackingActive";
        const string BlinkDriverLayer = "Pawlygon FT - Blink Guard";

        /// <summary>
        /// FacialExpressionsDisabled is on while the Disable Gestures toggle is on, or while face tracking
        /// is on when <paramref name="withFaceTracking"/> is set.
        /// </summary>
        public static void AddGestureDriver(AnimatorController controller, bool withFaceTracking, bool withToggle)
        {
            AnimatorCondition tracking = default, notTracking = default, toggled = default, notToggled = default;
            if (withFaceTracking)
            {
                FXGuardWriter.OnOffConditions(controller, UserFaceTracking, AnimatorControllerParameterType.Bool, out tracking, out notTracking);
            }
            if (withToggle)
            {
                FXGuardWriter.OnOffConditions(controller, DisableGesturesParameter, AnimatorControllerParameterType.Bool, out toggled, out notToggled);
            }

            var machine = AddLayer(controller, "Pawlygon FT - Gesture Guard");
            var enabled = DriverState(machine, "Gestures Enabled", new Vector3(300, 0), FXGuardWriter.GestureGuardParameter, 0);
            var disabled = DriverState(machine, "Gestures Disabled", new Vector3(300, 100), FXGuardWriter.GestureGuardParameter, 1);
            machine.defaultState = enabled;

            // Disabled when any source is on, enabled again once all of them are off
            if (withFaceTracking) Transition(enabled, disabled, tracking);
            if (withToggle) Transition(enabled, disabled, toggled);
            if (withFaceTracking && withToggle) Transition(disabled, enabled, notTracking, notToggled);
            else if (withFaceTracking) Transition(disabled, enabled, notTracking);
            else Transition(disabled, enabled, notToggled);
        }

        /// <summary>EyeTrackingActive follows UserEyeTracking.</summary>
        public static void AddBlinkDriver(AnimatorController controller)
        {
            // Pupil Emulation and Face Tracking Guards can both need it
            if (controller.layers.Any(l => l.name == BlinkDriverLayer)) return;

            FXGuardWriter.OnOffConditions(controller, UserEyeTracking, AnimatorControllerParameterType.Bool, out var tracking, out var notTracking);

            var machine = AddLayer(controller, BlinkDriverLayer);
            var off = DriverState(machine, "Eye Tracking Off", new Vector3(300, 0), FXGuardWriter.BlinkGuardParameter, 0);
            var on = DriverState(machine, "Eye Tracking On", new Vector3(300, 100), FXGuardWriter.BlinkGuardParameter, 1);
            machine.defaultState = off;

            Transition(off, on, tracking);
            Transition(on, off, notTracking);
        }

        /// <summary>LipTrackingActive follows UserFaceTracking.</summary>
        public static void AddLipDriver(AnimatorController controller)
        {
            FXGuardWriter.OnOffConditions(controller, UserFaceTracking, AnimatorControllerParameterType.Bool, out var tracking, out var notTracking);
            FXGuardWriter.EnsureParameter(controller, LipTrackingParameter, AnimatorControllerParameterType.Float);

            var machine = AddLayer(controller, "Pawlygon FT - Lip Tracking");
            var off = DriverState(machine, "Face Tracking Off", new Vector3(300, 0), LipTrackingParameter, 0);
            var on = DriverState(machine, "Face Tracking On", new Vector3(300, 100), LipTrackingParameter, 1);
            machine.defaultState = off;

            Transition(off, on, tracking);
            Transition(on, off, notTracking);
        }

        static AnimatorStateMachine AddLayer(AnimatorController controller, string name)
        {
            var machine = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
            if (AssetDatabase.Contains(controller)) AssetDatabase.AddObjectToAsset(machine, controller);
            controller.AddLayer(new AnimatorControllerLayer
            {
                name = controller.MakeUniqueLayerName(name),
                defaultWeight = 1,
                stateMachine = machine,
            });
            return machine;
        }

        static AnimatorState DriverState(AnimatorStateMachine machine, string name, Vector3 position, string parameter, float value)
        {
            var state = machine.AddState(name, position);
            state.writeDefaultValues = false;

            var driver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            driver.localOnly = true;
            driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
            {
                type = VRC_AvatarParameterDriver.ChangeType.Set,
                name = parameter,
                value = value,
            });
            return state;
        }

        static void Transition(AnimatorState from, AnimatorState to, params AnimatorCondition[] conditions)
        {
            var transition = from.AddTransition(to);
            transition.conditions = conditions;
            FXGuardWriter.Instant(transition);
        }
    }
}
