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
        readonly FTDynamic bakeView = new FTDynamic();
        VisualElement gestureBody, blinkBody, bakeCard;
        AnimatorController bakeableFX;
        VRCAvatarDescriptor currentAvatar;
        Label gestureNote;

        double nextAnalysis;
        List<(string source, AnimatorController controller, FXGuardAnalysis analysis)> analyses =
            new List<(string, AnimatorController, FXGuardAnalysis)>();

        protected override string Title => "Face Tracking Guards";
        protected override string Subtitle => "Pauses gesture expressions and blinking while tracking is active.";

        protected override string Details =>
            "Drives the FacialExpressionsDisabled and EyeTrackingActive parameters that guards in the FX controller wait for, like " +
            "Pawlygon VRCFT 1.x: gesture layers pause while UserFaceTracking is on or Disable Gestures is toggled, and blink layers " +
            "pause while UserEyeTracking is on. The guards themselves are part of the controller: added by Unity Tools' FX Gesture " +
            "Checker, by other face tracking add-ons, or by Bake Guards below, which writes them into a copy of the FX controller. " +
            "Nothing in the avatar's animators is changed when it's built.";

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
            gestureBody.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFTGuards.gesturesDisabledByDefault)), "Disabled By Default"));
            gestureBody.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFTGuards.menuName)), "In Menu"));
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

            // Card() returns the body; the card itself is hidden while there's nothing to bake or restore
            var bakeBody = FTUI.Card(body, "Bake Guards", out _);
            bakeBody.Add(bakeView.Element);
            bakeCard = bakeBody.parent;
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
            currentAvatar = avatar;
            if (EditorApplication.timeSinceStartup >= nextAnalysis)
            {
                nextAnalysis = EditorApplication.timeSinceStartup + AnalysisSeconds;
                analyses = Analyze(avatar);
            }
            bakeableFX = FTGuardBaker.BakeableFX(avatar);

            gestureBody.SetEnabled(Component.guardGestures);
            blinkBody.SetEnabled(Component.guardBlinking);
            gestureNote.text = "Paused " + GestureSources() + ". The toggle starts " + (Component.gesturesDisabledByDefault ? "on" : "off") + ".";

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
                issues.Info("No FX controller yet.", "There's nothing to guard or drive.");
            }

            var unguarded = Unguarded();
            if (unguarded.Count > 0)
            {
                var bakeable = unguarded.Count(x => x.inFX);
                var title = $"{Count(unguarded.Count, "layer")} {(unguarded.Count == 1 ? "isn't" : "aren't")} guarded.";
                var detail = "They keep running while tracking is active, so gestures and blinks fight face tracking. " +
                             (bakeable == unguarded.Count
                                 ? "Bake Guards adds the guards to a copy of the FX controller."
                                 : "Layers merged by VRCFury or Modular Avatar need guards in their own controller, e.g. with Unity Tools' FX Gesture Checker.");
                if (bakeable > 0) issues.Warning(title, detail, ("Bake Guards", Bake));
                else issues.Warning(title, detail);
            }

            var bakedBlinkGuards = analyses.Sum(a => a.analysis.BlinkLayers.Count(l => l.AlreadyGuarded));
            if (!Component.guardBlinking && bakedBlinkGuards > 0)
            {
                issues.Info($"Blinking is off, but {bakedBlinkGuards} blink {(bakedBlinkGuards == 1 ? "layer already has a guard" : "layers already have guards")} in the FX controller.",
                    "EyeTrackingActive is still driven because they read it, so they pause blinking while eye tracking is on. " +
                    "Blinking only decides what Bake Guards adds; remove those guards from the FX controller to keep blinking.");
            }

            UpdateGestures();
            UpdateBlinks();
            UpdateDriven();
            UpdateBake(avatar);
        }

        /// <summary>Checked layers without a guard, and whether they're in the avatar's own FX, which Bake Guards can copy.</summary>
        List<(string name, bool inFX)> Unguarded()
        {
            return FTGuardSelection.From(Component, analyses).Unguarded
                .Select(l => (l.CleanName, l.Controller == bakeableFX))
                .ToList();
        }

        void Bake()
        {
            if (currentAvatar != null && FTGuardBaker.Bake(Component, currentAvatar)) Reanalyze();
        }

        void Reanalyze()
        {
            nextAnalysis = 0;
            RefreshNow();
        }

        void UpdateBake(VRCAvatarDescriptor avatar)
        {
            var original = FTGuardBaker.OriginalOf(bakeableFX);
            var pending = Unguarded().Where(x => x.inFX).Select(x => x.name).ToList();
            bakeCard.style.display = pending.Count > 0 || original != null ? DisplayStyle.Flex : DisplayStyle.None;

            var key = (bakeableFX != null ? bakeableFX.GetInstanceID() : 0) + "|" + (original != null ? original.GetInstanceID() : 0) + "|" + string.Join(",", pending);
            bakeView.Update(key, container =>
            {
                if (bakeableFX == null)
                {
                    container.Add(FTUI.Text("The FX layer uses an override controller, which Bake Guards can't copy.", "ft-muted"));
                    return;
                }

                if (pending.Count > 0)
                {
                    container.Add(FTUI.Text("Writes the guards into a copy of the FX controller and makes the copy the avatar's FX controller. " +
                                            "The original file isn't changed.", "ft-muted", "ft-card__note"));
                }

                var buttons = new VisualElement();
                buttons.AddToClassList("ft-field-row");
                if (original != null)
                {
                    var label = FTUI.Text($"Using a baked copy of {original.name}.", "ft-muted");
                    label.tooltip = $"The avatar's FX controller is {bakeableFX.name}, made by Bake Guards.";
                    label.style.flexGrow = 1;
                    if (pending.Count == 0) buttons.Add(label);
                    else container.Add(label);
                }
                if (pending.Count > 0)
                {
                    var bake = new Button(Bake) { text = $"Bake Guards ({pending.Count})", tooltip = string.Join(", ", pending) };
                    bake.AddToClassList("ft-add-button");
                    bake.style.flexGrow = 1;
                    buttons.Add(bake);
                }
                if (original != null)
                {
                    var restore = new Button(() =>
                    {
                        if (!EditorUtility.DisplayDialog("Restore Original FX", $"Switches the avatar's FX controller back to '{original.name}'. " +
                                                         $"The copy '{bakeableFX.name}' is kept, so you can delete it yourself.", "Restore", "Cancel")) return;
                        FTGuardBaker.Restore(avatar);
                        Reanalyze();
                    }) { text = "Restore Original", tooltip = $"Switch back to {original.name}." };
                    restore.AddToClassList("ft-add-button");
                    restore.style.marginLeft = 4;
                    buttons.Add(restore);
                }
                container.Add(buttons);
            });
        }

        static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

        string GestureSources()
        {
            return Component.disableWithFaceTracking
                ? "while the Disable Gestures toggle or UserFaceTracking is on"
                : "while the Disable Gestures toggle is on";
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

            // Same rule as the build: each parameter is driven when an animator reads it
            var gestureSources = ReadBy(FXGuardWriter.GestureGuardParameter);

            var rows = new List<(string parameter, string source, string tooltip)>();
            if (FaceTrackingGuardsBuilder.GuardsGestures(Component) && gestureSources.Count > 0)
            {
                rows.Add((FXGuardWriter.GestureGuardParameter, GestureSources(), "Used by: " + string.Join(", ", gestureSources)));
            }
            // Driven whenever an animator reads it, even with Blinking off
            if (eyeSources.Count > 0)
            {
                rows.Add((FXGuardWriter.BlinkGuardParameter, "UserEyeTracking", "Used by: " + string.Join(", ", eyeSources)));
            }
            if (lipSources.Count > 0)
            {
                rows.Add((FTGuardDriver.LipTrackingParameter, "UserFaceTracking", "Used by: " + string.Join(", ", lipSources)));
            }

            drivenList.Update(string.Join("|", rows), container =>
            {
                if (rows.Count == 0)
                {
                    container.Add(FTUI.Text("Nothing to drive: no animator reads these parameters. Bake Guards adds guards that do.", "ft-muted"));
                    return;
                }

                container.Add(Chips(rows.Select(r => (r.parameter,
                    (r.source.StartsWith("while") ? "On " + r.source : "Follows " + r.source) + ".\n" + r.tooltip))));
                container.Add(FTUI.Text("Set by the avatar's owner and synced, 1 bit each.", "ft-muted", "ft-card__note"));
            });
        }

        void UpdateGestures()
        {
            var layers = analyses.SelectMany(a => a.analysis.GestureLayers.Select(l => (a.source, layer: l))).ToList();
            var status = layers.Select(x => (x.layer.AlreadyGuarded, FTGuardSelection.ShouldGuard(Component, x.layer))).ToList();
            gestureBadge.Update(BadgeKey(status), e => AddBadges(e, status));

            var key = string.Join("|", layers.Select(x => x.source + x.layer.Name + x.layer.GestureTransitions + x.layer.AlreadyGuarded
                                                           + string.Join(",", x.layer.ExpressionBlendshapes) + FTGuardSelection.ShouldGuard(Component, x.layer)));
            gestureList.Update(key, container =>
            {
                if (layers.Count == 0)
                {
                    container.Add(FTUI.Text("No gesture-driven layers in the FX controller.", "ft-muted"));
                    return;
                }

                // Guarded layers as chips, layers waiting for Bake Guards as rows, the rest tucked away
                var guarded = layers.Where(x => x.layer.AlreadyGuarded).ToList();
                var pending = layers.Where(x => !x.layer.AlreadyGuarded && FTGuardSelection.ShouldGuard(Component, x.layer)).ToList();
                var others = layers.Except(guarded).Except(pending).ToList();

                if (guarded.Count > 0) container.Add(Chips(guarded.Select(x => (FXGuardAnalysis.CleanLayerName(x.layer.Name), GestureTooltip(x.source, x.layer)))));
                foreach (var (source, layer) in pending) container.Add(GestureRow(source, layer));
                if (guarded.Count + pending.Count == 0) container.Add(FTUI.Text("No gesture layers animate the face.", "ft-muted"));
                if (others.Count > 0)
                {
                    var foldout = FTUI.Collapsible($"More layers ({others.Count})", false,
                        "Layers that use gestures without animating the face, or that were unchecked. Check one to guard it when baking.");
                    foreach (var (source, layer) in others) foldout.Add(GestureRow(source, layer));
                    container.Add(foldout);
                }
            });
        }

        static string BadgeKey(List<(bool guarded, bool selected)> layers)
        {
            return layers.Count(x => x.guarded) + "/" + layers.Count(x => !x.guarded && x.selected);
        }

        /// <summary>How many layers have a guard, and how many checked ones don't yet.</summary>
        static void AddBadges(VisualElement container, List<(bool guarded, bool selected)> layers)
        {
            var guarded = layers.Count(x => x.guarded);
            var missing = layers.Count(x => !x.guarded && x.selected);
            container.Add(FTUI.Badge($"{guarded} guarded", guarded > 0 ? FTKind.Ok : FTKind.Muted));
            if (missing > 0) container.Add(FTUI.Badge($"{missing} unguarded", FTKind.Warning, "Checked layers without a guard. Bake Guards adds them."));
        }

        static string GestureTooltip(string source, FXGuardAnalysis.GestureLayer layer)
        {
            var shapes = layer.ExpressionBlendshapes;
            return "In " + source + (shapes.Count > 0 ? ".\nGestures set: " + string.Join(", ", shapes) : ".\nUses gestures, doesn't animate the face.");
        }

        VisualElement GestureRow(string source, FXGuardAnalysis.GestureLayer layer)
        {
            var name = FXGuardAnalysis.CleanLayerName(layer.Name);
            var automatic = FTGuardSelection.IsAutomatic(layer);
            var shapes = layer.ExpressionBlendshapes;
            var details = shapes.Count > 0
                ? string.Join(", ", shapes.Take(4)) + (shapes.Count > 4 ? $" +{shapes.Count - 4}" : "")
                : "uses gestures, doesn't animate the face";
            var selected = FTGuardSelection.ShouldGuard(Component, layer);

            var row = CheckRow(name, details, selected, on =>
            {
                // Expression layers are on unless excluded, the others are off unless included
                if (automatic) SetMember(nameof(PawlygonFTGuards.excludedGestureLayers), name, !on);
                else SetMember(nameof(PawlygonFTGuards.includedGestureLayers), name, on);
            }, selected ? UnguardedBadge() : null);
            row.tooltip = GestureTooltip(source, layer);
            return row;
        }

        void UpdateBlinks()
        {
            var layers = analyses.SelectMany(a => a.analysis.BlinkLayers.Select(l => (a.source, layer: l))).ToList();
            var status = layers.Select(x => (x.layer.AlreadyGuarded, FTGuardSelection.ShouldGuard(Component, x.layer))).ToList();
            blinkBadge.Update(BadgeKey(status), e => AddBadges(e, status));

            var key = string.Join("|", layers.Select(x => x.source + x.layer.Name + x.layer.Score + x.layer.AlreadyGuarded + FTGuardSelection.ShouldGuard(Component, x.layer)));
            blinkList.Update(key, container =>
            {
                if (layers.Count == 0)
                {
                    container.Add(FTUI.Text("No blink layers detected in the FX controller.", "ft-muted"));
                    return;
                }

                // Guarded layers as chips, layers waiting for Bake Guards as rows, other candidates tucked away
                var guarded = layers.Where(x => x.layer.AlreadyGuarded).ToList();
                var pending = layers.Where(x => !x.layer.AlreadyGuarded && FTGuardSelection.ShouldGuard(Component, x.layer)).ToList();
                var others = layers.Except(guarded).Except(pending).ToList();

                if (guarded.Count > 0) container.Add(Chips(guarded.Select(x => (FXGuardAnalysis.CleanLayerName(x.layer.Name), BlinkTooltip(x.source, x.layer)))));
                foreach (var (source, layer) in pending) container.Add(BlinkRow(source, layer));
                if (guarded.Count + pending.Count == 0) container.Add(FTUI.Text("No likely blink layers.", "ft-muted"));
                if (others.Count > 0)
                {
                    var foldout = FTUI.Collapsible($"More candidates ({others.Count})", false,
                        "Layers that might blink but weren't detected with high confidence, or that were unchecked. Check one to guard it when baking.");
                    foreach (var (source, layer) in others) foldout.Add(BlinkRow(source, layer));
                    container.Add(foldout);
                }
            });
        }

        static string BlinkTooltip(string source, FXGuardAnalysis.BlinkLayer layer)
        {
            return $"In {source}.\nDetected from: {string.Join(", ", layer.Reasons)} ({layer.Confidence.ToString().ToLowerInvariant()} confidence).";
        }

        VisualElement BlinkRow(string source, FXGuardAnalysis.BlinkLayer layer)
        {
            var name = FXGuardAnalysis.CleanLayerName(layer.Name);
            var automatic = layer.Confidence == FXBlinkConfidence.High;
            var selected = FTGuardSelection.ShouldGuard(Component, layer);
            var details = $"{layer.Confidence.ToString().ToLowerInvariant()} confidence: {string.Join(", ", layer.Reasons)}";

            var row = CheckRow(name, details, selected, on =>
            {
                // High confidence layers are on unless excluded, others are off unless included
                if (automatic) SetMember(nameof(PawlygonFTGuards.excludedBlinkLayers), name, !on);
                else SetMember(nameof(PawlygonFTGuards.includedBlinkLayers), name, on);
            }, selected ? UnguardedBadge() : null);
            row.tooltip = BlinkTooltip(source, layer);
            return row;
        }

        /// <summary>Chips with their own tooltips.</summary>
        static VisualElement Chips(IEnumerable<(string text, string tooltip)> items)
        {
            var list = items.ToList();
            var chips = FTUI.Chips(list.Select(i => i.text));
            var index = 0;
            foreach (var chip in chips.Children()) chip.tooltip = list[index++].tooltip;
            return chips;
        }

        /// <summary>A checked layer without a guard, waiting for Bake Guards.</summary>
        static VisualElement UnguardedBadge() => FTUI.Badge("unguarded", FTKind.Warning, "Bake Guards adds a guard to it.");

        static VisualElement CheckRow(string name, string details, bool value, System.Action<bool> onChange, VisualElement badge)
        {
            var row = new VisualElement();
            row.AddToClassList("ft-check");

            var toggle = new Toggle { value = value, tooltip = "Guard this layer when baking." };
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
            var faceMeshes = FTGuardSelection.FaceMeshes(avatar);

            return FTAvatarAssets.GetSourceFXControllers(avatar)
                .Select(s => (s.source, s.controller, FXGuardAnalysis.Analyze(s.controller, faceMeshes)))
                .ToList();
        }
    }
}
