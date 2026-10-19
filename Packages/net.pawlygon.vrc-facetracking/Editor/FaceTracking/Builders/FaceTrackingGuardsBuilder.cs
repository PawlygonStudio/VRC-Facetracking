using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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

        /// <summary>What the descriptor pass decided to drive, for the animator pass.</summary>
        sealed class Drives
        {
            public bool Gestures, Blinking;
        }

        static readonly ConditionalWeakTable<PawlygonFTGuards, Drives> decided = new ConditionalWeakTable<PawlygonFTGuards, Drives>();

        /// <summary>
        /// Adds the driver layers, parameters and menu toggle before VRCFury and Modular Avatar run, so they merge
        /// them. Whether each driver is needed is decided from the controllers that will be merged into FX.
        /// </summary>
        protected override void BuildDescriptor(PawlygonFTGuards component, FTBuildContext context)
        {
            var faceMeshes = new FXFaceMeshes(context.Meshes, context.AvatarRoot.transform);
            var sources = FTAvatarAssets.GetSourceFXControllers(context.Descriptor).Select(s => s.controller).ToList();
            var analyses = sources.Select(c => FXGuardAnalysis.Analyze(c, faceMeshes)).ToList();
            // Pupil Emulation may have added a driver already
            var readers = context.Generated.HasFX ? sources.Append(context.Generated.FX).Distinct().ToList() : sources;

            // Each driver is only added when it has something to pause, or an animator already reads its
            // parameter (e.g. FX controllers guarded by Unity Tools), so avatars don't pay for unused synced bits.
            // EyeTrackingActive and LipTrackingActive are driven whenever they're read, like Pawlygon VRCFT 1.x
            // did, since animators such as Face Tracking Extras wait for them whatever is guarded here
            var drivesGestures = GuardsGestures(component)
                                 && (analyses.Any(a => a.GestureLayers.Any(l => ShouldGuard(component, l)))
                                     || readers.Any(c => UsesParameter(c, FXGuardWriter.GestureGuardParameter)));
            var drivesBlinking = (component.guardBlinking && analyses.Any(a => a.BlinkLayers.Any(l => ShouldGuard(component, l))))
                                 || readers.Any(c => UsesParameter(c, FXGuardWriter.BlinkGuardParameter));
            var drivesLipTracking = readers.Any(c => UsesParameter(c, FTGuardDriver.LipTrackingParameter));

            decided.Remove(component);
            decided.Add(component, new Drives { Gestures = drivesGestures, Blinking = drivesBlinking });
            if (!drivesGestures && !drivesBlinking && !drivesLipTracking) return;

            var fx = context.Generated.FX;
            if (drivesBlinking) FTGuardDriver.AddBlinkDriver(fx);
            if (drivesGestures) FTGuardDriver.AddGestureDriver(fx, component.disableWithFaceTracking, component.disableGesturesToggle);
            if (drivesLipTracking) FTGuardDriver.AddLipDriver(fx);
            AddParameters(component, context, drivesGestures, drivesBlinking, drivesLipTracking);
        }

        /// <summary>
        /// Guards the gesture and blink layers of the merged FX controller, which only exists after VRCFury and
        /// Modular Avatar ran.
        /// </summary>
        protected override void BuildAnimator(PawlygonFTGuards component, FTBuildContext context)
        {
            if (!decided.TryGetValue(component, out var drives)) return;

            var descriptor = context.Descriptor;
            var sourceFX = FTAvatarAssets.GetFX(descriptor);
            var analysis = FXGuardAnalysis.Analyze(FTAvatarUtils.BaseController(sourceFX) as AnimatorController,
                new FXFaceMeshes(context.Meshes, context.AvatarRoot.transform));

            var gestureLayers = GuardsGestures(component)
                ? analysis.GestureLayers.Where(l => ShouldGuard(component, l)).ToList()
                : new List<FXGuardAnalysis.GestureLayer>();
            var blinkLayers = component.guardBlinking
                ? analysis.BlinkLayers.Where(l => ShouldGuard(component, l)).ToList()
                : new List<FXGuardAnalysis.BlinkLayer>();
            WarnUndriven(component, gestureLayers.Select(l => l.Name), drives.Gestures, FXGuardWriter.GestureGuardParameter);
            WarnUndriven(component, blinkLayers.Select(l => l.Name), drives.Blinking, FXGuardWriter.BlinkGuardParameter);
            if (!drives.Gestures) gestureLayers.Clear();
            if (!drives.Blinking) blinkLayers.Clear();

            if (gestureLayers.Count > 0 || blinkLayers.Count > 0)
            {
                var fx = FTAvatarAssets.GetBuildFX(descriptor);

                // Blink guards first: the gesture guard then gates their Any State transitions, so it wins
                // on layers that have both, like the Unity Tools FX Check does
                if (blinkLayers.Count > 0)
                {
                    var spec = FXGuardWriter.CreateSpec(fx, FXGuardWriter.BlinkGuardState, FXGuardWriter.BlinkGuardParameter,
                        AnimatorControllerParameterType.Float, new Vector3(30, -140), FXGuardWriter.GestureGuardState);
                    foreach (var layer in blinkLayers) FXGuardWriter.EnsureGuard(fx, layer.Index, spec);
                }
                if (gestureLayers.Count > 0)
                {
                    var spec = FXGuardWriter.CreateSpec(fx, FXGuardWriter.GestureGuardState, FXGuardWriter.GestureGuardParameter,
                        AnimatorControllerParameterType.Bool, new Vector3(30, -80));
                    foreach (var layer in gestureLayers) FXGuardWriter.EnsureGuard(fx, layer.Index, spec);
                }

                EditorUtility.SetDirty(fx);
                AssetDatabase.SaveAssets();
            }

            // A warning, not an error: VRCFury's Parameter Compressor runs after this and may still bring it
            // under the limit. The SDK blocks the upload if it doesn't.
            var cost = descriptor.expressionParameters != null ? descriptor.expressionParameters.CalcTotalCost() : 0;
            if (cost > VRCExpressionParameters.MAX_PARAMETER_COST)
            {
                Debug.LogWarning($"[{FTErrorBoundary.Title}] The avatar's synced parameters use {cost} of {VRCExpressionParameters.MAX_PARAMETER_COST} bits " +
                                 "after adding the face tracking guards. Free up some synced parameters unless a parameter compressor handles it.", component);
            }
        }

        /// <summary>
        /// Layers merged in by something the descriptor pass couldn't see, e.g. a controller generated during the
        /// build, while their driver wasn't added. They're left unguarded, since nothing would set the guard.
        /// </summary>
        static void WarnUndriven(PawlygonFTGuards component, IEnumerable<string> layers, bool driven, string parameter)
        {
            var names = layers.Select(FXGuardAnalysis.CleanLayerName).ToList();
            if (driven || names.Count == 0) return;
            Debug.LogWarning($"[{FTErrorBoundary.Title}] {string.Join(", ", names.Select(n => "'" + n + "'"))} " +
                             $"{(names.Count == 1 ? "was" : "were")} merged into FX during the build, after Face Tracking Guards " +
                             $"decided not to drive {parameter}, so {(names.Count == 1 ? "it isn't" : "they aren't")} guarded.", component);
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
            if (drivesGestures && component.disableGesturesToggle)
            {
                // Read by the owner's driver only, so it doesn't need syncing
                FTAvatarAssets.AddParameter(parameters, FTGuardDriver.DisableGesturesParameter, VRCExpressionParameters.ValueType.Bool, 0, true, false);
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
