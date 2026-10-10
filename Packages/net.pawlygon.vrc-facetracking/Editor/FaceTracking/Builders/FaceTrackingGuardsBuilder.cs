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

        protected override void Build(PawlygonFTGuards component, FTBuildContext context)
        {
            var descriptor = context.Descriptor;
            var sourceFX = FTAvatarAssets.GetFX(descriptor);
            var analysis = FXGuardAnalysis.Analyze(FTAvatarUtils.BaseController(sourceFX) as AnimatorController,
                new FXFaceMeshes(context.Meshes, context.AvatarRoot.transform));

            // Each driver is only added when it has something to pause, or an animator already reads its
            // parameter (e.g. FX controllers guarded by Unity Tools), so avatars don't pay for unused synced bits
            var gestureLayers = GuardsGestures(component) ? analysis.GestureLayers.Where(l => ShouldGuard(component, l)).ToList() : null;
            var blinkLayers = component.guardBlinking ? analysis.BlinkLayers.Where(l => ShouldGuard(component, l)).ToList() : null;
            var drivesGestures = gestureLayers != null && (gestureLayers.Count > 0 || UsesParameter(sourceFX, FXGuardWriter.GestureGuardParameter));
            var drivesBlinking = blinkLayers != null && (blinkLayers.Count > 0 || UsesParameter(sourceFX, FXGuardWriter.BlinkGuardParameter));
            var drivesLipTracking = UsesParameter(sourceFX, FTGuardDriver.LipTrackingParameter);
            if (!drivesGestures && !drivesBlinking && !drivesLipTracking) return;

            // The copy has the same layers in the same order, so the analysis indices apply to it
            var fx = FTAvatarAssets.CopyFX(sourceFX, context.BuildFolder, out var assign);

            // Blink guards first: the gesture guard then gates their Any State transitions, so it wins
            // on layers that have both, like the Unity Tools FX Check does
            if (drivesBlinking)
            {
                var spec = FXGuardWriter.CreateSpec(fx, FXGuardWriter.BlinkGuardState, FXGuardWriter.BlinkGuardParameter,
                    AnimatorControllerParameterType.Float, new Vector3(30, -140), FXGuardWriter.GestureGuardState);
                foreach (var layer in blinkLayers) FXGuardWriter.EnsureGuard(fx, layer.Index, spec);
                FTGuardDriver.AddBlinkDriver(fx);
            }

            if (drivesGestures)
            {
                var spec = FXGuardWriter.CreateSpec(fx, FXGuardWriter.GestureGuardState, FXGuardWriter.GestureGuardParameter,
                    AnimatorControllerParameterType.Bool, new Vector3(30, -80));
                foreach (var layer in gestureLayers) FXGuardWriter.EnsureGuard(fx, layer.Index, spec);
                FTGuardDriver.AddGestureDriver(fx, component.disableWithFaceTracking, component.disableGesturesToggle);
            }

            if (drivesLipTracking) FTGuardDriver.AddLipDriver(fx);

            FTAvatarAssets.SetFX(descriptor, assign);
            AddParameters(component, context, drivesGestures, drivesBlinking, drivesLipTracking);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Gestures need something to pause them: face tracking, the menu toggle, or both.</summary>
        public static bool GuardsGestures(PawlygonFTGuards component)
        {
            return component.guardGestures && (component.disableWithFaceTracking || component.disableGesturesToggle);
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
        /// Layers whose gestures animate the face are guarded unless excluded. Layers that only check
        /// gestures are left alone unless included. Already guarded layers count as expression layers.
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
            var descriptor = context.Descriptor;
            var parameters = FTAvatarAssets.CopyParameters(descriptor, context.BuildFolder);

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
            if (drivesGestures && component.disableGesturesToggle)
            {
                // Read by the owner's driver only, so it doesn't need syncing
                FTAvatarAssets.AddParameter(parameters, FTGuardDriver.DisableGesturesParameter, VRCExpressionParameters.ValueType.Bool, 0, true, false);
                AddMenuToggle(component, context);
            }

            // A warning, not an error: VRCFury's Parameter Compressor runs after this and may still bring it
            // under the limit. The SDK blocks the upload if it doesn't.
            var cost = parameters.CalcTotalCost();
            if (cost > VRCExpressionParameters.MAX_PARAMETER_COST)
            {
                Debug.LogWarning($"[{FTErrorBoundary.Title}] The avatar's synced parameters use {cost} of {VRCExpressionParameters.MAX_PARAMETER_COST} bits " +
                                 "after adding the face tracking guards. Free up some synced parameters unless a parameter compressor handles it.", component);
            }
        }

        static void AddMenuToggle(PawlygonFTGuards component, FTBuildContext context)
        {
            var menuName = string.IsNullOrWhiteSpace(component.menuName) ? "Face Tracking" : component.menuName.Trim();
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconsFolder + "FaceTrackingIcon.png");
            var submenu = FTAvatarAssets.GetEditableSubmenu(context.Descriptor, context.BuildFolder, menuName, icon);
            if (submenu == null)
            {
                Debug.LogWarning($"[{FTErrorBoundary.Title}] The avatar's main menu is full, so the Disable Gestures toggle wasn't added. " +
                                 "Free a slot, or set In Menu on the Face Tracking Guards component to an existing submenu.", component);
                return;
            }
            if (submenu.controls.Count >= VRCExpressionsMenu.MAX_CONTROLS)
            {
                Debug.LogWarning($"[{FTErrorBoundary.Title}] The '{menuName}' menu is full, so the Disable Gestures toggle wasn't added.", component);
                return;
            }
            if (submenu.controls.Any(c => c.parameter?.name == FTGuardDriver.DisableGesturesParameter)) return;

            submenu.controls.Add(FTAvatarAssets.CreateControl("Disable Gestures", VRCExpressionsMenu.Control.ControlType.Toggle,
                FTGuardDriver.DisableGesturesParameter, AssetDatabase.LoadAssetAtPath<Texture2D>(IconsFolder + "Gestures_Disable_Icon.png")));
            EditorUtility.SetDirty(submenu);
        }
    }
}
