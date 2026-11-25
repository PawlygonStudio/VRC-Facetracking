using UnityEngine;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Face Tracking Toggles from 2.1. It adds nothing itself anymore: Face Tracking and Pupil Emulation read its
    /// settings through <see cref="FTLegacyToggles"/> while it's on the avatar. The build only says so.
    /// </summary>
    internal sealed class FaceTrackingTogglesBuilder : FTBuilder<PawlygonFTToggles>
    {
        public override FTPhase Phase => FTPhase.Setup;

        protected override void BuildDescriptor(PawlygonFTToggles component, FTBuildContext context)
        {
            Debug.LogWarning($"[{FTErrorBoundary.Title}] Face Tracking Toggles was replaced in 2.2: the Visemes menu is set in Face Tracking " +
                             "and the pupil toggles in Pupil Emulation. This avatar still has one, and its settings are used. " +
                             "Select it and click Move Settings to bring the avatar up to date.", component);
        }
    }
}
