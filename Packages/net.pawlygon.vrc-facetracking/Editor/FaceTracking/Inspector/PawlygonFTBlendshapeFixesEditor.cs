using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Pawlygon.FaceTracking.Editor
{
    [CustomEditor(typeof(PawlygonFTBlendshapeFixes))]
    internal sealed class PawlygonFTBlendshapeFixesEditor : FTComponentEditor<PawlygonFTBlendshapeFixes>
    {
        readonly FTDynamic detectedBadge = new FTDynamic();
        readonly FTDynamic detectedList = new FTDynamic();
        readonly FTDynamic manualBadge = new FTDynamic();
        readonly List<Image> manualStates = new List<Image>();

        // Scanning every component on the avatar is slow on big avatars, so it's cached briefly
        const double LegacyScanSeconds = 4;
        double nextLegacyScan;
        List<LegacyOscDetector.Finding> legacyFixes = new List<LegacyOscDetector.Finding>();
        VisualElement manualRows;
        int renderedManualCount = -1;

        SerializedProperty FixesProperty => serializedObject.FindProperty(nameof(PawlygonFTBlendshapeFixes.fixes));

        protected override string Title => "Blendshape Fixes";
        protected override string Subtitle => "Drives helper shapes that prevent clipping between face tracking shapes.";

        protected override string Details =>
            "Fix shapes like MouthSmileLeft-EyeClosedLeft are driven straight from face tracking when the avatar is built, " +
            "from the tracking keys behind the shapes they fix. Blendshapes named after face tracking shapes joined with '-' " +
            "are detected automatically. Manual entries replace detected ones with the same name.";

        protected override void BuildBody(VisualElement body)
        {
            body.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFTBlendshapeFixes.combine)), "Combine"));

            var detected = FTUI.Card(body, "Detected", out var detectedExtras);
            var autoDetect = new Toggle { text = "Auto-detect", tooltip = "Drive every blendshape named after the face tracking shapes it fixes, joined with '-'." };
            autoDetect.AddToClassList("ft-toggle");
            autoDetect.BindProperty(serializedObject.FindProperty(nameof(PawlygonFTBlendshapeFixes.autoDetect)));
            detectedExtras.Add(autoDetect);
            detectedExtras.Add(detectedBadge.Element);
            detected.Add(detectedList.Element);

            var manual = FTUI.Card(body, "Manual", out var manualExtras);
            manualExtras.Add(manualBadge.Element);
            manualRows = new VisualElement();
            manual.Add(manualRows);
            var add = new Button(() => AddManual("", "")) { text = "Add Fix" };
            add.AddToClassList("ft-add-button");
            manual.Add(add);
        }

        protected override void Refresh(VRCAvatarDescriptor avatar, FTIssues issues)
        {
            if (EditorApplication.timeSinceStartup >= nextLegacyScan)
            {
                nextLegacyScan = EditorApplication.timeSinceStartup + LegacyScanSeconds;
                legacyFixes = LegacyOscDetector.Find(avatar.gameObject).Where(f => f.IsBlendshapeFixes).ToList();
            }
            foreach (var finding in legacyFixes.Where(f => f.Holder != null))
            {
                var holder = finding.Holder;
                issues.Error($"1.x Blendshape Fixes on '{finding.Path}'.",
                    "This component replaces it: both would drive the fix shapes. Disable tags it EditorOnly (undoable). " +
                    "If you keep it, every build asks to remove it.",
                    ("Select", () => Select(holder)), ("Disable", () => LegacyOscDetector.Disable(holder)));
            }

            // Same meshes and mapping the build uses: the setup component's, or what's already on the descriptor
            var setup = FTAvatarUtils.GetBuildComponents<PawlygonFaceTracking>(avatar.gameObject).FirstOrDefault();
            FTFixPlan plan;
            if (setup != null)
            {
                var setupPlan = FaceTrackingSetupPlan.Resolve(setup, avatar);
                plan = FTFixPlan.Resolve(Component, setupPlan.Meshes, setupPlan.Set != null ? new[] { setupPlan.Set } : new VRCAvatarDescriptor.FaceTrackingBlendshapesSet[0]);
            }
            else if (avatar.enableFaceTracking)
            {
                plan = FTFixPlan.Resolve(Component, avatar.faceTrackingMeshes ?? new List<SkinnedMeshRenderer>(),
                    avatar.faceTrackingBlendshapesSetList ?? new List<VRCAvatarDescriptor.FaceTrackingBlendshapesSet>());
            }
            else
            {
                issues.Error("Face tracking isn't enabled on this avatar.",
                    "Add a Pawlygon Face Tracking component, or enable face tracking on the avatar descriptor.");
                plan = new FTFixPlan();
            }

            UpdateDetected(plan);
            UpdateManual(plan);
        }

        void UpdateDetected(FTFixPlan plan)
        {
            var detected = plan.Entries.Where(e => !e.IsManual).ToList();
            var driven = detected.Count(e => !e.Excluded);
            detectedBadge.Update($"{driven}/{detected.Count}", e => e.Add(FTUI.Badge($"{driven} driven", driven > 0 ? FTKind.Ok : FTKind.Muted)));

            var key = $"{Component.autoDetect}|" + string.Join("|", detected.Select(e => e.Blendshape + e.Expression + e.Excluded));
            detectedList.Update(key, container =>
            {
                if (!Component.autoDetect)
                {
                    container.Add(FTUI.Text("Auto-detect is off.", "ft-muted"));
                    return;
                }
                if (detected.Count == 0)
                {
                    container.Add(FTUI.Text("No fix shapes found on the face tracking meshes.", "ft-muted"));
                    return;
                }

                foreach (var entry in detected)
                {
                    var name = entry.Blendshape;
                    var row = new VisualElement { tooltip = "Fixes " + string.Join(" and ", entry.Sources) };
                    row.AddToClassList("ft-check");

                    var toggle = new Toggle { value = !entry.Excluded };
                    toggle.AddToClassList("ft-check__toggle");
                    toggle.RegisterValueChangedCallback(e => SetExcluded(name, !e.newValue));
                    row.Add(toggle);

                    var text = new VisualElement();
                    text.AddToClassList("ft-check__text");
                    text.Add(FTUI.Text(name, "ft-param__base"));
                    text.Add(FTUI.Text("= " + entry.Expression, "ft-param__expression"));
                    row.Add(text);
                    container.Add(row);
                }
            });
        }

        void UpdateManual(FTFixPlan plan)
        {
            var property = FixesProperty;
            manualBadge.Update(property.arraySize.ToString(), e => e.Add(FTUI.Badge(property.arraySize.ToString(), FTKind.Muted)));

            // Rows are only rebuilt when entries are added or removed, so typing keeps focus
            if (property.arraySize != renderedManualCount) RebuildManualRows();

            for (var i = 0; i < manualStates.Count && i < Component.fixes.Count; i++)
            {
                var name = Component.fixes[i].blendshape?.Trim();
                var entry = plan.Entries.FirstOrDefault(e => e.IsManual && e.Blendshape == name);
                string problem;
                if (string.IsNullOrEmpty(name)) problem = "Enter the fix blendshape.";
                else problem = entry?.Problem;

                // The build skips these with a warning, it doesn't fail
                manualStates[i].image = FTUI.Icon(problem == null ? FTKind.Ok : FTKind.Warning);
                manualStates[i].tooltip = problem ?? "= " + entry?.Expression;
            }
        }

        void RebuildManualRows()
        {
            var property = FixesProperty;
            renderedManualCount = property.arraySize;
            manualRows.Clear();
            manualStates.Clear();

            if (property.arraySize == 0)
            {
                manualRows.Add(FTUI.Text("No manual fixes.", "ft-muted"));
                return;
            }

            for (var i = 0; i < property.arraySize; i++)
            {
                var element = property.GetArrayElementAtIndex(i);
                var index = i;
                var row = new VisualElement();
                row.AddToClassList("ft-manual");

                var blendshape = new TextField { tooltip = "The fix blendshape that gets driven." };
                blendshape.AddToClassList("ft-manual__name");
                blendshape.BindProperty(element.FindPropertyRelative(nameof(FTBlendshapeFix.blendshape)));
                row.Add(blendshape);
                row.Add(FTUI.Text("fixes", "ft-manual__equals"));

                var sources = new TextField { tooltip = "The blendshapes it fixes, separated by commas." };
                sources.AddToClassList("ft-manual__expression");
                sources.BindProperty(element.FindPropertyRelative(nameof(FTBlendshapeFix.sources)));
                row.Add(sources);

                var state = new Image();
                state.AddToClassList("ft-manual__state");
                manualStates.Add(state);
                row.Add(state);

                var remove = new Button(() => RemoveManual(index)) { text = "✕", tooltip = "Remove" };
                remove.AddToClassList("ft-manual__button");
                row.Add(remove);

                manualRows.Add(row);
            }
        }

        void AddManual(string blendshape, string sources)
        {
            var property = FixesProperty;
            property.arraySize++;
            var element = property.GetArrayElementAtIndex(property.arraySize - 1);
            element.FindPropertyRelative(nameof(FTBlendshapeFix.blendshape)).stringValue = blendshape;
            element.FindPropertyRelative(nameof(FTBlendshapeFix.sources)).stringValue = sources;
            serializedObject.ApplyModifiedProperties();
            RefreshNow();
        }

        void RemoveManual(int index)
        {
            var property = FixesProperty;
            if (index >= property.arraySize) return;
            property.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            RefreshNow();
        }

        void SetExcluded(string name, bool excluded)
        {
            var list = serializedObject.FindProperty(nameof(PawlygonFTBlendshapeFixes.excludedFixes));
            var index = -1;
            for (var i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).stringValue == name) index = i;
            }

            if (excluded && index < 0)
            {
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).stringValue = name;
            }
            else if (!excluded && index >= 0)
            {
                list.DeleteArrayElementAtIndex(index);
            }
            serializedObject.ApplyModifiedProperties();
            RefreshNow();
        }
    }
}
