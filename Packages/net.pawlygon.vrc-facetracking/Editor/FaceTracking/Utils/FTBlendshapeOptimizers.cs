using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

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

        // Whether the installed version keeps the blendshapes the avatar descriptor's face tracking setup names.
        // VRCFury's optimizer runs at -10000, after the descriptor pass, so it sees the whole setup, and the Pupil
        // Emulation layer it merges. AAO's runs in NDMF's optimizing phase (-1025), after both passes. The versions
        // come from asmdef version defines rather than the Package Manager, which isn't safe to query during a build.
        // Shown in the fix message; keep it equal to the PAWLYGON_VRCFURY_KEEPS_FT expression in the asmdef
        const string VrcfuryKeepsFaceTrackingFrom = "1.1434.1-beta.2";
#if PAWLYGON_VRCFURY_KEEPS_FT
        static readonly bool VrcfuryKeepsFaceTracking = true;
#else
        static readonly bool VrcfuryKeepsFaceTracking = false;
#endif
        static readonly bool AaoKeepsFaceTracking = false;

        public static List<Finding> Find(GameObject avatarRoot)
        {
            var findings = new List<Finding>();
            foreach (var behaviour in avatarRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || FTAvatarUtils.IsEditorOnly(behaviour.transform, avatarRoot.transform)) continue;

                switch (behaviour.GetType().FullName)
                {
                    case "VF.Model.VRCFury":
                        if (!VrcfuryKeepsFaceTracking && IsVrcfuryOptimizer(behaviour))
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
                        if (!AaoKeepsFaceTracking && AaoOptimizesBlendshapes(behaviour))
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
    }
}
