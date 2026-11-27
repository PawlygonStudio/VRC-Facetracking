using System.Collections.Generic;
using System.Linq;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// A tongue morph blendshape: the correction for the tongue pointing two ways at once, like up and to the right.
    /// Face tracking has no key for it, so it's driven by its two direction keys multiplied: nothing while the tongue
    /// points one way only, and fully on when it points fully both ways.
    /// </summary>
    internal sealed class FTTongueMorph
    {
        /// <summary>The blendshape, as it's named on the avatar.</summary>
        public string Blendshape;

        /// <summary>The up or down key.</summary>
        public string Vertical;

        /// <summary>The left or right key.</summary>
        public string Horizontal;

        /// <summary>Why it isn't driven, or null when it is.</summary>
        public string Skipped;

        /// <summary>What drives it, as the inspector shows it.</summary>
        public string Label => $"{Vertical} × {Horizontal}";

        /// <summary>
        /// The two keys multiplied, each with the multiplier it has in the blendshapes set, since the morph isn't
        /// mapped in one.
        /// </summary>
        public string Expression(float verticalMultiplier, float horizontalMultiplier)
        {
            return $"{FTKeys.Scaled(Vertical, verticalMultiplier)} * {FTKeys.Scaled(Horizontal, horizontalMultiplier)}";
        }
    }

    internal static class FTTongueMorphs
    {
        static readonly (string vertical, string horizontal)[] Pairs =
        {
            ("TongueUp", "TongueRight"), ("TongueUp", "TongueLeft"), ("TongueDown", "TongueRight"), ("TongueDown", "TongueLeft"),
        };

        /// <summary>
        /// The tongue morphs among the blendshapes, by name: Unified Expressions' TongueUpRightMorph or SRanipal's
        /// Tongue_UpRight_Morph, ignoring case and separators. The first match of each wins.
        /// </summary>
        public static List<FTTongueMorph> Find(IEnumerable<string> blendshapes)
        {
            var byName = new Dictionary<string, string>();
            foreach (var name in blendshapes)
            {
                var normalized = Normalize(name);
                if (!byName.ContainsKey(normalized)) byName[normalized] = name;
            }

            var found = new List<FTTongueMorph>();
            foreach (var (vertical, horizontal) in Pairs)
            {
                // TongueUp + TongueRight is TongueUpRightMorph
                var wanted = Normalize(vertical + horizontal.Substring("Tongue".Length) + "Morph");
                if (byName.TryGetValue(wanted, out var blendshape))
                {
                    found.Add(new FTTongueMorph { Blendshape = blendshape, Vertical = vertical, Horizontal = horizontal });
                }
            }
            return found;
        }

        static string Normalize(string name)
        {
            var lower = new string((name ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            return lower.Replace("tounge", "tongue");
        }
    }
}
