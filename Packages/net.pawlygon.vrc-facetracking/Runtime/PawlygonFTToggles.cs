using UnityEngine;
using UnityEngine.Serialization;

namespace Pawlygon.FaceTracking
{
    /// <summary>
    /// How much of the visemes plays while face tracking is active. The numbers are the menu parameter's values:
    /// Full is 0, so turning any other level's toggle off in the menu goes back to full visemes.
    /// </summary>
    public enum FTVisemesLevel
    {
        Full = 0,
        Medium = 1,
        Low = 2,
        Off = 3,
    }

    /// <summary>
    /// Menu controls for face tracking. Visemes: a submenu that picks how much of the visemes plays while face tracking
    /// is active, so they don't play at full strength on top of the tracked mouth. Without a tracker they always play in
    /// full. Pupils: toggles for the headset's pupil tracking and for Pupil Emulation, which can play together.
    /// Applied at build time: the visemes get their own blendshapes set, whose weight an FX layer changes.
    /// </summary>
    [AddComponentMenu("Pawlygon/Pawlygon Face Tracking Toggles")]
    [DisallowMultipleComponent]
    [FTSingleInstance]
    public class PawlygonFTToggles : PawlygonFTComponent
    {
        public override int LatestVersion => 1;

        [Tooltip("Add a Visemes submenu to the avatar's menu, with Full, Medium, Low and Off. The level applies while face tracking is active.")]
        [FormerlySerializedAs("visemesToggle")]
        public bool visemesMenu = true;

        [Tooltip("The level the menu starts on. Full plays visemes as usual until another level is picked. The pick is saved.")]
        public FTVisemesLevel visemesDefault = FTVisemesLevel.Full;

        [Tooltip("Add a Pupil Tracking toggle to the menu, on to start with. Off ignores the pupil size the headset reports. Needs Pupil Emulation on the avatar.")]
        public bool pupilTrackingToggle = true;

        [Tooltip("Add a Pupil Emulation toggle to the menu, on to start with. Off stops the emulated pupil movement. Needs Pupil Emulation on the avatar.")]
        public bool pupilEmulationToggle = true;

        [Tooltip("Submenu of the avatar's main menu that gets the controls. Created if missing. Leave empty to put them in the main menu.")]
        public string menuName = "Face Tracking";
    }
}
