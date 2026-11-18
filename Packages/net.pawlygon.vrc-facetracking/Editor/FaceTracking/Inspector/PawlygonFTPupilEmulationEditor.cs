using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Pawlygon.FaceTracking.Editor
{
    [CustomEditor(typeof(PawlygonFTPupilEmulation))]
    internal sealed class PawlygonFTPupilEmulationEditor : FTComponentEditor<PawlygonFTPupilEmulation>
    {
        readonly FTDynamic shapesBadge = new FTDynamic();
        readonly FTDynamic shapesList = new FTDynamic();
        readonly FTDynamic replacedList = new FTDynamic();
        VisualElement replacedCard;

        // Scanning every component and controller on the avatar is slow on big avatars, so it's cached briefly
        const double FakePupilScanSeconds = 4;
        double nextFakePupilScan;
        readonly List<(string source, string layer)> fakePupils = new List<(string, string)>();
        readonly List<AnimatorController> removable = new List<AnimatorController>();
        int fakePupilControllers;

        protected override string Title => "Pupil Emulation";
        protected override string Subtitle => "Lively pupils, with or without pupil tracking.";

        protected override string Details =>
            "While eye tracking is active, the pupils drift gently and react to every blink: they reopen wide, tighten, then settle. " +
            "Mood reactions can add constriction with wide eyes and dilation with a frown. " +
            "When the headset tracks pupils, that plays on top, and Face Tracking Toggles can add a menu toggle for each. " +
            "When the avatar is built, the EyeDilation and " +
            "EyeConstrict blendshapes are taken over from native face tracking and driven by an FX layer.";

        protected override void BuildBody(VisualElement body)
        {
            var shapes = FTUI.Card(body, "Pupils", out var shapesExtras);
            shapesExtras.Add(shapesBadge.Element);
            shapes.Add(shapesList.Element);

            replacedCard = FTUI.Card(body, "Fake Pupil Layers", out _);
            replacedCard.Add(FTUI.Text("Fake pupil layers from 1.x (Face Tracking Extras, Custom Animations, or made by hand) animate the same " +
                                       "blendshapes as Pupil Emulation, so both would play at once. Builds don't change animators: remove them here.",
                "ft-muted", "ft-card__note"));
            replacedCard.Add(replacedList.Element);

            var idle = FTUI.Card(body, "Idle Drift", out _);
            idle.Add(Field(nameof(PawlygonFTPupilEmulation.idleDilationMin), "Dilation Min"));
            idle.Add(Field(nameof(PawlygonFTPupilEmulation.idleDilationMax), "Dilation Max"));

            var reflex = FTUI.Card(body, "Blink Reflex", out _);
            reflex.Add(Field(nameof(PawlygonFTPupilEmulation.reflexPeakDilation), "Peak Dilation"));
            reflex.Add(Field(nameof(PawlygonFTPupilEmulation.reflexConstrict), "Constriction"));
            reflex.Add(Field(nameof(PawlygonFTPupilEmulation.reflexDuration), "Duration"));
            reflex.Add(FTUI.Text("Eyelids are about 0 when closed and 0.75 when normally open.", "ft-muted", "ft-card__note"));
            reflex.Add(Field(nameof(PawlygonFTPupilEmulation.lidClosedBelow), "Closed Below"));
            reflex.Add(Field(nameof(PawlygonFTPupilEmulation.lidOpenAbove), "Reopened Above"));

            // Same layout as the Guards cards: the switch in the header, the settings greyed out while it's off
            var mood = FTUI.Card(body, "Mood Reactions", out var moodExtras);
            var moodProperty = serializedObject.FindProperty(nameof(PawlygonFTPupilEmulation.moodReactions));
            var moodToggle = new Toggle { tooltip = "Enabled" };
            moodToggle.AddToClassList("ft-toggle");
            moodToggle.BindProperty(moodProperty);
            moodExtras.Add(moodToggle);

            var moodBody = new VisualElement();
            moodBody.Add(FTUI.Text("While the pupils are emulated, wide eyes constrict them and a frown on both sides dilates them.",
                "ft-muted", "ft-card__note"));
            moodBody.Add(Field(nameof(PawlygonFTPupilEmulation.wideEyesConstrict), "Wide Eyes Constrict"));
            moodBody.Add(Field(nameof(PawlygonFTPupilEmulation.frownDilation), "Frown Dilation"));
            mood.Add(moodBody);

            moodBody.SetEnabled(moodProperty.boolValue);
            mood.TrackPropertyValue(moodProperty, p => moodBody.SetEnabled(p.boolValue));
        }

        PropertyField Field(string property, string label)
        {
            return new PropertyField(serializedObject.FindProperty(property), label);
        }

        protected override void Refresh(VRCAvatarDescriptor avatar, FTIssues issues)
        {
            // Same meshes and mapping the build uses: the setup component's, or what's already on the descriptor
            var setup = FTAvatarUtils.GetBuildComponents<PawlygonFaceTracking>(avatar.gameObject).FirstOrDefault();
            FTPupilEmulationPlan plan;
            if (setup != null)
            {
                var setupPlan = FaceTrackingSetupPlan.Resolve(setup, avatar);
                plan = FTPupilEmulationPlan.Resolve(setupPlan.Meshes,
                    setupPlan.Set != null ? new[] { setupPlan.Set } : new VRCAvatarDescriptor.FaceTrackingBlendshapesSet[0], setup.useAllTrackingKeys);
            }
            else if (avatar.enableFaceTracking)
            {
                plan = FTPupilEmulationPlan.Resolve(avatar.faceTrackingMeshes ?? new List<SkinnedMeshRenderer>(),
                    avatar.faceTrackingBlendshapesSetList ?? new List<VRCAvatarDescriptor.FaceTrackingBlendshapesSet>(), avatar.useAllTrackingKeys);
            }
            else
            {
                issues.Error("Face tracking isn't enabled on this avatar.",
                    "Add a Pawlygon Face Tracking component, or enable face tracking on the avatar descriptor.");
                plan = new FTPupilEmulationPlan();
            }

            if (Component.idleDilationMin > Component.idleDilationMax)
            {
                issues.Warning("Dilation Min is above Dilation Max.", "The idle drift uses Dilation Min as both ends.");
            }
            if (Component.lidOpenAbove <= Component.lidClosedBelow)
            {
                issues.Warning("Reopened Above should be higher than Closed Below.", "Otherwise noisy eyelid tracking can trigger reflexes that aren't blinks.");
            }

            if (Component.moodReactions && plan.Shapes.Count > 0 && !plan.RealTracking)
            {
                issues.Info("Mood reactions need All Tracking Keys.", "They read EyeWide and MouthFrown, which only come with All Tracking Keys.");
            }

            UpdateShapes(plan, issues);
            UpdateFakePupils(avatar, issues);
        }

        /// <summary>The fake pupil layers in the FX controller and in the controllers VRCFury / Modular Avatar will merge into it.</summary>
        void UpdateFakePupils(VRCAvatarDescriptor avatar, FTIssues issues)
        {
            if (EditorApplication.timeSinceStartup >= nextFakePupilScan)
            {
                nextFakePupilScan = EditorApplication.timeSinceStartup + FakePupilScanSeconds;
                ScanFakePupils(avatar);
            }

            void Remove()
            {
                if (!FTFakePupilLayers.Remove(avatar, removable)) return;
                nextFakePupilScan = 0;
                RefreshNow();
            }

            if (fakePupils.Count > 0)
            {
                var count = fakePupils.Count;
                var title = $"{count} fake pupil {(count == 1 ? "layer" : "layers")} will fight Pupil Emulation.";
                var detail = "They animate the same pupil blendshapes, so both would play at once. Pupil Emulation replaces them." +
                             (removable.Count < fakePupilControllers
                                 ? " Some are in a package controller used through an override controller, which has to be changed by hand."
                                 : "");
                if (removable.Count > 0) issues.Warning(title, detail, ("Remove", Remove));
                else issues.Warning(title, detail);
            }

            // Card() returns the body, so the whole card is its parent
            replacedCard.parent.style.display = fakePupils.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            var key = string.Join("|", fakePupils) + "|" + string.Join(",", removable.Select(c => c.GetInstanceID()));
            replacedList.Update(key, container =>
            {
                foreach (var group in fakePupils.GroupBy(f => f.source))
                {
                    container.Add(FTUI.Text(group.Key, "ft-group-title"));
                    container.Add(FTUI.Chips(group.Select(f => f.layer), null, "Fights Pupil Emulation until it's removed"));
                }
                if (removable.Count > 0)
                {
                    var button = new Button(Remove)
                    {
                        text = "Remove Fake Pupils",
                        tooltip = "Empties these layers. Controllers in Assets are changed in place (undoable). For controllers in packages, " +
                                  "a copy gets the change and the avatar uses the copy.",
                    };
                    button.AddToClassList("ft-add-button");
                    container.Add(button);
                }
            });
        }

        void ScanFakePupils(VRCAvatarDescriptor avatar)
        {
            fakePupils.Clear();
            removable.Clear();
            fakePupilControllers = 0;
            var referenced = FTControllerCopies.Referenced(avatar);
            var seen = new HashSet<AnimatorController>();

            void Add(string source, RuntimeAnimatorController runtime)
            {
                if (!(FTAvatarUtils.BaseController(runtime) is AnimatorController controller) || !seen.Add(controller)) return;
                var indices = FTFakePupilLayers.Find(controller);
                if (indices.Count == 0) return;

                fakePupils.AddRange(indices.Select(i => (source, controller.layers[i].name)));
                fakePupilControllers++;
                if (FTFakePupilLayers.CanRemove(controller, referenced)) removable.Add(controller);
            }

            var fx = FTAvatarAssets.GetFX(avatar);
            if (fx != null) Add($"FX ({fx.name})", fx);
            foreach (var behaviour in avatar.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour is VRC.SDKBase.VRC_AvatarDescriptor || behaviour is PawlygonFTComponent) continue;
                if (FTAvatarUtils.IsEditorOnly(behaviour.transform, avatar.transform)) continue;
                foreach (var controller in FTAvatarUtils.GetReferencedControllers(behaviour, (int)VRCAvatarDescriptor.AnimLayerType.FX))
                {
                    if (!LegacyOscDetector.IsPawlygonLegacyController(controller)) Add(FTAvatarUtils.GetPath(behaviour.transform, avatar.transform), controller);
                }
            }
        }

        void UpdateShapes(FTPupilEmulationPlan plan, FTIssues issues)
        {
            if (plan.Shapes.Count == 0)
            {
                issues.Warning("No EyeDilation or EyeConstrict blendshapes on the face tracking meshes.", "Pupil Emulation has nothing to drive and is skipped.");
            }
            else if (!plan.RealTracking)
            {
                issues.Info("All Tracking Keys is off, so the headset's pupil tracking can't play.", "Only the emulated movement plays while eye tracking is active.");
            }

            var count = plan.Shapes.Count;
            shapesBadge.Update(count.ToString(), e => e.Add(FTUI.Badge(count == 0 ? "none found" : $"{count} driven", count == 0 ? FTKind.Warning : FTKind.Ok)));

            var key = string.Join("|", plan.Shapes.Select(s => s.Name + ":" + string.Join(",", s.Keys))) + plan.RealTracking;
            shapesList.Update(key, container =>
            {
                if (count == 0)
                {
                    container.Add(FTUI.Text("Add EyeDilation / EyeConstrict blendshapes (or per eye, like EyeDilationLeft) to the face mesh.", "ft-muted"));
                    return;
                }

                foreach (var shape in plan.Shapes)
                {
                    container.Add(FTUI.Row(shape.Name, string.Join(", ", shape.Keys.Select(FTKeys.KeyName)),
                        shape.IsDilation ? "Dilates in the idle drift and the blink reflex." : "Tightens in the blink reflex."));
                }
                container.Add(FTUI.Text(plan.RealTracking
                    ? "The headset's pupil tracking plays on top of the emulated movement. Native face tracking leaves these blendshapes alone."
                    : "Native face tracking leaves these blendshapes alone.", "ft-muted", "ft-card__note"));
            });
        }
    }
}
