using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Tracking key and viseme helpers. Blendshape matching goes through the SDK's own auto-fill
    /// (internal, so called by reflection) to stay in sync with its alias tables. If the SDK ever
    /// moves it, matching falls back to exact Unified Expressions names.
    /// </summary>
    internal static class FTKeys
    {
        public static readonly int KeyCount = (int)VRCAvatarDescriptor.FaceTrackingKey.Max;
        public static readonly int VisemeCount = (int)VRC_AvatarDescriptor.Viseme.Count;

        static readonly MethodInfo FindKeyMethod;
        static readonly MethodInfo NamesForKeyMethod;
        static Dictionary<string, List<int>> keysByName;

        static FTKeys()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var autoFill = Type.GetType("VRC.SDK3A.Editor.FaceTrackingAutoFill, VRC.SDK3A.Editor");
            FindKeyMethod = autoFill?.GetMethod("FindFaceTrackingBlendshape", flags);
            NamesForKeyMethod = autoFill?.GetMethod("GetNamesForFaceTrackingKey", flags);

            if (FindKeyMethod == null)
            {
                Debug.LogWarning("[Pawlygon Face Tracking] The VRChat SDK face tracking auto-fill wasn't found. " +
                                 "Blendshapes will only be matched by their exact Unified Expressions names.");
            }
        }

        public static string KeyName(int key) => ((VRCAvatarDescriptor.FaceTrackingKey)key).ToString();

        /// <summary>The tracking key with this name, or -1.</summary>
        public static int KeyByName(string name)
        {
            if (string.IsNullOrEmpty(name) || !Enum.TryParse(name, out VRCAvatarDescriptor.FaceTrackingKey key)) return -1;
            var index = (int)key;
            return index >= 0 && index < KeyCount ? index : -1;
        }

        public static string VisemeName(int viseme) => ((VRC_AvatarDescriptor.Viseme)viseme).ToString();

        public static bool IsBasic(int key) => VRCAvatarDescriptor.IsBasicFaceTrackingKey(key);

        /// <summary>The pupil keys: EyeDilation and EyeConstrict, left and right.</summary>
        public static bool IsPupilKey(int key)
        {
            var name = KeyName(key);
            return name.StartsWith("EyeDilation", StringComparison.Ordinal) || name.StartsWith("EyeConstrict", StringComparison.Ordinal);
        }

        /// <summary>The EyeDilation keys (as opposed to EyeConstrict).</summary>
        public static bool IsDilationKey(int key) => KeyName(key).StartsWith("EyeDilation", StringComparison.Ordinal);

        /// <summary>Index into <paramref name="blendshapes"/> of the best match for a tracking key, or -1.</summary>
        public static int FindKey(string[] blendshapes, int key)
        {
            if (FindKeyMethod != null)
            {
                return (int)FindKeyMethod.Invoke(null, new object[] { blendshapes, (VRCAvatarDescriptor.FaceTrackingKey)key, null });
            }
            return Array.IndexOf(blendshapes, KeyName(key));
        }

        /// <summary>
        /// Tracking keys a blendshape name stands for, from the SDK's alias table (case-insensitive):
        /// a key's own name, its ARKit / SRanipal aliases, and combined names like LipFunnel, which cover
        /// several keys. Without the SDK table, only exact key names match.
        /// </summary>
        public static IReadOnlyList<int> KeysForName(string name)
        {
            if (keysByName == null)
            {
                keysByName = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                for (var key = 0; key < KeyCount; key++)
                {
                    var names = NamesForKeyMethod != null
                        ? (IEnumerable<string>)NamesForKeyMethod.Invoke(null, new object[] { (VRCAvatarDescriptor.FaceTrackingKey)key })
                        : null;
                    foreach (var alias in (names ?? new[] { KeyName(key) }).Append(KeyName(key)).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (!keysByName.TryGetValue(alias, out var keys)) keysByName[alias] = keys = new List<int>();
                        if (!keys.Contains(key)) keys.Add(key);
                    }
                }
            }
            return keysByName.TryGetValue(name, out var found) ? found : (IReadOnlyList<int>)Array.Empty<int>();
        }

        /// <summary>
        /// Other names the SDK matches to the key (ARKit, SRanipal and older Unified Expressions names), without the
        /// key's own name and case-insensitive duplicates.
        /// </summary>
        public static List<string> AliasesFor(int key)
        {
            var names = NamesForKeyMethod != null
                ? (IEnumerable<string>)NamesForKeyMethod.Invoke(null, new object[] { (VRCAvatarDescriptor.FaceTrackingKey)key }) ?? Enumerable.Empty<string>()
                : Enumerable.Empty<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { KeyName(key) };
            return names.Where(n => !string.IsNullOrWhiteSpace(n) && seen.Add(n)).ToList();
        }

        /// <summary>
        /// A blendshape that looks like a misspelling of the key's name or one of its aliases (a letter or two off, or
        /// two letters swapped), for keys nothing matched. Blendshapes in <paramref name="used"/> are skipped.
        /// </summary>
        public static string SuggestFor(int key, string[] blendshapes, ICollection<string> used)
        {
            var names = NamesForKeyMethod != null
                ? ((IEnumerable<string>)NamesForKeyMethod.Invoke(null, new object[] { (VRCAvatarDescriptor.FaceTrackingKey)key }) ?? Enumerable.Empty<string>())
                : Enumerable.Empty<string>();
            var targets = names.Append(KeyName(key)).Select(n => n.ToLowerInvariant()).Distinct().ToList();

            string best = null;
            var bestDistance = int.MaxValue;
            foreach (var blendshape in blendshapes)
            {
                if (used.Contains(blendshape)) continue;
                var candidate = blendshape.ToLowerInvariant();
                foreach (var target in targets)
                {
                    // One edit for short names, two for longer ones
                    var allowed = target.Length >= 8 ? 2 : 1;
                    if (Math.Abs(candidate.Length - target.Length) > allowed) continue;
                    var distance = Distance(candidate, target);
                    if (distance > 0 && distance <= allowed && distance < bestDistance)
                    {
                        best = blendshape;
                        bestDistance = distance;
                    }
                }
            }
            return best;
        }

        /// <summary>Edits between two names, counting two swapped neighbouring letters as one.</summary>
        static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (var j = 0; j <= b.Length; j++) d[0, j] = j;
            for (var i = 1; i <= a.Length; i++)
            {
                for (var j = 1; j <= b.Length; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
            return d[a.Length, b.Length];
        }

        /// <summary>
        /// Index into <paramref name="blendshapes"/> of the best match for a viseme, or -1. Only exact viseme
        /// names count (vrc.v_aa, v_aa, aa...). The SDK's auto-fill also matches substrings, which maps single
        /// letter visemes like "e" to unrelated shapes on avatars without visemes.
        /// </summary>
        public static int FindViseme(string[] blendshapes, int viseme)
        {
            var name = VisemeName(viseme);
            foreach (var candidate in new[] { "vrc.v_" + name, "v_" + name, "vrc_v_" + name, "viseme_" + name, name })
            {
                var index = Array.FindIndex(blendshapes, b => string.Equals(b, candidate, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) return index;
            }
            return -1;
        }
    }
}
