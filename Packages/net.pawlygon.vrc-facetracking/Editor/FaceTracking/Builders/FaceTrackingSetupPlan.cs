using System.Collections.Generic;
using System.Linq;
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

        // Meshes VRCFury Full Controllers configure for face tracking, with where. Auto mode leaves them to VRCFury,
        // which stops the build when a mesh is configured in more than one place.
        public readonly List<(SkinnedMeshRenderer mesh, string holder)> LeftToFullControllers = new List<(SkinnedMeshRenderer, string)>();
        Dictionary<SkinnedMeshRenderer, string> claimed;

        // Keys considered for mapping (all keys, or only the basic ones)
        public readonly List<int> MappedKeys = new List<int>();
        public readonly List<int> UnmappedKeys = new List<int>();

        // For the inspector: every blendshape on the meshes, what each key matched on its own, and the keys mapped by hand
        public string[] Blendshapes = new string[0];
        public readonly Dictionary<int, string> AutoMatches = new Dictionary<int, string>();
        public readonly HashSet<int> OverriddenKeys = new HashSet<int>();
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

            plan.claimed = FTFullControllerMeshes.Find(descriptor.gameObject);
            plan.ResolveMeshes(component, descriptor);
            if (plan.Meshes.Count == 0)
            {
                plan.Errors.Add(component.meshMode == FTMeshMode.Manual ? "No face tracking meshes are assigned."
                    : plan.LeftToFullControllers.Count > 0 ? "Every mesh with face tracking blendshapes is configured by a VRCFury Full Controller, so there's nothing left to set up."
                    : "No mesh with face tracking blendshapes was found on the avatar.");
                return plan;
            }
            var blendshapes = GetBlendshapeNames(plan.Meshes);
            plan.Blendshapes = blendshapes;
            plan.Set = FTBuildContext.CreateSet(PawlygonFaceTracking.BaseSetName, 1f);
            plan.MapKeys(component, blendshapes);
            plan.MapVisemes(component, descriptor, blendshapes);
            plan.ResolveEyes(component, descriptor);

            // After visemes, which can add the avatar's viseme mesh. VRCFury stops the build when a mesh is configured
            // both here and in a Full Controller, so it's reported here with the reason.
            foreach (var mesh in plan.Meshes.Where(m => plan.claimed.ContainsKey(m)))
            {
                plan.Errors.Add($"'{mesh.name}' is also configured for face tracking by the VRCFury Full Controller on '{plan.claimed[mesh]}'. " +
                                "VRCFury only allows one of them per mesh: remove it from " +
                                (component.meshMode == FTMeshMode.Manual ? "the Meshes list" : "the descriptor's lip sync") + " or from that Full Controller.");
            }

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
                if (!Enumerable.Range(0, FTKeys.KeyCount).Any(key => FTKeys.FindKey(names, key) >= 0)) continue;
                if (claimed.TryGetValue(mesh, out var holder)) LeftToFullControllers.Add((mesh, holder));
                else Meshes.Add(mesh);
            }
        }

        void MapKeys(PawlygonFaceTracking component, string[] blendshapes)
        {
            for (var key = 0; key < FTKeys.KeyCount; key++)
            {
                if (!component.useAllTrackingKeys && !FTKeys.IsBasic(key)) continue;

                var index = FTKeys.FindKey(blendshapes, key);
                if (index >= 0) AutoMatches[key] = blendshapes[index];

                var name = index >= 0 ? blendshapes[index] : null;
                var manual = component.keyOverrides?.LastOrDefault(o => o != null && o.key == FTKeys.KeyName(key));
                if (manual != null)
                {
                    if (string.IsNullOrEmpty(manual.blendshape))
                    {
                        name = null;
                        OverriddenKeys.Add(key);
                    }
                    else if (blendshapes.Contains(manual.blendshape))
                    {
                        name = manual.blendshape;
                        OverriddenKeys.Add(key);
                    }
                    else
                    {
                        Warnings.Add($"{FTKeys.KeyName(key)} is set to '{manual.blendshape}', which isn't on the face tracking meshes, so it's matched automatically.");
                    }
                }

                if (name != null)
                {
                    Set.faceTrackingBlendshapeNames[key] = name;
                    MappedKeys.Add(key);
                }
                else
                {
                    UnmappedKeys.Add(key);
                }
            }

            foreach (var manual in component.keyOverrides ?? new List<FTKeyOverride>())
            {
                if (manual == null) continue;
                var key = FTKeys.KeyByName(manual.key);
                if (key < 0) Warnings.Add($"'{manual.key}' isn't a tracking key, so its manual mapping is ignored.");
                else if (!component.useAllTrackingKeys && !FTKeys.IsBasic(key)) Warnings.Add($"{manual.key} is mapped by hand, but only used with All Tracking Keys.");
            }
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
