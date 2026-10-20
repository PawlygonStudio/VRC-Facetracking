using System.Collections.Generic;
using UnityEngine;

namespace Pawlygon.FaceTracking
{
    /// <summary>
    /// Stops hand gestures and blink animations in the FX layer from fighting face tracking, like Pawlygon VRCFT 1.x:
    /// drives the FacialExpressionsDisabled and EyeTrackingActive parameters that guards in the FX controller wait for,
    /// from VRChat's UserFaceTracking and UserEyeTracking and a Disable Gestures menu toggle. The guards are part of the
    /// controller (Unity Tools' FX Gesture Checker, or Bake Guards in the inspector); builds don't change animators.
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

        [Tooltip("The Disable Gestures menu toggle starts on, so gestures stay paused until it's turned off. Its state is saved.")]
        public bool gesturesDisabledByDefault = true;

        [Tooltip("Submenu of the avatar's main menu that gets the toggle. Created if missing.")]
        public string menuName = "Face Tracking";

        [Tooltip("Include blink layers when baking guards. EyeTrackingActive is driven whenever an animator reads it.")]
        public bool guardBlinking = true;

        [Tooltip("Expression layers Bake Guards leaves unguarded, by name.")]
        public List<string> excludedGestureLayers = new List<string>();

        [Tooltip("Layers that use gestures without animating the face, guarded anyway by Bake Guards, by name.")]
        public List<string> includedGestureLayers = new List<string>();

        [Tooltip("Detected blink layers Bake Guards leaves unguarded, by name.")]
        public List<string> excludedBlinkLayers = new List<string>();

        [Tooltip("Blink layer candidates Bake Guards guards even though they weren't detected with high confidence, by name.")]
        public List<string> includedBlinkLayers = new List<string>();
    }
}
