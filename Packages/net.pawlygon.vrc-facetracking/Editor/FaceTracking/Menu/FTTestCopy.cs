using System;
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
        const string MenuPath = "Tools/!Pawlygon/Face Tracking/Build Test Copy";

        [MenuItem(MenuPath, false, 100)]
        static void BuildSelected() => Build(SelectedAvatar());

        [MenuItem(MenuPath, true)]
        static bool CanBuildSelected() => !Application.isPlaying && SelectedAvatar() != null;

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
            var copy = Object.Instantiate(original, original.transform.parent);
            // Same name the SDK gives its upload copy, some build tools rely on it
            copy.name = original.name + "(Clone)";

            bool succeeded;
            try
            {
                succeeded = VRCBuildPipelineCallbacks.OnPreprocessAvatar(copy);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                succeeded = false;
            }

            if (!succeeded)
            {
                Object.DestroyImmediate(copy);
                EditorUtility.DisplayDialog(FTErrorBoundary.Title, "Building the test copy failed or was cancelled. See the Console for details.", "OK");
                return;
            }

            copy.name = "Pawlygon FT Test Copy - " + original.name;
            Undo.RegisterCreatedObjectUndo(copy, "Build Face Tracking Test Copy");
            Selection.activeGameObject = copy;
            EditorGUIUtility.PingObject(copy);
        }
    }
}
