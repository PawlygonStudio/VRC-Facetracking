using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using BlendshapesSet = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingBlendshapesSet;
using ParameterBehavior = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingParameterBehavior;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Face tracking configuration being assembled during a build. Builders add to it and
    /// <see cref="Commit"/> writes it to the descriptor once, so components can't overwrite each other.
    /// Without a setup component, it starts from whatever is already on the descriptor.
    /// </summary>
    internal sealed class FTBuildContext
    {
        // Limits enforced by the SDK inspector and performance scanners
        public const int MaxMeshes = 16;
        public const int MaxSets = 128;
        public const int MaxBehaviors = 128;

        public GameObject AvatarRoot { get; }
        public VRCAvatarDescriptor Descriptor { get; }

        public bool EnableFaceTracking;
        public bool UseAllTrackingKeys;
        public VRCAvatarDescriptor.VisemeDampenMode VisemeDampenMode;
        public float VisemeDampenAmount;
        public readonly List<SkinnedMeshRenderer> Meshes = new List<SkinnedMeshRenderer>();
        public readonly List<BlendshapesSet> Sets = new List<BlendshapesSet>();
        public readonly List<ParameterBehavior> Behaviors = new List<ParameterBehavior>();

        public FTPass Pass { get; }

        string buildFolder;
        FTGeneratedAssets generated;

        /// <summary>Folder for assets generated during this build, emptied the first time it's used.</summary>
        public string BuildFolder => buildFolder ?? (buildFolder = FTTempAssets.PrepareBuildFolder(AvatarRoot));

        /// <summary>
        /// The components' own FX layers, expression parameters and menu controls. Only in the descriptor pass, so
        /// VRCFury or Modular Avatar can merge them afterwards.
        /// </summary>
        public FTGeneratedAssets Generated
        {
            get
            {
                if (Pass != FTPass.Descriptor) throw new System.InvalidOperationException("Generated assets are only added in the descriptor pass.");
                return generated ?? (generated = new FTGeneratedAssets(this));
            }
        }

        FTBuildContext(VRCAvatarDescriptor descriptor, FTPass pass)
        {
            Descriptor = descriptor;
            AvatarRoot = descriptor.gameObject;
            Pass = pass;
        }

        public static FTBuildContext FromDescriptor(VRCAvatarDescriptor descriptor, FTPass pass)
        {
            var context = new FTBuildContext(descriptor, pass)
            {
                EnableFaceTracking = descriptor.enableFaceTracking,
                UseAllTrackingKeys = descriptor.useAllTrackingKeys,
                VisemeDampenMode = descriptor.visemeDampenMode,
                VisemeDampenAmount = descriptor.visemeDampenAmount,
            };
            if (descriptor.faceTrackingMeshes != null) context.Meshes.AddRange(descriptor.faceTrackingMeshes);
            if (descriptor.faceTrackingBlendshapesSetList != null) context.Sets.AddRange(descriptor.faceTrackingBlendshapesSetList);
            if (descriptor.faceTrackingParameterBehaviors != null) context.Behaviors.AddRange(descriptor.faceTrackingParameterBehaviors);
            return context;
        }

        /// <summary>Drops the existing configuration, for components that replace it.</summary>
        public void Clear()
        {
            EnableFaceTracking = false;
            UseAllTrackingKeys = false;
            VisemeDampenMode = VRCAvatarDescriptor.VisemeDampenMode.None;
            VisemeDampenAmount = 0f;
            Meshes.Clear();
            Sets.Clear();
            Behaviors.Clear();
        }

        /// <summary>
        /// A set with correctly sized arrays. The SDK's constructor leaves them null and the weight at 0.
        /// </summary>
        public static BlendshapesSet CreateSet(string setName, float weight)
        {
            return new BlendshapesSet
            {
                setName = setName,
                setWeight = weight,
                faceTrackingBlendshapeNames = Enumerable.Repeat("", FTKeys.KeyCount).ToArray(),
                faceTrackingBlendshapeMultipliers = Enumerable.Repeat(1f, FTKeys.KeyCount).ToArray(),
                visemeBlendshapeNames = Enumerable.Repeat("", FTKeys.VisemeCount).ToArray(),
                visemeBlendshapeMultipliers = Enumerable.Repeat(1f, FTKeys.VisemeCount).ToArray(),
            };
        }

        public void Commit()
        {
            var meshes = Meshes.Where(m => m != null).Distinct().ToList();
            var outside = meshes.Where(m => !FTAvatarUtils.IsPartOfAvatar(m, AvatarRoot.transform)).ToList();
            if (outside.Count > 0)
            {
                throw new FTBuildException("Face tracking meshes must be part of the avatar: " +
                                           string.Join(", ", outside.Select(m => m.name)));
            }
            if (meshes.Count > MaxMeshes)
            {
                throw new FTBuildException($"Face tracking supports up to {MaxMeshes} meshes, but {meshes.Count} were set up.");
            }
            if (Sets.Count > MaxSets)
            {
                throw new FTBuildException($"Face tracking supports up to {MaxSets} blendshape sets, but {Sets.Count} were set up.");
            }
            if (Behaviors.Count > MaxBehaviors)
            {
                throw new FTBuildException($"Face tracking supports up to {MaxBehaviors} parameter behaviors, but {Behaviors.Count} were set up.");
            }

            // The SDK silently forces all but one set with the same name to weight 0
            var duplicate = Sets.GroupBy(s => s.setName).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
            {
                throw new FTBuildException($"Two blendshape sets are named '{duplicate.Key}'. Set names must be unique.");
            }

            Descriptor.enableFaceTracking = EnableFaceTracking;
            Descriptor.useAllTrackingKeys = UseAllTrackingKeys;
            Descriptor.visemeDampenMode = VisemeDampenMode;
            Descriptor.visemeDampenAmount = VisemeDampenAmount;
            Descriptor.faceTrackingMeshes = meshes;
            Descriptor.faceTrackingBlendshapesSetList = new List<BlendshapesSet>(Sets);
            Descriptor.faceTrackingParameterBehaviors = new List<ParameterBehavior>(Behaviors);
            EditorUtility.SetDirty(Descriptor);

            generated?.Install();
        }
    }
}
