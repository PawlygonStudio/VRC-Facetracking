using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Pawlygon.FaceTracking.Editor
{
    [CustomEditor(typeof(PawlygonFTParameters))]
    internal sealed class PawlygonFTParametersEditor : FTComponentEditor<PawlygonFTParameters>
    {
        protected override string Title => "Pawlygon Face Tracking Parameters";

        protected override string Description =>
            "Forwards face tracking values to animator parameters when the avatar is built, so animators made for " +
            "OSC face tracking (FT/v2/..., OSCm/Proxy/FT/v2/...) keep working with native face tracking.";

        protected override void BuildFields(VisualElement root)
        {
            root.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFTParameters.autoDetect))));
            root.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFTParameters.parameters)), "Manual Parameters"));
            root.TrackSerializedObjectValue(serializedObject, _ => RefreshStatus());
        }

        protected override void BuildStatus(FTStatus status, VRCAvatarDescriptor avatar)
        {
            var setup = FTAvatarUtils.GetBuildComponents<PawlygonFaceTracking>(avatar.gameObject).FirstOrDefault();
            if (setup == null && !avatar.enableFaceTracking)
            {
                status.Error("Face tracking isn't enabled on this avatar. Add a Pawlygon Face Tracking component, " +
                             "or enable face tracking on the avatar descriptor.");
                return;
            }

            var useAllTrackingKeys = setup != null ? setup.useAllTrackingKeys : avatar.useAllTrackingKeys;
            var plan = FTForwardingPlan.ForPreview(Component, avatar, useAllTrackingKeys);
            foreach (var warning in plan.Warnings) status.Warning(warning);

            // Without a setup component, forwards already on the descriptor are kept
            var existing = setup != null ? 0 : (avatar.faceTrackingParameterBehaviors ?? new System.Collections.Generic.List<VRCAvatarDescriptor.FaceTrackingParameterBehavior>())
                .Count(b => b.targetMode == VRCAvatarDescriptor.ParameterBehaviorTargetMode.ForwardToAnimator
                            && plan.Entries.All(e => e.Parameter != b.outputParameterName));
            var total = plan.Entries.Count + existing;

            if (plan.Entries.Count == 0)
            {
                status.Info(Component.autoDetect
                    ? "No known face tracking parameters are used by this avatar's animators."
                    : "No parameters to forward.");
                return;
            }

            var rows = plan.Entries.Select(e => (e.Parameter, e.Expression, e.Source)).ToList();
            var key = "forward:" + string.Join("|", rows) + existing;
            status.Add(key, () =>
            {
                var box = new VisualElement
                {
                    style =
                    {
                        marginTop = 4, paddingTop = 4, paddingBottom = 4, paddingLeft = 4, paddingRight = 4,
                        borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
                        borderTopColor = Color.gray, borderBottomColor = Color.gray, borderLeftColor = Color.gray, borderRightColor = Color.gray,
                        borderTopLeftRadius = 3, borderTopRightRadius = 3, borderBottomLeftRadius = 3, borderBottomRightRadius = 3,
                    },
                };
                box.Add(new Label("Applied at build") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 3 } });
                var countText = $"{total} ({FTForwardingPlan.RankFor(total)})" + (existing > 0 ? $", including {existing} already on the descriptor" : "");
                box.Add(FTInspectorUI.Row("Forwarded", countText));

                var foldout = new Foldout { text = $"Parameters ({plan.Entries.Count})", value = true };
                foreach (var (parameter, expression, source) in rows)
                {
                    var row = new VisualElement { style = { marginBottom = 3 } };
                    row.Add(new Label(parameter) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
                    row.Add(new Label($"= {expression}") { style = { whiteSpace = WhiteSpace.Normal, marginLeft = 8 } });
                    row.Add(new Label(source) { style = { marginLeft = 8, color = Color.gray, fontSize = 10 } });
                    foldout.Add(row);
                }
                box.Add(foldout);
                return box;
            });
        }
    }
}
