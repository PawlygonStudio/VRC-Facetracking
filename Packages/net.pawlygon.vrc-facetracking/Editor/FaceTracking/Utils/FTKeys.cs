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
        static readonly MethodInfo FindVisemeMethod;
        static readonly MethodInfo NamesForKeyMethod;
        static Dictionary<string, List<int>> keysByName;

        static FTKeys()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var autoFill = Type.GetType("VRC.SDK3A.Editor.FaceTrackingAutoFill, VRC.SDK3A.Editor");
            FindKeyMethod = autoFill?.GetMethod("FindFaceTrackingBlendshape", flags);
            FindVisemeMethod = autoFill?.GetMethod("FindVisemeBlendshape", flags);
            NamesForKeyMethod = autoFill?.GetMethod("GetNamesForFaceTrackingKey", flags);

            if (FindKeyMethod == null || FindVisemeMethod == null)
            {
                Debug.LogWarning("[Pawlygon Face Tracking] The VRChat SDK face tracking auto-fill wasn't found. " +
                                 "Blendshapes will only be matched by their exact Unified Expressions names.");
            }
        }

        public static string KeyName(int key) => ((VRCAvatarDescriptor.FaceTrackingKey)key).ToString();

        public static string VisemeName(int viseme) => ((VRC_AvatarDescriptor.Viseme)viseme).ToString();

        public static bool IsBasic(int key) => VRCAvatarDescriptor.IsBasicFaceTrackingKey(key);

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

        /// <summary>Index into <paramref name="blendshapes"/> of the best match for a viseme, or -1.</summary>
        public static int FindViseme(string[] blendshapes, int viseme)
        {
            var name = VisemeName(viseme).ToLowerInvariant();
            if (FindVisemeMethod != null)
            {
                return (int)FindVisemeMethod.Invoke(null, new object[] { blendshapes, name, null });
            }
            return Array.FindIndex(blendshapes, b => string.Equals(b, "vrc.v_" + name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
