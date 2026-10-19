using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Blendshape optimizers on an avatar that would remove the face tracking blendshapes. Native face tracking names
    /// its blendshapes only in the avatar descriptor, so an optimizer that doesn't read it counts every blendshape no
    /// animator uses as unused. Found by type name, so neither tool needs to be installed.
    /// </summary>
    internal static class FTBlendshapeOptimizers
    {
        internal sealed class Finding
        {
            public GameObject Holder;
            public string Name;
            public string Fix;
        }

        // First versions that keep the blendshapes the avatar descriptor's face tracking setup names, or null while
        // none does. VRCFury's optimizer runs at -10000, after the descriptor pass, so it sees the whole setup, and
        // the Pupil Emulation layer it merges. AAO's runs in NDMF's optimizing phase (-1025), after both passes.
        const string VrcfuryKeepsFaceTrackingFrom = "1.1434.1-beta.2";
        const string AaoKeepsFaceTrackingFrom = null;

        public static List<Finding> Find(GameObject avatarRoot)
        {
            var findings = new List<Finding>();
            foreach (var behaviour in avatarRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || FTAvatarUtils.IsEditorOnly(behaviour.transform, avatarRoot.transform)) continue;

                switch (behaviour.GetType().FullName)
                {
                    case "VF.Model.VRCFury":
                        if (IsVrcfuryOptimizer(behaviour) && !KeepsFaceTracking(behaviour, VrcfuryKeepsFaceTrackingFrom))
                        {
                            findings.Add(new Finding
                            {
                                Holder = behaviour.gameObject,
                                Name = "VRCFury Blendshape Optimizer",
                                Fix = $"Update VRCFury to {VrcfuryKeepsFaceTrackingFrom} or newer, which keeps them, or remove it from this avatar.",
                            });
                        }
                        break;
                    // AAO only reads it on the avatar root
                    case "Anatawa12.AvatarOptimizer.TraceAndOptimize" when behaviour.gameObject == avatarRoot:
                        if (AaoOptimizesBlendshapes(behaviour) && !KeepsFaceTracking(behaviour, AaoKeepsFaceTrackingFrom))
                        {
                            findings.Add(new Finding { Holder = behaviour.gameObject, Name = "AAO Trace And Optimize", Fix = "Turn off its Optimize BlendShape option." });
                        }
                        break;
                }
            }
            return findings;
        }

        static bool IsVrcfuryOptimizer(MonoBehaviour behaviour)
        {
            using (var serialized = new SerializedObject(behaviour))
            {
                var content = serialized.FindProperty("content");
                return content != null && content.managedReferenceFullTypename.EndsWith("BlendshapeOptimizer");
            }
        }

        /// <summary>
        /// Optimize BlendShape freezes the blendshapes no animator moves and merges ones animated the same way.
        /// The debug options can skip each step.
        /// </summary>
        static bool AaoOptimizesBlendshapes(MonoBehaviour behaviour)
        {
            using (var serialized = new SerializedObject(behaviour))
            {
                // Called freezeBlendShape before AAO 1.8
                var optimize = serialized.FindProperty("optimizeBlendShape") ?? serialized.FindProperty("freezeBlendShape");
                if (optimize == null || !optimize.boolValue) return false;

                foreach (var skip in new[] { "skipFreezingNonAnimatedBlendShape", "skipFreezingMeaninglessBlendShape", "skipAutoMergeBlendShape" })
                {
                    var option = serialized.FindProperty("debugOptions." + skip);
                    if (option == null || !option.boolValue) return true;
                }
                return false;
            }
        }

        static bool KeepsFaceTracking(MonoBehaviour behaviour, string fromVersion)
        {
            if (fromVersion == null) return false;
            var installed = PackageInfo.FindForAssembly(behaviour.GetType().Assembly)?.version;
            return installed != null && CompareVersions(installed, fromVersion) >= 0;
        }

        /// <summary>
        /// Orders package versions like semantic versioning: 1.2.3-beta.1 &lt; 1.2.3-beta.2 &lt; 1.2.3 &lt; 1.2.4.
        /// Build metadata after + is ignored.
        /// </summary>
        internal static int CompareVersions(string a, string b)
        {
            Split(a, out var coreA, out var preA);
            Split(b, out var coreB, out var preB);
            for (var i = 0; i < Math.Max(coreA.Length, coreB.Length); i++)
            {
                var result = Number(coreA, i).CompareTo(Number(coreB, i));
                if (result != 0) return result;
            }

            // A release comes after its prereleases
            if (preA.Length == 0 || preB.Length == 0) return preB.Length.CompareTo(preA.Length);
            for (var i = 0; i < Math.Min(preA.Length, preB.Length); i++)
            {
                var numericA = int.TryParse(preA[i], out var numberA);
                var numericB = int.TryParse(preB[i], out var numberB);
                var result = numericA && numericB ? numberA.CompareTo(numberB)
                    : numericA != numericB ? (numericA ? -1 : 1)
                    : string.CompareOrdinal(preA[i], preB[i]);
                if (result != 0) return result;
            }
            return preA.Length.CompareTo(preB.Length);
        }

        static void Split(string version, out string[] core, out string[] prerelease)
        {
            var plus = version.IndexOf('+');
            if (plus >= 0) version = version.Substring(0, plus);
            var dash = version.IndexOf('-');
            core = (dash >= 0 ? version.Substring(0, dash) : version).Split('.');
            prerelease = dash >= 0 ? version.Substring(dash + 1).Split('.') : new string[0];
        }

        static int Number(string[] parts, int index) => index < parts.Length && int.TryParse(parts[index], out var number) ? number : 0;
    }
}
