using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Pawlygon.FaceTracking.Editor
{
    internal sealed class FaceTrackingGuardsBuilder : FTBuilder<PawlygonFTGuards>
    {
        const string IconsFolder = "Packages/net.pawlygon.vrc-facetracking/Icons/";

        public override FTPhase Phase => FTPhase.Animator;

        /// <summary>
        /// Adds the driver layers, parameters and menu toggle before VRCFury and Modular Avatar run, so they merge
        /// them. Nothing is guarded at build time: the guards come from the avatar's animators (baked by Unity Tools'
        /// FX Gesture Checker, by Bake Guards, or by other face tracking add-ons), and each parameter is only driven
        /// when one of them reads it, so avatars don't pay for unused synced bits.
        /// </summary>
        protected override void BuildDescriptor(PawlygonFTGuards component, FTBuildContext context)
        {
            var sources = FTAvatarAssets.GetSourceFXControllers(context.Descriptor).Select(s => s.controller).ToList();
            // Pupil Emulation may have added a driver already
            var readers = context.Generated.HasFX ? sources.Append(context.Generated.FX).Distinct().ToList() : sources;

            var drivesGestures = GuardsGestures(component) && readers.Any(c => UsesParameter(c, FXGuardWriter.GestureGuardParameter));
            var drivesBlinking = readers.Any(c => UsesParameter(c, FXGuardWriter.BlinkGuardParameter));
            var drivesLipTracking = readers.Any(c => UsesParameter(c, FTGuardDriver.LipTrackingParameter));
            if (!drivesGestures && !drivesBlinking && !drivesLipTracking) return;

            var fx = context.Generated.FX;
            if (drivesBlinking) FTGuardDriver.AddBlinkDriver(fx);
            if (drivesGestures) FTGuardDriver.AddGestureDriver(fx, component.disableWithFaceTracking);
            if (drivesLipTracking) FTGuardDriver.AddLipDriver(fx);
            AddParameters(component, context, drivesGestures, drivesBlinking, drivesLipTracking);
        }

        /// <summary>Checks the parameter budget once VRCFury and Modular Avatar merged everything.</summary>
        protected override void BuildAnimator(PawlygonFTGuards component, FTBuildContext context)
        {
            // A warning, not an error: VRCFury's Parameter Compressor runs after this and may still bring it
            // under the limit. The SDK blocks the upload if it doesn't.
            var descriptor = context.Descriptor;
            var cost = descriptor.expressionParameters != null ? descriptor.expressionParameters.CalcTotalCost() : 0;
            if (cost > VRCExpressionParameters.MAX_PARAMETER_COST)
            {
                Debug.LogWarning($"[{FTErrorBoundary.Title}] The avatar's synced parameters use {cost} of {VRCExpressionParameters.MAX_PARAMETER_COST} bits " +
                                 "after adding the face tracking guard parameters. Free up some synced parameters unless a parameter compressor handles it.", component);
            }
        }

        /// <summary>Gestures need something to pause them: face tracking, the menu toggle, or both.</summary>
        public static bool GuardsGestures(PawlygonFTGuards component)
        {
            return component.guardGestures;
        }

        /// <summary>
        /// Whether the controller declares the parameter. LipTrackingActive is only driven when an animator
        /// reads it, e.g. Custom Animations made for Pawlygon VRCFT 1.x.
        /// </summary>
        public static bool UsesParameter(RuntimeAnimatorController controller, string parameter)
        {
            return FTAvatarUtils.BaseController(controller) is AnimatorController animator
                   && animator.parameters.Any(p => p.name == parameter);
        }

        /// <summary>
        /// Which layers Bake Guards guards: layers whose gestures animate the face unless excluded, and layers that only
        /// check gestures when included. Already guarded layers count as expression layers.
        /// </summary>
        public static bool ShouldGuard(PawlygonFTGuards component, FXGuardAnalysis.GestureLayer layer)
        {
            var name = FXGuardAnalysis.CleanLayerName(layer.Name);
            if (IsAutomatic(layer)) return !component.excludedGestureLayers.Contains(name);
            return component.includedGestureLayers.Contains(name);
        }

        public static bool IsAutomatic(FXGuardAnalysis.GestureLayer layer) => layer.DrivesExpressions || layer.AlreadyGuarded;

        public static bool ShouldGuard(PawlygonFTGuards component, FXGuardAnalysis.BlinkLayer layer)
        {
            var name = FXGuardAnalysis.CleanLayerName(layer.Name);
            if (component.excludedBlinkLayers.Contains(name)) return false;
            return layer.Confidence == FXBlinkConfidence.High || layer.AlreadyGuarded || component.includedBlinkLayers.Contains(name);
        }

        /// <summary>
        /// The guard parameters are set on the owner only, so they're synced. VRChat converts the synced
        /// Bool to the Float EyeTrackingActive is in the animator.
        /// </summary>
        static void AddParameters(PawlygonFTGuards component, FTBuildContext context, bool drivesGestures, bool drivesBlinking, bool drivesLipTracking)
        {
            var parameters = context.Generated.Parameters;

            if (drivesGestures)
            {
                FTAvatarAssets.AddParameter(parameters, FXGuardWriter.GestureGuardParameter, VRCExpressionParameters.ValueType.Bool, 0, false, true);
            }
            if (drivesBlinking)
            {
                FTAvatarAssets.AddParameter(parameters, FXGuardWriter.BlinkGuardParameter, VRCExpressionParameters.ValueType.Bool, 0, false, true);
            }
            if (drivesLipTracking)
            {
                FTAvatarAssets.AddParameter(parameters, FTGuardDriver.LipTrackingParameter, VRCExpressionParameters.ValueType.Bool, 0, false, true);
            }
            if (drivesGestures)
            {
                // Read by the owner's driver only, so it doesn't need syncing
                FTAvatarAssets.AddParameter(parameters, FTGuardDriver.DisableGesturesParameter, VRCExpressionParameters.ValueType.Bool,
                    component.gesturesDisabledByDefault ? 1 : 0, true, false);
                AddMenuToggle(component, context);
            }
        }

        static void AddMenuToggle(PawlygonFTGuards component, FTBuildContext context)
        {
            var menuName = string.IsNullOrWhiteSpace(component.menuName) ? "Face Tracking" : component.menuName.Trim();
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconsFolder + "FaceTrackingIcon.png");
            // The avatar's own submenu of that name may have one already
            var existing = FTAvatarAssets.FindSubmenu(context.Descriptor.expressionsMenu, menuName)?.subMenu;
            if (existing != null && existing.controls.Any(c => c.parameter?.name == FTGuardDriver.DisableGesturesParameter)) return;

            var submenu = context.Generated.GetSubmenu(menuName, icon);
            if (submenu.controls.Any(c => c.parameter?.name == FTGuardDriver.DisableGesturesParameter)) return;

            submenu.controls.Add(FTAvatarAssets.CreateControl("Disable Gestures", VRCExpressionsMenu.Control.ControlType.Toggle,
                FTGuardDriver.DisableGesturesParameter, AssetDatabase.LoadAssetAtPath<Texture2D>(IconsFolder + "Gestures_Disable_Icon.png")));
            EditorUtility.SetDirty(submenu);
        }
    }
}
