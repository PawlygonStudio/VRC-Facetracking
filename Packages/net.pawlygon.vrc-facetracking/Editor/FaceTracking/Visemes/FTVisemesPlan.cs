using System.Collections.Generic;
using System.Linq;
using BlendshapesSet = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingBlendshapesSet;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// What Face Tracking's Visemes menu adds. Resolved the same way for the build and the inspector. A blendshapes set has one weight for everything in it, so turning visemes down alone needs them in a
    /// set of their own: each set's viseme mappings move to a new set, whose weight an FX layer then changes.
    /// </summary>
    internal sealed class FTVisemesPlan
    {
        /// <summary>The level picked in the Visemes menu, as an <see cref="FTVisemesLevel"/>: saved, and only read by the owner's driver.</summary>
        public const string VisemesLevelParameter = "PawlygonFT/Visemes";

        /// <summary>
        /// The level in effect, as two bits: the picked one while face tracking is active, Full otherwise. Set by the
        /// owner and synced. Two bools and not an int, since not every tool that merges parameters keeps an int's
        /// reduced bit width.
        /// </summary>
        public const string VisemesBitAParameter = "PawlygonFT/VisemesA";
        public const string VisemesBitBParameter = "PawlygonFT/VisemesB";

        public const string VisemesMenuName = "Visemes";

        /// <summary>The levels, in menu order.</summary>
        public static readonly FTVisemesLevel[] Levels = { FTVisemesLevel.Full, FTVisemesLevel.Medium, FTVisemesLevel.Low, FTVisemesLevel.Off };

        /// <summary>How much of a viseme set's weight a level keeps.</summary>
        public static float LevelWeight(FTVisemesLevel level)
        {
            switch (level)
            {
                case FTVisemesLevel.Medium: return 2f / 3f;
                case FTVisemesLevel.Low: return 1f / 3f;
                case FTVisemesLevel.Off: return 0f;
                default: return 1f;
            }
        }

        public static bool BitA(FTVisemesLevel level) => ((int)level & 1) != 0;
        public static bool BitB(FTVisemesLevel level) => ((int)level & 2) != 0;

        public const string VisemeSetName = "Pawlygon Visemes";

        internal sealed class VisemeSet
        {
            /// <summary>The set the visemes come from.</summary>
            public string Source;
            /// <summary>The new set that gets them.</summary>
            public string Name;
            public float Weight;
            public int Visemes;
        }

        /// <summary>The viseme sets the menu turns down. Empty when there's no menu.</summary>
        public readonly List<VisemeSet> VisemeSets = new List<VisemeSet>();

        /// <summary>Why the Visemes menu isn't added although it's on, or null.</summary>
        public string VisemesSkipped;

        /// <summary>It's skipped because the avatar has no face tracking visemes, which isn't worth a warning.</summary>
        public bool NoVisemes;

        public bool AddsVisemesMenu => VisemeSets.Count > 0;

        /// <summary>Synced bits the Visemes menu's parameters use.</summary>
        public int SyncedBits => AddsVisemesMenu ? 2 : 0;

        /// <param name="sets">The avatar's blendshapes sets, as they are before the visemes are split off.</param>
        /// <param name="faceTrackingLipSync">Whether lip sync is (or will be) the face tracking viseme mode.</param>
        /// <param name="hasMerger">Whether VRCFury or Modular Avatar is there to merge the layers, parameters and menu.</param>
        public static FTVisemesPlan Resolve(bool visemesMenu, IEnumerable<BlendshapesSet> sets, bool faceTrackingLipSync, bool hasMerger)
        {
            var plan = new FTVisemesPlan();
            if (!visemesMenu) return plan;

            var setList = sets.Where(s => s != null).ToList();
            var withVisemes = setList.Where(s => CountVisemes(s) > 0).ToList();
            if (!faceTrackingLipSync || withVisemes.Count == 0)
            {
                plan.VisemesSkipped = "Lip sync doesn't use face tracking visemes, so there's nothing for the Visemes menu to turn down.";
                plan.NoVisemes = true;
                return plan;
            }
            if (!hasMerger)
            {
                plan.VisemesSkipped = "The Visemes menu needs VRCFury or Modular Avatar. " + FTMerger.MissingMessage;
                return plan;
            }

            var taken = new HashSet<string>(setList.Select(s => s.setName ?? ""));
            foreach (var set in withVisemes)
            {
                var name = set.setName == PawlygonFaceTracking.BaseSetName ? VisemeSetName : $"{set.setName} Visemes";
                var unique = name;
                for (var i = 2; !taken.Add(unique); i++) unique = $"{name} {i}";
                plan.VisemeSets.Add(new VisemeSet { Source = set.setName, Name = unique, Weight = set.setWeight, Visemes = CountVisemes(set) });
            }
            return plan;
        }

        /// <summary>
        /// Moves each planned set's viseme mappings into its new set, added after the existing ones. The key mappings stay
        /// where they are.
        /// </summary>
        public void SplitVisemes(List<BlendshapesSet> sets)
        {
            foreach (var planned in VisemeSets)
            {
                var source = sets.FirstOrDefault(s => s != null && s.setName == planned.Source && CountVisemes(s) > 0);
                if (source == null) continue;

                var visemes = FTBuildContext.CreateSet(planned.Name, planned.Weight);
                for (var i = 0; i < FTKeys.VisemeCount && i < source.visemeBlendshapeNames.Length; i++)
                {
                    visemes.visemeBlendshapeNames[i] = source.visemeBlendshapeNames[i] ?? "";
                    source.visemeBlendshapeNames[i] = "";
                    if (source.visemeBlendshapeMultipliers == null || i >= source.visemeBlendshapeMultipliers.Length) continue;
                    visemes.visemeBlendshapeMultipliers[i] = source.visemeBlendshapeMultipliers[i];
                    source.visemeBlendshapeMultipliers[i] = 1f;
                }
                sets.Add(visemes);
            }
        }

        static int CountVisemes(BlendshapesSet set)
        {
            return set.visemeBlendshapeNames?.Count(n => !string.IsNullOrEmpty(n)) ?? 0;
        }
    }
}
