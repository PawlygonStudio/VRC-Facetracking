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
            var drivesLipTracking = UsesLipTracking(sourceFX);
            var guardsGestures = GuardsGestures(component);
            if (!guardsGestures && !component.guardBlinking && !drivesLipTracking) return;

            var fx = FTAvatarAssets.CopyFX(sourceFX, context.BuildFolder, out var assign);
            var analysis = FXGuardAnalysis.Analyze(fx, new FXFaceMeshes(context.Meshes, context.AvatarRoot.transform));

            // Blink guards first: the gesture guard then gates their Any State transitions, so it wins
            // on layers that have both, like the Unity Tools FX Check does
            if (component.guardBlinking)
            {
                var spec = FXGuardWriter.CreateSpec(fx, FXGuardWriter.BlinkGuardState, FXGuardWriter.BlinkGuardParameter,
                    AnimatorControllerParameterType.Float, new Vector3(30, -140), FXGuardWriter.GestureGuardState);
                foreach (var layer in analysis.BlinkLayers.Where(l => ShouldGuard(component, l)))
                {
                    FXGuardWriter.EnsureGuard(fx, layer.Index, spec);
                }
                FTGuardDriver.AddBlinkDriver(fx);
            }

            if (guardsGestures)
            {
                var spec = FXGuardWriter.CreateSpec(fx, FXGuardWriter.GestureGuardState, FXGuardWriter.GestureGuardParameter,
                    AnimatorControllerParameterType.Bool, new Vector3(30, -80));
                foreach (var layer in analysis.GestureLayers.Where(l => ShouldGuard(component, l)))
                {
                    FXGuardWriter.EnsureGuard(fx, layer.Index, spec);
                }
                FTGuardDriver.AddGestureDriver(fx, component.disableWithFaceTracking, component.disableGesturesToggle);
            }

            if (drivesLipTracking) FTGuardDriver.AddLipDriver(fx);

            FTAvatarAssets.SetFX(descriptor, assign);
            AddParameters(component, context, drivesLipTracking);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// LipTrackingActive is only driven when an animator reads it, e.g. Custom Animations made for
        /// Pawlygon VRCFT 1.x, so avatars that don't use it don't pay for a synced parameter.
        /// </summary>
        /// <summary>Gestures need something to pause them: face tracking, the menu toggle, or both.</summary>
        public static bool GuardsGestures(PawlygonFTGuards component)
        {
            return component.guardGestures && (component.disableWithFaceTracking || component.disableGesturesToggle);
        }

        public static bool UsesLipTracking(RuntimeAnimatorController controller)
        {
            return FTAvatarUtils.BaseController(controller) is AnimatorController animator
                   && animator.parameters.Any(p => p.name == FTGuardDriver.LipTrackingParameter);
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
        static void AddParameters(PawlygonFTGuards component, FTBuildContext context, bool drivesLipTracking)
        {
            var descriptor = context.Descriptor;
            var parameters = FTAvatarAssets.CopyParameters(descriptor, context.BuildFolder);

            if (GuardsGestures(component))
            {
                FTAvatarAssets.AddParameter(parameters, FXGuardWriter.GestureGuardParameter, VRCExpressionParameters.ValueType.Bool, 0, false, true);
            }
            if (component.guardBlinking)
            {
                FTAvatarAssets.AddParameter(parameters, FXGuardWriter.BlinkGuardParameter, VRCExpressionParameters.ValueType.Bool, 0, false, true);
            }
            if (drivesLipTracking)
            {
                FTAvatarAssets.AddParameter(parameters, FTGuardDriver.LipTrackingParameter, VRCExpressionParameters.ValueType.Bool, 0, false, true);
            }
            if (GuardsGestures(component) && component.disableGesturesToggle)
            {
                // Read by the owner's driver only, so it doesn't need syncing
                FTAvatarAssets.AddParameter(parameters, FTGuardDriver.DisableGesturesParameter, VRCExpressionParameters.ValueType.Bool, 0, true, false);
                AddMenuToggle(component, context);
            }

            var cost = parameters.CalcTotalCost();
            if (cost > VRCExpressionParameters.MAX_PARAMETER_COST)
            {
                throw new FTBuildException($"The avatar's synced parameters use {cost} of {VRCExpressionParameters.MAX_PARAMETER_COST} bits after " +
                                           "adding the face tracking guards. Free up some synced parameters.");
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
                                 "Free a slot or set an existing submenu name on the Face Tracking Guards component.", component);
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
