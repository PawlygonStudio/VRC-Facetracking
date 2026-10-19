using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Blendshape optimizers on an avatar that would remove the face tracking blendshapes. Native face tracking names
    /// its blendshapes only in the avatar descriptor, which none of them reads yet, so they count every blendshape no
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
        // none does. VRCFury's optimizer runs at -10000, after the descriptor pass, so it'd see the whole setup.
        // AAO's runs in NDMF's optimizing phase (-1025), after both passes.
        const string VrcfuryKeepsFaceTrackingFrom = null;
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
                            findings.Add(new Finding { Holder = behaviour.gameObject, Name = "VRCFury Blendshape Optimizer", Fix = "Remove it from this avatar." });
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
            return installed != null && ParseVersion(installed) >= ParseVersion(fromVersion);
        }

        /// <summary>The numeric part of a package version: 1.2.3-beta.1 → 1.2.3.</summary>
        static Version ParseVersion(string version)
        {
            var cut = version.IndexOfAny(new[] { '-', '+' });
            return Version.TryParse(cut >= 0 ? version.Substring(0, cut) : version, out var parsed) ? parsed : new Version(0, 0);
        }
    }
}
