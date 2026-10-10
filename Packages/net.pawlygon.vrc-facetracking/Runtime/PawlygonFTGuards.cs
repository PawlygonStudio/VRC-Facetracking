using System.Collections.Generic;
using UnityEngine;

namespace Pawlygon.FaceTracking
{
    /// <summary>
    /// Stops hand gestures and blink animations in the FX layer from fighting face tracking.
    /// At build time, guards are added to a copy of the FX controller. They're keyed on the same
    /// parameters as Pawlygon VRCFT 1.x (FacialExpressionsDisabled, EyeTrackingActive), which are
    /// driven from VRChat's UserFaceTracking and UserEyeTracking, and from a Disable Gestures menu toggle.
    /// </summary>
    [AddComponentMenu("Pawlygon/Pawlygon Face Tracking Guards")]
    [DisallowMultipleComponent]
    [FTSingleInstance]
    public class PawlygonFTGuards : PawlygonFTComponent
    {
        public override int LatestVersion => 1;

        [Tooltip("Pause gesture-driven facial expressions.")]
        public bool guardGestures = true;

        [Tooltip("Pause gestures automatically while face tracking is active.")]
        public bool disableWithFaceTracking = true;

        [Tooltip("Add a Disable Gestures menu toggle that pauses gestures whenever it's on, with or without face tracking.")]
        public bool disableGesturesToggle = true;

        [Tooltip("Submenu of the avatar's main menu that gets the toggle. Created if missing.")]
        public string menuName = "Face Tracking";

        [Tooltip("Disable blink animations while eye tracking is active.")]
        public bool guardBlinking = true;

        [Tooltip("Expression layers left unguarded, by name.")]
        public List<string> excludedGestureLayers = new List<string>();

        [Tooltip("Layers that use gestures without animating the face, guarded anyway, by name.")]
        public List<string> includedGestureLayers = new List<string>();

        [Tooltip("Detected blink layers left unguarded, by name.")]
        public List<string> excludedBlinkLayers = new List<string>();

        [Tooltip("Blink layer candidates guarded even though they weren't detected with high confidence, by name.")]
        public List<string> includedBlinkLayers = new List<string>();
    }
}
