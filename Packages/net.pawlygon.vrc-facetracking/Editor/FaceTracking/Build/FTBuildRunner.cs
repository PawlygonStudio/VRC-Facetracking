using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Applies every Pawlygon face tracking component on an avatar, in two passes (see <see cref="FTPass"/>).
    /// Only ever runs on a copy of the avatar (the SDK's upload clone, a test copy, or play mode).
    /// </summary>
    internal static class FTBuildRunner
    {
        public static void Run(GameObject avatarRoot, FTPass pass)
        {
            var descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) return;

            var components = FTAvatarUtils.GetBuildComponents<PawlygonFTComponent>(avatarRoot);
            if (components.Count == 0) return;

            CheckComponents(components, avatarRoot.transform);
            foreach (var component in components) component.Upgrade();

            var steps = components
                .Select((component, index) => (component, index, builder: FTBuilderRegistry.Get(component.GetType())))
                .ToList();

            if (pass == FTPass.Descriptor)
            {
                foreach (var step in steps.Where(s => s.builder == null))
                {
                    Debug.LogWarning($"[Pawlygon Face Tracking] Nothing builds {step.component.GetType().Name}, skipping it.", step.component);
                }
            }

            // The animator pass starts from what the descriptor pass committed
            var context = FTBuildContext.FromDescriptor(descriptor, pass);
            foreach (var step in steps.Where(s => s.builder != null).OrderBy(s => s.builder.Phase).ThenBy(s => s.index))
            {
                try
                {
                    step.builder.Build(pass, step.component, context);
                }
                catch (Exception e) when (!(e is FTBuildCancelledException))
                {
                    var name = FTAvatarUtils.DisplayName(step.component.GetType());
                    var path = FTAvatarUtils.GetPath(step.component.transform, avatarRoot.transform);
                    throw new FTBuildException($"{name} on '{path}' failed:\n{e.Message}", e);
                }
            }

            context.Commit();
            // Components stay for the animator pass, which removes them
            if (pass == FTPass.Descriptor) return;

            RemoveComponents(components, avatarRoot.transform);
            FTMerger.Find()?.CleanUp(avatarRoot);

            // The SDK's own check of custom expressions, e.g. typos in manually entered ones
            foreach (var warning in FaceTrackingExpressionValidator.CollectWarnings(descriptor))
            {
                Debug.LogWarning($"[{FTErrorBoundary.Title}] {warning}", descriptor);
            }
        }

        /// <summary>
        /// The SDK strips editor-only components later anyway. Removing them now also lets us drop the
        /// empty objects our prefabs leave behind.
        /// </summary>
        static void RemoveComponents(System.Collections.Generic.List<PawlygonFTComponent> components, Transform avatarRoot)
        {
            foreach (var component in components)
            {
                if (component == null) continue;
                var holder = component.gameObject;
                UnityEngine.Object.DestroyImmediate(component);

                var isEmpty = holder.transform != avatarRoot && holder.transform.childCount == 0 && holder.GetComponents<Component>().Length == 1;
                if (isEmpty) UnityEngine.Object.DestroyImmediate(holder);
            }
        }

        static void CheckComponents(System.Collections.Generic.List<PawlygonFTComponent> components, Transform avatarRoot)
        {
            var tooNew = components.FirstOrDefault(c => c.IsTooNew);
            if (tooNew != null)
            {
                throw new FTBuildException($"'{FTAvatarUtils.GetPath(tooNew.transform, avatarRoot)}' was saved by a newer " +
                                           "version of Pawlygon Face Tracking. Update the package to build this avatar.");
            }

            foreach (var group in components.GroupBy(c => c.GetType()))
            {
                if (group.Count() < 2 || !Attribute.IsDefined(group.Key, typeof(FTSingleInstanceAttribute))) continue;

                var paths = string.Join("\n", group.Select(c => "• " + FTAvatarUtils.GetPath(c.transform, avatarRoot)));
                throw new FTBuildException($"Only one {FTAvatarUtils.DisplayName(group.Key)} is allowed per avatar, found:\n{paths}");
            }
        }
    }
}
