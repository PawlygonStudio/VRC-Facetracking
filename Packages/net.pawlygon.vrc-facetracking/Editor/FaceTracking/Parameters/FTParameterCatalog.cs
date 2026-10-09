using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Known OSC face tracking parameters and how to rebuild each from native tracking keys.
    /// Combined parameters follow the VRCFaceTracking v2 definitions.
    /// Directions and scales still need checking against the client.
    /// </summary>
    internal static class FTParameterCatalog
    {
        // FT/v2/X, v2/X and the OSCmooth proxy OSCm/Proxy/FT/v2/X, optionally renamed VF{n}_ by VRCFury
        static readonly Regex ParameterPattern = new Regex(@"^(?:VF\d+_)?(?:OSCm/Proxy/)?(?:FT/)?v2/([A-Za-z]+)$");

        static readonly Dictionary<string, string> Combined = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Eyes
            { "EyeLeftX", "EyeLookInLeft - EyeLookOutLeft" },
            { "EyeRightX", "EyeLookOutRight - EyeLookInRight" },
            { "EyeY", "(EyeLookUpLeft + EyeLookUpRight - EyeLookDownLeft - EyeLookDownRight) / 2" },
            { "EyeLidLeft", "(1 - EyeClosedLeft) * 0.75 + EyeWideLeft * 0.25" },
            { "EyeLidRight", "(1 - EyeClosedRight) * 0.75 + EyeWideRight * 0.25" },
            { "PupilDilation", "(EyeDilationLeft + EyeDilationRight - EyeConstrictLeft - EyeConstrictRight) / 4 + 0.5" },

            // Brows
            { "BrowExpressionLeft", "(BrowInnerUpLeft + BrowOuterUpLeft) / 2 - (BrowLowererLeft * 0.75 + BrowPinchLeft * 0.25)" },
            { "BrowExpressionRight", "(BrowInnerUpRight + BrowOuterUpRight) / 2 - (BrowLowererRight * 0.75 + BrowPinchRight * 0.25)" },

            // Cheeks and nose
            { "CheekPuffSuck", "(CheekPuffLeft + CheekPuffRight - CheekSuckLeft - CheekSuckRight) / 2" },
            { "CheekPuffSuckLeft", "CheekPuffLeft - CheekSuckLeft" },
            { "CheekPuffSuckRight", "CheekPuffRight - CheekSuckRight" },
            { "NoseSneer", "(NoseSneerLeft + NoseSneerRight) / 2" },

            // Jaw
            { "JawX", "JawRight - JawLeft" },

            // Lips
            { "LipFunnel", "(LipFunnelUpperLeft + LipFunnelUpperRight + LipFunnelLowerLeft + LipFunnelLowerRight) / 4" },
            { "LipPucker", "(LipPuckerUpperLeft + LipPuckerUpperRight + LipPuckerLowerLeft + LipPuckerLowerRight) / 4" },
            { "LipSuckUpper", "(LipSuckUpperLeft + LipSuckUpperRight) / 2" },
            { "LipSuckLower", "(LipSuckLowerLeft + LipSuckLowerRight) / 2" },

            // Mouth
            { "MouthUpperUp", "(MouthUpperUpLeft + MouthUpperUpRight) / 2" },
            { "MouthLowerDown", "(MouthLowerDownLeft + MouthLowerDownRight) / 2" },
            { "MouthX", "(MouthUpperRight + MouthLowerRight - MouthUpperLeft - MouthLowerLeft) / 2" },
            { "MouthPress", "(MouthPressLeft + MouthPressRight) / 2" },
            { "SmileFrownLeft", "MouthCornerPullLeft - MouthFrownLeft" },
            { "SmileFrownRight", "MouthCornerPullRight - MouthFrownRight" },
            { "MouthTightenerStretchLeft", "MouthTightenerLeft - MouthStretchLeft" },
            { "MouthTightenerStretchRight", "MouthTightenerRight - MouthStretchRight" },

            // Tongue
            { "TongueX", "TongueRight - TongueLeft" },
            { "TongueY", "TongueUp - TongueDown" },
        };

        static readonly HashSet<string> KeyNames =
            new HashSet<string>(Enumerable.Range(0, FTKeys.KeyCount).Select(FTKeys.KeyName), StringComparer.Ordinal);

        /// <summary>
        /// The expression for a known face tracking parameter, or null. Unified Expressions base
        /// shapes (FT/v2/JawOpen) map straight to their tracking key.
        /// </summary>
        public static string GetExpression(string parameterName)
        {
            var match = ParameterPattern.Match(parameterName);
            if (!match.Success) return null;

            var baseName = match.Groups[1].Value;
            if (Combined.TryGetValue(baseName, out var expression)) return expression;
            return KeyNames.Contains(baseName) ? baseName : null;
        }

        /// <summary>Tracking keys referenced by an expression, to check them against the basic key set.</summary>
        public static IEnumerable<string> GetReferencedKeys(string expression)
        {
            if (string.IsNullOrEmpty(expression)) return Enumerable.Empty<string>();
            return Regex.Matches(expression, @"[A-Za-z_][A-Za-z0-9_]*")
                .Cast<Match>()
                .Select(m => m.Value)
                .Where(KeyNames.Contains)
                .Distinct();
        }
    }
}
