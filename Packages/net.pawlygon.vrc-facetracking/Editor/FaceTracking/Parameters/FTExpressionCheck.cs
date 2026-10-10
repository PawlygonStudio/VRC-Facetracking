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

        static IEnumerable<string> VisemeInputs => Enumerable.Range(0, FTKeys.VisemeCount)
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

        /// <summary>A menu of every input, grouped by face region, that calls <paramref name="onPick"/> with the name.</summary>
        public static GenericMenu InputMenu(Action<string> onPick)
        {
            var menu = new GenericMenu();
            for (var key = 0; key < FTKeys.KeyCount; key++)
            {
                var name = FTKeys.KeyName(key);
                menu.AddItem(new UnityEngine.GUIContent($"{Region(name)}/{name}"), false, () => onPick(name));
            }
            menu.AddSeparator("");
            menu.AddItem(new UnityEngine.GUIContent("Voice"), false, () => onPick("Voice"));
            foreach (var viseme in VisemeInputs)
            {
                menu.AddItem(new UnityEngine.GUIContent("Visemes/" + viseme), false, () => onPick(viseme));
            }
            return menu;
        }

        static string Region(string key)
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
