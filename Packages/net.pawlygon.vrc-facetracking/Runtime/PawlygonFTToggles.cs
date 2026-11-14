using UnityEngine;

namespace Pawlygon.FaceTracking
{
    /// <summary>
    /// Menu toggles for face tracking. Disable Visemes: while the toggle is on, visemes are muted whenever face tracking
    /// is active, so they don't play on top of the tracked mouth, and they come back on their own without a tracker.
    /// Applied at build time: the visemes get their own blendshapes set, whose weight an FX layer turns down.
    /// </summary>
    [AddComponentMenu("Pawlygon/Pawlygon Face Tracking Toggles")]
    [DisallowMultipleComponent]
    [FTSingleInstance]
    public class PawlygonFTToggles : PawlygonFTComponent
    {
        public override int LatestVersion => 1;

        [Tooltip("Add a Disable Visemes toggle to the avatar's menu. While it's on, visemes are muted while face tracking is active.")]
        public bool visemesToggle = true;

        [Tooltip("The Disable Visemes toggle starts on, so visemes are muted with face tracking until it's turned off. Its state is saved.")]
        public bool visemesDisabledByDefault = false;

        [Tooltip("Submenu of the avatar's main menu that gets the toggles. Created if missing. Leave empty to put them in the main menu.")]
        public string menuName = "Face Tracking";
    }
}
