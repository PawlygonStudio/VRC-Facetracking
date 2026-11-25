using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using EyeGazeMode = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.CustomEyeLookSettings.EyeGazeMode;

namespace Pawlygon.FaceTracking.Editor
{
    [CustomEditor(typeof(PawlygonFaceTracking))]
    internal sealed class PawlygonFaceTrackingEditor : FTComponentEditor<PawlygonFaceTracking>
    {
        // Scanning every component on the avatar is slow on big avatars, so these are cached briefly
        const double SlowScanSeconds = 4;

        VisualElement keysTable;
        readonly FTDynamic meshBadge = new FTDynamic();
        readonly FTDynamic meshList = new FTDynamic();
        readonly FTDynamic keysBadge = new FTDynamic();
        readonly FTDynamic keysDetails = new FTDynamic();
        readonly FTDynamic keysRows = new FTDynamic();
        FTMark lipSyncMark, dampenMark, eyelidsMark, gazeMark, eyeRangeMark;
        readonly List<(string region, Button button)> regionButtons = new List<(string, Button)>();

        // The face region the list is limited to, or null for all of them
        string keyRegion;
        PropertyField manualMeshes;
        PropertyField tongueMode;
        VisualElement keysTools;
        Button[] filterButtons;
        Label bulkLabel;

        readonly FTDynamic visemesBadges = new FTDynamic();
        readonly FTDynamic visemesParameters = new FTDynamic();
        VisualElement visemesBody;
        Label visemesNote;
        List<int> shownKeys = new List<int>();

        enum KeyFilter
        {
            All,
            Unmapped,
            Edited,
        }

        KeyFilter keyFilter;
        string keySearch = "";

        // Suggestions only change with the blendshapes and the unmapped keys, and are slow to find
        string suggestionsKey;
        Dictionary<int, string> suggestions = new Dictionary<int, string>();
        Label lipSyncResult, dampenResult, eyelidsResult, gazeResult, eyeRangeResult, eyeRangeNote;
        VisualElement eyeRangeRow;

        // The eye preview turns the eyes to what the build will write, from the last refresh
        readonly FTEyePreview eyePreview = new FTEyePreview();
        VRCAvatarDescriptor previewAvatar;
        FaceTrackingSetupPlan previewPlan;

        double nextSlowScan;
        List<LegacyOscDetector.Finding> legacyFindings = new List<LegacyOscDetector.Finding>();
        List<VRCAvatarDescriptor.CustomAnimLayer> legacyLayers = new List<VRCAvatarDescriptor.CustomAnimLayer>();
        List<FTBlendshapeOptimizers.Finding> blendshapeOptimizers = new List<FTBlendshapeOptimizers.Finding>();

        protected override string Title => "Face Tracking";
        protected override string Subtitle => "Native face tracking, applied when the avatar is built.";

        protected override string Details =>
            "Sets up VRChat's native face tracking on the avatar descriptor when the avatar is built or uploaded: " +
            "meshes, blendshape mapping, visemes, eyelids and eye gaze. It can also add a Visemes submenu to the avatar's menu, " +
            "to turn visemes down while face tracking is active. The avatar in your scene isn't changed. " +
            "To inspect the result, right-click the avatar > !Pawlygon > Face Tracking > Build Test Copy.";

