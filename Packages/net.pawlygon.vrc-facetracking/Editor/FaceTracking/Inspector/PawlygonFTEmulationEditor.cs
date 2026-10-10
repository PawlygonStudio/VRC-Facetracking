using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Pawlygon.FaceTracking.Editor
{
    [CustomEditor(typeof(PawlygonFTEmulation))]
    internal sealed class PawlygonFTEmulationEditor : FTComponentEditor<PawlygonFTEmulation>
    {
        readonly FTDynamic shapesBadge = new FTDynamic();
        readonly FTDynamic shapesList = new FTDynamic();

        protected override string Title => "Face Tracking Emulation";
        protected override string Subtitle => "Fills in and tidies up face tracking shapes.";

        protected override string Details =>
            "Emulations fill in shapes from related ones, for trackers that don't send them (e.g. brows from the eyelids on " +
            "eye-only trackers). They only ever add to the tracked value. Limits relax shapes that fight each other. When the " +
            "avatar is built, each affected blendshape is driven by one native face tracking expression, without animators or " +
            "synced parameters.";

        protected override void BuildBody(VisualElement body)
        {
            var emulations = FTUI.Card(body, "Emulations", out _);
            AddRule(emulations, nameof(PawlygonFTEmulation.tongueOpensJaw), nameof(PawlygonFTEmulation.tongueOpensJawAmount), "Tongue Opens Jaw");
            AddRule(emulations, nameof(PawlygonFTEmulation.browsUpWithWideEyes), nameof(PawlygonFTEmulation.browsUpWithWideEyesAmount), "Brows Up With Wide Eyes");
            AddRule(emulations, nameof(PawlygonFTEmulation.browsDownWithSquint), nameof(PawlygonFTEmulation.browsDownWithSquintAmount), "Brows Down With Squint");
            AddRule(emulations, nameof(PawlygonFTEmulation.angryBrows), nameof(PawlygonFTEmulation.angryBrowsAmount), "Angry Brows");
            AddRule(emulations, nameof(PawlygonFTEmulation.sadBrows), nameof(PawlygonFTEmulation.sadBrowsAmount), "Sad Brows");

            var limits = FTUI.Card(body, "Limits", out _);
            limits.Add(Field(nameof(PawlygonFTEmulation.limitFunnelByChinRaise), "Funnel Relaxed by Chin Raise"));
            limits.Add(Field(nameof(PawlygonFTEmulation.limitRaisersByMouthClosed), "Raisers Relaxed by Closed Mouth"));
            limits.Add(Field(nameof(PawlygonFTEmulation.limitTongueByFunnel), "Tongue Pulled In by Funnel"));
            limits.Add(Field(nameof(PawlygonFTEmulation.limitLipsByFunnel), "Lips Relaxed by Funnel"));
            limits.Add(Field(nameof(PawlygonFTEmulation.limitJawByMouthSideways), "Jaw Sideways Reduced by Mouth"));
            limits.Add(Field(nameof(PawlygonFTEmulation.limitFrownByMouthSideways), "Frown Reduced by Mouth Sideways"));
            limits.Add(Field(nameof(PawlygonFTEmulation.limitLipSuckByMouthClosed), "Lip Suck Relaxed by Closed Mouth"));

            var shapes = FTUI.Card(body, "Rewritten Blendshapes", out var shapesExtras);
            shapesExtras.Add(shapesBadge.Element);
            shapes.Add(shapesList.Element);
        }

        void AddRule(VisualElement card, string toggle, string amount, string label)
        {
            card.Add(Field(toggle, label));
            var amountField = Field(amount, "Amount");
            amountField.style.marginLeft = 16;
            card.Add(amountField);

            var property = serializedObject.FindProperty(toggle);
            void Update() => amountField.SetEnabled(property.boolValue);
            Update();
            amountField.TrackPropertyValue(property, _ => Update());
        }

        PropertyField Field(string property, string label) => new PropertyField(serializedObject.FindProperty(property), label);

        protected override void Refresh(VRCAvatarDescriptor avatar, FTIssues issues)
        {
            // Same mapping the build uses: the setup component's, or what's already on the descriptor
            var setup = FTAvatarUtils.GetBuildComponents<PawlygonFaceTracking>(avatar.gameObject).FirstOrDefault();
            FTEmulationPlan plan;
            if (setup != null)
            {
                var setupPlan = FaceTrackingSetupPlan.Resolve(setup, avatar);
                plan = FTEmulationPlan.Resolve(Component,
                    setupPlan.Set != null ? new[] { setupPlan.Set } : new VRCAvatarDescriptor.FaceTrackingBlendshapesSet[0], setup.useAllTrackingKeys);
            }
            else if (avatar.enableFaceTracking)
            {
                plan = FTEmulationPlan.Resolve(Component,
                    avatar.faceTrackingBlendshapesSetList ?? new List<VRCAvatarDescriptor.FaceTrackingBlendshapesSet>(), avatar.useAllTrackingKeys);
            }
            else
            {
                issues.Error("Face tracking isn't enabled on this avatar.",
                    "Add a Pawlygon Face Tracking component, or enable face tracking on the avatar descriptor.");
                plan = new FTEmulationPlan();
            }

            if (plan.NeedsAllTrackingKeys)
            {
                issues.Warning("Needs All Tracking Keys.", "Turn on All Tracking Keys in the Face Tracking component; brows, mouth and tongue keys need it.");
            }
            foreach (var skipped in plan.SkippedRules)
            {
                issues.Info($"{skipped.Rule}: partly skipped.", skipped.Reason);
            }

            var count = plan.Shapes.Count;
            shapesBadge.Update(count.ToString(), e => e.Add(FTUI.Badge($"{count} rewritten", count > 0 ? FTKind.Ok : FTKind.Muted)));
            var key = string.Join("|", plan.Shapes.Select(s => s.Blendshape + s.Expression));
            shapesList.Update(key, container =>
            {
                if (count == 0)
                {
                    container.Add(FTUI.Text("Nothing to rewrite: no enabled rule has a blendshape on this avatar.", "ft-muted"));
                    return;
                }
                foreach (var shape in plan.Shapes)
                {
                    container.Add(FTUI.Row(shape.Blendshape, string.Join(", ", shape.Rules), shape.Expression));
                }
                container.Add(FTUI.Text("Hover a row to see its expression.", "ft-muted", "ft-card__note"));
            });
        }
    }
}
