using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using ParameterBehavior = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingParameterBehavior;

namespace Pawlygon.FaceTracking.Editor
{
    internal sealed class FaceTrackingSetupBuilder : FTBuilder<PawlygonFaceTracking>
    {
        // Each component's plan, from the descriptor pass to the animator pass
        static readonly ConditionalWeakTable<PawlygonFaceTracking, FaceTrackingSetupPlan> plans =
            new ConditionalWeakTable<PawlygonFaceTracking, FaceTrackingSetupPlan>();

        public override FTPhase Phase => FTPhase.Setup;

        protected override void BuildDescriptor(PawlygonFaceTracking component, FTBuildContext context)
        {
            var descriptor = context.Descriptor;
            ConfirmMatching();
            var plan = FaceTrackingSetupPlan.Resolve(component, descriptor);
            if (plan.Errors.Count > 0) throw new FTBuildException(string.Join("\n", plan.Errors));

            foreach (var warning in plan.Warnings)
            {
                Debug.LogWarning($"[{FTErrorBoundary.Title}] {warning}", component);
            }

            // Replaces whatever face tracking setup the descriptor had
            context.Clear();
            context.EnableFaceTracking = true;
            context.UseAllTrackingKeys = component.useAllTrackingKeys;
            context.VisemeDampenMode = (VRCAvatarDescriptor.VisemeDampenMode)(int)component.visemeDampenMode;
            context.VisemeDampenAmount = component.visemeDampenAmount;
            context.Meshes.AddRange(plan.Meshes);
            context.Sets.Add(plan.Set);
            if (plan.TongueSteps != null)
            {
                context.TongueSteps = plan.TongueSteps;
                foreach (var (blendshape, expression) in plan.TongueSteps.Expressions())
                {
                    context.Behaviors.Add(CreateBlendshapeBehavior(blendshape, expression, PawlygonFaceTracking.BaseSetName));
                }
            }

            ApplyAvatarSettings(plan, descriptor);
            plans.Remove(component);
            plans.Add(component, plan);

            AddVisemesMenu(component, context, plan);
        }

        /// <summary>
        /// The Visemes menu. A blendshapes set has one weight for everything in it, so the visemes move to a set of
        /// their own, and layers merged by VRCFury or Modular Avatar change that set's weight. Without the menu the
        /// avatar builds with the base set alone.
        /// </summary>
        static void AddVisemesMenu(PawlygonFaceTracking component, FTBuildContext context, FaceTrackingSetupPlan setup)
        {
            var settings = FTLegacyToggles.Visemes(component, context.AvatarRoot);
            var plan = FTVisemesPlan.Resolve(settings.Menu, context.Sets, setup.UseFaceTrackingLipSync, FTMerger.Find() != null);
            if (plan.VisemesSkipped != null && !plan.NoVisemes)
            {
                Debug.LogWarning($"[{FTErrorBoundary.Title}] The Visemes menu wasn't added. {plan.VisemesSkipped}", component);
            }
            if (!plan.AddsVisemesMenu) return;

            plan.SplitVisemes(context.Sets);

            var fx = context.Generated.FX;
            FTVisemeLayers.AddVisemesDriver(fx);
            FTVisemeLayers.AddVisemesWeightLayer(fx, plan.VisemeSets);

            var parameters = context.Generated.Parameters;
            // Read by the owner's driver only, so it doesn't need syncing
            FTAvatarAssets.AddParameter(parameters, FTVisemesPlan.VisemesLevelParameter, VRCExpressionParameters.ValueType.Int, (int)settings.Default, true, false);
            FTAvatarAssets.AddParameter(parameters, FTVisemesPlan.VisemesBitAParameter, VRCExpressionParameters.ValueType.Bool, 0, false, true);
            FTAvatarAssets.AddParameter(parameters, FTVisemesPlan.VisemesBitBParameter, VRCExpressionParameters.ValueType.Bool, 0, false, true);

            // A Visemes submenu with one toggle per level, each setting the menu parameter to its level
            var menu = FTMenu.ControlsMenu(settings.MenuName, context, FTVisemesPlan.VisemesLevelParameter);
            if (menu == null) return;

            var icon = FTMenu.Icon("Visemes_Icon.png");
            var levels = context.Generated.GetSubmenu(menu, FTVisemesPlan.VisemesMenuName, icon);
            foreach (var level in FTVisemesPlan.Levels)
            {
                var control = FTAvatarAssets.CreateControl(level.ToString(), VRCExpressionsMenu.Control.ControlType.Toggle, FTVisemesPlan.VisemesLevelParameter, icon);
                control.value = (int)level;
                levels.controls.Add(control);
            }
            EditorUtility.SetDirty(levels);
        }

