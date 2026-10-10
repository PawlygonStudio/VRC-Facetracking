using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pawlygon.FaceTracking
{
    public enum FTFixCombine
    {
        Average,
        Minimum,
        Multiply,
        Maximum,
    }

    [Serializable]
    public class FTBlendshapeFix
    {
        [Tooltip("The fix blendshape that gets driven.")]
        public string blendshape = "";

        [Tooltip("The blendshapes it fixes, separated by commas, e.g. MouthSmileLeft, EyeClosedLeft.")]
        public string sources = "";
    }

    /// <summary>
    /// Drives blendshape fixes: helper shapes that prevent clipping while two face tracking shapes are
    /// active together, like MouthSmileLeft-EyeClosedLeft. They're driven straight from face tracking
    /// at build time, without any animator.
    /// </summary>
    [AddComponentMenu("Pawlygon/Pawlygon Face Tracking Blendshape Fixes")]
    [DisallowMultipleComponent]
    public class PawlygonFTBlendshapeFixes : PawlygonFTComponent
    {
        public override int LatestVersion => 1;

        [Tooltip("Drive every blendshape named after the face tracking shapes it fixes, joined with '-' (e.g. MouthSmileLeft-EyeClosedLeft).")]
        public bool autoDetect = true;

        [Tooltip("How the fixed shapes are combined into the fix shape's value.")]
        public FTFixCombine combine = FTFixCombine.Average;

        [Tooltip("Detected fixes left undriven, by blendshape name.")]
        public List<string> excludedFixes = new List<string>();

        [Tooltip("Extra fixes. An entry with the same blendshape as a detected fix replaces it.")]
        public List<FTBlendshapeFix> fixes = new List<FTBlendshapeFix>();
    }
}
