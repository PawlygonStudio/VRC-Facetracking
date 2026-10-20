using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Quick checks of a custom expression while it's typed. The SDK validator still runs at build
    /// time and has the final word.
    /// </summary>
    internal static class FTExpressionCheck
    {
        static readonly HashSet<string> Functions = new HashSet<string>(StringComparer.Ordinal) { "abs", "min", "max", "clamp" };
        static readonly Regex Token = new Regex(@"[A-Za-z_][A-Za-z0-9_]*|\d+(\.\d*)?|\.\d+|[()+\-*/,]|\S");

        static HashSet<string> inputs;

        static HashSet<string> Inputs
        {
            get
            {
                if (inputs != null) return inputs;
                inputs = new HashSet<string>(Enumerable.Range(0, FTKeys.KeyCount).Select(FTKeys.KeyName), StringComparer.Ordinal) { "Voice" };
                foreach (var viseme in VisemeInputs) inputs.Add(viseme);
                return inputs;
            }
        }

        internal static IEnumerable<string> VisemeInputs => Enumerable.Range(0, FTKeys.VisemeCount)
            .Select(v => VRCAvatarDescriptor.GetFaceTrackingVisemeInputName((VRC_AvatarDescriptor.Viseme)v));

        /// <summary>Null when the expression looks fine, otherwise a short problem description.</summary>
        public static string Validate(string expression, bool useAllTrackingKeys, out bool isWarning)
        {
            isWarning = false;
            if (string.IsNullOrWhiteSpace(expression)) return "Empty expression.";

            var depth = 0;
            foreach (Match match in Token.Matches(expression))
            {
                var token = match.Value;
                if (token == "(") depth++;
                else if (token == ")" && --depth < 0) return "Unbalanced parentheses.";
                else if (char.IsLetter(token[0]) || token[0] == '_')
                {
                    if (Functions.Contains(token)) continue;
                    if (!Inputs.Contains(token)) return $"Unknown input '{token}'. Names are case-sensitive.";
                }
                else if (!char.IsDigit(token[0]) && !(token[0] == '.' && token.Length > 1) && "+-*/,".IndexOf(token[0]) < 0)
                {
                    return $"Unexpected '{token}'.";
                }
            }
            if (depth != 0) return "Unbalanced parentheses.";

            if (!useAllTrackingKeys)
            {
                var unavailable = FTParameterCatalog.GetReferencedKeys(expression)
                    .FirstOrDefault(k => !FTKeys.IsBasic(Enumerable.Range(0, FTKeys.KeyCount).First(i => FTKeys.KeyName(i) == k)));
                if (unavailable != null)
                {
                    isWarning = true;
                    return $"'{unavailable}' needs Use All Tracking Keys.";
                }
            }
            return null;
        }

        /// <summary>
        /// The functions and operators an expression can use, as templates: <paramref name="onPick"/> gets the text to
        /// insert and where the cursor goes in it.
        /// </summary>
        public static GenericMenu FunctionMenu(Action<string, int> onPick)
        {
            var menu = new GenericMenu();
            void Add(string label, string text, int cursor) => menu.AddItem(new UnityEngine.GUIContent(label), false, () => onPick(text, cursor));

            Add("abs(x)    Absolute value", "abs()", 4);
            Add("min(a, b)    Smaller of the two", "min(, )", 4);
            Add("max(a, b)    Larger of the two", "max(, )", 4);
            Add("clamp(x, min, max)    Keeps x between min and max", "clamp(, 0, 1)", 6);
            menu.AddSeparator("");
            Add("( )    Parentheses", "()", 1);
            Add("+    Add", " + ", 3);
            Add("-    Subtract", " - ", 3);
            Add("*    Multiply", " * ", 3);
            Add("/    Divide", " / ", 3);
            return menu;
        }

        internal static string Region(string key)
        {
            if (key.StartsWith("EyeLook")) return "Eye Look";
            if (key.StartsWith("Eye")) return "Eyes";
            if (key.StartsWith("Brow")) return "Brows";
            if (key.StartsWith("Nose") || key.StartsWith("Nasal")) return "Nose";
            if (key.StartsWith("Cheek")) return "Cheeks";
            if (key.StartsWith("Jaw") || key == "MouthClosed") return "Jaw";
            if (key.StartsWith("Lip")) return "Lips";
            if (key.StartsWith("Mouth")) return "Mouth";
            if (key.StartsWith("Tongue")) return "Tongue";
            return "Throat & Neck";
        }
    }
}
