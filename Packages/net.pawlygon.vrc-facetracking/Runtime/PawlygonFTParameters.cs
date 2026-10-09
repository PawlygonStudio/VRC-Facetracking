using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pawlygon.FaceTracking
{
    [Serializable]
    public class FTForwardedParameter
    {
        [Tooltip("Animator parameter that receives the value.")]
        public string parameter = "";

        [Tooltip("Tracking keys combined with + - * / abs() min() max() clamp(), e.g. JawRight - JawLeft. The result is clamped to -1..1.")]
        public string expression = "";
    }

    /// <summary>
    /// Forwards face tracking values to animator parameters at build time, so custom animators
    /// built for OSC face tracking parameters keep working with native face tracking.
    /// </summary>
    [AddComponentMenu("Pawlygon/Pawlygon Face Tracking Parameters")]
    [DisallowMultipleComponent]
    public class PawlygonFTParameters : PawlygonFTComponent
    {
        public override int LatestVersion => 1;

        [Tooltip("Forward every known face tracking parameter (FT/v2/..., OSCm/Proxy/FT/v2/...) used by the avatar's animators.")]
        public bool autoDetect = true;

        [Tooltip("Extra parameters to forward. An entry with the same name as a detected parameter replaces it.")]
        public List<FTForwardedParameter> parameters = new List<FTForwardedParameter>();
    }
}
