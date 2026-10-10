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

        protected override string Title => "Pupil Emulation";
        protected override string Subtitle => "Lively pupils on headsets without pupil tracking.";

        protected override string Details =>
            "While eye tracking is active, the pupils drift gently and react to every blink: they reopen wide, tighten, then settle. " +
            "Mood reactions can add constriction with wide eyes and dilation with a frown. " +
            "As soon as the headset reports real pupil dilation, that plays instead. When the avatar is built, the EyeDilation and " +
            "EyeConstrict blendshapes are taken over from native face tracking and driven by an FX layer.";

        protected override void BuildBody(VisualElement body)
        {
            var shapes = FTUI.Card(body, "Pupils", out var shapesExtras);
            shapesExtras.Add(shapesBadge.Element);
            shapes.Add(shapesList.Element);

            replacedCard = FTUI.Card(body, "Replaced at Build", out _);
            replacedCard.Add(FTUI.Text("Fake pupil layers from 1.x (Face Tracking Extras, or made by hand) would fight Pupil Emulation, " +
                                       "so they're emptied in the uploaded copy. Your controllers aren't changed.", "ft-muted", "ft-card__note"));
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

            var mood = FTUI.Card(body, "Mood Reactions", out _);
            mood.Add(Field(nameof(PawlygonFTPupilEmulation.moodReactions), "React to Expression"));
            var wide = Field(nameof(PawlygonFTPupilEmulation.wideEyesConstrict), "Wide Eyes Constrict");
            var frown = Field(nameof(PawlygonFTPupilEmulation.frownDilation), "Frown Dilates");
            mood.Add(wide);
            mood.Add(frown);
            var moodProperty = serializedObject.FindProperty(nameof(PawlygonFTPupilEmulation.moodReactions));
            void UpdateMood()
            {
                wide.SetEnabled(moodProperty.boolValue);
                frown.SetEnabled(moodProperty.boolValue);
            }
            UpdateMood();
            mood.TrackPropertyValue(moodProperty, _ => UpdateMood());
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
            UpdateReplaced(avatar);
        }

        /// <summary>The fake pupil layers in the FX controller and in the controllers VRCFury / Modular Avatar will merge into it.</summary>
        void UpdateReplaced(VRCAvatarDescriptor avatar)
        {
            var found = new List<(string source, string layer)>();
            var seen = new HashSet<AnimatorController>();

            void Add(string source, RuntimeAnimatorController runtime)
            {
                if (!(FTAvatarUtils.BaseController(runtime) is AnimatorController controller) || !seen.Add(controller)) return;
                found.AddRange(FTFakePupilLayers.Find(controller).Select(i => (source, controller.layers[i].name)));
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

            replacedCard.style.display = found.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            replacedList.Update(string.Join("|", found), container =>
            {
                foreach (var group in found.GroupBy(f => f.source))
                {
                    container.Add(FTUI.Text(group.Key, "ft-group-title"));
                    container.Add(FTUI.Chips(group.Select(f => f.layer), null, "Emptied in the uploaded copy"));
                }
            });
        }

        void UpdateShapes(FTPupilEmulationPlan plan, FTIssues issues)
        {
            if (plan.Shapes.Count == 0)
            {
                issues.Warning("No EyeDilation or EyeConstrict blendshapes on the face tracking meshes.", "Pupil Emulation has nothing to drive and is skipped.");
            }
            else if (!plan.RealTracking)
            {
                issues.Info("All Tracking Keys is off, so real pupil tracking can't take over.", "The pupils are emulated whenever eye tracking is active.");
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
                    ? "Real pupil tracking takes over once the headset reports it. Native face tracking leaves these blendshapes alone."
                    : "Native face tracking leaves these blendshapes alone.", "ft-muted", "ft-card__note"));
            });
        }
    }
}
