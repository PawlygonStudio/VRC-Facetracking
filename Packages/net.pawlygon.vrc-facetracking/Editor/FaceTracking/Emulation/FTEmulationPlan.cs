using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using BlendshapesSet = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingBlendshapesSet;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// The blendshapes a <see cref="PawlygonFTEmulation"/> component rewrites. Each affected shape is taken out of
    /// the blendshape sets and driven by one expression instead: its tracked value, raised to any emulated value
    /// (emulations never lower it), then scaled down by the enabled limits. Resolved the same way for the build and
    /// the inspector.
    /// </summary>
    internal sealed class FTEmulationPlan
    {
        internal sealed class Shape
        {
            public string Blendshape;
            public string Set;
            public readonly List<(int Key, float Multiplier)> Keys = new List<(int, float)>();
            public readonly List<string> Emulations = new List<string>();
            public readonly List<string> Limits = new List<string>();
            public readonly List<string> Rules = new List<string>();

            public string Expression
            {
                get
                {
                    var tracked = Keys.Count == 1
                        ? Scaled(FTKeys.KeyName(Keys[0].Key), Keys[0].Multiplier)
                        : $"({string.Join(" + ", Keys.Select(k => Scaled(FTKeys.KeyName(k.Key), k.Multiplier)))}) / {Keys.Count}";
                    var value = Emulations.Aggregate(tracked, (current, emulation) => $"max({current}, {emulation})");
                    if (Limits.Count == 0) return value;
                    return $"{(Emulations.Count > 0 ? value : Wrap(value))} * {string.Join(" * ", Limits)}";
                }
            }

            static string Scaled(string key, float multiplier) => Mathf.Approximately(multiplier, 1f) ? key : $"{key} * {Number(multiplier)}";
            static string Wrap(string value) => value.Contains(' ') ? $"({value})" : value;
        }

        /// <summary>A rule that couldn't be applied, and why.</summary>
        internal sealed class Skipped
        {
            public string Rule;
            public string Reason;
        }

        public readonly List<Shape> Shapes = new List<Shape>();
        public readonly List<Skipped> SkippedRules = new List<Skipped>();
        public bool NeedsAllTrackingKeys;

        const string Funnel = "(LipFunnelUpperLeft + LipFunnelUpperRight + LipFunnelLowerLeft + LipFunnelLowerRight) / 4";
        const string MouthRight = "(MouthUpperRight + MouthLowerRight - MouthUpperLeft - MouthLowerLeft) / 2";
        const string MouthLeft = "(MouthUpperLeft + MouthLowerLeft - MouthUpperRight - MouthLowerRight) / 2";
        static readonly string[] Sides = { "Left", "Right" };

        public static FTEmulationPlan Resolve(PawlygonFTEmulation component, IEnumerable<BlendshapesSet> sets, bool useAllTrackingKeys)
        {
            var plan = new FTEmulationPlan();
            if (!useAllTrackingKeys)
            {
                // Brows, mouth and tongue keys all need them
                plan.NeedsAllTrackingKeys = true;
                return plan;
            }

            var builder = new Builder(plan, sets.Where(s => s?.faceTrackingBlendshapeNames != null).ToList());

            if (component.tongueOpensJaw)
            {
                builder.Emulate("Tongue opens jaw", "JawOpen", Scale("TongueOut", component.tongueOpensJawAmount));
            }
            foreach (var side in Sides)
            {
                if (component.browsUpWithWideEyes)
                {
                    var term = Scale($"EyeWide{side}", component.browsUpWithWideEyesAmount);
                    builder.Emulate("Brows up with wide eyes", $"BrowInnerUp{side}", term);
                    builder.Emulate("Brows up with wide eyes", $"BrowOuterUp{side}", term);
                }
                if (component.browsDownWithSquint)
                {
                    builder.Emulate("Brows down with squint", $"BrowLowerer{side}", Scale($"EyeSquint{side}", component.browsDownWithSquintAmount));
                }
                if (component.angryBrows)
                {
                    builder.Emulate("Angry brows", $"BrowLowerer{side}", Scale($"max(NoseSneer{side}, MouthRaiserLower)", component.angryBrowsAmount));
                }
                if (component.sadBrows)
                {
                    builder.Emulate("Sad brows", $"BrowInnerUp{side}",
                        $"min(MouthFrownLeft, MouthFrownRight) * {Number(component.sadBrowsAmount / 0.7f)} * clamp(1 - MouthClosed / 2, 0, 1)");
                }
            }

            if (component.limitFunnelByChinRaise)
            {
                foreach (var key in new[] { "LipFunnelUpperLeft", "LipFunnelUpperRight", "LipFunnelLowerLeft", "LipFunnelLowerRight" })
                {
                    builder.Limit("Lip funnel relaxed by chin raise", key, "clamp(1 - MouthRaiserLower / 1.5, 0, 1)");
                }
            }
            if (component.limitRaisersByMouthClosed)
            {
                builder.Limit("Mouth raisers relaxed by closed mouth", "MouthRaiserUpper", "clamp(1 - MouthClosed, 0, 1)");
                builder.Limit("Mouth raisers relaxed by closed mouth", "MouthRaiserLower", "clamp(1 - MouthClosed, 0, 1)");
            }
            if (component.limitTongueByFunnel)
            {
                builder.Limit("Tongue pulled in by lip funnel", "TongueOut", $"clamp(1 - {Funnel} / 0.7, 0, 1)");
            }
            if (component.limitLipsByFunnel)
            {
                foreach (var key in new[] { "MouthUpperUpLeft", "MouthUpperUpRight", "MouthLowerDownLeft", "MouthLowerDownRight",
                             "LipPuckerUpperLeft", "LipPuckerUpperRight", "LipPuckerLowerLeft", "LipPuckerLowerRight" })
                {
                    builder.Limit("Lips relaxed by lip funnel", key, $"clamp(1 - {Funnel}, 0, 1)");
                }
            }
            if (component.limitJawByMouthSideways)
            {
                builder.Limit("Jaw sideways reduced by mouth sideways", "JawLeft", $"clamp(1 - abs({MouthRight}) / 2, 0, 1)");
                builder.Limit("Jaw sideways reduced by mouth sideways", "JawRight", $"clamp(1 - abs({MouthRight}) / 2, 0, 1)");
            }
            if (component.limitFrownByMouthSideways)
            {
                builder.Limit("Frown reduced by mouth sideways", "MouthFrownRight", $"clamp(1 - max({MouthRight}, 0) / 3, 0, 1)");
                builder.Limit("Frown reduced by mouth sideways", "MouthFrownLeft", $"clamp(1 - max({MouthLeft}, 0) / 3, 0, 1)");
            }
            if (component.limitLipSuckByMouthClosed)
            {
                foreach (var key in new[] { "LipSuckUpperLeft", "LipSuckUpperRight", "LipSuckLowerLeft", "LipSuckLowerRight" })
                {
                    builder.Limit("Lip suck relaxed by closed mouth", key, "clamp(1 - MouthClosed / 3, 0, 1)");
                }
            }
            return plan;
        }

        /// <summary>Takes the plan's shapes out of every set, so only their expressions drive them.</summary>
        public void UnmapShapes(IEnumerable<BlendshapesSet> sets)
        {
            var keys = new HashSet<int>(Shapes.SelectMany(s => s.Keys.Select(k => k.Key)));
            foreach (var set in sets.Where(s => s?.faceTrackingBlendshapeNames != null))
            {
                foreach (var key in keys.Where(k => k < set.faceTrackingBlendshapeNames.Length))
                {
                    set.faceTrackingBlendshapeNames[key] = "";
                }
            }
        }

        static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        static string Scale(string term, float amount) => Mathf.Approximately(amount, 1f) ? term : $"{term} * {Number(amount)}";

        sealed class Builder
        {
            readonly FTEmulationPlan plan;
            readonly List<BlendshapesSet> sets;
            readonly Dictionary<string, int> keysByName;

            public Builder(FTEmulationPlan plan, List<BlendshapesSet> sets)
            {
                this.plan = plan;
                this.sets = sets;
                keysByName = Enumerable.Range(0, FTKeys.KeyCount).ToDictionary(FTKeys.KeyName, k => k);
            }

            public void Emulate(string rule, string targetKey, string term)
            {
                var shape = ShapeFor(rule, targetKey);
                if (shape == null) return;
                shape.Emulations.Add(term);
                if (!shape.Rules.Contains(rule)) shape.Rules.Add(rule);
            }

            public void Limit(string rule, string targetKey, string factor)
            {
                var shape = ShapeFor(rule, targetKey);
                if (shape == null) return;
                if (!shape.Limits.Contains(factor)) shape.Limits.Add(factor);
                if (!shape.Rules.Contains(rule)) shape.Rules.Add(rule);
            }

            /// <summary>
            /// The shape the key drives, with every key that drives the same shape. Null, and the rule noted as
            /// skipped, when the key isn't mapped to a blendshape.
            /// </summary>
            Shape ShapeFor(string rule, string keyName)
            {
                if (!keysByName.TryGetValue(keyName, out var key))
                {
                    Skip(rule, $"{keyName} isn't a tracking key in this SDK.");
                    return null;
                }

                foreach (var set in sets)
                {
                    if (key >= set.faceTrackingBlendshapeNames.Length) continue;
                    var name = set.faceTrackingBlendshapeNames[key];
                    if (string.IsNullOrEmpty(name)) continue;

                    var shape = plan.Shapes.FirstOrDefault(s => s.Blendshape == name);
                    if (shape != null) return shape;

                    shape = new Shape { Blendshape = name, Set = set.setName };
                    for (var other = 0; other < set.faceTrackingBlendshapeNames.Length; other++)
                    {
                        if (set.faceTrackingBlendshapeNames[other] != name) continue;
                        var multiplier = set.faceTrackingBlendshapeMultipliers != null && other < set.faceTrackingBlendshapeMultipliers.Length
                            ? set.faceTrackingBlendshapeMultipliers[other]
                            : 1f;
                        shape.Keys.Add((other, multiplier));
                    }
                    plan.Shapes.Add(shape);
                    return shape;
                }

                Skip(rule, $"no blendshape for {keyName}.");
                return null;
            }

            void Skip(string rule, string reason)
            {
                var existing = plan.SkippedRules.FirstOrDefault(s => s.Rule == rule);
                if (existing == null) plan.SkippedRules.Add(new Skipped { Rule = rule, Reason = reason });
                else if (!existing.Reason.Contains(reason)) existing.Reason += " " + reason;
            }
        }
    }
}
