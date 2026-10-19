using UnityEngine;

namespace Pawlygon.FaceTracking
{
    /// <summary>
    /// Fills in face tracking shapes from related ones, for trackers that don't send them (e.g. brows from the
    /// eyelids on eye-only trackers), and limits shapes that fight each other (e.g. tongue out while funneling).
    /// Emulations only ever add to the tracked value, so they never override real tracking. Applied at build time
    /// with native face tracking expressions, without animators or synced parameters.
    /// </summary>
    [AddComponentMenu("Pawlygon/Pawlygon Face Tracking Emulation")]
    [DisallowMultipleComponent]
    [FTSingleInstance]
    public class PawlygonFTEmulation : PawlygonFTComponent
    {
        public override int LatestVersion => 1;

        [Tooltip("Sticking the tongue out opens the jaw a little.")]
        public bool tongueOpensJaw = true;
        [Range(0f, 1f)] public float tongueOpensJawAmount = 0.25f;

        [Tooltip("Opening the eyes wide raises the brows.")]
        public bool browsUpWithWideEyes = true;
        [Range(0f, 1f)] public float browsUpWithWideEyesAmount = 0.33f;

        [Tooltip("Squinting lowers the brows.")]
        public bool browsDownWithSquint = true;
        [Range(0f, 1f)] public float browsDownWithSquintAmount = 0.67f;

        [Tooltip("Sneering or raising the chin lowers the brows into an angry look.")]
        public bool angryBrows = false;
        [Range(0f, 1f)] public float angryBrowsAmount = 1f;

        [Tooltip("Frowning with both mouth corners raises the inner brows into a sad look, less while the mouth is closed.")]
        public bool sadBrows = false;
        [Range(0f, 1f)] public float sadBrowsAmount = 1f;

        [Tooltip("Raising the chin (lower mouth raiser) relaxes the lip funnel.")]
        public bool limitFunnelByChinRaise = true;

        [Tooltip("A closed mouth relaxes the upper and lower mouth raisers.")]
        public bool limitRaisersByMouthClosed = true;

        [Tooltip("Funneling the lips pulls the tongue back in. Avoids false tongue out on some trackers.")]
        public bool limitTongueByFunnel = true;

        [Tooltip("Funneling the lips relaxes the upper lip raise, lower lip down and pucker.")]
        public bool limitLipsByFunnel = false;

        [Tooltip("Moving the mouth sideways reduces the jaw's sideways movement.")]
        public bool limitJawByMouthSideways = false;

        [Tooltip("Moving the mouth sideways reduces the frown on that side.")]
        public bool limitFrownByMouthSideways = false;

        [Tooltip("A closed mouth relaxes the lip suck.")]
        public bool limitLipSuckByMouthClosed = false;
    }
}
