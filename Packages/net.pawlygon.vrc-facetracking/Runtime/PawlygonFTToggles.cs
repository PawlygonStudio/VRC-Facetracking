using UnityEngine;
using UnityEngine.Serialization;

namespace Pawlygon.FaceTracking
{
    /// <summary>
    /// Face Tracking Toggles from 2.1, replaced in 2.2: the Visemes menu is part of Face Tracking, and the pupil
    /// toggles are part of Pupil Emulation. Kept so avatars that still have one don't end up with a missing script.
    /// While it's on an avatar its settings are used in place of the new ones, and Move Settings in its inspector
    /// copies them over and removes it.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    [FTSingleInstance]
    public class PawlygonFTToggles : PawlygonFTComponent
    {
        public override int LatestVersion => 1;

        [FormerlySerializedAs("visemesToggle")]
        public bool visemesMenu = true;

        public FTVisemesLevel visemesDefault = FTVisemesLevel.Full;

        public bool pupilTrackingToggle = true;

        public bool pupilEmulationToggle = true;

        public string menuName = "Face Tracking";
    }
}
