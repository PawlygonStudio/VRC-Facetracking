using UnityEngine;

namespace Pawlygon.FaceTracking
{
    /// <summary>
    /// Gives the pupils life on headsets without pupil tracking: while eye tracking is active they drift gently
    /// and react to every blink. As soon as the headset reports real pupil dilation, it plays that instead.
    /// At build time, the pupil blendshapes are taken over from native face tracking and driven by an FX layer.
    /// </summary>
    [AddComponentMenu("Pawlygon/Pawlygon Face Tracking Pupil Emulation")]
    [DisallowMultipleComponent]
    [FTSingleInstance]
    public class PawlygonFTPupilEmulation : PawlygonFTComponent
    {
        public override int LatestVersion => 1;

        [Tooltip("Lowest EyeDilation of the idle drift.")]
        [Range(0f, 100f)] public float idleDilationMin = 0f;

        [Tooltip("Highest EyeDilation of the idle drift.")]
        [Range(0f, 100f)] public float idleDilationMax = 10f;

        [Tooltip("EyeDilation right after the eyes reopen from a blink.")]
        [Range(0f, 100f)] public float reflexPeakDilation = 70f;

        [Tooltip("EyeConstrict at the tightest point of the blink reflex.")]
        [Range(0f, 100f)] public float reflexConstrict = 50f;

        [Tooltip("Seconds for the blink reflex to settle back to the idle drift.")]
        [Min(0.3f)] public float reflexDuration = 1.8f;

        [Tooltip("Both eyelids below this count as a blink (0 closed, 0.75 normally open).")]
        [Range(0f, 1f)] public float lidClosedBelow = 0.15f;

        [Tooltip("Either eyelid above this, after a blink, starts the reflex. The gap to Closed Below stops noisy tracking from triggering false reflexes.")]
        [Range(0f, 1f)] public float lidOpenAbove = 0.35f;

        [Tooltip("The emulated pupils react to the expression: wide eyes constrict them, frowning with both mouth corners dilates them.")]
        public bool moodReactions = true;

        [Tooltip("EyeConstrict added when both eyes are fully wide.")]
        [Range(0f, 100f)] public float wideEyesConstrict = 70f;

        [Tooltip("EyeDilation added when both mouth corners fully frown.")]
        [Range(0f, 100f)] public float frownDilation = 70f;
    }
}
