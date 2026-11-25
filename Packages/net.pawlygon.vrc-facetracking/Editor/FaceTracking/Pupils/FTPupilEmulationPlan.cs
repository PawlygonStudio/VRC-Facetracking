using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BlendshapesSet = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingBlendshapesSet;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// What a <see cref="PawlygonFTPupilEmulation"/> component will drive: the blendshapes the pupil keys map to,
    /// whether the headset's pupil tracking can play on top, and which menu toggles Face Tracking Toggles adds.
    /// Resolved the same way for the build and the inspector.
    /// </summary>
    internal sealed class FTPupilEmulationPlan
    {
        /// <summary>A pupil blendshape and the tracking keys that map to it.</summary>
        internal sealed class Shape
        {
            public string Name;
            public bool IsDilation;
            public readonly List<int> Keys = new List<int>();

            /// <summary>The keys' multipliers in the set they were mapped in, where they aren't 1.</summary>
            public readonly Dictionary<int, float> Multipliers = new Dictionary<int, float>();

            /// <summary>Left, Right, or Both when the shape's keys cover both eyes.</summary>
            public string Side
            {
                get
                {
                    var names = Keys.Select(FTKeys.KeyName).ToList();
                    if (names.All(n => n.EndsWith("Left"))) return "Left";
                    if (names.All(n => n.EndsWith("Right"))) return "Right";
                    return "Both";
                }
            }
        }

        /// <summary>
        /// The pupil shapes of one eye (or of both, when they share shapes), forwarded as a single signed value:
        /// dilation minus constriction, -1 to 1. A pupil doesn't dilate and constrict at once, so nothing is lost,
        /// and it takes one forwarded parameter per eye instead of one per shape.
        /// </summary>
        internal sealed class Channel
        {
            public string Side;
            public readonly List<Shape> Shapes = new List<Shape>();

            public string Parameter => ParameterPrefix + (Side == "Both" ? "Pupil" : "Pupil" + Side);

            public IEnumerable<Shape> DilationShapes => Shapes.Where(s => s.IsDilation);
            public IEnumerable<Shape> ConstrictShapes => Shapes.Where(s => !s.IsDilation);

            /// <summary>The average of the dilation keys minus the average of the constriction keys, like native face tracking would apply them.</summary>
            public string Expression
            {
                get
                {
                    var dilation = Average(DilationShapes);
                    var constrict = Average(ConstrictShapes);
                    if (constrict == null) return dilation;
                    return $"{dilation ?? "0"} - {constrict}";
                }
            }

            /// <summary>The shapes' keys, each with the multiplier it had in its set, since the pupil keys are taken out of the sets.</summary>
            static string Average(IEnumerable<Shape> shapes)
            {
                var keys = new List<string>();
                var seen = new HashSet<int>();
                foreach (var shape in shapes)
                {
                    foreach (var key in shape.Keys.Where(seen.Add))
                    {
                        keys.Add(FTKeys.Scaled(FTKeys.KeyName(key), shape.Multipliers.TryGetValue(key, out var multiplier) ? multiplier : 1f));
                    }
                }
                if (keys.Count == 0) return null;
                return keys.Count == 1 ? keys[0] : $"({string.Join(" + ", keys)}) / {keys.Count}";
            }
        }

        public const string ParameterPrefix = "PawlygonFT/Pupils/";

        /// <summary>The Pupil Tracking menu toggle: whether the headset's pupil tracking plays. Saved and synced.</summary>
        public const string TrackingParameter = "PawlygonFT/PupilTracking";

        /// <summary>The Pupil Emulation menu toggle: whether the emulated pupil movement plays. Saved and synced.</summary>
        public const string EmulationParameter = "PawlygonFT/PupilEmulation";

        public readonly List<Shape> Shapes = new List<Shape>();

        /// <summary>The pupil keys are available (they need all tracking keys), so the headset's pupil tracking can play.</summary>
        public bool RealTracking;

        /// <summary>Face Tracking Toggles adds a Pupil Tracking toggle to the menu. Without it, pupil tracking always plays.</summary>
        public bool TrackingToggle;

        /// <summary>Face Tracking Toggles adds a Pupil Emulation toggle to the menu. Without it, the emulation always plays.</summary>
        public bool EmulationToggle;

        /// <summary>Synced bits the menu toggles use.</summary>
        public int ToggleBits => (TrackingToggle ? 1 : 0) + (EmulationToggle ? 1 : 0);

        /// <summary>
        /// Which menu toggles Face Tracking Toggles asks for. There's nothing to toggle without pupil blendshapes, and
        /// no pupil tracking to toggle without the pupil keys.
        /// </summary>
        public void ResolveToggles(PawlygonFTToggles toggles)
        {
            if (toggles == null || Shapes.Count == 0) return;
            TrackingToggle = toggles.pupilTrackingToggle && RealTracking;
            EmulationToggle = toggles.pupilEmulationToggle;
        }

        public IEnumerable<Shape> DilationShapes => Shapes.Where(s => s.IsDilation);
        public IEnumerable<Shape> ConstrictShapes => Shapes.Where(s => !s.IsDilation);

        /// <summary>The shapes grouped by eye, each forwarded as one value for real pupil tracking.</summary>
        public List<Channel> Channels => Shapes.GroupBy(s => s.Side)
            .Select(g =>
            {
                var channel = new Channel { Side = g.Key };
                channel.Shapes.AddRange(g);
                return channel;
            })
            .ToList();

        /// <summary>
        /// Each pupil key's blendshape: the one a set already maps it to, or the SDK's best match among the face
        /// tracking meshes' blendshapes, which is what native face tracking would use.
        /// </summary>
        public static FTPupilEmulationPlan Resolve(IEnumerable<SkinnedMeshRenderer> meshes, IEnumerable<BlendshapesSet> sets, bool useAllTrackingKeys)
        {
            var plan = new FTPupilEmulationPlan();
            var meshList = meshes.Where(m => m != null && m.sharedMesh != null).ToList();
            var names = meshList.SelectMany(m => Enumerable.Range(0, m.sharedMesh.blendShapeCount).Select(m.sharedMesh.GetBlendShapeName))
                .Distinct().ToArray();
            var setList = sets.Where(s => s?.faceTrackingBlendshapeNames != null).ToList();

            var pupilKeys = Enumerable.Range(0, FTKeys.KeyCount).Where(FTKeys.IsPupilKey).ToList();
            plan.RealTracking = useAllTrackingKeys || pupilKeys.All(FTKeys.IsBasic);

            foreach (var key in pupilKeys)
            {
                var source = setList.FirstOrDefault(s => key < s.faceTrackingBlendshapeNames.Length
                                                         && !string.IsNullOrEmpty(s.faceTrackingBlendshapeNames[key])
                                                         && names.Contains(s.faceTrackingBlendshapeNames[key]));
                var shape = source?.faceTrackingBlendshapeNames[key];
                var multiplier = source?.faceTrackingBlendshapeMultipliers != null && key < source.faceTrackingBlendshapeMultipliers.Length
                    ? source.faceTrackingBlendshapeMultipliers[key]
                    : 1f;
                if (shape == null)
                {
                    var index = FTKeys.FindKey(names, key);
                    if (index >= 0) shape = names[index];
                }
                if (shape == null) continue;

                var entry = plan.Shapes.FirstOrDefault(s => s.Name == shape);
                if (entry == null)
                {
                    entry = new Shape { Name = shape, IsDilation = FTKeys.IsDilationKey(key) };
                    plan.Shapes.Add(entry);
                }
                entry.Keys.Add(key);
                if (!Mathf.Approximately(multiplier, 1f)) entry.Multipliers[key] = multiplier;
            }
            return plan;
        }

        /// <summary>
        /// The plan for an avatar as its build would resolve it, with the menu toggles its Face Tracking Toggles asks
        /// for. Null when the avatar has no Pupil Emulation, or face tracking isn't enabled on it.
        /// </summary>
        public static FTPupilEmulationPlan ForAvatar(VRC.SDK3.Avatars.Components.VRCAvatarDescriptor avatar)
        {
            if (FTAvatarUtils.GetBuildComponents<PawlygonFTPupilEmulation>(avatar.gameObject).Count == 0) return null;

            FTPupilEmulationPlan plan;
            var setup = FTAvatarUtils.GetBuildComponents<PawlygonFaceTracking>(avatar.gameObject).FirstOrDefault();
            if (setup != null)
            {
                var setupPlan = FaceTrackingSetupPlan.Resolve(setup, avatar);
                plan = Resolve(setupPlan.Meshes, setupPlan.Set != null ? new[] { setupPlan.Set } : new BlendshapesSet[0], setup.useAllTrackingKeys);
            }
            else if (avatar.enableFaceTracking)
            {
                plan = Resolve(avatar.faceTrackingMeshes ?? new List<SkinnedMeshRenderer>(),
                    avatar.faceTrackingBlendshapesSetList ?? new List<BlendshapesSet>(), avatar.useAllTrackingKeys);
            }
            else
            {
                return null;
            }

            plan.ResolveToggles(FTAvatarUtils.GetBuildComponents<PawlygonFTToggles>(avatar.gameObject).FirstOrDefault());
            return plan;
        }

        /// <summary>Removes the pupil keys from every set, so native face tracking leaves the pupil blendshapes alone.</summary>
        public static void UnmapPupilKeys(IEnumerable<BlendshapesSet> sets)
        {
            foreach (var set in sets.Where(s => s?.faceTrackingBlendshapeNames != null))
            {
                for (var key = 0; key < set.faceTrackingBlendshapeNames.Length; key++)
                {
                    if (FTKeys.IsPupilKey(key)) set.faceTrackingBlendshapeNames[key] = "";
                }
            }
        }
    }
}
