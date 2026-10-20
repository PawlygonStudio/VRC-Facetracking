using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// The parameters a <see cref="PawlygonFTParameters"/> component forwards. At build time the
    /// avatar's final playable layers are scanned, after VRCFury and Modular Avatar merged theirs.
    /// The inspector preview also looks at the controllers those tools will merge.
    /// </summary>
    internal sealed class FTForwardingPlan
    {
        internal sealed class Entry
        {
            public string Parameter;
            public string Expression;
            public bool IsManual;
            public string Source;
            public AnimatorControllerParameterType? Type;
        }

        // Forwarded parameter counts for the Excellent / Good / Medium / Poor ranks
        static readonly (int max, string rank)[] Ranks = { (10, "Excellent"), (20, "Good"), (30, "Medium"), (40, "Poor") };

        public readonly List<Entry> Entries = new List<Entry>();
        public readonly List<string> Warnings = new List<string>();

        // Known face tracking parameters a controller declares but no layer reads, so they aren't forwarded
        public readonly List<string> Unread = new List<string>();

        public static FTForwardingPlan ForBuild(PawlygonFTParameters component, VRCAvatarDescriptor descriptor, bool useAllTrackingKeys)
        {
            var controllers = GetLayerControllers(descriptor).Select(c => (c, "Playable layers"));
            return Resolve(component, controllers, useAllTrackingKeys);
        }

        public static FTForwardingPlan ForPreview(PawlygonFTParameters component, VRCAvatarDescriptor descriptor, bool useAllTrackingKeys)
        {
            var controllers = GetLayerControllers(descriptor).Select(c => (c, "Playable layers")).ToList();
            var root = descriptor.transform;
            foreach (var behaviour in descriptor.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour is VRC.SDKBase.VRC_AvatarDescriptor || behaviour is PawlygonFTComponent) continue;
                if (FTAvatarUtils.IsEditorOnly(behaviour.transform, root)) continue;

                foreach (var controller in FTAvatarUtils.GetReferencedControllers(behaviour))
                {
                    // Legacy controllers are removed from the build, so they don't count
                    if (LegacyOscDetector.IsPawlygonLegacyController(controller)) continue;
                    controllers.Add((controller, FTAvatarUtils.GetPath(behaviour.transform, root)));
                }
            }
            return Resolve(component, controllers, useAllTrackingKeys);
        }

        static FTForwardingPlan Resolve(PawlygonFTParameters component,
            IEnumerable<(RuntimeAnimatorController controller, string source)> controllers, bool useAllTrackingKeys)
        {
            var plan = new FTForwardingPlan();
            var types = new Dictionary<string, AnimatorControllerParameterType>();

            foreach (var (controller, source) in controllers)
            {
                if (!(FTAvatarUtils.BaseController(controller) is AnimatorController animator)) continue;
                // Controllers made for OSC face tracking often declare far more parameters than they use, and each
                // forwarded parameter counts toward the avatar's performance rank
                var read = new HashSet<string>(animator.layers.SelectMany(FTFakePupilLayers.ReadParameters));
                foreach (var parameter in animator.parameters)
                {
                    types[parameter.name] = parameter.type;
                    if (!component.autoDetect || plan.Entries.Any(e => e.Parameter == parameter.name)) continue;

                    var expression = FTParameterCatalog.GetExpression(parameter.name);
                    if (expression == null) continue;
                    if (!read.Contains(parameter.name))
                    {
                        if (!plan.Unread.Contains(parameter.name)) plan.Unread.Add(parameter.name);
                        continue;
                    }
                    plan.Unread.Remove(parameter.name);
                    plan.Entries.Add(new Entry { Parameter = parameter.name, Expression = expression, Source = source });
                }
            }

            foreach (var manual in component.parameters)
            {
                var name = manual.parameter?.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                if (string.IsNullOrWhiteSpace(manual.expression))
                {
                    plan.Warnings.Add($"'{name}' has no expression, it's skipped.");
                    continue;
                }
                plan.Entries.RemoveAll(e => e.Parameter == name);
                plan.Entries.Add(new Entry { Parameter = name, Expression = manual.expression.Trim(), IsManual = true, Source = "Manual" });
            }

            foreach (var entry in plan.Entries)
            {
                if (types.TryGetValue(entry.Parameter, out var type)) entry.Type = type;
                if (entry.Type.HasValue && entry.Type != AnimatorControllerParameterType.Float)
                {
                    plan.Warnings.Add($"'{entry.Parameter}' is a {entry.Type} parameter. Forwarded values are floats, make it a Float.");
                }
            }

            if (!useAllTrackingKeys)
            {
                var needsAllKeys = plan.Entries.Where(e => FTParameterCatalog.GetReferencedKeys(e.Expression).Any(k => !FTKeys.IsBasic(KeyIndex(k)))).ToList();
                if (needsAllKeys.Count > 0)
                {
                    plan.Warnings.Add("Use All Tracking Keys is off, so only eye look and eyelid keys are available. " +
                                      $"These won't work: {string.Join(", ", needsAllKeys.Select(e => e.Parameter))}.");
                }
            }
            return plan;
        }

        public static string RankFor(int forwardedCount)
        {
            foreach (var (max, rank) in Ranks)
            {
                if (forwardedCount <= max) return rank;
            }
            return "Very Poor";
        }

        static int KeyIndex(string keyName)
        {
            for (var i = 0; i < FTKeys.KeyCount; i++)
            {
                if (FTKeys.KeyName(i) == keyName) return i;
            }
            return -1;
        }

        static IEnumerable<RuntimeAnimatorController> GetLayerControllers(VRCAvatarDescriptor descriptor)
        {
            if (!descriptor.customizeAnimationLayers) return Enumerable.Empty<RuntimeAnimatorController>();
            return (descriptor.baseAnimationLayers ?? new VRCAvatarDescriptor.CustomAnimLayer[0])
                .Concat(descriptor.specialAnimationLayers ?? new VRCAvatarDescriptor.CustomAnimLayer[0])
                .Where(layer => !layer.isDefault && layer.animatorController != null)
                .Select(layer => layer.animatorController)
                .Distinct();
        }
    }
}
