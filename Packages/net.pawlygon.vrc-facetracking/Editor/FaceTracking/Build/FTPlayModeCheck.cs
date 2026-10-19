using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// In play mode, avatars are only built when something runs the SDK's build hooks on them: VRCFury's
    /// play mode or Av3Emulator. Without either (e.g. only Gesture Manager), face tracking isn't applied,
    /// so this says so instead of leaving the guards and menu silently missing.
    /// </summary>
    [InitializeOnLoad]
    internal static class FTPlayModeCheck
    {
        // Av3Emulator builds avatars in Start, so give play mode a moment before checking
        const double DelaySeconds = 2;

        static double checkAt;

        static FTPlayModeCheck()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode) return;
                checkAt = EditorApplication.timeSinceStartup + DelaySeconds;
                EditorApplication.update += Check;
            };
        }

        static void Check()
        {
            if (!Application.isPlaying)
            {
                EditorApplication.update -= Check;
                return;
            }
            if (EditorApplication.timeSinceStartup < checkAt) return;
            EditorApplication.update -= Check;

            var unbuilt = new List<VRCAvatarDescriptor>();
            foreach (var avatar in Object.FindObjectsOfType<VRCAvatarDescriptor>())
            {
                if (avatar.GetComponent<PawlygonFTTestCopy>() != null) continue;
                if (FTAvatarUtils.GetBuildComponents<PawlygonFTComponent>(avatar.gameObject).Count > 0) unbuilt.Add(avatar);
            }

            foreach (var avatar in unbuilt)
            {
                Debug.LogWarning($"[{FTErrorBoundary.Title}] '{avatar.name}' wasn't built for play mode, so its face tracking guards and " +
                                 "menu aren't active. Use Av3Emulator or VRCFury's play mode to test them.", avatar);
            }
        }
    }
}
