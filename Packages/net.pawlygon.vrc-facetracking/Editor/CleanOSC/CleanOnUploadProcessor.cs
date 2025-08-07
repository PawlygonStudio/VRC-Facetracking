using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRC.Core;
using VRC.SDK3A.Editor;
using VRC.SDKBase.Editor;

namespace Pawlygon.CleanOnUpload
{
    public class CleanOnUploadProcessor
    {
		[InitializeOnLoadMethod]
		public static void RegisterCallback()
        {
            VRCSdkControlPanel.OnSdkPanelEnable += OnSdkPanelEnable;
        }

        private static void OnSdkPanelEnable(object sender, EventArgs args)
        {
            if (VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out var builder))
            {
                builder.OnSdkUploadSuccess += OnSdkUploadSuccess;
            }
        }

        private static void OnSdkUploadSuccess(object sender, string avatarId)
        {
            Debug.Log($"Upload success, will try to remove OSC config...");
            TryDeleteOscConfigFile(avatarId);
        }
        
        [MenuItem("Tools/!Pawlygon/Advanced/Remove OSC Config File")]
        private static void RemoveOSCConfig()
        {
            if (!APIUser.IsLoggedIn)
            {
                Debug.LogError("You need to be Logged in VRChat SDK Panel.");
                return;
            }

            var activeObject = Selection.activeGameObject;
            if (activeObject == null)
            {
                Debug.LogError("You need to select an Avatar on the Hierarchy.");
                return;
            }

            var pipeline = activeObject.transform.GetComponentInParent<PipelineManager>();
            if (pipeline == null)
            {
                Debug.LogError("Selected Avatar does not have a Blueprint ID.");
                return;
            }
            
            Debug.Log($"Trying to delete OSC config file of {pipeline.blueprintId}");
            TryDeleteOscConfigFile(pipeline.blueprintId);
        }

        private static void TryDeleteOscConfigFile(string avatarId)
        {
            if (string.IsNullOrEmpty(avatarId)) return;
            if (!APIUser.IsLoggedIn) return;

            var userId = APIUser.CurrentUser.id;
            if (ContainsPathTraversalElements(userId) || ContainsPathTraversalElements(avatarId))
            {
                // Prevent the remote chance of a path traversal
                return;
            }

            var endbit = $"/VRChat/VRChat/OSC/{userId}/Avatars/{avatarId}.json";
            var oscConfigFile = $"{VRC_SdkBuilder.GetLocalLowPath()}{endbit}";
            var printLocation = $"%LOCALAPPDATA%Low{endbit}"; // Doesn't print the account name to the logs
            if (!File.Exists(oscConfigFile)) return;

            var fileAttributes = File.GetAttributes(oscConfigFile);
            if (fileAttributes.HasFlag(FileAttributes.Directory)) return;

            try
            {
                File.Delete(oscConfigFile);
                Debug.Log($"Removed the OSC config file located at {printLocation}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to remove the OSC config file at {printLocation}");
                throw;
            }
        }

        private static bool ContainsPathTraversalElements(string susStr)
        {
            return susStr.Contains("/") || susStr.Contains("\\") || susStr.Contains(".") || susStr.Contains("*");
        }
    }
}