        protected override void BuildBody(VisualElement body)
        {
            var meshesPage = FTUI.Card(body, "Meshes", out var meshExtras);
            meshExtras.Add(meshBadge.Element);
            meshesPage.Add(FTUI.Segmented(serializedObject.FindProperty(nameof(PawlygonFaceTracking.meshMode)),
                "Auto picks every mesh with face tracking blendshapes."));
            manualMeshes = new PropertyField(serializedObject.FindProperty(nameof(PawlygonFaceTracking.meshes)), "Meshes");
            meshesPage.Add(manualMeshes);
            meshesPage.Add(meshList.Element);

            var keysPage = FTUI.Card(body, "Tracking Keys", out var keysExtras);
            keysExtras.Add(keysBadge.Element);
            keysPage.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFaceTracking.useAllTrackingKeys)), "All Tracking Keys"));
            // Only shown for avatars with tongue steps, see Refresh
            tongueMode = new PropertyField(serializedObject.FindProperty(nameof(PawlygonFaceTracking.tongueMode)), "Tongue Mode");
            tongueMode.style.display = DisplayStyle.None;
            keysPage.Add(tongueMode);
            keysPage.Add(keysDetails.Element);
            keysPage.Add(BuildKeysTools());

            // The rows scroll inside a fixed height, under their column names. The scroll view is built once, so
            // it stays where it is when the rows are redrawn.
            keysTable = new VisualElement();
            var header = new VisualElement();
            header.AddToClassList("ft-table__header");
            header.Add(FTUI.Text("Tracking Key", "ft-key__name"));
            header.Add(FTUI.Text("Blendshape", "ft-table__grow"));
            header.Add(FTUI.Text("Multiplier", "ft-table__last"));
            keysTable.Add(header);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("ft-table__scroll");
            scroll.Add(keysRows.Element);
            keysTable.Add(scroll);
            keysPage.Add(keysTable);

            var lipSync = FTUI.Card(body, "Lip Sync", out _);
            lipSync.Add(FieldWithResult(nameof(PawlygonFaceTracking.lipSync), "Mode", out lipSyncResult, out lipSyncMark));
            lipSync.Add(FieldWithResult(nameof(PawlygonFaceTracking.visemeDampenMode), "Dampening", out dampenResult, out dampenMark));
            var dampenAmount = new PropertyField(serializedObject.FindProperty(nameof(PawlygonFaceTracking.visemeDampenAmount)), "Amount");
            dampenAmount.AddToClassList("ft-sub-field");
            lipSync.Add(dampenAmount);

            // Same layout as the Guards cards: badges and the switch in the header, the settings greyed out while it's off
            var visemes = FTUI.Card(body, "Visemes Menu", out var visemesExtras);
            visemesExtras.Add(visemesBadges.Element);
            visemesBadges.Element.style.flexDirection = FlexDirection.Row;
            var visemesToggle = new Toggle { tooltip = "Add the Visemes submenu to the avatar's menu." };
            visemesToggle.AddToClassList("ft-toggle");
            visemesToggle.BindProperty(serializedObject.FindProperty(nameof(PawlygonFaceTracking.visemesMenu)));
            visemesExtras.Add(visemesToggle);

            visemesBody = new VisualElement();
            visemesNote = FTUI.Text("", "ft-muted", "ft-card__note");
            visemesBody.Add(visemesNote);
            visemesBody.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFaceTracking.visemesDefault)), "Default Level"));
            visemesBody.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFaceTracking.menuName)), "In Menu"));
            visemesBody.Add(visemesParameters.Element);
            visemes.Add(visemesBody);

            var eyes = FTUI.Card(body, "Eyes", out _);
            eyes.Add(FieldWithResult(nameof(PawlygonFaceTracking.eyelids), "Eyelids", out eyelidsResult, out eyelidsMark));
            eyes.Add(FieldWithResult(nameof(PawlygonFaceTracking.eyeGaze), "Gaze", out gazeResult, out gazeMark));
            // Only shown when eye gaze rotates the bones, see UpdateEyeRange
            eyeRangeRow = new VisualElement();
            eyeRangeRow.Add(FieldWithResult(nameof(PawlygonFaceTracking.eyeRotationRange), "Rotation", out eyeRangeResult, out eyeRangeMark));
            var detail = new VisualElement();
            detail.AddToClassList("ft-eye-detail");
            eyeRangeNote = FTUI.Text("", "ft-muted", "ft-eye-detail__note");
            detail.Add(eyeRangeNote);
            detail.Add(BuildEyePreview());
            eyeRangeRow.Add(detail);
            eyes.Add(eyeRangeRow);

            void UpdateVisibility()
            {
                manualMeshes.style.display = Component.meshMode == FTMeshMode.Manual ? DisplayStyle.Flex : DisplayStyle.None;
                dampenAmount.style.display = Component.visemeDampenMode != FTVisemeDampenMode.None ? DisplayStyle.Flex : DisplayStyle.None;
            }
            UpdateVisibility();
            body.TrackSerializedObjectValue(serializedObject, _ => UpdateVisibility());
        }

        VisualElement BuildEyePreview()
        {
            var row = new VisualElement();
            row.AddToClassList("ft-eye-preview");
            row.Add(FTUI.Text("Hold to preview", "ft-eye-preview__label"));
            foreach (var (direction, arrow) in new[]
                     {
                         (FTLegacyEyeRange.Direction.Up, "↑"), (FTLegacyEyeRange.Direction.Down, "↓"),
                         (FTLegacyEyeRange.Direction.Left, "←"), (FTLegacyEyeRange.Direction.Right, "→"),
                     })
            {
                row.Add(eyePreview.HoldButton(arrow, $"Hold to look {direction.ToString().ToLowerInvariant()} in the Scene view.", () => PreviewRotations(direction)));
            }
            return row;
        }

        (Transform, Transform, VRCAvatarDescriptor.CustomEyeLookSettings.EyeRotations)? PreviewRotations(FTLegacyEyeRange.Direction direction)
        {
            if (previewAvatar == null || previewPlan == null) return null;
            var settings = previewAvatar.customEyeLookSettings;
            if (settings.leftEye == null || settings.rightEye == null) return null;
            var rotations = previewPlan.EyeRange != null
                ? FTLegacyEyeRange.Apply(previewAvatar, previewPlan.EyeRange, direction)
                : FTLegacyEyeRange.FromDescriptor(previewAvatar, direction);
            return (settings.leftEye, settings.rightEye, rotations);
        }

        void OnDisable() => eyePreview.Stop();

        /// <summary>A setting, what it comes to for this avatar, and a mark that shows when there's a problem with it.</summary>
        VisualElement FieldWithResult(string property, string label, out Label result, out FTMark mark)
        {
            var row = new VisualElement();
            row.AddToClassList("ft-field-row");
            row.Add(new PropertyField(serializedObject.FindProperty(property), label));
            result = FTUI.Text("", "ft-result");
            row.Add(result);
            mark = new FTMark();
            row.Add(mark);
            return row;
        }

        protected override void Refresh(VRCAvatarDescriptor avatar, FTIssues issues)
        {
            AddAvatarIssues(avatar, issues);

            var plan = FaceTrackingSetupPlan.Resolve(Component, avatar);
            foreach (var error in plan.Errors) issues.Error(error);

            // Every warning is listed at the top. One about a single setting also puts an icon next to it.
            foreach (var warning in plan.Warnings.Where(w => w != plan.TongueConflict))
            {
                issues.Warning(warning);
                switch (plan.AreaOf(warning))
                {
                    case FTSetupArea.LipSync: issues.Mark(lipSyncMark, FTKind.Warning, warning); break;
                    case FTSetupArea.Dampening: issues.Mark(dampenMark, FTKind.Warning, warning); break;
                    case FTSetupArea.Eyelids: issues.Mark(eyelidsMark, FTKind.Warning, warning); break;
                    case FTSetupArea.Gaze: issues.Mark(gazeMark, FTKind.Warning, warning); break;
                    case FTSetupArea.Rotation: issues.Mark(eyeRangeMark, FTKind.Warning, warning); break;
                }
            }
            if (plan.TongueConflict != null)
            {
                issues.Warning("TongueOut is used, the tongue steps aren't.", plan.TongueConflict, ("Use Steps", () => SetTongueMode(FTTongueMode.Steps)));
            }
            tongueMode.style.display = plan.FoundTongueSteps != null || Component.tongueMode != FTTongueMode.Auto ? DisplayStyle.Flex : DisplayStyle.None;

            UpdateMeshes(plan);
            UpdateKeys(plan, issues);
            previewAvatar = avatar;
            previewPlan = plan;
            UpdateResults(plan, avatar);
            UpdateVisemesMenu(plan, avatar, issues);
        }

        /// <summary>The Visemes menu the build will add, from the same plan it uses.</summary>
        void UpdateVisemesMenu(FaceTrackingSetupPlan setup, VRCAvatarDescriptor avatar, FTIssues issues)
        {
            var settings = FTLegacyToggles.Visemes(Component, avatar.gameObject);
            if (settings.Legacy != null)
            {
                var legacy = settings.Legacy;
                issues.Warning("A Face Tracking Toggles component from 2.1 is on this avatar.",
                    "Its Visemes settings are used in place of the ones here. Move Settings copies them to Face Tracking and Pupil Emulation " +
                    "and removes it (undoable).",
                    ("Move Settings", () => { FTLegacyToggles.MoveSettings(legacy, avatar.gameObject); RefreshNow(); }),
                    ("Select", () => Select(legacy.gameObject)));
            }

            var sets = setup.Set != null ? new[] { setup.Set } : new VRCAvatarDescriptor.FaceTrackingBlendshapesSet[0];
            var plan = FTVisemesPlan.Resolve(settings.Menu, sets, setup.UseFaceTrackingLipSync, FTMerger.Find() != null);

            // The 2.1 component's settings can't be edited here
            visemesBody.SetEnabled(settings.Menu && settings.Legacy == null);
            visemesNote.text = VisemesNote(settings);

            var count = plan.VisemeSets.Sum(s => s.Visemes);
            var bits = plan.SyncedBits;
            visemesBadges.Update($"{settings.Menu}|{plan.AddsVisemesMenu}|{count}|{bits}|{plan.VisemesSkipped}", container =>
            {
                if (!settings.Menu)
                {
                    container.Add(FTUI.Badge("off", FTKind.Muted));
                    return;
                }
                if (!plan.AddsVisemesMenu)
                {
                    container.Add(FTUI.Badge("not added", plan.NoVisemes ? FTKind.Muted : FTKind.Warning, plan.VisemesSkipped));
                    return;
                }

                container.Add(FTUI.Badge($"{count} {(count == 1 ? "viseme" : "visemes")}", FTKind.Muted,
                    "The avatar's visemes, which the menu turns down. When the avatar is built they move to a blendshapes set of their own, " +
                    $"'{FTVisemesPlan.VisemeSetName}', so they can be turned down without the rest of the face."));
                container.Add(FTUI.Badge($"{bits} synced bits", FTKind.Ok,
                    "The level in effect is synced, so everyone sees the same. The level picked in the menu isn't."));
            });

            visemesParameters.Update(plan.AddsVisemesMenu.ToString(), container =>
            {
                if (!plan.AddsVisemesMenu) return;

                container.Add(FTUI.Text("Levels", "ft-group-title"));
                container.Add(FTUI.Chips(FTVisemesPlan.Levels.Select(level =>
                {
                    var percent = Mathf.RoundToInt(FTVisemesPlan.LevelWeight(level) * 100f);
                    return ($"{level} {percent}%", $"Visemes play at {percent}% of their strength while face tracking is active.");
                })));

                container.Add(FTUI.Text("Parameters", "ft-group-title"));
                container.Add(FTUI.Chips(new[]
                {
                    (FTVisemesPlan.VisemesLevelParameter, "The level picked in the Visemes menu. Saved, not synced."),
                    (FTVisemesPlan.VisemesBitAParameter,
                        "With VisemesB, the level in effect: the picked one while face tracking is active, Full otherwise. Set on your side and synced, 1 bit."),
                    (FTVisemesPlan.VisemesBitBParameter, "With VisemesA, the level in effect. Set on your side and synced, 1 bit."),
                }));
            });
        }

        /// <summary>What visemes do with these settings, and where the menu that changes it is.</summary>
        static string VisemesNote(FTLegacyToggles.VisemeSettings settings)
        {
            if (!settings.Menu) return "Visemes always play in full, on top of the tracked mouth.";

            var where = $"The Visemes submenu in {FTMenu.Describe(settings.MenuName)} picks how much of them plays while face tracking is active.";
            switch (settings.Default)
            {
                case FTVisemesLevel.Full:
                    return $"Visemes play as usual. {where}";
                case FTVisemesLevel.Off:
                    return $"With face tracking active, visemes are off until another level is picked. Without face tracking they always play in full. {where}";
                default:
                    var percent = Mathf.RoundToInt(FTVisemesPlan.LevelWeight(settings.Default) * 100f);
                    return $"With face tracking active, visemes play at {percent}% until another level is picked. Without face tracking they always play in full. {where}";
            }
        }

        void SetTongueMode(FTTongueMode mode)
        {
            serializedObject.FindProperty(nameof(PawlygonFaceTracking.tongueMode)).enumValueIndex = (int)mode;
            serializedObject.ApplyModifiedProperties();
            RefreshNow();
        }

        void AddAvatarIssues(VRCAvatarDescriptor avatar, FTIssues issues)
        {
            if (!FTKeys.AutoFillAvailable)
            {
                issues.Error("Blendshapes are only matched by their exact Unified Expressions names.", FTKeys.AutoFillMissingMessage);
            }

            var other =FTAvatarUtils.GetBuildComponents<PawlygonFaceTracking>(avatar.gameObject).FirstOrDefault(c => c != Component);
            if (other != null)
            {
                issues.Error("Another Pawlygon Face Tracking is on this avatar.",
                    $"Only one is allowed per avatar. The other one is on '{FTAvatarUtils.GetPath(other.transform, avatar.transform)}'.",
                    ("Select", () => Select(other.gameObject)));
            }

            if (EditorApplication.timeSinceStartup >= nextSlowScan)
            {
                nextSlowScan = EditorApplication.timeSinceStartup + SlowScanSeconds;
                legacyFindings = LegacyOscDetector.Find(avatar.gameObject);
                legacyLayers = LegacyOscDetector.FindInDescriptor(avatar);
                blendshapeOptimizers = FTBlendshapeOptimizers.Find(avatar.gameObject);
            }

            foreach (var finding in legacyFindings.Where(f => f.Holder != null))
            {
                var holder = finding.Holder;
                // Disabling the avatar root would hide the whole avatar, so only offer to select it
                var onRoot = holder == avatar.gameObject;
                var actions = onRoot
                    ? new[] { ("Select", (System.Action)(() => Select(holder))) }
                    : new[] { ("Select", (System.Action)(() => Select(holder))), ("Disable", () => LegacyOscDetector.Disable(holder)) };
                // Our own 1.x setups get the step by step upgrade window
                if (finding.IsPawlygonLegacy) actions = actions.Append(("Guide", (System.Action)FTUpgradeWindow.Open)).ToArray();
                if (finding.IsBlendshapeFixes)
                {
                    issues.Error($"1.x Blendshape Fixes on '{finding.Path}'.",
                        "The Blendshape Fixes component replaces it: both would drive the fix shapes, and it would cost extra forwarded parameters. " +
                        "Disable tags it EditorOnly (undoable). If you keep it, every build asks to remove it.", actions);
                }
                else if (finding.IsPawlygonLegacy)
                {
                    issues.Error($"Legacy OSC face tracking on '{finding.Path}'.",
                        "It can't run together with native face tracking: both drive the same blendshapes. " +
                        "Disable tags it EditorOnly (undoable). If you keep it, every build asks to remove it.", actions);
                }
                else
                {
                    issues.Warning($"'{finding.Path}' looks like OSC face tracking.",
                        $"It merges '{finding.Controller.name}', which uses FT/v2 parameters. It can't run together with native face tracking.", actions);
                }
            }

            foreach (var layer in legacyLayers)
            {
                var type = layer.type;
                var name = layer.animatorController.name;
                // The Additive slot only ever held our Eye Rotation controller; an FX slot may hold the avatar's own layers too
                var actions = type == VRCAvatarDescriptor.AnimLayerType.FX
                    ? new[] { ("Select", (System.Action)(() => Select(avatar.gameObject))) }
                    : new[] { ("Select", (System.Action)(() => Select(avatar.gameObject))), ("Clear", () => LegacyOscDetector.ClearDescriptorLayer(avatar, type)) };
                issues.Error($"The descriptor's {type} layer uses the 1.x controller '{name}'.",
                    "It hands the eyes over to animation while eye tracking is on, which stops native eye tracking from rotating them. " +
                    (type == VRCAvatarDescriptor.AnimLayerType.FX
                        ? "Replace it with the avatar's own FX controller."
                        : "Clear sets the layer back to VRChat's default (undoable)."), actions);
            }

            if (avatar.enableFaceTracking || avatar.faceTrackingBlendshapesSetList?.Count > 0 || avatar.faceTrackingParameterBehaviors?.Count > 0)
            {
                issues.Warning("Replaces the descriptor's face tracking setup.",
                    "The avatar descriptor already has native face tracking configured. This component replaces it at build; the scene isn't changed.");
            }

            if (FTMerger.Find() == null)
            {
                var needing = FTAvatarUtils.GetBuildComponents<PawlygonFTComponent>(avatar.gameObject)
                    .Where(c => c is PawlygonFTGuards || c is PawlygonFTPupilEmulation)
                    .Select(c => FTAvatarUtils.DisplayName(c.GetType())).Distinct().ToList();
                if (needing.Count > 0)
                {
                    issues.Error("VRCFury or Modular Avatar is needed.",
                        $"{string.Join(" and ", needing)} add animator layers, which VRCFury (1.935.0 or newer) or Modular Avatar (1.9.0 or newer) " +
                        "merges into the avatar. Install one of them with the VRChat Creator Companion, or the build stops.");
                }
            }

            foreach (var optimizer in blendshapeOptimizers.Where(o => o.Holder != null))
            {
                var holder = optimizer.Holder;
                issues.Error($"{optimizer.Name} would strip face tracking blendshapes.",
                    $"It removes the blendshapes it thinks are unused, including face tracking's, so the build stops. {optimizer.Fix}",
                    ("Select", () => Select(holder)));
            }
        }

        void UpdateMeshes(FaceTrackingSetupPlan plan)
        {
            var names = plan.Meshes.Select(m => m.name).ToList();
            meshBadge.Update($"{names.Count}", e => e.Add(FTUI.Badge(names.Count == 1 ? "1 mesh" : $"{names.Count} meshes",
                names.Count == 0 ? FTKind.Error : FTKind.Muted)));

            var left = plan.LeftToFullControllers.Where(x => x.mesh != null).ToList();
            var key = Component.meshMode + string.Join("|", names) + "|" + string.Join("|", left.Select(x => x.mesh.name + x.holder));
            meshList.Update(key, e =>
            {
                if (plan.Meshes.Count == 0 && left.Count == 0)
                {
                    if (Component.meshMode == FTMeshMode.Auto) e.Add(FTUI.Text("No mesh with face tracking blendshapes found.", "ft-muted", "ft-card__note"));
                    return;
                }
                var meshes = plan.Meshes.ToList();
                // In Manual mode the list above already shows them
                if (meshes.Count > 0 && Component.meshMode == FTMeshMode.Auto)
                {
                    e.Add(FTUI.Chips(names, name => Select(meshes.FirstOrDefault(m => m != null && m.name == name)), "Click to select"));
                }
                if (left.Count > 0)
                {
                    // VRCFury sets these up itself, and stops the build if they're configured here too
                    e.Add(FTUI.Text("Left to VRCFury: " + string.Join(", ", left.Select(x => $"{x.mesh.name} (Full Controller on {x.holder})")) + ".",
                        "ft-muted", "ft-card__note"));
                }
            });
        }

        /// <summary>The search field and filter, built once so typing doesn't lose focus when the list redraws.</summary>
        VisualElement BuildKeysTools()
        {
            keysTools = new VisualElement();
            keysTools.AddToClassList("ft-key-tools");

            var search = new ToolbarSearchField { tooltip = "Search tracking keys and blendshapes" };
            search.AddToClassList("ft-key-tools__search");
            search.RegisterValueChangedCallback(e =>
            {
                keySearch = e.newValue ?? "";
                RefreshNow();
            });
            keysTools.Add(search);

            var segmented = new VisualElement();
            segmented.AddToClassList("ft-segmented");
            segmented.AddToClassList("ft-key-tools__filter");
            var filters = (KeyFilter[])System.Enum.GetValues(typeof(KeyFilter));
            filterButtons = new Button[filters.Length];
            for (var i = 0; i < filters.Length; i++)
            {
                var filter = filters[i];
                var button = new Button(() =>
                {
                    keyFilter = filter;
                    RefreshNow();
                }) { text = filter.ToString() };
                button.AddToClassList("ft-segmented__button");
                if (i == 0) button.AddToClassList("ft-segmented__button--first");
                if (i == filters.Length - 1) button.AddToClassList("ft-segmented__button--last");
                filterButtons[i] = button;
                segmented.Add(button);
            }
            keysTools.Add(segmented);

            var wrapper = new VisualElement();
            wrapper.Add(keysTools);
            wrapper.Add(BuildBulkMultiplier());
            wrapper.Add(BuildRegions());
            keysTools = wrapper;
            return keysTools;
        }

        /// <summary>One button per face region, to show that region's keys only.</summary>
        VisualElement BuildRegions()
        {
            var row = new VisualElement();
            row.AddToClassList("ft-key-regions");
            var regions = new (string region, string label)[]
            {
                (null, "All"), ("Eyes", "Eyes"), ("Brows", "Brows"), ("Cheeks & Nose", "Cheeks"), ("Jaw", "Jaw"),
                ("Mouth", "Mouth"), ("Lips", "Lips"), ("Tongue", "Tongue"), ("Neck & Throat", "Neck"),
            };
            for (var i = 0; i < regions.Length; i++)
            {
                var region = regions[i].region;
                var button = new Button(() =>
                {
                    keyRegion = region;
                    RefreshNow();
                }) { text = regions[i].label, tooltip = region ?? "Every face region" };
                button.AddToClassList("ft-key-region");
                regionButtons.Add((region, button));
                row.Add(button);
            }
            return row;
        }

        /// <summary>Sets the multiplier of every key the list shows, so a search or a filter picks which.</summary>
        VisualElement BuildBulkMultiplier()
        {
            var row = new VisualElement();
            row.AddToClassList("ft-key-bulk");
            bulkLabel = FTUI.Text("", "ft-muted", "ft-key-bulk__label");
            row.Add(bulkLabel);

            var field = new FloatField { value = 1f, tooltip = "1 is full strength, 0.8 stops the blendshapes at 80%. 0 to 10." };
            field.AddToClassList("ft-key__multiplier");
            row.Add(field);

            var set = new Button(() => SetMultipliers(shownKeys.Select(FTKeys.KeyName).ToList(), field.value))
            {
                text = "Set",
                tooltip = "Sets this multiplier on every mapped key shown below. Search or filter first to pick the keys.",
            };
            set.AddToClassList("ft-key-bulk__button");
            row.Add(set);
            return row;
        }

        void UpdateKeys(FaceTrackingSetupPlan plan, FTIssues notes)
        {
            if (plan.Set == null)
            {
                keysBadge.Hide();
                keysDetails.Hide();
                keysRows.Hide();
                keysTools.style.display = DisplayStyle.None;
                keysTable.style.display = DisplayStyle.None;
                return;
            }
            keysTools.style.display = DisplayStyle.Flex;
            keysTable.style.display = DisplayStyle.Flex;

            var mapped = plan.MappedKeys.Count;
            var total = mapped + plan.UnmappedKeys.Count;
            var fraction = total == 0 ? 0f : (float)mapped / total;
            var kind = fraction >= 0.9f ? FTKind.Ok : fraction >= 0.5f ? FTKind.Info : FTKind.Warning;
            keysBadge.Update($"{mapped}/{total}", e => e.Add(FTUI.Badge($"{mapped}/{total} mapped", kind)));
            keysDetails.Update($"{fraction}", e => e.Add(FTUI.Progress(fraction, fraction >= 0.5f ? FTKind.Ok : FTKind.Warning)));

            UpdateSuggestions(plan);
            if (suggestions.Count > 0)
            {
                notes.Info($"{(suggestions.Count == 1 ? "1 unmapped key has" : $"{suggestions.Count} unmapped keys have")} a blendshape with a similar name.",
                    string.Join("\n", suggestions.Select(x => $"{FTKeys.KeyName(x.Key)}: {x.Value}")) + "\nIt may be misspelled. Use them to map those keys by hand.",
                    ("Use", () => { foreach (var x in suggestions.ToList()) SetOverride(FTKeys.KeyName(x.Key), x.Value, plan); }));
            }

            if (plan.ArkitOverlaps.Count > 0)
            {
                var count = plan.ArkitOverlaps.Count;
                notes.Info($"{(count == 1 ? "1 key is" : $"{count} keys are")} left unmapped so blendshapes don't play twice.",
                    "This avatar has Unified Expressions and ARKit blendshapes. Unified Expressions comes first, and these keys' only match is an " +
                    "ARKit blendshape that also moves a key it already drives:\n" +
                    string.Join("\n", plan.ArkitOverlaps.OrderBy(x => x.Key).Select(x => $"{FTKeys.KeyName(x.Key)}: {x.Value.Arkit} (moves {FTKeys.KeyName(x.Value.Covered)})")) +
                    "\nMap them by hand if you want the ARKit blendshape anyway.");
            }

            // Filter buttons show their counts
            var counts = new[] { total, plan.UnmappedKeys.Count, plan.OverriddenKeys.Union(plan.Multipliers.Keys).Count() };
            for (var i = 0; i < filterButtons.Length; i++)
            {
                var filter = (KeyFilter)i;
                filterButtons[i].text = filter == KeyFilter.All ? "All" : $"{filter} ({counts[i]})";
                filterButtons[i].EnableInClassList("ft-segmented__button--selected", filter == keyFilter);
            }
            foreach (var (region, button) in regionButtons) button.EnableInClassList("ft-key-region--selected", region == keyRegion);

            var keys = plan.MappedKeys.Concat(plan.UnmappedKeys).OrderBy(k => k).ToList();
            var visible = keys.Where(k => MatchesFilter(plan, k)).ToList();
            var filtering = keyFilter != KeyFilter.All || keyRegion != null || !string.IsNullOrWhiteSpace(keySearch);

            shownKeys = visible.Where(k => plan.MappedKeys.Contains(k)).ToList();
            bulkLabel.text = filtering
                ? $"Multiplier for the {shownKeys.Count} {(shownKeys.Count == 1 ? "key" : "keys")} shown"
                : $"Multiplier for all {shownKeys.Count} keys";

            var listKey = string.Join("|", visible.Select(k => k + ":" + plan.Set.faceTrackingBlendshapeNames[k] + (plan.OverriddenKeys.Contains(k) ? "*" : "")
                                                              + (plan.Multipliers.TryGetValue(k, out var scale) ? "x" + scale : "")
                                                              + (suggestions.TryGetValue(k, out var hint) ? "?" + hint : "")
                                                              + (plan.ArkitOverlaps.TryGetValue(k, out var skip) ? "!" + skip.Arkit : "")))
                          + keyRegion + keyFilter + (plan.TongueSteps != null ? "|steps:" + string.Join(",", plan.TongueSteps.Names) : "");
            keysRows.Update(listKey, container =>
            {
                if (visible.Count == 0)
                {
                    container.Add(FTUI.Text(keyFilter == KeyFilter.Edited && string.IsNullOrWhiteSpace(keySearch)
                        ? "No keys are changed. Click a key's blendshape to map it by hand, or type in its multiplier."
                        : "No matching keys.", "ft-muted", "ft-card__note"));
                    return;
                }

                foreach (var group in visible.GroupBy(k => KeyGroup(FTKeys.KeyName(k))))
                {
                    // With every region shown, each gets a heading with how much of it is mapped
                    if (keyRegion == null)
                    {
                        var groupKeys = keys.Where(k => KeyGroup(FTKeys.KeyName(k)) == group.Key).ToList();
                        var heading = new VisualElement();
                        heading.AddToClassList("ft-key-group");
                        heading.Add(FTUI.Text(group.Key, "ft-key-group__name"));
                        heading.Add(FTUI.Text($"{groupKeys.Count(k => plan.MappedKeys.Contains(k))}/{groupKeys.Count}", "ft-key-group__count"));
                        container.Add(heading);
                    }

                    var index = 0;
                    foreach (var key in group)
                    {
                        var row = KeyRow(plan, key);
                        row.EnableInClassList("ft-key--alt", index++ % 2 == 1);
                        container.Add(row);
                    }
                }
            });
        }

        bool MatchesFilter(FaceTrackingSetupPlan plan, int key)
        {
            if (keyRegion != null && KeyGroup(FTKeys.KeyName(key)) != keyRegion) return false;
            if (keyFilter == KeyFilter.Unmapped && !plan.UnmappedKeys.Contains(key)) return false;
            if (keyFilter == KeyFilter.Edited && !plan.OverriddenKeys.Contains(key) && !plan.Multipliers.ContainsKey(key)) return false;
            if (string.IsNullOrWhiteSpace(keySearch)) return true;

            var search = keySearch.Trim();
            var blendshape = plan.Set.faceTrackingBlendshapeNames[key] ?? "";
            if (plan.TongueSteps != null && FTKeys.KeyName(key) == FTTongueSteps.Key) blendshape = string.Join(" ", plan.TongueSteps.Names);
            return FTKeys.KeyName(key).IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0
                   || blendshape.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        VisualElement KeyRow(FaceTrackingSetupPlan plan, int key)
        {
            var keyName = FTKeys.KeyName(key);
            var blendshape = plan.Set.faceTrackingBlendshapeNames[key];
            var mapped = plan.MappedKeys.Contains(key);
            var edited = plan.OverriddenKeys.Contains(key);
            plan.AutoMatches.TryGetValue(key, out var auto);

            var row = new VisualElement();
            row.AddToClassList("ft-key");
            row.Add(FTUI.Text(keyName, "ft-key__name"));

            if (plan.TongueSteps != null && keyName == FTTongueSteps.Key)
            {
                var steps = FTUI.Text($"{plan.TongueSteps.Step1} → {plan.TongueSteps.Step2}", "ft-key__value");
                steps.tooltip = $"Tongue steps: {plan.TongueSteps.Step1} over the first half of TongueOut, {plan.TongueSteps.Step2} over the second. " +
                                "Set Tongue Mode to TongueOut to map a single blendshape instead.";
                row.Add(steps);
                row.Add(MultiplierField(plan, key));
                row.Add(Spacer("ft-key__reset"));
                return row;
            }

            var value = new Button { text = mapped ? blendshape : "unmapped" };
            value.AddToClassList("ft-key__value");
            value.EnableInClassList("ft-key__value--unmapped", !mapped);
            value.EnableInClassList("ft-key__value--edited", edited);
            value.tooltip = edited
                ? $"Mapped by hand. Automatic match: {auto ?? "none"}."
                : mapped
                    ? (string.Equals(blendshape, keyName, System.StringComparison.OrdinalIgnoreCase) ? "Matched by name." : $"Matched by alias: {blendshape}.") + " Click to change."
                    : plan.ArkitOverlaps.TryGetValue(key, out var overlap)
                        ? $"Its only match is the ARKit blendshape '{overlap.Arkit}', which also moves {FTKeys.KeyName(overlap.Covered)}. " +
                          "That key already has a Unified Expressions blendshape, so it would play twice. Click to map it by hand anyway."
                        : "No blendshape matches this key. Click to pick one.";
            var arrow = new VisualElement { pickingMode = PickingMode.Ignore };
            arrow.AddToClassList("unity-base-popup-field__arrow");
            arrow.AddToClassList("ft-key__arrow");
            value.Add(arrow);
            value.clicked += () =>
            {
                new FTBlendshapePicker(keyName, plan.Blendshapes, picked => SetOverride(keyName, picked, plan)).Show(value.worldBound);
            };
            row.Add(value);

            if (!mapped && !edited && plan.ArkitOverlaps.TryGetValue(key, out var skipped))
            {
                var note = FTUI.Text($"skips {skipped.Arkit}", "ft-key__note");
                note.tooltip = value.tooltip;
                row.Add(note);
            }
            if (!mapped && suggestions.TryGetValue(key, out var suggestion))
            {
                var use = new Button(() => SetOverride(keyName, suggestion, plan)) { text = suggestion + "?", tooltip = $"'{suggestion}' looks like a misspelling of {keyName}. Click to use it." };
                use.AddToClassList("ft-key__suggestion");
                row.Add(use);
            }
            // Every row keeps both columns, so they line up under the header
            if (mapped) row.Add(MultiplierField(plan, key));
            else row.Add(Spacer("ft-key__multiplier"));
            if (edited)
            {
                var reset = new Button(() => SetOverride(keyName, null, plan)) { text = "↺", tooltip = $"Back to the automatic match ({auto ?? "unmapped"})." };
                reset.AddToClassList("ft-key__reset");
                row.Add(reset);
            }
            else row.Add(Spacer("ft-key__reset"));
            return row;
        }

        static VisualElement Spacer(string className)
        {
            var spacer = new VisualElement();
            spacer.AddToClassList(className);
            return spacer;
        }

        /// <summary>
        /// How strongly the key moves its blendshape. Applied when Enter is pressed or the field loses focus, so the
        /// list doesn't redraw while typing.
        /// </summary>
        FloatField MultiplierField(FaceTrackingSetupPlan plan, int key)
        {
            var keyName = FTKeys.KeyName(key);
            var changed = plan.Multipliers.TryGetValue(key, out var value);
            var field = new FloatField { value = changed ? value : 1f, isDelayed = true };
            field.AddToClassList("ft-key__multiplier");
            field.EnableInClassList("ft-key__multiplier--edited", changed);
            field.tooltip = $"Multiplier: how strongly {keyName} moves its blendshape. 1 is full strength, 0.8 stops it at 80%. 0 to {FaceTrackingSetupPlan.MaxMultiplier:0}.";
            field.RegisterValueChangedCallback(e => SetMultipliers(new List<string> { keyName }, e.newValue));
            return field;
        }

        /// <summary>Stores the multiplier for the keys. Only values other than 1 are kept.</summary>
        void SetMultipliers(List<string> keyNames, float value)
        {
            value = Mathf.Clamp(value, 0f, FaceTrackingSetupPlan.MaxMultiplier);
            var list = serializedObject.FindProperty(nameof(PawlygonFaceTracking.keyMultipliers));
            for (var i = list.arraySize - 1; i >= 0; i--)
            {
                if (keyNames.Contains(list.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(FTKeyMultiplier.key)).stringValue)) list.DeleteArrayElementAtIndex(i);
            }
            if (!Mathf.Approximately(value, 1f))
            {
                foreach (var keyName in keyNames)
                {
                    list.arraySize++;
                    var entry = list.GetArrayElementAtIndex(list.arraySize - 1);
                    entry.FindPropertyRelative(nameof(FTKeyMultiplier.key)).stringValue = keyName;
                    entry.FindPropertyRelative(nameof(FTKeyMultiplier.multiplier)).floatValue = value;
                }
            }
            serializedObject.ApplyModifiedProperties();
            RefreshNow();
        }

        /// <summary>
        /// Maps a key by hand, or back to the automatic match with null. Picking what it would match on its own just
        /// removes the manual mapping.
        /// </summary>
        void SetOverride(string keyName, string blendshape, FaceTrackingSetupPlan plan)
        {
            var key = FTKeys.KeyByName(keyName);
            plan.AutoMatches.TryGetValue(key, out var auto);
            if (blendshape != null && blendshape == (auto ?? "")) blendshape = null;

            var list = serializedObject.FindProperty(nameof(PawlygonFaceTracking.keyOverrides));
            for (var i = list.arraySize - 1; i >= 0; i--)
            {
                if (list.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(FTKeyOverride.key)).stringValue == keyName) list.DeleteArrayElementAtIndex(i);
            }
            if (blendshape != null)
            {
                list.arraySize++;
                var entry = list.GetArrayElementAtIndex(list.arraySize - 1);
                entry.FindPropertyRelative(nameof(FTKeyOverride.key)).stringValue = keyName;
                entry.FindPropertyRelative(nameof(FTKeyOverride.blendshape)).stringValue = blendshape;
            }
            serializedObject.ApplyModifiedProperties();
            RefreshNow();
        }

        void UpdateSuggestions(FaceTrackingSetupPlan plan)
        {
            var key = plan.Blendshapes.Length + "|" + string.Join(",", plan.Blendshapes.Take(8)) + "|" + string.Join(",", plan.UnmappedKeys)
                      + "|" + string.Join(",", plan.MappedKeys.Select(k => plan.Set.faceTrackingBlendshapeNames[k]));
            if (key == suggestionsKey) return;
            suggestionsKey = key;

            var used = new HashSet<string>(plan.MappedKeys.Select(k => plan.Set.faceTrackingBlendshapeNames[k]));
            suggestions = new Dictionary<int, string>();
            foreach (var unmapped in plan.UnmappedKeys)
            {
                // Keys left unmapped on purpose don't get suggestions
                if (plan.OverriddenKeys.Contains(unmapped) || plan.ArkitOverlaps.ContainsKey(unmapped)) continue;
                var suggestion = FTKeys.SuggestFor(unmapped, plan.Blendshapes, used);
                if (suggestion == null) continue;
                suggestions[unmapped] = suggestion;
                used.Add(suggestion);
            }
        }

        /// <summary>Face regions to group the tracking keys by, in face order.</summary>
        static string KeyGroup(string key)
        {
            if (key.StartsWith("Eye")) return "Eyes";
            if (key.StartsWith("Brow")) return "Brows";
            if (key.StartsWith("Cheek") || key.StartsWith("Nose") || key.StartsWith("Nasal")) return "Cheeks & Nose";
            if (key.StartsWith("Jaw")) return "Jaw";
            if (key.StartsWith("Mouth")) return "Mouth";
            if (key.StartsWith("Lip")) return "Lips";
            if (key.StartsWith("Tongue")) return "Tongue";
            return "Neck & Throat";
        }

        void UpdateResults(FaceTrackingSetupPlan plan, VRCAvatarDescriptor avatar)
        {
            const string unchanged = "unchanged";
            if (plan.Set == null)
            {
                lipSyncResult.text = dampenResult.text = eyelidsResult.text = gazeResult.text = eyeRangeResult.text = eyeRangeNote.text = "";
                return;
            }

            lipSyncResult.text = plan.UseFaceTrackingLipSync ? $"→ {plan.MappedVisemes}/{FTKeys.VisemeCount} visemes" : unchanged;
            lipSyncResult.tooltip = plan.VisemesFromDescriptor ? "Taken from the avatar's existing viseme setup." : "Matched by blendshape name.";
            dampenResult.text = Component.visemeDampenMode == FTVisemeDampenMode.None ? "off" : $"→ {Mathf.RoundToInt(Component.visemeDampenAmount * 100)}%";
            eyelidsResult.text = plan.UseFaceTrackingEyelids ? "→ EyeClosed" : unchanged;
            gazeResult.text = plan.GazeMode.HasValue ? "→ " + ObjectNames.NicifyVariableName(plan.GazeMode.Value.ToString()) : unchanged;
            UpdateEyeRange(plan, avatar);
        }

        void UpdateEyeRange(FaceTrackingSetupPlan plan, VRCAvatarDescriptor avatar)
        {
            var settings = avatar.customEyeLookSettings;
            var rotatesBones = plan.GazeMode == EyeGazeMode.Both || plan.GazeMode == EyeGazeMode.BoneRotation;
            var hasEyeBones = settings.leftEye != null && settings.rightEye != null;
            var fromRig = Component.eyeRotationRange == FTEyeRotationRange.HumanoidRig;
            eyeRangeRow.style.display = rotatesBones || fromRig ? DisplayStyle.Flex : DisplayStyle.None;

            // Keep Existing reads the descriptor; Humanoid Rig only shows a result once it could be worked out
            var shown = hasEyeBones && (!fromRig || plan.EyeRange != null);
            eyeRangeResult.text = shown ? EyeRangeText(avatar, plan) : "";
            eyeRangeResult.tooltip = shown ? "How far the eyes turn from Looking Straight: up, down, and to the sides." : "";
            if (!shown) eyePreview.Stop();
            eyeRangeRow.Q(className: "ft-eye-detail").style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;

            if (!fromRig)
            {
                eyeRangeNote.text = "From the descriptor's Eye Look section. Edit it there.";
            }
            else
            {
                eyeRangeNote.text = plan.EyeRange != null
                    ? "From the rig's humanoid eye limits. Only the upload gets it."
                    : "";
            }
        }

        static string EyeRangeText(VRCAvatarDescriptor avatar, FaceTrackingSetupPlan plan)
        {
            int Degrees(FTLegacyEyeRange.Direction direction)
            {
                var rotations = plan.EyeRange != null
                    ? FTLegacyEyeRange.Apply(avatar, plan.EyeRange, direction)
                    : FTLegacyEyeRange.FromDescriptor(avatar, direction);
                return Mathf.RoundToInt(FTLegacyEyeRange.Degrees(avatar, rotations));
            }
            var sides = (Degrees(FTLegacyEyeRange.Direction.Left) + Degrees(FTLegacyEyeRange.Direction.Right)) / 2;
            return $"{(plan.EyeRange != null ? "→ " : "")}↑{Degrees(FTLegacyEyeRange.Direction.Up)}° ↓{Degrees(FTLegacyEyeRange.Direction.Down)}° ↔{sides}°";
        }

    }
}
