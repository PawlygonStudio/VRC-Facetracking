using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using Object = UnityEngine.Object;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Runs the full avatar build on a copy in the scene, so the result can be inspected
    /// without uploading. The SDK's control panel only shows the unprocessed scene avatar.
    /// </summary>
    internal static class FTTestCopy
    {
        // Right-click in the Hierarchy. GameObject/ items with a priority under 50 show in its context menu.
        const string MenuPath = "GameObject/!Pawlygon/Face Tracking/Build Test Copy";
        const string CopyPrefix = "Pawlygon FT Test Copy - ";

        [MenuItem(MenuPath, false, 40)]
        static void BuildSelected() => Build(SelectedAvatar());

        [MenuItem(MenuPath, true, 40)]
        static bool CanBuildSelected()
        {
            var avatar = SelectedAvatar();
            return !Application.isPlaying && avatar != null && avatar.GetComponent<PawlygonFTTestCopy>() == null;
        }

        static VRCAvatarDescriptor SelectedAvatar()
        {
            var selected = Selection.activeGameObject;
            return selected != null ? selected.GetComponentInParent<VRCAvatarDescriptor>(true) : null;
        }

        public static void Build(VRCAvatarDescriptor avatar)
        {
            if (avatar == null) return;
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog(FTErrorBoundary.Title, "Exit play mode to build a test copy.", "OK");
                return;
            }

            var original = avatar.gameObject;
            var copyName = CopyPrefix + original.name;

            // Replace the previous test copy instead of piling them up
            foreach (var root in original.scene.GetRootGameObjects().Where(r => r.name == copyName && r.GetComponent<PawlygonFTTestCopy>() != null))
            {
                Undo.DestroyObjectImmediate(root);
            }

            var copy = Object.Instantiate(original, original.transform.parent);
            // Same name the SDK gives its upload copy, some build tools rely on it
            copy.name = original.name + "(Clone)";

            bool succeeded;
            var lockMaterials = SuspendMaterialLocking();
            try
            {
                succeeded = VRCBuildPipelineCallbacks.OnPreprocessAvatar(copy);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                succeeded = false;
            }
            finally
            {
                ResumeMaterialLocking(lockMaterials);
                // Lets tools that change settings for a build put them back, like lilToon
                RunPostprocess();
            }

            if (!succeeded)
            {
                Object.DestroyImmediate(copy);
                EditorUtility.DisplayDialog(FTErrorBoundary.Title, "Building the test copy failed or was cancelled. See the Console for details.", "OK");
                return;
            }

            copy.AddComponent<PawlygonFTTestCopy>();
            copy.name = copyName;
            Undo.RegisterCreatedObjectUndo(copy, "Build Face Tracking Test Copy");
            Selection.activeGameObject = copy;
            EditorGUIUtility.PingObject(copy);
        }

        static List<IVRCSDKPreprocessAvatarCallback> Callbacks()
        {
            var field = typeof(VRCBuildPipelineCallbacks).GetField("_preprocessAvatarCallbacks", BindingFlags.Static | BindingFlags.NonPublic);
            return field?.GetValue(null) as List<IVRCSDKPreprocessAvatarCallback>;
        }

        /// <summary>
        /// Poiyomi locks the materials of the avatar it builds, which for a test copy are the original
        /// avatar's materials. It's left out of the test build, like Av3Emulator does.
        /// </summary>
        static List<IVRCSDKPreprocessAvatarCallback> SuspendMaterialLocking()
        {
            var callbacks = Callbacks();
            if (callbacks == null) return new List<IVRCSDKPreprocessAvatarCallback>();

            var locking = callbacks.Where(c => c.GetType().Name == "LockMaterialsOnUpload").ToList();
            foreach (var callback in locking) callbacks.Remove(callback);
            return locking;
        }

        static void ResumeMaterialLocking(List<IVRCSDKPreprocessAvatarCallback> suspended)
        {
            var callbacks = Callbacks();
            if (callbacks == null || suspended.Count == 0) return;

            callbacks.AddRange(suspended.Where(c => !callbacks.Contains(c)));
            callbacks.Sort((a, b) => a.callbackOrder.CompareTo(b.callbackOrder));
        }

        static void RunPostprocess()
        {
            try
            {
                typeof(VRCBuildPipelineCallbacks).GetMethod("OnPostprocessAvatar", BindingFlags.Static | BindingFlags.Public, null, Type.EmptyTypes, null)
                    ?.Invoke(null, null);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
