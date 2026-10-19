using System;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Checks for the legacy OSC face tracking setup. Runs before NDMF / Modular Avatar (about -11000)
    /// and VRCFury (-10000) so it can be removed before they merge it into the avatar.
    /// </summary>
    internal sealed class FTLegacyCheckHook : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -20000;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            // A test copy was already built: skip it in play mode, refuse to build it again otherwise
            if (avatarGameObject.GetComponent<PawlygonFTTestCopy>() != null)
            {
                if (Application.isPlaying) return true;
                return FTErrorBoundary.Run(() => throw new FTBuildException(
                    "This is a Pawlygon Face Tracking test copy, which is already built. Build or upload the original avatar instead."));
            }

            return FTErrorBoundary.Run(() =>
            {
                if (!Application.isPlaying) FTTempAssets.CleanUnused();
                if (FTAvatarUtils.GetBuildComponents<PawlygonFTComponent>(avatarGameObject).Count == 0) return;

                var optimizer = FTAvatarUtils.FindBlendshapeOptimizer(avatarGameObject);
                if (optimizer != null)
                {
                    throw new FTBuildException($"VRCFury's Blendshape Optimizer on '{FTAvatarUtils.GetPath(optimizer.transform, avatarGameObject.transform)}' " +
                                               "would remove the face tracking blendshapes before face tracking is set up. Remove it from this avatar.");
                }

                LegacyOscCleanup.Run(avatarGameObject);
            });
        }
    }

    /// <summary>
    /// Applies the face tracking components. Runs after NDMF / Modular Avatar and VRCFury so the final
    /// merged controllers are visible, and before the SDK's own processing (-2048).
    /// </summary>
    internal sealed class FTBuildHook : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -9000;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            return FTErrorBoundary.Run(() => FTBuildRunner.Run(avatarGameObject));
        }
    }

    internal static class FTErrorBoundary
    {
        public const string Title = "Pawlygon Face Tracking";

        /// <summary>Returns false, which stops the build, if the action fails or is cancelled.</summary>
        public static bool Run(Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (FTBuildCancelledException e)
            {
                Debug.LogWarning($"[{Title}] {e.Message}");
                return false;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                // Play mode (Av3Emulator, Gesture Manager) keeps going anyway, so the Console is enough there
                if (!Application.isBatchMode && !Application.isPlaying)
                {
                    var message = e is FTBuildException ? e.Message : e.ToString();
                    EditorUtility.DisplayDialog(Title, $"The avatar build failed.\n\n{message}\n\nSee the Console for details.", "OK");
                }
                return false;
            }
        }
    }
}
