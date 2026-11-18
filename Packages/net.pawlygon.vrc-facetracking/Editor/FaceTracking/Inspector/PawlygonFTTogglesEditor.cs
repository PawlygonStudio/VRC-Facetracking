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
        readonly FTDynamic pupilBadges = new FTDynamic();
        readonly FTDynamic pupilParameters = new FTDynamic();
        VisualElement visemesBody;
        VisualElement pupilBody;
        Label visemesNote;
        Label pupilNote;

        protected override string Title => "Face Tracking Toggles";
        protected override string Subtitle => "Menu controls for face tracking.";

        protected override string Details =>
            "Visemes: adds a Visemes submenu with Full, Lively, Subtle and Off. The level applies while face tracking is active, " +
            "so visemes don't play at full strength on top of the tracked mouth, and without a tracker they always play in full. " +
            "Pupils: adds toggles for the headset's pupil tracking and for Pupil Emulation, which play together when both are on. " +
            "When the avatar is built, the visemes get their own blendshapes set, and animator layers merged by VRCFury or " +
            "Modular Avatar do the rest. The avatar in your scene isn't changed.";

        protected override void BuildBody(VisualElement body)
        {
            var menu = FTUI.Card(body, "Menu", out _);
            menu.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFTToggles.menuName)), "In Menu"));

            // Same layout as the Guards cards: badges and the switch in the header, the settings greyed out while it's off
            var visemes = FTUI.Card(body, "Visemes", out var visemesExtras);
            visemesExtras.Add(visemesBadges.Element);
            visemesBadges.Element.style.flexDirection = FlexDirection.Row;

            var toggle = new Toggle { tooltip = "Add the Visemes submenu to the menu." };
            toggle.AddToClassList("ft-toggle");
            toggle.BindProperty(serializedObject.FindProperty(nameof(PawlygonFTToggles.visemesMenu)));
            visemesExtras.Add(toggle);

            visemesBody = new VisualElement();
            visemesNote = FTUI.Text("", "ft-muted", "ft-card__note");
            visemesBody.Add(visemesNote);
            visemesBody.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFTToggles.visemesDefault)), "Default Level"));
            visemesBody.Add(visemesParameters.Element);
            visemes.Add(visemesBody);

            var pupils = FTUI.Card(body, "Pupils", out var pupilExtras);
            pupilExtras.Add(pupilBadges.Element);
            pupilBadges.Element.style.flexDirection = FlexDirection.Row;

            pupilBody = new VisualElement();
            pupilNote = FTUI.Text("", "ft-muted", "ft-card__note");
            pupilBody.Add(pupilNote);
            pupilBody.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFTToggles.pupilTrackingToggle)), "Pupil Tracking Toggle"));
            pupilBody.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFTToggles.pupilEmulationToggle)), "Pupil Emulation Toggle"));
            pupilBody.Add(pupilParameters.Element);
            pupils.Add(pupilBody);
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

            RefreshVisemes(ResolvePlan(avatar, issues), issues);
            RefreshPupils(avatar);
        }

        void RefreshVisemes(FTTogglesPlan plan, FTIssues issues)
        {
            if (plan.VisemesSkipped != null) issues.Warning("The Visemes menu won't be added.", plan.VisemesSkipped);

            visemesBody.SetEnabled(Component.visemesMenu);
            visemesNote.text = VisemesNote();

            // The sets are only named when there's more than one: a single one is just the avatar's visemes
            var count = plan.VisemeSets.Sum(s => s.Visemes);
            var sources = plan.VisemeSets.Select(s => s.Source).ToList();
            var bits = plan.SyncedBits;
            visemesBadges.Update($"{Component.visemesMenu}|{plan.AddsVisemesMenu}|{count}|{bits}|{string.Join(",", sources)}", container =>
            {
                if (!Component.visemesMenu)
                {
                    container.Add(FTUI.Badge("off", FTKind.Muted));
                    return;
                }
                if (!plan.AddsVisemesMenu)
                {
                    container.Add(FTUI.Badge("not added", FTKind.Warning, plan.VisemesSkipped));
                    return;
                }

                container.Add(FTUI.Badge($"{count} {(count == 1 ? "viseme" : "visemes")}", FTKind.Muted,
                    "The avatar's visemes, which the menu turns down. When the avatar is built they move to a blendshapes set of their own, " +
                    "so they can be turned down without the rest of the face." +
                    (sources.Count > 1 ? $"\nFrom {sources.Count} blendshapes sets: {string.Join(", ", sources)}." : "")));
                container.Add(FTUI.Badge($"{bits} synced bits", FTKind.Ok,
                    "The level in effect is synced, so everyone sees the same. The level picked in the menu isn't."));
            });

            visemesParameters.Update(plan.AddsVisemesMenu.ToString(), container =>
            {
                if (!plan.AddsVisemesMenu) return;

                container.Add(FTUI.Text("Levels", "ft-group-title"));
                container.Add(FTUI.Chips(FTTogglesPlan.Levels.Select(level =>
                {
                    var percent = UnityEngine.Mathf.RoundToInt(FTTogglesPlan.LevelWeight(level) * 100f);
                    return ($"{level} {percent}%", $"Visemes play at {percent}% of their strength while face tracking is active.");
                })));

                container.Add(FTUI.Text("Parameters", "ft-group-title"));
                container.Add(FTUI.Chips(new[]
                {
                    (FTTogglesPlan.VisemesLevelParameter, "The level picked in the Visemes menu. Saved, not synced."),
                    (FTTogglesPlan.VisemesBitAParameter,
                        "With VisemesB, the level in effect: the picked one while face tracking is active, Full otherwise. Set on your side and synced, 1 bit."),
                    (FTTogglesPlan.VisemesBitBParameter,
                        "With VisemesA, the level in effect. Set on your side and synced, 1 bit."),
                }));
            });
        }

        void RefreshPupils(VRCAvatarDescriptor avatar)
        {
            var plan = FTPupilEmulationPlan.ForAvatar(avatar);
            var available = plan != null && plan.Shapes.Count > 0;
            var asked = Component.pupilTrackingToggle || Component.pupilEmulationToggle;
            pupilNote.text = PupilNote(plan, available);

            var bits = available ? plan.ToggleBits : 0;
            pupilBadges.Update($"{available}|{asked}|{bits}", container =>
            {
                if (!asked)
                {
                    container.Add(FTUI.Badge("off", FTKind.Muted));
                    return;
                }
                if (!available)
                {
                    container.Add(FTUI.Badge("not added", FTKind.Muted, "The pupil toggles change what Pupil Emulation plays, so they need it on the avatar."));
                    return;
                }
                container.Add(FTUI.Badge($"{bits} synced {(bits == 1 ? "bit" : "bits")}", FTKind.Ok,
                    "Each toggle is synced, so everyone sees the same pupils."));
            });

            var tracking = available && plan.TrackingToggle;
            var emulation = available && plan.EmulationToggle;
            pupilParameters.Update($"{tracking}|{emulation}", container =>
            {
                if (!tracking && !emulation) return;

                var chips = new List<(string, string)>();
                if (tracking) chips.Add((FTPupilEmulationPlan.TrackingParameter, "The Pupil Tracking menu toggle. On to start with, saved and synced, 1 bit."));
                if (emulation) chips.Add((FTPupilEmulationPlan.EmulationParameter, "The Pupil Emulation menu toggle. On to start with, saved and synced, 1 bit."));
                container.Add(FTUI.Text("Parameters", "ft-group-title"));
                container.Add(FTUI.Chips(chips));
            });
        }

        string MenuText()
        {
            var menuName = Component.menuName?.Trim() ?? "";
            return menuName.Length == 0 ? "the main menu" : $"the {menuName} menu";
        }

        /// <summary>What visemes do with these settings, and where the menu that changes it is.</summary>
        string VisemesNote()
        {
            var where = $"The Visemes submenu in {MenuText()} picks how much of them plays while face tracking is active.";
            switch (Component.visemesDefault)
            {
                case FTVisemesLevel.Full:
                    return $"Visemes play as usual. {where}";
                case FTVisemesLevel.Off:
                    return $"With face tracking active, visemes are off until another level is picked. Without face tracking they always play in full. {where}";
                default:
                    var percent = UnityEngine.Mathf.RoundToInt(FTTogglesPlan.LevelWeight(Component.visemesDefault) * 100f);
                    return $"With face tracking active, visemes play at {percent}% until another level is picked. Without face tracking they always play in full. {where}";
            }
        }

        /// <summary>What the pupils do with these settings.</summary>
        string PupilNote(FTPupilEmulationPlan plan, bool available)
        {
            if (!available)
            {
                return plan == null
                    ? "The avatar has no Pupil Emulation, so there are no pupil toggles to add."
                    : "The face tracking meshes have no EyeDilation or EyeConstrict blendshapes, so there are no pupil toggles to add.";
            }
            if (!plan.RealTracking)
            {
                return "All Tracking Keys is off, so only the emulated pupils play and there's no pupil tracking to toggle.";
            }
            return $"The emulated pupils and the headset's pupil tracking play together. Each toggle in {MenuText()} turns one of them off, " +
                   "and a part without a toggle always plays.";
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