        /// <summary>
        /// Applies the lip sync and eye settings again, in case a tool changed them in between, like VRCFury's
        /// Visemes (lip sync to parameter only) and Remove Built-in Blinking (eyelids to none) do.
        /// </summary>
        protected override void BuildAnimator(PawlygonFaceTracking component, FTBuildContext context)
        {
            if (plans.TryGetValue(component, out var plan)) ApplyAvatarSettings(plan, context.Descriptor);
        }

        /// <summary>
        /// Without the SDK's auto-fill, only exact Unified Expressions names are matched, which leaves most of an ARKit
        /// or SRanipal avatar's face unmapped. An upload like that shouldn't happen unnoticed, so the build asks first.
        /// </summary>
        static void ConfirmMatching()
        {
            if (FTKeys.AutoFillAvailable) return;

            // Play mode and batch builds can't prompt, and FTKeys already logged the warning
            if (Application.isPlaying || Application.isBatchMode) return;

            var proceed = UnityEditor.EditorUtility.DisplayDialog(FTErrorBoundary.Title,
                FTKeys.AutoFillMissingMessage + "\n\nThe Keys tab of the Face Tracking inspector shows what's mapped.",
                "Continue with exact names only", "Cancel build");
            if (!proceed) throw new FTBuildCancelledException("Build cancelled: blendshapes would only be matched by their exact Unified Expressions names.");
        }

        /// <summary>Drives a blendshape with an expression, fading with the given set like a mapping in it would.</summary>
        internal static ParameterBehavior CreateBlendshapeBehavior(string blendshape, string expression, string setName)
        {
            return new ParameterBehavior
            {
                targetMode = VRCAvatarDescriptor.ParameterBehaviorTargetMode.ApplyToBlendshape,
                targetBlendshapeName = blendshape,
                weightBlendSetName = setName ?? "",
                outputParameterName = "",
                targetBlendSetName = "",
                inputTrackingKeys = new int[0],
                inputTrackingKeyNames = new string[0],
                inputInverted = new bool[0],
                combineMode = VRCAvatarDescriptor.ParameterBehaviorCombineMode.Custom,
                customExpression = expression,
            };
        }

        static void ApplyAvatarSettings(FaceTrackingSetupPlan plan, VRCAvatarDescriptor descriptor)
        {
            if (plan.UseFaceTrackingLipSync)
            {
                descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShapeFaceTracking;
            }
            if (plan.EnableEyeLook)
            {
                descriptor.enableEyeLook = true;
            }
            if (plan.UseFaceTrackingEyelids)
            {
                descriptor.customEyeLookSettings.eyelidType = VRCAvatarDescriptor.EyelidType.FaceTracking;
            }
            if (plan.GazeMode.HasValue)
            {
                descriptor.customEyeLookSettings.eyeGazeMode = plan.GazeMode.Value;
            }
            if (plan.EyeRange != null)
            {
                var range = plan.EyeRange;
                var settings = descriptor.customEyeLookSettings;
                settings.eyesLookingUp = FTLegacyEyeRange.Apply(descriptor, range, FTLegacyEyeRange.Direction.Up);
                settings.eyesLookingDown = FTLegacyEyeRange.Apply(descriptor, range, FTLegacyEyeRange.Direction.Down);
                settings.eyesLookingLeft = FTLegacyEyeRange.Apply(descriptor, range, FTLegacyEyeRange.Direction.Left);
                settings.eyesLookingRight = FTLegacyEyeRange.Apply(descriptor, range, FTLegacyEyeRange.Direction.Right);
                descriptor.customEyeLookSettings = settings;
            }
        }
    }
}
