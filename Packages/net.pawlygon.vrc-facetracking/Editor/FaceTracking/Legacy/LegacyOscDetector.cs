using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Finds OSC based face tracking setups (Pawlygon VRCFT 1.x and similar templates) merged by
    /// VRCFury or Modular Avatar components. They can't run alongside native face tracking. Also finds the 1.x
    /// Blendshape Fixes from Pawlygon Unity Tools, which the Blendshape Fixes component replaces.
    /// </summary>
    internal static class LegacyOscDetector
    {
        const string LegacyAnimatorsFolder = "Packages/net.pawlygon.vrc-facetracking/Deprecated/Animators";

        // Every controller Pawlygon Unity Tools' Blendshape Fixes (1.7.1, for VRCFT 1.x) generates declares it
        const string LegacyFixesMarker = "Pawlygon/FTFixes/One";

        static HashSet<string> legacyControllerGuids;

        internal sealed class Finding
        {
            public Component Component;
            public RuntimeAnimatorController Controller;
            public string Path;

            // True for our own 1.x controllers. Other templates are only detected by parameter names,
            // so they're reported but never removed automatically.
            public bool IsPawlygonLegacy;

            // Pawlygon Unity Tools' 1.x Blendshape Fixes, replaced by the Blendshape Fixes component
            public bool IsBlendshapeFixes;

            public GameObject Holder => Component != null ? Component.gameObject : null;

            public string Describe()
            {
                var source = IsBlendshapeFixes ? "Pawlygon VRCFT 1.x Blendshape Fixes"
                    : IsPawlygonLegacy ? "Pawlygon VRCFT 1.x" : "OSC face tracking controller";
                return $"{Path} ({source}: {(Controller != null ? Controller.name : "?")})";
            }
        }

        public static List<Finding> Find(GameObject avatarRoot)
        {
            var findings = new List<Finding>();
            var root = avatarRoot.transform;

            foreach (var behaviour in avatarRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                // Null for missing scripts. The descriptor's own layers aren't merged by anything we could remove.
                if (behaviour == null || behaviour is PawlygonFTComponent || behaviour is VRC_AvatarDescriptor) continue;
                if (FTAvatarUtils.IsEditorOnly(behaviour.transform, root)) continue;
                // One finding per GameObject, e.g. a Modular Avatar prefab merging both FX and Additive
                if (findings.Any(f => f.Component.gameObject == behaviour.gameObject)) continue;

                foreach (var controller in FTAvatarUtils.GetReferencedControllers(behaviour))
                {
                    var isPawlygonLegacy = IsPawlygonLegacyController(controller);
                    if (!isPawlygonLegacy && !UsesOscParameters(controller)) continue;

                    findings.Add(new Finding
                    {
                        Component = behaviour,
                        Controller = controller,
                        Path = FTAvatarUtils.GetPath(behaviour.transform, root),
                        IsPawlygonLegacy = isPawlygonLegacy,
                        IsBlendshapeFixes = IsLegacyBlendshapeFixes(controller),
                    });
                    break;
                }
            }
            return findings;
        }

        // OSC face tracking templates use more than a handful of these. Animators that only read a few,
        // like Face Tracking Extras, are what the Parameters component supports, not something to remove.
        const int MinOscParameters = 10;

        /// <summary>
        /// Descriptor layers set to one of our 1.x controllers by hand, like the Eye Rotation controller in Additive.
        /// Only reported: they aren't merged by anything we could remove, and an FX slot may hold the avatar's own layers too.
        /// </summary>
        public static List<VRCAvatarDescriptor.CustomAnimLayer> FindInDescriptor(VRCAvatarDescriptor descriptor)
        {
            return descriptor.baseAnimationLayers
                .Where(l => !l.isDefault && l.animatorController != null && IsLegacyOscController(l.animatorController))
                .ToList();
        }

        /// <summary>Sets a descriptor layer back to VRChat's default controller. Reversible with Undo.</summary>
        public static void ClearDescriptorLayer(VRCAvatarDescriptor descriptor, VRCAvatarDescriptor.AnimLayerType type)
        {
            Undo.RecordObject(descriptor, "Clear 1.x face tracking layer");
            for (var i = 0; i < descriptor.baseAnimationLayers.Length; i++)
            {
                if (descriptor.baseAnimationLayers[i].type != type) continue;
                descriptor.baseAnimationLayers[i].isDefault = true;
                descriptor.baseAnimationLayers[i].animatorController = null;
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(descriptor);
        }

        /// <summary>
        /// Removes a finding from a build copy. The whole GameObject goes when it's just a merge prefab (no
        /// children, only VRCFury / Modular Avatar components, like the 1.x prefabs), otherwise only the component.
        /// </summary>
        public static void Strip(Finding finding, Transform avatarRoot)
        {
            if (finding.Component == null) return;

            var holder = finding.Component.gameObject;
            var isMergePrefab = holder.transform != avatarRoot
                                && holder.transform.childCount == 0
                                && holder.GetComponents<Component>().All(c => c is Transform || IsMergeTool(c));
            if (isMergePrefab) Object.DestroyImmediate(holder);
            else Object.DestroyImmediate(finding.Component);
        }

        /// <summary>
        /// Tags the object EditorOnly and deactivates it. VRCFury also processes inactive objects,
        /// so the tag is what keeps it out of the build. Reversible with Undo.
        /// </summary>
        public static void Disable(GameObject holder)
        {
            if (holder == null) return;
            Undo.RecordObject(holder, "Disable legacy face tracking");
            holder.tag = "EditorOnly";
            holder.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(holder);
        }

        static bool IsMergeTool(Component component)
        {
            var ns = component != null ? component.GetType().Namespace ?? "" : "";
            return ns.StartsWith("VF") || ns.StartsWith("nadena");
        }

        /// <summary>Our own 1.x controllers: the OSC face tracking ones in Deprecated/Animators, and Unity Tools' 1.x Blendshape Fixes.</summary>
        public static bool IsPawlygonLegacyController(RuntimeAnimatorController controller)
        {
            return IsLegacyOscController(controller) || IsLegacyBlendshapeFixes(controller);
        }

        /// <summary>
        /// The FX controller Pawlygon Unity Tools' Blendshape Fixes generates for VRCFT 1.x: it drives fix shapes from the
        /// 1.x face tracking parameters, which the Blendshape Fixes component now drives from native face tracking.
        /// </summary>
        public static bool IsLegacyBlendshapeFixes(RuntimeAnimatorController controller)
        {
            return FTAvatarUtils.BaseController(controller) is AnimatorController animator
                   && animator.parameters.Any(p => p.name == LegacyFixesMarker)
                   && animator.layers.SelectMany(FTFakePupilLayers.ReadParameters).Any(p => FTParameterCatalog.GetExpression(p) != null);
        }

        static bool IsLegacyOscController(RuntimeAnimatorController controller)
        {
            // Not cached while empty, in case the asset database wasn't ready the first time
            if (legacyControllerGuids == null || legacyControllerGuids.Count == 0)
            {
                legacyControllerGuids = AssetDatabase.IsValidFolder(LegacyAnimatorsFolder)
                    ? new HashSet<string>(AssetDatabase.FindAssets("t:AnimatorController", new[] { LegacyAnimatorsFolder }))
                    : new HashSet<string>();
            }
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(FTAvatarUtils.BaseController(controller), out var guid, out long _)
                   && legacyControllerGuids.Contains(guid);
        }

        static bool UsesOscParameters(RuntimeAnimatorController controller)
        {
            return FTAvatarUtils.BaseController(controller) is AnimatorController animator
                   && animator.parameters.Count(p => p.name.StartsWith("FT/v2/") || p.name.StartsWith("v2/")) >= MinOscParameters;
        }
    }
}
