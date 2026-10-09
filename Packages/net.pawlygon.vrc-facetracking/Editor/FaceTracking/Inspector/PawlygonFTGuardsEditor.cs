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
    [CustomEditor(typeof(PawlygonFTGuards))]
    internal sealed class PawlygonFTGuardsEditor : FTComponentEditor<PawlygonFTGuards>
    {
        const double AnalysisSeconds = 4;

        readonly FTDynamic gestureBadge = new FTDynamic();
        readonly FTDynamic gestureList = new FTDynamic();
        readonly FTDynamic blinkBadge = new FTDynamic();
        readonly FTDynamic blinkList = new FTDynamic();
        VisualElement gestureBody, blinkBody, menuField;

        double nextAnalysis;
        List<(string source, AnimatorController controller, FXGuardAnalysis analysis)> analyses =
            new List<(string, AnimatorController, FXGuardAnalysis)>();

        protected override string Title => "Face Tracking Guards";
        protected override string Subtitle => "Pauses gesture expressions and blinking while tracking is active.";

        protected override string Details =>
            "When the avatar is built, adds guards to a copy of the FX controller: gesture-driven expression layers pause while " +
            "UserFaceTracking is on, and blink layers pause while UserEyeTracking is on. Guards use the same FacialExpressionsDisabled " +
            "and EyeTrackingActive parameters as Pawlygon VRCFT 1.x, so FX controllers already guarded with Unity Tools keep working.";

        protected override void BuildBody(VisualElement body)
        {
            var gestures = FTUI.Card(body, "Gestures", out var gestureExtras);
            gestureExtras.Add(gestureBadge.Element);
            gestureExtras.Add(EnabledToggle(nameof(PawlygonFTGuards.guardGestures)));
            gestureBody = new VisualElement();
            gestureBody.Add(FTUI.Text("Paused while UserFaceTracking is on.", "ft-muted", "ft-card__note"));
            gestureBody.Add(gestureList.Element);
            gestureBody.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFTGuards.allowGesturesToggle)), "Allow Gestures Toggle"));
            menuField = new PropertyField(serializedObject.FindProperty(nameof(PawlygonFTGuards.menuName)), "In Menu");
            gestureBody.Add(menuField);
            gestures.Add(gestureBody);

            var blinking = FTUI.Card(body, "Blinking", out var blinkExtras);
            blinkExtras.Add(blinkBadge.Element);
            blinkExtras.Add(EnabledToggle(nameof(PawlygonFTGuards.guardBlinking)));
            blinkBody = new VisualElement();
            blinkBody.Add(FTUI.Text("Paused while UserEyeTracking is on.", "ft-muted", "ft-card__note"));
            blinkBody.Add(blinkList.Element);
            blinking.Add(blinkBody);
        }

        Toggle EnabledToggle(string property)
        {
            var toggle = new Toggle { tooltip = "Enabled" };
            toggle.AddToClassList("ft-toggle");
            toggle.BindProperty(serializedObject.FindProperty(property));
            return toggle;
        }

        protected override void Refresh(VRCAvatarDescriptor avatar, FTIssues issues)
        {
            if (EditorApplication.timeSinceStartup >= nextAnalysis)
            {
                nextAnalysis = EditorApplication.timeSinceStartup + AnalysisSeconds;
                analyses = Analyze(avatar);
            }

            gestureBody.SetEnabled(Component.guardGestures);
            blinkBody.SetEnabled(Component.guardBlinking);
            menuField.style.display = Component.allowGesturesToggle ? DisplayStyle.Flex : DisplayStyle.None;

            foreach (var (_, controller, _) in analyses)
            {
                foreach (var name in new[] { FXGuardWriter.GestureGuardParameter, FXGuardWriter.BlinkGuardParameter, FTGuardDriver.UserFaceTracking, FTGuardDriver.UserEyeTracking })
                {
                    if (controller.parameters.Any(p => p.name == name && p.type == AnimatorControllerParameterType.Trigger))
                    {
                        issues.Error($"'{name}' is a Trigger in '{controller.name}'.", "Guards need it to be a Bool, Float or Int. Change its type in that controller.");
                    }
                }
            }

            if (analyses.Count == 0)
            {
                issues.Info("No FX controller to guard yet.", "Only the driver layers will be added when the avatar is built.");
            }

            UpdateGestures();
            UpdateBlinks();
        }

        void UpdateGestures()
        {
            var layers = analyses.SelectMany(a => a.analysis.GestureLayers.Select(l => (a.source, layer: l))).ToList();
            var guarded = layers.Count(x => FaceTrackingGuardsBuilder.ShouldGuard(Component, x.layer));
            gestureBadge.Update($"{guarded}", e => e.Add(FTUI.Badge($"{guarded} guarded", guarded > 0 ? FTKind.Ok : FTKind.Muted)));

            var key = string.Join("|", layers.Select(x => x.source + x.layer.Name + x.layer.GestureTransitions + x.layer.AlreadyGuarded
                                                           + string.Join(",", x.layer.ExpressionBlendshapes) + FaceTrackingGuardsBuilder.ShouldGuard(Component, x.layer)));
            gestureList.Update(key, container =>
            {
                if (layers.Count == 0)
                {
                    container.Add(FTUI.Text("No gesture-driven layers in the FX controller.", "ft-muted"));
                    return;
                }

                // Layers that check gestures without animating the face (e.g. pausing blinks) are tucked away
                var main = layers.Where(x => FaceTrackingGuardsBuilder.IsAutomatic(x.layer) || FaceTrackingGuardsBuilder.ShouldGuard(Component, x.layer)).ToList();
                var others = layers.Except(main).ToList();

                AddGestureRows(container, main);
                if (main.Count == 0) container.Add(FTUI.Text("No gesture layers animate the face.", "ft-muted"));
                if (others.Count > 0)
                {
                    var foldout = new Foldout { text = $"Other gesture layers ({others.Count})", value = false, tooltip = "These use gestures but don't animate face tracking blendshapes." };
                    AddGestureRows(foldout, others);
                    container.Add(foldout);
                }
            });
        }

        void AddGestureRows(VisualElement container, List<(string source, FXGuardAnalysis.GestureLayer layer)> layers)
        {
            foreach (var group in layers.GroupBy(x => x.source))
            {
                container.Add(FTUI.Text(group.Key, "ft-group-title"));
                foreach (var (_, layer) in group)
                {
                    var name = FXGuardAnalysis.CleanLayerName(layer.Name);
                    var automatic = FaceTrackingGuardsBuilder.IsAutomatic(layer);
                    var shapes = layer.ExpressionBlendshapes;
                    var details = shapes.Count > 0
                        ? string.Join(", ", shapes.Take(4)) + (shapes.Count > 4 ? $" +{shapes.Count - 4}" : "")
                        : "uses gestures, doesn't animate the face";
                    var badge = layer.AlreadyGuarded
                        ? FTUI.Badge("already guarded", FTKind.Muted, "Guarded by Unity Tools in the original FX. That guard stays and is driven by face tracking.")
                        : null;

                    var row = CheckRow(name, details, FaceTrackingGuardsBuilder.ShouldGuard(Component, layer), on =>
                    {
                        // Expression layers are on unless excluded, the others are off unless included
                        if (automatic) SetMember(nameof(PawlygonFTGuards.excludedGestureLayers), name, !on);
                        else SetMember(nameof(PawlygonFTGuards.includedGestureLayers), name, on);
                    }, badge);
                    row.tooltip = shapes.Count > 0 ? "Gestures set: " + string.Join(", ", shapes) : null;
                    container.Add(row);
                }
            }
        }

        void UpdateBlinks()
        {
            var layers = analyses.SelectMany(a => a.analysis.BlinkLayers.Select(l => (a.source, layer: l))).ToList();
            var guarded = layers.Count(x => FaceTrackingGuardsBuilder.ShouldGuard(Component, x.layer));
            blinkBadge.Update($"{guarded}", e => e.Add(FTUI.Badge($"{guarded} guarded", guarded > 0 ? FTKind.Ok : FTKind.Muted)));

            var key = string.Join("|", layers.Select(x => x.source + x.layer.Name + x.layer.Score + x.layer.AlreadyGuarded + FaceTrackingGuardsBuilder.ShouldGuard(Component, x.layer)));
            blinkList.Update(key, container =>
            {
                if (layers.Count == 0)
                {
                    container.Add(FTUI.Text("No blink layers detected in the FX controller.", "ft-muted"));
                    return;
                }

                // Low confidence candidates are usually just small layers, so they're tucked away unless chosen
                var main = layers.Where(x => x.layer.Confidence != FXBlinkConfidence.Low || FaceTrackingGuardsBuilder.ShouldGuard(Component, x.layer)).ToList();
                var others = layers.Except(main).ToList();

                AddBlinkRows(container, main);
                if (main.Count == 0) container.Add(FTUI.Text("No likely blink layers.", "ft-muted"));
                if (others.Count > 0)
                {
                    var foldout = new Foldout { text = $"Other candidates ({others.Count})", value = false };
                    AddBlinkRows(foldout, others);
                    container.Add(foldout);
                }
            });
        }

        void AddBlinkRows(VisualElement container, List<(string source, FXGuardAnalysis.BlinkLayer layer)> layers)
        {
            foreach (var group in layers.GroupBy(x => x.source))
            {
                container.Add(FTUI.Text(group.Key, "ft-group-title"));
                foreach (var (_, layer) in group)
                {
                    var name = FXGuardAnalysis.CleanLayerName(layer.Name);
                    var automatic = layer.Confidence == FXBlinkConfidence.High || layer.AlreadyGuarded;
                    var kind = layer.Confidence == FXBlinkConfidence.High ? FTKind.Ok : layer.Confidence == FXBlinkConfidence.Medium ? FTKind.Info : FTKind.Muted;
                    var badge = FTUI.Badge(layer.AlreadyGuarded ? "already guarded" : layer.Confidence.ToString(), layer.AlreadyGuarded ? FTKind.Muted : kind,
                        "Detected from: " + string.Join(", ", layer.Reasons));

                    container.Add(CheckRow(name, string.Join(", ", layer.Reasons), FaceTrackingGuardsBuilder.ShouldGuard(Component, layer), on =>
                    {
                        // High confidence layers are on unless excluded, others are off unless included
                        if (automatic) SetMember(nameof(PawlygonFTGuards.excludedBlinkLayers), name, !on);
                        else SetMember(nameof(PawlygonFTGuards.includedBlinkLayers), name, on);
                    }, badge));
                }
            }
        }

        static VisualElement CheckRow(string name, string details, bool value, System.Action<bool> onChange, VisualElement badge)
        {
            var row = new VisualElement();
            row.AddToClassList("ft-check");

            var toggle = new Toggle { value = value };
            toggle.AddToClassList("ft-check__toggle");
            toggle.RegisterValueChangedCallback(e => onChange(e.newValue));
            row.Add(toggle);

            var text = new VisualElement();
            text.AddToClassList("ft-check__text");
            text.Add(FTUI.Text(name, "ft-param__base"));
            text.Add(FTUI.Text(details, "ft-param__expression"));
            row.Add(text);

            if (badge != null) row.Add(badge);
            return row;
        }

        void SetMember(string listProperty, string value, bool present)
        {
            var list = serializedObject.FindProperty(listProperty);
            var index = -1;
            for (var i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).stringValue == value) index = i;
            }

            if (present && index < 0)
            {
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).stringValue = value;
            }
            else if (!present && index >= 0)
            {
                list.DeleteArrayElementAtIndex(index);
            }
            serializedObject.ApplyModifiedProperties();
            RefreshNow();
        }

        /// <summary>
        /// The controllers that end up in FX: the descriptor's own, and the ones VRCFury or
        /// Modular Avatar will merge into FX. Legacy face tracking controllers are left out.
        /// </summary>
        static List<(string, AnimatorController, FXGuardAnalysis)> Analyze(VRCAvatarDescriptor avatar)
        {
            var result = new List<(string, AnimatorController, FXGuardAnalysis)>();
            var seen = new HashSet<AnimatorController>();

            // Same meshes the build uses: the setup component's, or the ones already on the descriptor
            var setup = FTAvatarUtils.GetBuildComponents<PawlygonFaceTracking>(avatar.gameObject).FirstOrDefault();
            var meshes = setup != null
                ? FaceTrackingSetupPlan.Resolve(setup, avatar).Meshes
                : avatar.faceTrackingMeshes ?? new List<SkinnedMeshRenderer>();
            var faceMeshes = new FXFaceMeshes(meshes, avatar.transform);

            void Add(string source, RuntimeAnimatorController runtime)
            {
                if (!(FTAvatarUtils.BaseController(runtime) is AnimatorController controller) || !seen.Add(controller)) return;
                result.Add((source, controller, FXGuardAnalysis.Analyze(controller, faceMeshes)));
            }

            var fx = FTAvatarAssets.GetFX(avatar);
            if (fx != null) Add($"FX ({fx.name})", fx);

            foreach (var behaviour in avatar.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour is VRC.SDKBase.VRC_AvatarDescriptor || behaviour is PawlygonFTComponent) continue;
                if (FTAvatarUtils.IsEditorOnly(behaviour.transform, avatar.transform)) continue;

                foreach (var controller in FTAvatarUtils.GetReferencedControllers(behaviour, (int)VRCAvatarDescriptor.AnimLayerType.FX))
                {
                    if (LegacyOscDetector.IsPawlygonLegacyController(controller)) continue;
                    Add(FTAvatarUtils.GetPath(behaviour.transform, avatar.transform), controller);
                }
            }
            return result;
        }
    }
}
