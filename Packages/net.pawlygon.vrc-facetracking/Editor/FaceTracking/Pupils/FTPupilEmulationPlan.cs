using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BlendshapesSet = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingBlendshapesSet;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// What a <see cref="PawlygonFTPupilEmulation"/> component will drive: the blendshapes the pupil keys map to,
    /// and whether real pupil tracking can take over. Resolved the same way for the build and the inspector.
    /// </summary>
    internal sealed class FTPupilEmulationPlan
    {
        /// <summary>A pupil blendshape and the tracking keys that map to it.</summary>
        internal sealed class Shape
        {
            public string Name;
            public bool IsDilation;
            public readonly List<int> Keys = new List<int>();

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
                    var dilation = Average(DilationShapes.SelectMany(s => s.Keys).Distinct().ToList());
                    var constrict = Average(ConstrictShapes.SelectMany(s => s.Keys).Distinct().ToList());
                    if (constrict == null) return dilation;
                    return $"{dilation ?? "0"} - {constrict}";
                }
            }

            static string Average(List<int> keys)
            {
                if (keys.Count == 0) return null;
                return keys.Count == 1 ? FTKeys.KeyName(keys[0]) : $"({string.Join(" + ", keys.Select(FTKeys.KeyName))}) / {keys.Count}";
            }
        }

        public const string ParameterPrefix = "PawlygonFT/Pupils/";

        public readonly List<Shape> Shapes = new List<Shape>();

        /// <summary>The pupil keys are available (they need all tracking keys), so real pupil tracking can take over.</summary>
        public bool RealTracking;

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
                var shape = setList.Select(s => key < s.faceTrackingBlendshapeNames.Length ? s.faceTrackingBlendshapeNames[key] : null)
                    .FirstOrDefault(n => !string.IsNullOrEmpty(n) && names.Contains(n));
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
            }
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
