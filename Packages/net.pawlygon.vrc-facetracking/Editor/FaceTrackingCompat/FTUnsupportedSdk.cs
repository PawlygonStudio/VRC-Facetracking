#if !PAWLYGON_FT_NATIVE
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace Pawlygon.FaceTracking.Editor
{
    // Only compiled when the installed VRChat SDK has no native face tracking, so the components
    // say why they do nothing instead of failing silently.
    static class FTUnsupportedSdk
    {
        public const string Message =
            "Pawlygon Face Tracking needs a VRChat Avatars SDK with native face tracking (3.10.6 beta or newer). " +
            "With the SDK installed in this project, this component does nothing.";
    }

    [CustomEditor(typeof(PawlygonFTComponent), true)]
    internal sealed class FTUnsupportedSdkEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(FTUnsupportedSdk.Message, MessageType.Error);
        }
    }

    internal sealed class FTUnsupportedSdkHook : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -9000;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            var component = avatarGameObject.GetComponentInChildren<PawlygonFTComponent>(true);
            if (component != null) Debug.LogWarning("[Pawlygon Face Tracking] " + FTUnsupportedSdk.Message, component);
            return true;
        }
    }
}
#endif
