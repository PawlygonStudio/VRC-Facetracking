using System.Collections.Generic;
using System.Linq;
using BlendshapesSet = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingBlendshapesSet;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// What a <see cref="PawlygonFTToggles"/> component adds. Resolved the same way for the build and the inspector.
    /// A blendshapes set has one weight for everything in it, so muting visemes alone needs them in a set of their own:
    /// each set's viseme mappings move to a new set, which an FX layer then fades.
    /// </summary>
    internal sealed class FTTogglesPlan
    {
        /// <summary>The menu toggle: saved, and only read by the owner's driver.</summary>
        public const string VisemesParameter = "PawlygonFT/Visemes";

        /// <summary>On while the toggle is off and face tracking is active. Set by the owner and synced.</summary>
        public const string VisemesMutedParameter = "PawlygonFT/VisemesMuted";

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

        /// <summary>The viseme sets the toggle fades. Empty when there's no toggle.</summary>
        public readonly List<VisemeSet> VisemeSets = new List<VisemeSet>();

        /// <summary>Why the Visemes toggle isn't added although it's on, or null.</summary>
        public string VisemesSkipped;

        public bool AddsVisemesToggle => VisemeSets.Count > 0;

        /// <summary>Synced bits the component's parameters use.</summary>
        public int SyncedBits => AddsVisemesToggle ? 1 : 0;

        /// <param name="sets">The avatar's blendshapes sets, as they are before the visemes are split off.</param>
        /// <param name="faceTrackingLipSync">Whether lip sync is (or will be) the face tracking viseme mode.</param>
        /// <param name="hasMerger">Whether VRCFury or Modular Avatar is there to merge the layers, parameters and menu.</param>
        public static FTTogglesPlan Resolve(PawlygonFTToggles component, IEnumerable<BlendshapesSet> sets, bool faceTrackingLipSync, bool hasMerger)
        {
            var plan = new FTTogglesPlan();
            if (!component.visemesToggle) return plan;

            var setList = sets.Where(s => s != null).ToList();
            var withVisemes = setList.Where(s => CountVisemes(s) > 0).ToList();
            if (!faceTrackingLipSync || withVisemes.Count == 0)
            {
                plan.VisemesSkipped = "Lip sync doesn't use face tracking visemes, so there's nothing for the Visemes toggle to mute.";
                return plan;
            }
            if (!hasMerger)
            {
                plan.VisemesSkipped = "The Visemes toggle needs VRCFury or Modular Avatar. " + FTMerger.MissingMessage;
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
