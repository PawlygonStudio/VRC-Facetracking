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
        protected override string Title => "Pawlygon Face Tracking";

        protected override string Description =>
            "Sets up VRChat native face tracking when the avatar is built or uploaded. " +
            "Your avatar and its descriptor aren't changed in the scene.";

        protected override void BuildFields(VisualElement root)
        {
            var meshMode = serializedObject.FindProperty(nameof(PawlygonFaceTracking.meshMode));
            var meshes = new PropertyField(serializedObject.FindProperty(nameof(PawlygonFaceTracking.meshes)));

            root.Add(new PropertyField(meshMode));
            root.Add(meshes);
            root.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFaceTracking.useAllTrackingKeys))));
            root.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFaceTracking.lipSync))));
            root.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFaceTracking.eyelids))));
            root.Add(new PropertyField(serializedObject.FindProperty(nameof(PawlygonFaceTracking.eyeGaze))));

            var dampenMode = serializedObject.FindProperty(nameof(PawlygonFaceTracking.visemeDampenMode));
            var dampenAmount = new PropertyField(serializedObject.FindProperty(nameof(PawlygonFaceTracking.visemeDampenAmount)));
            root.Add(new PropertyField(dampenMode));
            root.Add(dampenAmount);

            void UpdateVisibility()
            {
                meshes.style.display = meshMode.enumValueIndex == (int)FTMeshMode.Manual ? DisplayStyle.Flex : DisplayStyle.None;
                dampenAmount.style.display = dampenMode.enumValueIndex != (int)FTVisemeDampenMode.None ? DisplayStyle.Flex : DisplayStyle.None;
            }
            UpdateVisibility();
            root.TrackSerializedObjectValue(serializedObject, _ =>
            {
                UpdateVisibility();
                RefreshStatus();
            });
        }

        protected override void BuildStatus(FTStatus status, VRCAvatarDescriptor avatar)
        {
            var otherSetups = FTAvatarUtils.GetBuildComponents<PawlygonFaceTracking>(avatar.gameObject).Where(c => c != Component).ToList();
            if (otherSetups.Count > 0)
            {
                status.Box("Only one Pawlygon Face Tracking component is allowed per avatar. Another one is on " +
                           $"'{FTAvatarUtils.GetPath(otherSetups[0].transform, avatar.transform)}'.",
                    HelpBoxMessageType.Error, ("Select", () => Selection.activeGameObject = otherSetups[0].gameObject));
            }

            AddLegacyStatus(status, avatar);

            if (avatar.enableFaceTracking || avatar.faceTrackingBlendshapesSetList?.Count > 0 || avatar.faceTrackingParameterBehaviors?.Count > 0)
            {
                status.Warning("The avatar descriptor already has a face tracking setup. It will be replaced by this component when the avatar is built.");
            }

            if (HasVrcFuryBlendshapeOptimizer(avatar))
            {
                status.Warning("VRCFury's Blendshape Optimizer doesn't know about native face tracking and will remove the face tracking blendshapes. Remove it or keep it disabled.");
            }

            AddPlanStatus(status, avatar);

            status.Add("testcopy", () => new Button(() => FTTestCopy.Build(avatar))
            {
                text = "Build Test Copy",
                tooltip = "Builds a copy of the avatar in the scene with face tracking applied, so you can inspect the result on its descriptor.",
                style = { marginTop = 6, height = 24 },
            });
        }

        static void AddLegacyStatus(FTStatus status, VRCAvatarDescriptor avatar)
        {
            foreach (var finding in LegacyOscDetector.Find(avatar.gameObject))
            {
                var holder = finding.Holder;
                var select = ("Select", (System.Action)(() => Selection.activeGameObject = holder));

                if (finding.IsPawlygonLegacy)
                {
                    status.Box($"Legacy OSC face tracking found on '{finding.Path}'. You can't use it together with native face tracking: " +
                               "both would drive the same blendshapes. If you keep it, you'll be asked to remove it from each build.",
                        HelpBoxMessageType.Error, select, ("Disable", () => DisableLegacy(holder)));
                }
                else
                {
                    status.Box($"'{finding.Path}' merges '{finding.Controller.name}', which looks like an OSC face tracking setup. " +
                               "You can't use it together with native face tracking. Remove or disable it.",
                        HelpBoxMessageType.Warning, select, ("Disable", () => DisableLegacy(holder)));
                }
            }
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

        void AddPlanStatus(FTStatus status, VRCAvatarDescriptor avatar)
        {
            var plan = FaceTrackingSetupPlan.Resolve(Component, avatar);
            foreach (var error in plan.Errors) status.Error(error);
            foreach (var warning in plan.Warnings) status.Warning(warning);
            if (plan.Set == null) return;

            var keyTotal = plan.MappedKeys.Count + plan.UnmappedKeys.Count;
            var meshes = string.Join(", ", plan.Meshes.Select(m => m.name));
            var visemes = plan.UseFaceTrackingLipSync
                ? $"{plan.MappedVisemes} of {FTKeys.VisemeCount}" + (plan.VisemesFromDescriptor ? " (from the avatar's viseme setup)" : "")
                : "Unchanged";
            var eyelids = plan.UseFaceTrackingEyelids ? "Face tracking" : "Unchanged";
            var gaze = plan.GazeMode.HasValue ? ObjectNames.NicifyVariableName(plan.GazeMode.Value.ToString()) : "Unchanged";
            var unmapped = string.Join(", ", plan.UnmappedKeys.Select(FTKeys.KeyName));

            status.Add($"plan:{meshes}|{plan.MappedKeys.Count}/{keyTotal}|{visemes}|{eyelids}|{gaze}|{unmapped}", () =>
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
                box.Add(FTInspectorUI.Row("Meshes", meshes));
                box.Add(FTInspectorUI.Row("Tracking keys", $"{plan.MappedKeys.Count} of {keyTotal} mapped"));
                box.Add(FTInspectorUI.Row("Visemes", visemes));
                box.Add(FTInspectorUI.Row("Eyelids", eyelids));
                box.Add(FTInspectorUI.Row("Eye gaze", gaze));

                if (plan.UnmappedKeys.Count > 0)
                {
                    var foldout = new Foldout { text = $"Unmapped keys ({plan.UnmappedKeys.Count})", value = false };
                    foldout.Add(new Label(unmapped) { style = { whiteSpace = WhiteSpace.Normal } });
                    box.Add(foldout);
                }
                return box;
            });
        }

        static bool HasVrcFuryBlendshapeOptimizer(VRCAvatarDescriptor avatar)
        {
            foreach (var behaviour in avatar.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().FullName != "VF.Model.VRCFury") continue;
                if (FTAvatarUtils.IsEditorOnly(behaviour.transform, avatar.transform)) continue;

                using (var serialized = new SerializedObject(behaviour))
                {
                    var content = serialized.FindProperty("content");
                    if (content != null && content.managedReferenceFullTypename.EndsWith("BlendshapeOptimizer")) return true;
                }
            }
            return false;
        }
    }
}
