using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Assets generated during a build (FX copies, menus, parameters) have to be saved for the upload.
    /// They go in a small local package, so they stay out of the project's Assets folder.
    /// Every build gets its own folder. Folders no avatar in the open scenes uses anymore are deleted
    /// before edit mode builds and when entering play mode, never while playing.
    /// </summary>
    [InitializeOnLoad]
    internal static class FTTempAssets
    {
        const string PackageName = "net.pawlygon.facetracking.temp";
        const string Root = "Packages/" + PackageName;
        const string BuildsFolder = Root + "/Builds";

        // Keeps asset paths well under Windows' path length limit
        const int MaxNameLength = 24;

        const string PackageJson =
            "{\n" +
            "  \"name\": \"" + PackageName + "\",\n" +
            "  \"displayName\": \"Pawlygon Face Tracking Temp Files\",\n" +
            "  \"version\": \"0.0.0\",\n" +
            "  \"description\": \"Generated when avatars with Pawlygon Face Tracking are built. Safe to delete.\",\n" +
            "  \"hideInEditor\": false\n" +
            "}\n";

        static FTTempAssets()
        {
            EditorApplication.delayCall += EnsurePackage;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) CleanUnused();
            };
        }

        static void EnsurePackage()
        {
            var packageJson = Path.Combine(Root, "package.json");
            if (File.Exists(packageJson) && File.ReadAllText(packageJson) == PackageJson) return;

            Directory.CreateDirectory(Root);
            File.WriteAllText(packageJson, PackageJson);
            Client.Resolve();
        }

        /// <summary>A new, empty folder for this build of the avatar.</summary>
        public static string PrepareBuildFolder(GameObject avatar)
        {
            if (!AssetDatabase.IsValidFolder(Root))
            {
                EnsurePackage();
                throw new FTBuildException("Pawlygon Face Tracking is still setting up its temporary files package. Wait a moment and build again.");
            }

            if (!AssetDatabase.IsValidFolder(BuildsFolder)) AssetDatabase.CreateFolder(Root, "Builds");

            var name = Regex.Replace(avatar.name.Replace("(Clone)", "").Trim(), @"[^\w\- ]", "_").Trim();
            if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength).Trim();
            if (string.IsNullOrEmpty(name)) name = "Avatar";

            var avatarFolder = BuildsFolder + "/" + name;
            if (!AssetDatabase.IsValidFolder(avatarFolder)) AssetDatabase.CreateFolder(BuildsFolder, name);

            // Never reuse a path: recreating an asset at a path deleted in the same session can make
            // Unity reload the old file over the new object
            var build = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            AssetDatabase.CreateFolder(avatarFolder, build);
            return avatarFolder + "/" + build;
        }

        /// <summary>
        /// Deletes build folders that no avatar in the open scenes references, e.g. old test copies that
        /// were removed. Folders of test copies still in a scene are kept.
        /// </summary>
        public static void CleanUnused()
        {
            if (Application.isPlaying || !AssetDatabase.IsValidFolder(BuildsFolder)) return;

            var used = new HashSet<string>();
            foreach (var descriptor in Resources.FindObjectsOfTypeAll<VRCAvatarDescriptor>())
            {
                if (descriptor == null || EditorUtility.IsPersistent(descriptor) || !descriptor.gameObject.scene.isLoaded) continue;
                foreach (var asset in ReferencedAssets(descriptor))
                {
                    var folder = BuildFolderOf(AssetDatabase.GetAssetPath(asset));
                    if (folder != null) used.Add(folder);
                }
            }

            foreach (var avatarFolder in AssetDatabase.GetSubFolders(BuildsFolder))
            {
                foreach (var entry in Directory.GetFileSystemEntries(avatarFolder).Where(e => !e.EndsWith(".meta")))
                {
                    var path = entry.Replace('\\', '/');
                    if (!used.Contains(path)) AssetDatabase.DeleteAsset(path);
                }
                if (Directory.GetFileSystemEntries(avatarFolder).All(e => e.EndsWith(".meta"))) AssetDatabase.DeleteAsset(avatarFolder);
            }
        }

        static IEnumerable<Object> ReferencedAssets(VRCAvatarDescriptor descriptor)
        {
            var layers = (descriptor.baseAnimationLayers ?? new VRCAvatarDescriptor.CustomAnimLayer[0])
                .Concat(descriptor.specialAnimationLayers ?? new VRCAvatarDescriptor.CustomAnimLayer[0]);
            foreach (var layer in layers)
            {
                if (layer.animatorController == null) continue;
                yield return layer.animatorController;
                if (layer.animatorController is AnimatorOverrideController overrideController && overrideController.runtimeAnimatorController != null)
                {
                    yield return overrideController.runtimeAnimatorController;
                }
            }
            if (descriptor.expressionParameters != null) yield return descriptor.expressionParameters;
            foreach (var menu in Menus(descriptor.expressionsMenu, new HashSet<VRCExpressionsMenu>())) yield return menu;
        }

        static IEnumerable<VRCExpressionsMenu> Menus(VRCExpressionsMenu menu, HashSet<VRCExpressionsMenu> seen)
        {
            if (menu == null || !seen.Add(menu)) yield break;
            yield return menu;
            foreach (var control in menu.controls ?? new List<VRCExpressionsMenu.Control>())
            {
                foreach (var submenu in Menus(control.subMenu, seen)) yield return submenu;
            }
        }

        /// <summary>Builds/{avatar}/{build} for an asset inside one, otherwise null.</summary>
        static string BuildFolderOf(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith(BuildsFolder + "/")) return null;
            var parts = assetPath.Substring(BuildsFolder.Length + 1).Split('/');
            return parts.Length >= 3 ? $"{BuildsFolder}/{parts[0]}/{parts[1]}" : null;
        }
    }
}
