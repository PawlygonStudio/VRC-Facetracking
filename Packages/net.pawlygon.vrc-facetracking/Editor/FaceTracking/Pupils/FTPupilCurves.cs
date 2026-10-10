using System.Linq;
using UnityEngine;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// The emulated pupil motion: a gentle idle drift, and a reflex after each blink. Values are blendshape
    /// weights (0 to 100).
    /// </summary>
    internal static class FTPupilCurves
    {
        /// <summary>The idle drift repeats every 12 s (waves of 3, 4 and 6 s line up only then).</summary>
        public const float IdleLength = 12f;

        /// <summary>
        /// EyeDilation during the idle drift: three waves whose periods only line up every 12 s, so it never
        /// reads as a regular pulse.
        /// </summary>
        public static float IdleDilation(PawlygonFTPupilEmulation settings, float time)
        {
            var wave = 0.5f * Mathf.Sin(2f * Mathf.PI * time / 3f)
                       + 0.3f * Mathf.Sin(2f * Mathf.PI * time / 4f + 1.3f)
                       + 0.2f * Mathf.Sin(2f * Mathf.PI * time / 6f + 2.1f);
            var max = Mathf.Max(settings.idleDilationMin, settings.idleDilationMax);
            return Mathf.Lerp(settings.idleDilationMin, max, Mathf.Clamp01(0.5f + 0.5f * wave));
        }

        public static AnimationCurve IdleCurve(PawlygonFTPupilEmulation settings)
        {
            var samples = Mathf.RoundToInt(IdleLength * 10f);
            var times = Enumerable.Range(0, samples + 1).Select(i => i * IdleLength / samples).ToArray();
            return Smooth(times, times.Select(t => IdleDilation(settings, t)).ToArray());
        }

        public static float ReflexDuration(PawlygonFTPupilEmulation settings) => Mathf.Max(0.3f, settings.reflexDuration);

        /// <summary>
        /// The blink reflex: the eyes reopen wide (they were in the dark), tighten quickly, then relax and end on
        /// the idle drift's first value so the hand-off is seamless.
        /// </summary>
        public static (AnimationCurve Dilation, AnimationCurve Constrict) ReflexCurves(PawlygonFTPupilEmulation settings)
        {
            var d = ReflexDuration(settings);
            var max = Mathf.Max(settings.idleDilationMin, settings.idleDilationMax);
            var relaxed = Mathf.Lerp(settings.idleDilationMin, max, 0.8f);
            float[] times = { 0f, 0.18f * d, 0.4f * d, 0.7f * d, d };

            var dilation = Smooth(times, new[] { settings.reflexPeakDilation, 0f, 0f, relaxed, IdleDilation(settings, 0f) });
            var constrict = Smooth(times, new[] { 0f, settings.reflexConstrict, settings.reflexConstrict * 0.35f, 0f, 0f });
            return (dilation, constrict);
        }

        static AnimationCurve Smooth(float[] times, float[] values)
        {
            var curve = new AnimationCurve(times.Select((t, i) => new Keyframe(t, values[i])).ToArray());
            for (var k = 0; k < curve.length; k++) curve.SmoothTangents(k, 0f);
            return curve;
        }
    }
}
