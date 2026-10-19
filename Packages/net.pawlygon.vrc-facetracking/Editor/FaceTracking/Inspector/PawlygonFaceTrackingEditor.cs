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
        PropertyField manualMeshes;
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
            UpdateKeys(plan);
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
                    $"It doesn't know about native face tracking and removes blendshapes it thinks are unused, so the build stops. {optimizer.Fix}",
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

        void UpdateKeys(FaceTrackingSetupPlan plan)
        {
            if (plan.Set == null)
            {
                keysBadge.Hide();
                keysDetails.Hide();
                return;
            }

            var mapped = plan.MappedKeys.Count;
            var total = mapped + plan.UnmappedKeys.Count;
            var fraction = total == 0 ? 0f : (float)mapped / total;
            var kind = fraction >= 0.9f ? FTKind.Ok : fraction >= 0.5f ? FTKind.Info : FTKind.Warning;

            keysBadge.Update($"{mapped}/{total}", e => e.Add(FTUI.Badge($"{mapped}/{total} mapped", kind)));

            var unmapped = plan.UnmappedKeys.Select(FTKeys.KeyName).ToList();
            keysDetails.Update(string.Join("|", unmapped) + total, e =>
            {
                e.Add(FTUI.Progress(fraction, fraction >= 0.5f ? FTKind.Ok : FTKind.Warning));
                if (unmapped.Count == 0) return;

                var foldout = FTUI.Collapsible($"Unmapped ({unmapped.Count})");
                foldout.Add(FTUI.Chips(unmapped, null, "No blendshape matches this tracking key"));
                e.Add(foldout);
            });
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
