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
        public const string AllowGesturesParameter = "PawlygonFT/AllowGestures";

        /// <summary>FacialExpressionsDisabled is on while face tracking is on, unless gestures are allowed from the menu.</summary>
        public static void AddGestureDriver(AnimatorController controller, bool withAllowToggle)
        {
            FXGuardWriter.OnOffConditions(controller, UserFaceTracking, AnimatorControllerParameterType.Bool, out var tracking, out var notTracking);
            AnimatorCondition allowed = default, notAllowed = default;
            if (withAllowToggle)
            {
                FXGuardWriter.OnOffConditions(controller, AllowGesturesParameter, AnimatorControllerParameterType.Bool, out allowed, out notAllowed);
            }

            var machine = AddLayer(controller, "Pawlygon FT - Gesture Guard");
            var enabled = DriverState(machine, "Gestures Enabled", new Vector3(300, 0), FXGuardWriter.GestureGuardParameter, 0);
            var disabled = DriverState(machine, "Gestures Disabled", new Vector3(300, 100), FXGuardWriter.GestureGuardParameter, 1);
            machine.defaultState = enabled;

            Transition(enabled, disabled, withAllowToggle ? new[] { tracking, notAllowed } : new[] { tracking });
            Transition(disabled, enabled, notTracking);
            if (withAllowToggle) Transition(disabled, enabled, allowed);
        }

        /// <summary>EyeTrackingActive follows UserEyeTracking.</summary>
        public static void AddBlinkDriver(AnimatorController controller)
        {
            FXGuardWriter.OnOffConditions(controller, UserEyeTracking, AnimatorControllerParameterType.Bool, out var tracking, out var notTracking);

            var machine = AddLayer(controller, "Pawlygon FT - Blink Guard");
            var off = DriverState(machine, "Eye Tracking Off", new Vector3(300, 0), FXGuardWriter.BlinkGuardParameter, 0);
            var on = DriverState(machine, "Eye Tracking On", new Vector3(300, 100), FXGuardWriter.BlinkGuardParameter, 1);
            machine.defaultState = off;

            Transition(off, on, tracking);
            Transition(on, off, notTracking);
        }

        static AnimatorStateMachine AddLayer(AnimatorController controller, string name)
        {
            var machine = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(machine, controller);
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
