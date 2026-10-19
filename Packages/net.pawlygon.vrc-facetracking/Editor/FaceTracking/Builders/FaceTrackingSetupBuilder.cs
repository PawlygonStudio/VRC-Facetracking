using System.Runtime.CompilerServices;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

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

            ApplyAvatarSettings(plan, descriptor);
            plans.Remove(component);
            plans.Add(component, plan);
        }

        /// <summary>
        /// Applies the lip sync and eye settings again, in case a tool changed them in between, like VRCFury's
        /// Visemes (lip sync to parameter only) and Remove Built-in Blinking (eyelids to none) do.
        /// </summary>
        protected override void BuildAnimator(PawlygonFaceTracking component, FTBuildContext context)
        {
            if (plans.TryGetValue(component, out var plan)) ApplyAvatarSettings(plan, context.Descriptor);
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
        }
    }
}
