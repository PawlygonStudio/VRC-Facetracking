using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using BlendshapesSet = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingBlendshapesSet;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// The blendshape fixes a <see cref="PawlygonFTBlendshapeFixes"/> component drives. Each fixed shape is
    /// traced back to the tracking keys driving it in the blendshape sets, so the fix follows the same
    /// face tracking values. Resolved the same way for the build and the inspector preview.
    /// </summary>
    internal sealed class FTFixPlan
    {
        internal sealed class Entry
        {
            public string Blendshape;
            public List<string> Sources;
            public string Expression;
            public string WeightSet;
            public bool IsManual;
            public bool Excluded;
            public string Problem;
        }

        readonly struct Driver
        {
            public readonly string Key;
            public readonly float Multiplier;
            public readonly string Set;

            public Driver(string key, float multiplier, string set)
            {
                Key = key;
                Multiplier = multiplier;
                Set = set;
            }
        }

        public readonly List<Entry> Entries = new List<Entry>();

        public static FTFixPlan Resolve(PawlygonFTBlendshapeFixes component, IEnumerable<SkinnedMeshRenderer> meshes, IEnumerable<BlendshapesSet> sets)
        {
            var plan = new FTFixPlan();
            var blendshapes = new HashSet<string>();
            foreach (var mesh in meshes.Where(m => m != null && m.sharedMesh != null))
            {
                for (var i = 0; i < mesh.sharedMesh.blendShapeCount; i++) blendshapes.Add(mesh.sharedMesh.GetBlendShapeName(i));
            }
            var setList = sets.Where(set => set != null).ToList();
            var drivers = MapDrivers(setList);

            if (component.autoDetect)
            {
                foreach (var name in blendshapes.Where(n => n.Contains('-') && !drivers.ContainsKey(n)).OrderBy(n => n))
                {
                    var parts = name.Split('-').Select(p => p.Trim()).ToList();
                    if (parts.Count < 2 || !parts.All(part => DriversFor(drivers, part).Count > 0)) continue;
                    plan.Entries.Add(new Entry { Blendshape = name, Sources = parts, Excluded = component.excludedFixes.Contains(name) });
                }
            }

            foreach (var fix in component.fixes)
            {
                var name = fix.blendshape?.Trim();
                if (string.IsNullOrEmpty(name)) continue;

                var sources = (fix.sources ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                var entry = new Entry { Blendshape = name, Sources = sources, IsManual = true };
                if (sources.Count < 2) entry.Problem = "List at least two blendshapes it fixes.";
                else if (!blendshapes.Contains(name)) entry.Problem = $"'{name}' isn't on the face tracking meshes.";
                else
                {
                    var undriven = sources.FirstOrDefault(src => DriversFor(drivers, src).Count == 0);
                    if (undriven != null) entry.Problem = $"'{undriven}' isn't driven by face tracking.";
                }

                plan.Entries.RemoveAll(e => e.Blendshape == name);
                plan.Entries.Add(entry);
            }

            foreach (var entry in plan.Entries.Where(e => e.Problem == null))
            {
                var sourceDrivers = entry.Sources.Select(src => DriversFor(drivers, src)).ToList();
                entry.Expression = Combine(sourceDrivers.Select(SourceExpression).ToList(), component.combine);
                // Fades with the set the fixed shapes come from, or the first set for shapes only known by name
                entry.WeightSet = sourceDrivers.SelectMany(d => d).Select(d => d.Set).FirstOrDefault(n => n != null)
                                  ?? setList.FirstOrDefault()?.setName;
            }
            return plan;
        }

        /// <summary>Blendshape name -> the tracking keys that drive it, from every set.</summary>
        static Dictionary<string, List<Driver>> MapDrivers(IEnumerable<BlendshapesSet> sets)
        {
            var drivers = new Dictionary<string, List<Driver>>();
            foreach (var set in sets.Where(s => s?.faceTrackingBlendshapeNames != null))
            {
                for (var key = 0; key < set.faceTrackingBlendshapeNames.Length && key < FTKeys.KeyCount; key++)
                {
                    var name = set.faceTrackingBlendshapeNames[key];
                    if (string.IsNullOrEmpty(name)) continue;

                    var multiplier = set.faceTrackingBlendshapeMultipliers != null && key < set.faceTrackingBlendshapeMultipliers.Length
                        ? set.faceTrackingBlendshapeMultipliers[key]
                        : 1f;
                    if (!drivers.TryGetValue(name, out var list)) drivers[name] = list = new List<Driver>();
                    list.Add(new Driver(FTKeys.KeyName(key), multiplier, set.setName));
                }
            }
            return drivers;
        }

        /// <summary>
        /// The tracking keys behind a fixed shape. A blendshape driven by the sets uses those keys and
        /// multipliers. Otherwise the name is looked up in the SDK's alias table, so combined shapes the
        /// avatar has but face tracking doesn't drive (LipFunnel next to the four LipFunnel* shapes) still work.
        /// </summary>
        static List<Driver> DriversFor(Dictionary<string, List<Driver>> drivers, string blendshape)
        {
            if (drivers.TryGetValue(blendshape, out var mapped)) return mapped;
            return FTKeys.KeysForName(blendshape).Select(key => new Driver(FTKeys.KeyName(key), 1f, null)).ToList();
        }

        /// <summary>A shape driven by several keys (e.g. corner pull and slant) follows the strongest one.</summary>
        static string SourceExpression(List<Driver> drivers)
        {
            var terms = drivers
                .Select(d => Mathf.Approximately(d.Multiplier, 1f) ? d.Key : $"{d.Key} * {d.Multiplier.ToString(CultureInfo.InvariantCulture)}")
                .Distinct()
                .ToList();
            return Fold(terms, "max");
        }

        static string Combine(List<string> terms, FTFixCombine combine)
        {
            switch (combine)
            {
                case FTFixCombine.Minimum: return Fold(terms, "min");
                case FTFixCombine.Maximum: return Fold(terms, "max");
                case FTFixCombine.Multiply: return string.Join(" * ", terms.Select(Group));
                default: return $"({string.Join(" + ", terms)}) / {terms.Count}";
            }
        }

        /// <summary>min and max take two arguments, so longer lists nest: max(a, max(b, c)).</summary>
        static string Fold(List<string> terms, string function)
        {
            var result = terms[terms.Count - 1];
            for (var i = terms.Count - 2; i >= 0; i--) result = $"{function}({terms[i]}, {result})";
            return result;
        }

        static string Group(string term) => term.Contains(' ') && !term.EndsWith(")") ? $"({term})" : term;
    }
}
