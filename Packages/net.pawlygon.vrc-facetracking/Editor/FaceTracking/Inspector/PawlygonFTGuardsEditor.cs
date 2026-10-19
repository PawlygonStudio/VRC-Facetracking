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
        readonly FTDynamic drivenList = new FTDynamic();
        VisualElement gestureBody, blinkBody, menuField;
        Label gestureNote;

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
            gestureNote = FTUI.Text("", "ft-muted", "ft-card__note");
            gestureBody.Add(gestureNote);
            gestureBody.Add(gestureList.Element);
            gestureBody.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFTGuards.disableWithFaceTracking)), "Disable With Face Tracking"));
            gestureBody.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFTGuards.disableGesturesToggle)), "Disable Gestures Toggle"));
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

            var driven = FTUI.Card(body, "Driven Parameters", out _);
            driven.Add(drivenList.Element);
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
            menuField.style.display = Component.disableGesturesToggle ? DisplayStyle.Flex : DisplayStyle.None;
            gestureNote.text = "Paused " + GestureSources() + ".";

            if (Component.guardGestures && !FaceTrackingGuardsBuilder.GuardsGestures(Component))
            {
                issues.Warning("Gestures are never paused.", "Turn on Disable With Face Tracking, the Disable Gestures toggle, or both.");
            }

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

            var bakedBlinkGuards = analyses.Sum(a => a.analysis.BlinkLayers.Count(l => l.AlreadyGuarded));
            if (!Component.guardBlinking && bakedBlinkGuards > 0)
            {
                issues.Info($"Blinking is off, but {bakedBlinkGuards} blink {(bakedBlinkGuards == 1 ? "layer already has a guard" : "layers already have guards")} in the FX controller.",
                    "EyeTrackingActive is still driven because they read it, so they pause blinking while eye tracking is on. " +
                    "Remove those guards from the FX controller to keep blinking.");
            }

            UpdateGestures();
            UpdateBlinks();
            UpdateDriven();
        }

        string GestureSources()
        {
            if (Component.disableWithFaceTracking && Component.disableGesturesToggle) return "while UserFaceTracking is on or Disable Gestures is toggled";
            if (Component.disableWithFaceTracking) return "while UserFaceTracking is on";
            if (Component.disableGesturesToggle) return "while Disable Gestures is toggled";
            return "never";
        }

        /// <summary>The Pawlygon VRCFT 1.x parameters this component sets, and from what.</summary>
        void UpdateDriven()
        {
            List<string> ReadBy(string parameter) => analyses
                .Where(a => a.controller.parameters.Any(p => p.name == parameter))
                .Select(a => a.source)
                .ToList();
            var lipSources = ReadBy(FTGuardDriver.LipTrackingParameter);
            var eyeSources = ReadBy(FXGuardWriter.BlinkGuardParameter);

            // Same rule as the build: driven when something is guarded or an animator reads the parameter
            bool Reads(string parameter) => analyses.Any(a => a.controller.parameters.Any(p => p.name == parameter));
            var guardedGestures = analyses.Any(a => a.analysis.GestureLayers.Any(l => FaceTrackingGuardsBuilder.ShouldGuard(Component, l)));
            var guardedBlinks = analyses.Any(a => a.analysis.BlinkLayers.Any(l => FaceTrackingGuardsBuilder.ShouldGuard(Component, l)));

            var rows = new List<(string parameter, string source, string tooltip)>();
            if (FaceTrackingGuardsBuilder.GuardsGestures(Component) && (guardedGestures || Reads(FXGuardWriter.GestureGuardParameter)))
            {
                rows.Add((FXGuardWriter.GestureGuardParameter, GestureSources(), "Pauses the guarded gesture layers."));
            }
            // Driven whenever an animator reads it, even with Blinking off
            if ((Component.guardBlinking && guardedBlinks) || eyeSources.Count > 0)
            {
                rows.Add((FXGuardWriter.BlinkGuardParameter, "UserEyeTracking", Component.guardBlinking && guardedBlinks
                    ? "Pauses the guarded blink layers."
                    : "Used by: " + string.Join(", ", eyeSources)));
            }
            if (lipSources.Count > 0)
            {
                rows.Add((FTGuardDriver.LipTrackingParameter, "UserFaceTracking", "Used by: " + string.Join(", ", lipSources)));
            }

            drivenList.Update(string.Join("|", rows), container =>
            {
                if (rows.Count == 0)
                {
                    container.Add(FTUI.Text("Nothing to drive: no layers are guarded and no animator reads these parameters.", "ft-muted"));
                    return;
                }

                foreach (var (parameter, source, tooltip) in rows)
                {
                    var row = new VisualElement { tooltip = tooltip };
                    row.AddToClassList("ft-param");
                    var text = new VisualElement();
                    text.AddToClassList("ft-param__text");
                    text.Add(FTUI.Text(parameter, "ft-param__base"));
                    text.Add(FTUI.Text(source.StartsWith("while") ? "on " + source : "follows " + source, "ft-param__expression"));
                    row.Add(text);
                    container.Add(row);
                }
                container.Add(FTUI.Text("Set by the avatar's owner and synced, 1 bit each.", "ft-muted", "ft-card__note"));
            });
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
                    var foldout = FTUI.Collapsible($"Other gesture layers ({others.Count})", false, "These use gestures but don't animate face tracking blendshapes.");
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
                    var foldout = FTUI.Collapsible($"Other candidates ({others.Count})");
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

        /// <summary>The controllers that end up in FX, each analyzed with the meshes the build will use.</summary>
        static List<(string, AnimatorController, FXGuardAnalysis)> Analyze(VRCAvatarDescriptor avatar)
        {
            // Same meshes the build uses: the setup component's, or the ones already on the descriptor
            var setup = FTAvatarUtils.GetBuildComponents<PawlygonFaceTracking>(avatar.gameObject).FirstOrDefault();
            var meshes = setup != null
                ? FaceTrackingSetupPlan.Resolve(setup, avatar).Meshes
                : avatar.faceTrackingMeshes ?? new List<SkinnedMeshRenderer>();
            var faceMeshes = new FXFaceMeshes(meshes, avatar.transform);

            return FTAvatarAssets.GetSourceFXControllers(avatar)
                .Select(s => (s.source, s.controller, FXGuardAnalysis.Analyze(s.controller, faceMeshes)))
                .ToList();
        }
    }
}
