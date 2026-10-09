using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDKBase;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Finds OSC based face tracking setups (Pawlygon VRCFT 1.x and similar templates) merged by
    /// VRCFury or Modular Avatar components. They can't run alongside native face tracking.
    /// </summary>
    internal static class LegacyOscDetector
    {
        const string LegacyAnimatorsFolder = "Packages/net.pawlygon.vrc-facetracking/Animators";

        static HashSet<string> legacyControllerGuids;

        internal sealed class Finding
        {
            public Component Component;
            public RuntimeAnimatorController Controller;
            public string Path;

            // True for our own 1.x controllers. Other templates are only detected by parameter names,
            // so they're reported but never removed automatically.
            public bool IsPawlygonLegacy;

            public GameObject Holder => Component != null ? Component.gameObject : null;

            public string Describe()
            {
                var source = IsPawlygonLegacy ? "Pawlygon VRCFT 1.x" : "OSC face tracking controller";
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

                foreach (var controller in GetReferencedControllers(behaviour))
                {
                    var isPawlygonLegacy = IsPawlygonLegacyController(controller);
                    if (!isPawlygonLegacy && !UsesOscParameters(controller)) continue;

                    findings.Add(new Finding
                    {
                        Component = behaviour,
                        Controller = controller,
                        Path = FTAvatarUtils.GetPath(behaviour.transform, root),
                        IsPawlygonLegacy = isPawlygonLegacy,
                    });
                    break;
                }
            }
            return findings;
        }

        /// <summary>
        /// Removes a finding from a build copy: its whole GameObject, or only the component if it sits on the avatar root.
        /// </summary>
        public static void Strip(Finding finding, Transform avatarRoot)
        {
            if (finding.Component == null) return;
            if (finding.Component.transform == avatarRoot)
            {
                Object.DestroyImmediate(finding.Component);
            }
            else
            {
                Object.DestroyImmediate(finding.Component.gameObject);
            }
        }

        static List<RuntimeAnimatorController> GetReferencedControllers(MonoBehaviour behaviour)
        {
            var controllers = new List<RuntimeAnimatorController>();
            using (var serialized = new SerializedObject(behaviour))
            {
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType == SerializedPropertyType.ObjectReference
                        && property.objectReferenceValue is RuntimeAnimatorController controller)
                    {
                        controllers.Add(controller);
                    }
                }
            }
            return controllers;
        }

        static bool IsPawlygonLegacyController(RuntimeAnimatorController controller)
        {
            if (legacyControllerGuids == null)
            {
                legacyControllerGuids = AssetDatabase.IsValidFolder(LegacyAnimatorsFolder)
                    ? new HashSet<string>(AssetDatabase.FindAssets("t:AnimatorController", new[] { LegacyAnimatorsFolder }))
                    : new HashSet<string>();
            }
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(BaseController(controller), out var guid, out long _)
                   && legacyControllerGuids.Contains(guid);
        }

        static bool UsesOscParameters(RuntimeAnimatorController controller)
        {
            return BaseController(controller) is AnimatorController animator
                   && animator.parameters.Any(p => p.name.StartsWith("FT/v2/") || p.name.StartsWith("v2/"));
        }

        static RuntimeAnimatorController BaseController(RuntimeAnimatorController controller)
        {
            return controller is AnimatorOverrideController overrideController ? overrideController.runtimeAnimatorController : controller;
        }
    }
}
