using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// A tongue made of two step blendshapes, like SRanipal's Tongue_LongStep1 and Tongue_LongStep2. Native face
    /// tracking has a single TongueOut key and a blendshape set maps it to one blendshape, so the steps are driven by
    /// expressions instead: the first step over the first half of TongueOut, the second over the other half.
    /// </summary>
    internal sealed class FTTongueSteps
    {
        public const string Key = "TongueOut";

        // Any name with "tongue" (or the common "tounge") ending in Step1 / Step2, ignoring case and separators
        static readonly Regex StepName = new Regex(@"tongue.*step([12])$");

        public string Step1;
        public string Step2;

        public IEnumerable<string> Names => new[] { Step1, Step2 };

        /// <summary>The first Step1 / Step2 pair among the blendshapes, or null without both.</summary>
        public static FTTongueSteps Find(IEnumerable<string> blendshapes)
        {
            string step1 = null, step2 = null;
            foreach (var name in blendshapes)
            {
                var match = StepName.Match(Normalize(name));
                if (!match.Success) continue;
                if (match.Groups[1].Value == "1") step1 = step1 ?? name;
                else step2 = step2 ?? name;
            }
            return step1 != null && step2 != null ? new FTTongueSteps { Step1 = step1, Step2 = step2 } : null;
        }

        public bool Contains(string blendshape) => blendshape == Step1 || blendshape == Step2;

        /// <summary>The value of a step, 0 to 1, for a tongue out <paramref name="value"/> expression.</summary>
        public static string Remap(int step, string value)
        {
            var wrapped = value.Contains(' ') ? $"({value})" : value;
            return step == 1 ? $"clamp({wrapped} * 2, 0, 1)" : $"clamp({wrapped} * 2 - 1, 0, 1)";
        }

        /// <summary>Each step blendshape with its expression.</summary>
        public IEnumerable<(string Blendshape, string Expression)> Expressions(string value = Key)
        {
            yield return (Step1, Remap(1, value));
            yield return (Step2, Remap(2, value));
        }

        static string Normalize(string name)
        {
            var lower = new string((name ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
            return lower.Replace("tounge", "tongue");
        }
    }
}
