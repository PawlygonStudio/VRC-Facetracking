using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using ParameterBehavior = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingParameterBehavior;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Rewrites the shapes the enabled emulations and limits touch: each is taken out of the blendshape sets and
    /// applied by one face tracking expression instead.
    /// </summary>
    internal sealed class FaceTrackingEmulationBuilder : FTBuilder<PawlygonFTEmulation>
    {
        // After Blendshape Fixes, which traces its fix shapes through the set mappings this removes
        public override FTPhase Phase => FTPhase.Animator;

        protected override void BuildDescriptor(PawlygonFTEmulation component, FTBuildContext context)
        {
            if (!context.EnableFaceTracking)
            {
                throw new FTBuildException("Face tracking isn't enabled on this avatar. Add a Pawlygon Face Tracking component, " +
                                           "or enable face tracking on the avatar descriptor.");
            }

            var plan = FTEmulationPlan.Resolve(component, context.Sets, context.UseAllTrackingKeys);
            if (plan.NeedsAllTrackingKeys)
            {
                Debug.LogWarning($"[{FTErrorBoundary.Title}] Face Tracking Emulation needs All Tracking Keys, so it was skipped.", component);
                return;
            }
            foreach (var skipped in plan.SkippedRules)
            {
                Debug.Log($"[{FTErrorBoundary.Title}] Emulation '{skipped.Rule}' skipped: {skipped.Reason}", component);
            }

            plan.UnmapShapes(context.Sets);
            foreach (var shape in plan.Shapes)
            {
                context.Behaviors.RemoveAll(b => b.targetMode == VRCAvatarDescriptor.ParameterBehaviorTargetMode.ApplyToBlendshape
                                                 && b.targetBlendshapeName == shape.Blendshape);
                context.Behaviors.Add(new ParameterBehavior
                {
                    targetMode = VRCAvatarDescriptor.ParameterBehaviorTargetMode.ApplyToBlendshape,
                    targetBlendshapeName = shape.Blendshape,
                    // Fades with the set the shape came from, like its mapping did
                    weightBlendSetName = shape.Set ?? "",
                    outputParameterName = "",
                    targetBlendSetName = "",
                    inputTrackingKeys = new int[0],
                    inputTrackingKeyNames = new string[0],
                    inputInverted = new bool[0],
                    combineMode = VRCAvatarDescriptor.ParameterBehaviorCombineMode.Custom,
                    customExpression = shape.Expression,
                });
            }
        }
    }
}
