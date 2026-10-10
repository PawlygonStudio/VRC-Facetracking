using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using BlendshapesSet = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingBlendshapesSet;
using EyeGazeMode = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.CustomEyeLookSettings.EyeGazeMode;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// What a <see cref="PawlygonFaceTracking"/> component will do to an avatar. Resolved the same way
    /// for the build and for the inspector preview, without changing anything.
    /// </summary>
    internal sealed class FaceTrackingSetupPlan
    {
        public readonly List<SkinnedMeshRenderer> Meshes = new List<SkinnedMeshRenderer>();
        public BlendshapesSet Set;

        // Keys considered for mapping (all keys, or only the basic ones)
        public readonly List<int> MappedKeys = new List<int>();
        public readonly List<int> UnmappedKeys = new List<int>();
        // Pupil keys whose blendshapes are left to the avatar's animators
        public readonly List<int> AnimatorKeys = new List<int>();
        public int MappedVisemes;
        public bool VisemesFromDescriptor;

        public bool UseFaceTrackingLipSync;
        public bool UseFaceTrackingEyelids;
        public EyeGazeMode? GazeMode;
        public bool EnableEyeLook => UseFaceTrackingEyelids || GazeMode.HasValue;

        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public static FaceTrackingSetupPlan Resolve(PawlygonFaceTracking component, VRCAvatarDescriptor descriptor)
        {
            var plan = new FaceTrackingSetupPlan();
            var avatarRoot = descriptor.transform;

            plan.ResolveMeshes(component, descriptor);
            if (plan.Meshes.Count == 0)
            {
                plan.Errors.Add(component.meshMode == FTMeshMode.Auto
                    ? "No mesh with face tracking blendshapes was found on the avatar."
                    : "No face tracking meshes are assigned.");
                return plan;
            }
            var blendshapes = GetBlendshapeNames(plan.Meshes);
            plan.Set = FTBuildContext.CreateSet(PawlygonFaceTracking.BaseSetName, 1f);
            plan.MapKeys(component, descriptor, blendshapes);
            plan.MapVisemes(component, descriptor, blendshapes);
            plan.ResolveEyes(component, descriptor);

            // After visemes, which can add the avatar's viseme mesh
            if (plan.Meshes.Count > FTBuildContext.MaxMeshes)
            {
                plan.Errors.Add($"{plan.Meshes.Count} meshes were found, but face tracking supports up to {FTBuildContext.MaxMeshes}. " +
                                "Use Manual mesh mode to pick them.");
            }

            if (component.visemeDampenMode != FTVisemeDampenMode.None && (!plan.UseFaceTrackingLipSync || !component.useAllTrackingKeys))
            {
                plan.Warnings.Add("Viseme dampening only works with face tracking visemes and all tracking keys, so it will have no effect.");
            }

            plan.Meshes.RemoveAll(m => !FTAvatarUtils.IsPartOfAvatar(m, avatarRoot));
            return plan;
        }

        void ResolveMeshes(PawlygonFaceTracking component, VRCAvatarDescriptor descriptor)
        {
            var avatarRoot = descriptor.transform;
            if (component.meshMode == FTMeshMode.Manual)
            {
                foreach (var mesh in component.meshes.Where(m => m != null).Distinct())
                {
                    if (FTAvatarUtils.IsPartOfAvatar(mesh, avatarRoot)) Meshes.Add(mesh);
                    else Errors.Add($"'{mesh.name}' isn't part of this avatar.");
                }
                if (component.meshes.Any(m => m == null)) Warnings.Add("The mesh list has empty entries, they're ignored.");
                return;
            }

            foreach (var mesh in FTAvatarUtils.GetBuildComponents<SkinnedMeshRenderer>(descriptor.gameObject))
            {
                var names = GetBlendshapeNames(new[] { mesh });
                if (names.Length == 0) continue;
                if (Enumerable.Range(0, FTKeys.KeyCount).Any(key => FTKeys.FindKey(names, key) >= 0)) Meshes.Add(mesh);
            }
        }

        void MapKeys(PawlygonFaceTracking component, VRCAvatarDescriptor descriptor, string[] blendshapes)
        {
            var pupilsToAnimators = PupilsLeftToAnimators(component, descriptor, blendshapes);
            for (var key = 0; key < FTKeys.KeyCount; key++)
            {
                if (!component.useAllTrackingKeys && !FTKeys.IsBasic(key)) continue;

                var index = FTKeys.FindKey(blendshapes, key);
                if (index >= 0 && pupilsToAnimators && FTKeys.IsPupilKey(key))
                {
                    AnimatorKeys.Add(key);
                }
                else if (index >= 0)
                {
                    Set.faceTrackingBlendshapeNames[key] = blendshapes[index];
                    MappedKeys.Add(key);
                }
                else
                {
                    UnmappedKeys.Add(key);
                }
            }
        }

        /// <summary>
        /// Whether the pupil keys stay unmapped so animators own those blendshapes. In Auto, that's when an
        /// animator on the avatar animates a blendshape they'd map to, e.g. Face Tracking Extras' fake pupil
        /// dilation: native face tracking would otherwise keep writing them (0 without pupil tracking).
        /// </summary>
        static bool PupilsLeftToAnimators(PawlygonFaceTracking component, VRCAvatarDescriptor descriptor, string[] blendshapes)
        {
            if (component.pupils == FTPupilMode.FaceTracking) return false;
            if (component.pupils == FTPupilMode.KeepExisting) return true;

            var pupilShapes = Enumerable.Range(0, FTKeys.KeyCount)
                .Where(FTKeys.IsPupilKey)
                .Select(key => FTKeys.FindKey(blendshapes, key))
                .Where(index => index >= 0)
                .Select(index => blendshapes[index])
                .ToList();
            if (pupilShapes.Count == 0) return false;

            var animated = AnimatedBlendshapes(descriptor);
            return pupilShapes.Any(animated.Contains);
        }

        /// <summary>
        /// Blendshapes animated by the avatar's playable layers and by the controllers VRCFury or Modular Avatar
        /// will merge (already merged at build time). The 1.x OSC controllers are left out, they're removed.
        /// </summary>
        static HashSet<string> AnimatedBlendshapes(VRCAvatarDescriptor descriptor)
        {
            var controllers = new HashSet<RuntimeAnimatorController>();
            if (descriptor.customizeAnimationLayers)
            {
                foreach (var layer in (descriptor.baseAnimationLayers ?? new VRCAvatarDescriptor.CustomAnimLayer[0])
                         .Concat(descriptor.specialAnimationLayers ?? new VRCAvatarDescriptor.CustomAnimLayer[0]))
                {
                    if (!layer.isDefault && layer.animatorController != null) controllers.Add(layer.animatorController);
                }
            }

            var root = descriptor.transform;
            foreach (var behaviour in descriptor.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour is VRC_AvatarDescriptor || behaviour is PawlygonFTComponent) continue;
                if (FTAvatarUtils.IsEditorOnly(behaviour.transform, root)) continue;
                foreach (var controller in FTAvatarUtils.GetReferencedControllers(behaviour))
                {
                    if (!LegacyOscDetector.IsPawlygonLegacyController(controller)) controllers.Add(controller);
                }
            }

            const string prefix = "blendShape.";
            var names = new HashSet<string>();
            foreach (var clip in controllers.SelectMany(c => c.animationClips).Where(c => c != null).Distinct())
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith(prefix))
                    {
                        names.Add(binding.propertyName.Substring(prefix.Length));
                    }
                }
            }
            return names;
        }

        void MapVisemes(PawlygonFaceTracking component, VRCAvatarDescriptor descriptor, string[] blendshapes)
        {
            var wantsFaceTrackingLipSync = component.lipSync == FTLipSyncMode.FaceTrackingVisemes;
            var alreadyFaceTrackingLipSync = descriptor.lipSync == VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShapeFaceTracking;
            if (!wantsFaceTrackingLipSync && !alreadyFaceTrackingLipSync) return;

            // Reuse the avatar's existing viseme setup when it has one
            var existing = descriptor.lipSync == VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape ? descriptor.VisemeBlendShapes : null;
            if (existing != null && existing.Length >= FTKeys.VisemeCount && descriptor.VisemeSkinnedMesh != null)
            {
                if (!Meshes.Contains(descriptor.VisemeSkinnedMesh))
                {
                    Meshes.Add(descriptor.VisemeSkinnedMesh);
                    blendshapes = GetBlendshapeNames(Meshes);
                }
                for (var viseme = 0; viseme < FTKeys.VisemeCount; viseme++)
                {
                    if (string.IsNullOrEmpty(existing[viseme]) || !blendshapes.Contains(existing[viseme])) continue;
                    Set.visemeBlendshapeNames[viseme] = existing[viseme];
                    MappedVisemes++;
                }
                VisemesFromDescriptor = MappedVisemes > 0;
            }

            if (!VisemesFromDescriptor)
            {
                for (var viseme = 0; viseme < FTKeys.VisemeCount; viseme++)
                {
                    var index = FTKeys.FindViseme(blendshapes, viseme);
                    if (index < 0) continue;
                    Set.visemeBlendshapeNames[viseme] = blendshapes[index];
                    MappedVisemes++;
                }
            }

            if (MappedVisemes == 0 && wantsFaceTrackingLipSync && !alreadyFaceTrackingLipSync)
            {
                Warnings.Add("No viseme blendshapes were found, so lip sync is left unchanged.");
                return;
            }
            UseFaceTrackingLipSync = wantsFaceTrackingLipSync || alreadyFaceTrackingLipSync;
        }

        void ResolveEyes(PawlygonFaceTracking component, VRCAvatarDescriptor descriptor)
        {
            if (component.eyelids == FTEyelidMode.FaceTracking)
            {
                var hasEyelids = MappedKeys.Contains((int)VRCAvatarDescriptor.FaceTrackingKey.EyeClosedLeft)
                                 && MappedKeys.Contains((int)VRCAvatarDescriptor.FaceTrackingKey.EyeClosedRight);
                if (hasEyelids) UseFaceTrackingEyelids = true;
                else Warnings.Add("EyeClosedLeft/Right blendshapes weren't found, so eyelids are left unchanged.");
            }

            var hasEyeLookShapes = MappedKeys.Any(key => FTKeys.KeyName(key).StartsWith("EyeLook"));
            var settings = descriptor.customEyeLookSettings;
            var hasEyeBones = descriptor.enableEyeLook && settings.leftEye != null && settings.rightEye != null;

            switch (component.eyeGaze)
            {
                case FTGazeMode.Auto:
                    if (hasEyeLookShapes) GazeMode = hasEyeBones ? EyeGazeMode.Both : EyeGazeMode.FaceTracking;
                    else if (hasEyeBones) GazeMode = EyeGazeMode.BoneRotation;
                    break;
                case FTGazeMode.Both:
                    GazeMode = EyeGazeMode.Both;
                    break;
                case FTGazeMode.FaceTracking:
                    GazeMode = EyeGazeMode.FaceTracking;
                    break;
                case FTGazeMode.BoneRotation:
                    GazeMode = EyeGazeMode.BoneRotation;
                    break;
            }

            if ((GazeMode == EyeGazeMode.Both || GazeMode == EyeGazeMode.FaceTracking) && !hasEyeLookShapes)
            {
                Warnings.Add("Eye gaze uses face tracking blendshapes, but no EyeLook blendshapes were found.");
            }
            if ((GazeMode == EyeGazeMode.Both || GazeMode == EyeGazeMode.BoneRotation) && !hasEyeBones)
            {
                Warnings.Add("Eye gaze rotates the eye bones, but they aren't set up in the descriptor's Eye Look section.");
            }
        }

        /// <summary>All blendshape names of the meshes, first occurrence wins, like the SDK's mesh catalog.</summary>
        static string[] GetBlendshapeNames(IEnumerable<SkinnedMeshRenderer> meshes)
        {
            var names = new List<string>();
            var seen = new HashSet<string>();
            foreach (var mesh in meshes.Where(m => m != null && m.sharedMesh != null).Take(FTBuildContext.MaxMeshes))
            {
                for (var i = 0; i < mesh.sharedMesh.blendShapeCount; i++)
                {
                    var name = mesh.sharedMesh.GetBlendShapeName(i);
                    if (seen.Add(name)) names.Add(name);
                }
            }
            return names.ToArray();
        }
    }
}
