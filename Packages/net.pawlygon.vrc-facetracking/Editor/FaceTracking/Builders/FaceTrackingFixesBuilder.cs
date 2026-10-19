using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using ParameterBehavior = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingParameterBehavior;

namespace Pawlygon.FaceTracking.Editor
{
    internal sealed class FaceTrackingFixesBuilder : FTBuilder<PawlygonFTBlendshapeFixes>
    {
        public override FTPhase Phase => FTPhase.Behaviors;

        protected override void BuildDescriptor(PawlygonFTBlendshapeFixes component, FTBuildContext context)
        {
            if (!context.EnableFaceTracking)
            {
                throw new FTBuildException("Face tracking isn't enabled on this avatar. Add a Pawlygon Face Tracking component, " +
                                           "or enable face tracking on the avatar descriptor.");
            }

            var plan = FTFixPlan.Resolve(component, context.Meshes, context.Sets);
            foreach (var entry in plan.Entries.Where(e => e.Problem != null))
            {
                Debug.LogWarning($"[{FTErrorBoundary.Title}] Blendshape fix '{entry.Blendshape}' skipped: {entry.Problem}", component);
            }

            foreach (var entry in plan.Entries.Where(e => e.Problem == null && !e.Excluded))
            {
                context.Behaviors.RemoveAll(b => b.targetMode == VRCAvatarDescriptor.ParameterBehaviorTargetMode.ApplyToBlendshape
                                                 && b.targetBlendshapeName == entry.Blendshape);
                context.Behaviors.Add(new ParameterBehavior
                {
                    targetMode = VRCAvatarDescriptor.ParameterBehaviorTargetMode.ApplyToBlendshape,
                    targetBlendshapeName = entry.Blendshape,
                    // Fades with the set the fixed shapes come from
                    weightBlendSetName = entry.WeightSet ?? "",
                    outputParameterName = "",
                    targetBlendSetName = "",
                    inputTrackingKeys = new int[0],
                    inputTrackingKeyNames = new string[0],
                    inputInverted = new bool[0],
                    combineMode = VRCAvatarDescriptor.ParameterBehaviorCombineMode.Custom,
                    customExpression = entry.Expression,
                });
            }
        }
    }
}
