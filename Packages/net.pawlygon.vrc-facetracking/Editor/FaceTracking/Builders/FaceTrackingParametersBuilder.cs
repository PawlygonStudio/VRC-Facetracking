using UnityEngine;
using VRC.SDK3.Avatars.Components;
using ParameterBehavior = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingParameterBehavior;

namespace Pawlygon.FaceTracking.Editor
{
    internal sealed class FaceTrackingParametersBuilder : FTBuilder<PawlygonFTParameters>
    {
        // The SDK truncates longer output names
        const int MaxParameterNameLength = 64;

        public override FTPhase Phase => FTPhase.Behaviors;

        protected override void BuildAnimator(PawlygonFTParameters component, FTBuildContext context)
        {
            if (!context.EnableFaceTracking)
            {
                throw new FTBuildException("Face tracking isn't enabled on this avatar. Add a Pawlygon Face Tracking component, " +
                                           "or enable face tracking on the avatar descriptor.");
            }

            var plan = FTForwardingPlan.ForBuild(component, context.Descriptor, context.UseAllTrackingKeys);
            foreach (var warning in plan.Warnings)
            {
                Debug.LogWarning($"[{FTErrorBoundary.Title}] {warning}", component);
            }

            foreach (var entry in plan.Entries)
            {
                if (entry.Parameter.Length > MaxParameterNameLength)
                {
                    throw new FTBuildException($"'{entry.Parameter}' is longer than {MaxParameterNameLength} characters, " +
                                               "face tracking can't forward to it.");
                }

                // A later component, or this one, replaces an existing forward to the same parameter
                context.Behaviors.RemoveAll(b => b.targetMode == VRCAvatarDescriptor.ParameterBehaviorTargetMode.ForwardToAnimator
                                                 && b.outputParameterName == entry.Parameter);
                context.Behaviors.Add(CreateForward(entry.Parameter, entry.Expression));
            }
        }

        internal static ParameterBehavior CreateForward(string parameter, string expression)
        {
            return new ParameterBehavior
            {
                targetMode = VRCAvatarDescriptor.ParameterBehaviorTargetMode.ForwardToAnimator,
                outputParameterName = parameter,
                targetBlendSetName = "",
                targetBlendshapeName = "",
                weightBlendSetName = "",
                // Custom expressions reference tracking keys by name, the input list is unused
                inputTrackingKeys = new int[0],
                inputTrackingKeyNames = new string[0],
                inputInverted = new bool[0],
                combineMode = VRCAvatarDescriptor.ParameterBehaviorCombineMode.Custom,
                customExpression = expression,
            };
        }
    }
}
