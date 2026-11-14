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
        readonly FTDynamic visemesBadge = new FTDynamic();
        readonly FTDynamic visemesResult = new FTDynamic();
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
            var visemes = FTUI.Card(body, "Visemes", out var visemesExtras);
            visemesExtras.Add(visemesBadge.Element);

            var enabled = serializedObject.FindProperty(nameof(PawlygonFTToggles.visemesToggle));
            var toggle = new Toggle { tooltip = "Enabled" };
            toggle.AddToClassList("ft-toggle");
            toggle.BindProperty(enabled);
            visemesExtras.Add(toggle);

            visemesBody = new VisualElement();
            visemesNote = FTUI.Text("", "ft-muted", "ft-card__note");
            visemesBody.Add(visemesNote);
            visemesBody.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFTToggles.visemesOnByDefault)), "On By Default"));
            visemesBody.Add(visemesResult.Element);
            visemes.Add(visemesBody);

            var menu = FTUI.Card(body, "Menu", out _);
            menu.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFTToggles.menuName)), "In Menu"));
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

            visemesBody.SetEnabled(Component.visemesToggle);
            visemesNote.text = "The toggle starts " + (Component.visemesOnByDefault ? "on" : "off") + ". While it's off, visemes are muted while UserFaceTracking is on.";

            var plan = ResolvePlan(avatar, issues);
            if (plan.VisemesSkipped != null) issues.Warning("The Visemes toggle won't be added.", plan.VisemesSkipped);

            var kind = plan.AddsVisemesToggle ? FTKind.Ok : FTKind.Muted;
            var badge = !Component.visemesToggle ? "off" : plan.AddsVisemesToggle ? "1 synced bit" : "not added";
            visemesBadge.Update(badge, e => e.Add(FTUI.Badge(badge, kind)));

            var key = string.Join("|", plan.VisemeSets.Select(s => $"{s.Source}>{s.Name}:{s.Visemes}")) + Component.menuName + Component.visemesToggle;
            visemesResult.Update(key, container =>
            {
                if (!plan.AddsVisemesToggle) return;

                var menuName = Component.menuName?.Trim() ?? "";
                container.Add(FTUI.Row("Menu toggle", menuName.Length == 0 ? "Visemes, in the main menu" : $"Visemes, in {menuName}",
                    $"{FTTogglesPlan.VisemesParameter}: saved, not synced."));
                container.Add(FTUI.Row("Synced", FTTogglesPlan.VisemesMutedParameter,
                    "Set on your side while the toggle is off and face tracking is on, and synced so others see the same."));
                foreach (var set in plan.VisemeSets)
                {
                    container.Add(FTUI.Row(set.Name, $"{set.Visemes} visemes from {set.Source}",
                        "The visemes move to a blendshapes set of their own, so they can be faded without the rest of the face."));
                }
            });
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
