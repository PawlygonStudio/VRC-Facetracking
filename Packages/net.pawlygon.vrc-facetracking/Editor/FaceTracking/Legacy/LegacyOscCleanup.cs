using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// When an avatar uses native face tracking, asks the user what to do with a legacy OSC face
    /// tracking setup still on it. Removal only ever happens on the build copy.
    /// </summary>
    internal static class LegacyOscCleanup
    {
        public static void Run(GameObject avatarRoot)
        {
            if (FTAvatarUtils.GetBuildComponents<PawlygonFaceTracking>(avatarRoot).Count == 0) return;

            var findings = LegacyOscDetector.Find(avatarRoot);
            if (findings.Count == 0) return;

            foreach (var finding in findings.Where(f => !f.IsPawlygonLegacy))
            {
                Debug.LogWarning($"[{FTErrorBoundary.Title}] {finding.Describe()} looks like an OSC face tracking setup. " +
                                 "It can't run alongside native face tracking, remove or disable it.", finding.Component);
            }

            var legacy = findings.Where(f => f.IsPawlygonLegacy).ToList();
            if (legacy.Count == 0) return;

            var list = string.Join("\n", legacy.Select(f => "• " + f.Describe()));

            // Play mode (Av3Emulator, Gesture Manager) and batch builds can't prompt, and only touch a copy anyway
            if (Application.isPlaying || Application.isBatchMode)
            {
                Debug.LogWarning($"[{FTErrorBoundary.Title}] Removed the legacy OSC face tracking setup from this build copy:\n{list}");
            }
            else
            {
                var remove = EditorUtility.DisplayDialog(
                    FTErrorBoundary.Title,
                    "This avatar still has the legacy OSC face tracking setup:\n\n" + list +
                    "\n\nYou can't use it together with native face tracking: both would drive the same blendshapes." +
                    "\n\nRemove it from this build? Your scene isn't changed. " +
                    "Disable it from the Pawlygon Face Tracking inspector to stop this prompt.",
                    "Remove from this build", "Cancel build");

                if (!remove)
                {
                    throw new FTBuildCancelledException("Build cancelled: the legacy OSC face tracking setup is still on the avatar.");
                }
            }

            foreach (var finding in legacy) LegacyOscDetector.Strip(finding, avatarRoot.transform);
        }
    }
}
