using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Pawlygon.FaceTracking.Editor
{
    [CustomEditor(typeof(PawlygonFTToggles))]
    internal sealed class PawlygonFTTogglesEditor : FTComponentEditor<PawlygonFTToggles>
    {
        readonly FTDynamic visemesBadges = new FTDynamic();
        readonly FTDynamic visemesParameters = new FTDynamic();
        VisualElement visemesBody;
        Label visemesNote;

        protected override string Title => "Face Tracking Toggles";
        protected override string Subtitle => "Menu toggles for face tracking.";

        protected override string Details =>
            "Adds a Visemes toggle to the avatar's menu. While it's off, visemes are muted whenever face tracking is active, so " +
            "they don't play on top of the tracked mouth, and they come back on their own without a tracker. When the avatar is " +
            "built, the visemes get their own blendshapes set, and an animator layer merged by VRCFury or Modular Avatar fades it. " +
            "The avatar in your scene isn't changed.";

        protected override void BuildBody(VisualElement body)
        {
            // Same layout as the Guards cards: badges and the switch in the header, the settings greyed out while it's off
            var visemes = FTUI.Card(body, "Visemes", out var visemesExtras);
            visemesExtras.Add(visemesBadges.Element);
            visemesBadges.Element.style.flexDirection = FlexDirection.Row;

            var toggle = new Toggle { tooltip = "Add the Visemes toggle to the menu." };
            toggle.AddToClassList("ft-toggle");
            toggle.BindProperty(serializedObject.FindProperty(nameof(PawlygonFTToggles.visemesToggle)));
            visemesExtras.Add(toggle);

            visemesBody = new VisualElement();
            visemesNote = FTUI.Text("", "ft-muted", "ft-card__note");
            visemesBody.Add(visemesNote);
            visemesBody.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFTToggles.visemesOnByDefault)), "On By Default"));
            visemesBody.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFTToggles.menuName)), "In Menu"));
            visemesBody.Add(visemesParameters.Element);
            visemes.Add(visemesBody);
        }

        protected override void Refresh(VRCAvatarDescriptor avatar, FTIssues issues)
        {
            var other = FTAvatarUtils.GetBuildComponents<PawlygonFTToggles>(avatar.gameObject).FirstOrDefault(c => c != Component);
            if (other != null)
            {
                issues.Error("Another Pawlygon Face Tracking Toggles is on this avatar.",
                    $"Only one is allowed per avatar. The other one is on '{FTAvatarUtils.GetPath(other.transform, avatar.transform)}'.",
                    ("Select", () => Select(other.gameObject)));
            }

            var plan = ResolvePlan(avatar, issues);
            if (plan.VisemesSkipped != null) issues.Warning("The Visemes toggle won't be added.", plan.VisemesSkipped);

            visemesBody.SetEnabled(Component.visemesToggle);
            visemesNote.text = Note();

            // The sets are only named when there's more than one: a single one is just the avatar's visemes
            var count = plan.VisemeSets.Sum(s => s.Visemes);
            var sources = plan.VisemeSets.Select(s => s.Source).ToList();
            var bits = plan.SyncedBits;
            visemesBadges.Update($"{Component.visemesToggle}|{plan.AddsVisemesToggle}|{count}|{bits}|{string.Join(",", sources)}", container =>
            {
                if (!Component.visemesToggle)
                {
                    container.Add(FTUI.Badge("off", FTKind.Muted));
                    return;
                }
                if (!plan.AddsVisemesToggle)
                {
                    container.Add(FTUI.Badge("not added", FTKind.Warning, plan.VisemesSkipped));
                    return;
                }

                container.Add(FTUI.Badge($"{count} {(count == 1 ? "viseme" : "visemes")}", FTKind.Muted,
                    "The avatar's visemes, which the toggle mutes. When the avatar is built they move to a blendshapes set of their own, " +
                    "so they can be faded without the rest of the face." +
                    (sources.Count > 1 ? $"\nFrom {sources.Count} blendshapes sets: {string.Join(", ", sources)}." : "")));
                container.Add(FTUI.Badge($"{bits} synced {(bits == 1 ? "bit" : "bits")}", FTKind.Ok,
                    "Whether visemes are muted is synced, so everyone sees the same. The toggle itself isn't."));
            });

            visemesParameters.Update(plan.AddsVisemesToggle.ToString(), container =>
            {
                if (!plan.AddsVisemesToggle) return;

                container.Add(FTUI.Text("Parameters", "ft-group-title"));
                container.Add(FTUI.Chips(new[]
                {
                    (FTTogglesPlan.VisemesParameter, "The menu toggle. Saved, not synced."),
                    (FTTogglesPlan.VisemesMutedParameter,
                        "On while the toggle is off and face tracking is active. Set on your side and synced, 1 bit."),
                }));
            });
        }

        /// <summary>What visemes do with these settings, and where the toggle that changes it is.</summary>
        string Note()
        {
            var menuName = Component.menuName?.Trim() ?? "";
            var menu = menuName.Length == 0 ? "the main menu" : $"the {menuName} menu";
            return (Component.visemesOnByDefault
                       ? $"With face tracking active, visemes play until the Visemes toggle in {menu} is turned off."
                       : $"With face tracking active, visemes are off until the Visemes toggle in {menu} is turned on.") +
                   " Without face tracking they always play.";
        }

        /// <summary>The same plan the build resolves: from the setup component's sets, or the ones already on the descriptor.</summary>
        FTTogglesPlan ResolvePlan(VRCAvatarDescriptor avatar, FTIssues issues)
        {
            var hasMerger = FTMerger.Find() != null;
            var setup = FTAvatarUtils.GetBuildComponents<PawlygonFaceTracking>(avatar.gameObject).FirstOrDefault();
            if (setup != null)
            {
                var setupPlan = FaceTrackingSetupPlan.Resolve(setup, avatar);
                var sets = setupPlan.Set != null ? new[] { setupPlan.Set } : new VRCAvatarDescriptor.FaceTrackingBlendshapesSet[0];
                return FTTogglesPlan.Resolve(Component, sets, setupPlan.UseFaceTrackingLipSync, hasMerger);
            }
            if (avatar.enableFaceTracking)
            {
                return FTTogglesPlan.Resolve(Component,
                    avatar.faceTrackingBlendshapesSetList ?? new List<VRCAvatarDescriptor.FaceTrackingBlendshapesSet>(),
                    avatar.lipSync == VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShapeFaceTracking, hasMerger);
            }

            issues.Error("Face tracking isn't enabled on this avatar.",
                "Add a Pawlygon Face Tracking component, or enable face tracking on the avatar descriptor.");
            return new FTTogglesPlan();
        }
    }
}
