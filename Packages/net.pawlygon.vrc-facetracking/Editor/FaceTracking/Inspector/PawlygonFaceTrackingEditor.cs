using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Pawlygon.FaceTracking.Editor
{
    [CustomEditor(typeof(PawlygonFaceTracking))]
    internal sealed class PawlygonFaceTrackingEditor : FTComponentEditor<PawlygonFaceTracking>
    {
        // Scanning every component on the avatar is slow on big avatars, so these are cached briefly
        const double SlowScanSeconds = 4;

        readonly FTDynamic meshBadge = new FTDynamic();
        readonly FTDynamic meshList = new FTDynamic();
        readonly FTDynamic keysBadge = new FTDynamic();
        readonly FTDynamic keysDetails = new FTDynamic();
        readonly FTDynamic keysList = new FTDynamic();
        PropertyField manualMeshes;
        VisualElement keysTools;
        Button[] filterButtons;

        enum KeyFilter
        {
            All,
            Unmapped,
            Edited,
        }

        KeyFilter keyFilter;
        string keySearch = "";
        readonly HashSet<string> openGroups = new HashSet<string>();

        // Suggestions only change with the blendshapes and the unmapped keys, and are slow to find
        string suggestionsKey;
        Dictionary<int, string> suggestions = new Dictionary<int, string>();
        Label lipSyncResult, eyelidsResult, gazeResult;

        double nextSlowScan;
        List<LegacyOscDetector.Finding> legacyFindings = new List<LegacyOscDetector.Finding>();
        List<FTBlendshapeOptimizers.Finding> blendshapeOptimizers = new List<FTBlendshapeOptimizers.Finding>();

        protected override string Title => "Face Tracking";
        protected override string Subtitle => "Native face tracking, applied when the avatar is built.";

        protected override string Details =>
            "Sets up VRChat's native face tracking on the avatar descriptor when the avatar is built or uploaded: " +
            "meshes, blendshape mapping, visemes, eyelids and eye gaze. The avatar in your scene isn't changed. " +
            "To inspect the result, right-click the avatar > !Pawlygon > Face Tracking > Build Test Copy.";

        protected override void BuildBody(VisualElement body)
        {
            var meshes = FTUI.Card(body, "Meshes", out var meshExtras);
            meshExtras.Add(meshBadge.Element);
            meshes.Add(FTUI.Segmented(serializedObject.FindProperty(nameof(PawlygonFaceTracking.meshMode)),
                "Auto picks every mesh with face tracking blendshapes."));
            manualMeshes = new PropertyField(serializedObject.FindProperty(nameof(PawlygonFaceTracking.meshes)), "Meshes");
            meshes.Add(manualMeshes);
            meshes.Add(meshList.Element);

            var keys = FTUI.Card(body, "Tracking Keys", out var keysExtras);
            keysExtras.Add(keysBadge.Element);
            keys.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFaceTracking.useAllTrackingKeys)), "All Tracking Keys"));
            keys.Add(keysDetails.Element);
            keys.Add(BuildKeysTools());
            keys.Add(keysList.Element);

            var face = FTUI.Card(body, "Lip Sync & Eyes", out _);
            face.Add(FieldWithResult(nameof(PawlygonFaceTracking.lipSync), "Lip Sync", out lipSyncResult));
            face.Add(FieldWithResult(nameof(PawlygonFaceTracking.eyelids), "Eyelids", out eyelidsResult));
            face.Add(FieldWithResult(nameof(PawlygonFaceTracking.eyeGaze), "Eye Gaze", out gazeResult));

            var advanced = FTUI.Collapsible("Advanced");
            advanced.AddToClassList("ft-advanced");
            var dampenMode = serializedObject.FindProperty(nameof(PawlygonFaceTracking.visemeDampenMode));
            var dampenAmount = new PropertyField(serializedObject.FindProperty(nameof(PawlygonFaceTracking.visemeDampenAmount)), "Dampen Amount");
            advanced.Add(new PropertyField(dampenMode, "Viseme Dampening"));
            advanced.Add(dampenAmount);
            face.Add(advanced);

            void UpdateVisibility()
            {
                manualMeshes.style.display = Component.meshMode == FTMeshMode.Manual ? DisplayStyle.Flex : DisplayStyle.None;
                dampenAmount.style.display = Component.visemeDampenMode != FTVisemeDampenMode.None ? DisplayStyle.Flex : DisplayStyle.None;
            }
            UpdateVisibility();
            body.TrackSerializedObjectValue(serializedObject, _ => UpdateVisibility());
        }

        VisualElement FieldWithResult(string property, string label, out Label result)
        {
            var row = new VisualElement();
            row.AddToClassList("ft-field-row");
            row.Add(new PropertyField(serializedObject.FindProperty(property), label));
            result = FTUI.Text("", "ft-result");
            row.Add(result);
            return row;
        }

        protected override void Refresh(VRCAvatarDescriptor avatar, FTIssues issues)
        {
            AddAvatarIssues(avatar, issues);

            var plan = FaceTrackingSetupPlan.Resolve(Component, avatar);
            foreach (var error in plan.Errors) issues.Error(error);
            foreach (var warning in plan.Warnings) issues.Warning(warning);

            UpdateMeshes(plan);
            UpdateKeys(plan, issues);
            UpdateResults(plan);
        }

        void AddAvatarIssues(VRCAvatarDescriptor avatar, FTIssues issues)
        {
            var other = FTAvatarUtils.GetBuildComponents<PawlygonFaceTracking>(avatar.gameObject).FirstOrDefault(c => c != Component);
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
                blendshapeOptimizers = FTBlendshapeOptimizers.Find(avatar.gameObject);
            }

            foreach (var finding in legacyFindings.Where(f => f.Holder != null))
            {
                var holder = finding.Holder;
                // Disabling the avatar root would hide the whole avatar, so only offer to select it
                var onRoot = holder == avatar.gameObject;
                var actions = onRoot
                    ? new[] { ("Select", (System.Action)(() => Select(holder))) }
                    : new[] { ("Select", (System.Action)(() => Select(holder))), ("Disable", () => DisableLegacy(holder)) };
                if (finding.IsPawlygonLegacy)
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

            var key = Component.meshMode + string.Join("|", names);
            meshList.Update(key, e =>
            {
                if (Component.meshMode == FTMeshMode.Manual) return;
                if (plan.Meshes.Count == 0)
                {
                    e.Add(FTUI.Text("No mesh with face tracking blendshapes found.", "ft-muted"));
                    return;
                }
                var meshes = plan.Meshes.ToList();
                e.Add(FTUI.Chips(names, name => Select(meshes.FirstOrDefault(m => m != null && m.name == name)), "Click to select"));
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
            return keysTools;
        }

        void UpdateKeys(FaceTrackingSetupPlan plan, FTIssues issues)
        {
            if (plan.Set == null)
            {
                keysBadge.Hide();
                keysDetails.Hide();
                keysList.Hide();
                keysTools.style.display = DisplayStyle.None;
                return;
            }
            keysTools.style.display = DisplayStyle.Flex;

            var mapped = plan.MappedKeys.Count;
            var total = mapped + plan.UnmappedKeys.Count;
            var fraction = total == 0 ? 0f : (float)mapped / total;
            var kind = fraction >= 0.9f ? FTKind.Ok : fraction >= 0.5f ? FTKind.Info : FTKind.Warning;

            keysBadge.Update($"{mapped}/{total}", e => e.Add(FTUI.Badge($"{mapped}/{total} mapped", kind)));
            keysDetails.Update($"{fraction}", e => e.Add(FTUI.Progress(fraction, fraction >= 0.5f ? FTKind.Ok : FTKind.Warning)));

            UpdateSuggestions(plan);
            if (suggestions.Count > 0)
            {
                issues.Info($"{(suggestions.Count == 1 ? "1 unmapped key has" : $"{suggestions.Count} unmapped keys have")} a blendshape with a similar name.",
                    string.Join("\n", suggestions.Select(x => $"{FTKeys.KeyName(x.Key)}: {x.Value}")) + "\nIt may be misspelled. Use them to map those keys by hand.",
                    ("Use", () => { foreach (var x in suggestions.ToList()) SetOverride(FTKeys.KeyName(x.Key), x.Value, plan); }));
            }

            // Filter buttons show their counts
            var counts = new[] { total, plan.UnmappedKeys.Count, plan.OverriddenKeys.Count };
            for (var i = 0; i < filterButtons.Length; i++)
            {
                var filter = (KeyFilter)i;
                filterButtons[i].text = filter == KeyFilter.All ? "All" : $"{filter} ({counts[i]})";
                filterButtons[i].EnableInClassList("ft-segmented__button--selected", filter == keyFilter);
            }

            var keys = plan.MappedKeys.Concat(plan.UnmappedKeys).OrderBy(k => k).ToList();
            var visible = keys.Where(k => MatchesFilter(plan, k)).ToList();
            var filtering = keyFilter != KeyFilter.All || !string.IsNullOrWhiteSpace(keySearch);

            var listKey = string.Join("|", visible.Select(k => k + ":" + plan.Set.faceTrackingBlendshapeNames[k] + (plan.OverriddenKeys.Contains(k) ? "*" : "")
                                                              + (suggestions.TryGetValue(k, out var hint) ? "?" + hint : "")))
                          + filtering + string.Join(",", openGroups);
            keysList.Update(listKey, container =>
            {
                if (visible.Count == 0)
                {
                    container.Add(FTUI.Text(keyFilter == KeyFilter.Edited && string.IsNullOrWhiteSpace(keySearch)
                        ? "No keys are mapped by hand. Click a key's blendshape to change it."
                        : "No matching keys.", "ft-muted", "ft-card__note"));
                    return;
                }

                foreach (var group in visible.GroupBy(k => KeyGroup(FTKeys.KeyName(k))))
                {
                    var groupKeys = keys.Where(k => KeyGroup(FTKeys.KeyName(k)) == group.Key).ToList();
                    var groupMapped = groupKeys.Count(k => plan.MappedKeys.Contains(k));
                    var name = group.Key;

                    // Searching or filtering opens every group that has results
                    var foldout = FTUI.Collapsible($"{name}  {groupMapped}/{groupKeys.Count}", filtering || openGroups.Contains(name));
                    foldout.RegisterValueChangedCallback(e =>
                    {
                        if (e.target != foldout || filtering) return;
                        if (e.newValue) openGroups.Add(name);
                        else openGroups.Remove(name);
                    });
                    foreach (var key in group) foldout.Add(KeyRow(plan, key));
                    container.Add(foldout);
                }
            });
        }

        bool MatchesFilter(FaceTrackingSetupPlan plan, int key)
        {
            if (keyFilter == KeyFilter.Unmapped && !plan.UnmappedKeys.Contains(key)) return false;
            if (keyFilter == KeyFilter.Edited && !plan.OverriddenKeys.Contains(key)) return false;
            if (string.IsNullOrWhiteSpace(keySearch)) return true;

            var search = keySearch.Trim();
            var blendshape = plan.Set.faceTrackingBlendshapeNames[key] ?? "";
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

            var value = new Button { text = mapped ? blendshape : "unmapped" };
            value.AddToClassList("ft-key__value");
            value.EnableInClassList("ft-key__value--unmapped", !mapped);
            value.EnableInClassList("ft-key__value--edited", edited);
            value.tooltip = edited
                ? $"Mapped by hand. Automatic match: {auto ?? "none"}."
                : mapped
                    ? (string.Equals(blendshape, keyName, System.StringComparison.OrdinalIgnoreCase) ? "Matched by name." : $"Matched by alias: {blendshape}.") + " Click to change."
                    : "No blendshape matches this key. Click to pick one.";
            value.clicked += () =>
            {
                new FTBlendshapePicker(keyName, plan.Blendshapes, picked => SetOverride(keyName, picked, plan)).Show(value.worldBound);
            };
            row.Add(value);

            if (!mapped && suggestions.TryGetValue(key, out var suggestion))
            {
                var use = new Button(() => SetOverride(keyName, suggestion, plan)) { text = suggestion + "?", tooltip = $"'{suggestion}' looks like a misspelling of {keyName}. Click to use it." };
                use.AddToClassList("ft-key__suggestion");
                row.Add(use);
            }
            if (edited)
            {
                var reset = new Button(() => SetOverride(keyName, null, plan)) { text = "↺", tooltip = $"Back to the automatic match ({auto ?? "unmapped"})." };
                reset.AddToClassList("ft-key__reset");
                row.Add(reset);
            }
            return row;
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
                if (plan.OverriddenKeys.Contains(unmapped)) continue;
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

        void UpdateResults(FaceTrackingSetupPlan plan)
        {
            const string unchanged = "unchanged";
            if (plan.Set == null)
            {
                lipSyncResult.text = eyelidsResult.text = gazeResult.text = "";
                return;
            }

            lipSyncResult.text = plan.UseFaceTrackingLipSync ? $"→ {plan.MappedVisemes}/{FTKeys.VisemeCount} visemes" : unchanged;
            lipSyncResult.tooltip = plan.VisemesFromDescriptor ? "Taken from the avatar's existing viseme setup." : "Matched by blendshape name.";
            eyelidsResult.text = plan.UseFaceTrackingEyelids ? "→ EyeClosed" : unchanged;
            gazeResult.text = plan.GazeMode.HasValue ? "→ " + ObjectNames.NicifyVariableName(plan.GazeMode.Value.ToString()) : unchanged;
        }

        /// <summary>
        /// Tags the object EditorOnly and deactivates it. VRCFury also processes inactive objects,
        /// so the tag is what keeps it out of the build. Reversible with Undo.
        /// </summary>
        static void DisableLegacy(GameObject holder)
        {
            if (holder == null) return;
            Undo.RecordObject(holder, "Disable legacy face tracking");
            holder.tag = "EditorOnly";
            holder.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(holder);
        }

    }
}
