using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Checks for the legacy OSC face tracking setup and blendshape optimizers. Runs before NDMF / Modular Avatar
    /// (about -11000) and VRCFury (-10000) so the legacy setup can be removed before they merge it into the avatar.
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

                var optimizer = FTBlendshapeOptimizers.Find(avatarGameObject).FirstOrDefault();
                if (optimizer != null)
                {
                    throw new FTBuildException($"{optimizer.Name} on '{FTAvatarUtils.GetPath(optimizer.Holder.transform, avatarGameObject.transform)}' " +
                                               $"would remove the face tracking blendshapes. {optimizer.Fix}");
                }

                LegacyOscCleanup.Run(avatarGameObject);
            });
        }
    }

    /// <summary>
    /// Writes the face tracking setup to the descriptor (meshes, blendshape sets, visemes, eyelids, expressions) and
    /// builds the components' own FX layers, parameters and menu controls. Runs before NDMF / Modular Avatar and
    /// VRCFury, which merge those (see <see cref="FTGeneratedAssets"/>), and whose blendshape optimizers then see
    /// which blendshapes face tracking uses.
    /// </summary>
    internal sealed class FTDescriptorBuildHook : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -11500;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            return FTErrorBoundary.Run(() => FTBuildRunner.Run(avatarGameObject, FTPass.Descriptor));
        }
    }

    /// <summary>
    /// Finishes the components on the merged FX controller: guards in the avatar's gesture and blink layers, 1.x
    /// fake pupil layers emptied, parameters forwarded to what the playable layers read. Runs after NDMF / Modular
    /// Avatar and VRCFury so the final merged controllers are visible, and before the SDK's own processing (-2048).
    /// </summary>
    internal sealed class FTBuildHook : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -9000;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            return FTErrorBoundary.Run(() => FTBuildRunner.Run(avatarGameObject, FTPass.Animator));
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
