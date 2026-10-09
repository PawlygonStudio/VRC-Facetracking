using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Assets generated during a build (FX copies, menus, parameters) have to be saved for the upload.
    /// They go in a small local package, so they stay out of the project's Assets folder.
    /// Each avatar gets a folder that's replaced on every build.
    /// </summary>
    [InitializeOnLoad]
    internal static class FTTempAssets
    {
        const string PackageName = "net.pawlygon.facetracking.temp";
        const string Root = "Packages/" + PackageName;
        const string BuildsFolder = Root + "/Builds";

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
        }

        static void EnsurePackage()
        {
            var packageJson = Path.Combine(Root, "package.json");
            if (File.Exists(packageJson) && File.ReadAllText(packageJson) == PackageJson) return;

            Directory.CreateDirectory(Root);
            File.WriteAllText(packageJson, PackageJson);
            Client.Resolve();
        }

        /// <summary>An empty folder for this avatar's generated assets.</summary>
        public static string PrepareBuildFolder(GameObject avatar)
        {
            if (!AssetDatabase.IsValidFolder(Root))
            {
                EnsurePackage();
                throw new FTBuildException("Pawlygon Face Tracking is still setting up its temporary files package. Wait a moment and build again.");
            }

            if (!AssetDatabase.IsValidFolder(BuildsFolder)) AssetDatabase.CreateFolder(Root, "Builds");

            var name = Regex.Replace(avatar.name.Replace("(Clone)", "").Trim(), @"[^\w\- ]", "_");
            if (string.IsNullOrEmpty(name)) name = "Avatar";

            var avatarFolder = BuildsFolder + "/" + name;
            if (!AssetDatabase.IsValidFolder(avatarFolder)) AssetDatabase.CreateFolder(BuildsFolder, name);

            // Every build gets new asset paths. Recreating an asset at a path deleted in the same build
            // can make Unity reload the old file over the new object, losing changes made to it.
            foreach (var old in Directory.GetFileSystemEntries(avatarFolder).Where(e => !e.EndsWith(".meta")))
            {
                AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
            }
            var build = System.DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            AssetDatabase.CreateFolder(avatarFolder, build);
            return avatarFolder + "/" + build;
        }
    }
}
