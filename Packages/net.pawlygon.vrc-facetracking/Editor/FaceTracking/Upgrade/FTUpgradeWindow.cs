using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;
using EyeGazeMode = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.CustomEyeLookSettings.EyeGazeMode;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Walks an avatar from Pawlygon VRCFT 1.x to 2.0: removing the 1.x prefabs and adding the new one. Opens
    /// by itself when a scene with a 1.x setup is open, once per scene and session, unless it's turned off for the
    /// project. Also under Tools > !Pawlygon > Face Tracking.
    /// </summary>
    internal sealed class FTUpgradeWindow : EditorWindow
    {
        const string MenuPath = "Tools/!Pawlygon/Face Tracking/Upgrade from 1.x";
        const string PrefabGuid = "42420136500449ad84ede7b1284015ac";
        const string GuideUrl = "https://github.com/PawlygonStudio/VRC-Facetracking#upgrading-from-1x";
        const string ShownScenesKey = "PawlygonFT.UpgradeWindow.ShownScenes";

        // Per project: EditorPrefs are shared by every project on the machine
        static string HiddenKey => "PawlygonFT.UpgradeWindow.Hidden." + Application.dataPath;

        sealed class AvatarStatus
        {
            public VRCAvatarDescriptor Avatar;
            public List<LegacyOscDetector.Finding> FaceTracking;
            public List<LegacyOscDetector.Finding> Fixes;
            public bool HasNewPrefab;
            public bool Done => FaceTracking.Count == 0 && Fixes.Count == 0 && HasNewPrefab;
        }

        // Avatars this window found a 1.x setup on, so they stay listed (as done) once it's removed
        readonly HashSet<int> tracked = new HashSet<int>();
        VisualElement content;
        bool rebuildQueued;

        [MenuItem(MenuPath, false, 20)]
        static void OpenFromMenu() => Open();

        public static void Open()
        {
            var window = GetWindow<FTUpgradeWindow>();
            window.titleContent = new GUIContent("Upgrade to 2.0");
            window.minSize = new Vector2(380, 320);
            window.Show();
            window.Rebuild();
        }

        [InitializeOnLoadMethod]
        static void Watch()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += OpenForLegacyScenes;
            EditorSceneManager.sceneOpened += (_, __) => EditorApplication.delayCall += OpenForLegacyScenes;
        }

        /// <summary>Opens the window for open scenes with a 1.x setup it hasn't been shown for this session.</summary>
        static void OpenForLegacyScenes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorPrefs.GetBool(HiddenKey)) return;

            var shown = new HashSet<string>(SessionState.GetString(ShownScenesKey, "").Split('|'));
            var avatars = Scan().Where(a => a.FaceTracking.Count + a.Fixes.Count > 0).ToList();
            var scenes = avatars.Select(a => a.Avatar.gameObject.scene.path).Where(p => !string.IsNullOrEmpty(p) && !shown.Contains(p)).Distinct().ToList();
            if (scenes.Count == 0) return;

            SessionState.SetString(ShownScenesKey, string.Join("|", shown.Concat(scenes)));
            Open();
        }

        /// <summary>Avatars in the open scenes with what's left to upgrade. Test copies are left out.</summary>
        static List<AvatarStatus> Scan()
        {
            var result = new List<AvatarStatus>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var avatar in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<VRCAvatarDescriptor>(true)))
                {
                    if (avatar.GetComponent<PawlygonFTTestCopy>() != null) continue;
                    var legacy = LegacyOscDetector.Find(avatar.gameObject).Where(f => f.IsPawlygonLegacy).ToList();
                    result.Add(new AvatarStatus
                    {
                        Avatar = avatar,
                        FaceTracking = legacy.Where(f => !f.IsBlendshapeFixes).ToList(),
                        Fixes = legacy.Where(f => f.IsBlendshapeFixes).ToList(),
                        HasNewPrefab = FTAvatarUtils.GetBuildComponents<PawlygonFTComponent>(avatar.gameObject).Count > 0,
                    });
                }
            }
            return result;
        }

        void OnEnable() => EditorApplication.hierarchyChanged += QueueRebuild;
        void OnDisable() => EditorApplication.hierarchyChanged -= QueueRebuild;
        void OnFocus() => Rebuild();

        void QueueRebuild()
        {
            if (rebuildQueued) return;
            rebuildQueued = true;
            EditorApplication.delayCall += () =>
            {
                rebuildQueued = false;
                if (this != null) Rebuild();
            };
        }

        void CreateGUI()
        {
            var root = FTUI.Root();
            root.AddToClassList("ft-window");
            root.style.flexGrow = 1;
            rootVisualElement.Add(root);

            root.Add(FTUI.Text("Pawlygon Face Tracking 2.0 uses VRChat's native face tracking. The 1.x prefabs come off and one " +
                               "new prefab goes on. Face Tracking Extras, Custom Animations and your own 1.x animations keep working.",
                "ft-muted", "ft-card__note"));

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            content = new VisualElement();
            scroll.Add(content);
            root.Add(scroll);

            var footer = new VisualElement();
            footer.AddToClassList("ft-field-row");
            var guide = new Button(() => Application.OpenURL(GuideUrl)) { text = "Open the Full Guide" };
            guide.AddToClassList("ft-add-button");
            footer.Add(guide);
            var hidden = new Toggle("Don't open this by itself for this project") { value = EditorPrefs.GetBool(HiddenKey) };
            hidden.style.marginLeft = 8;
            hidden.RegisterValueChangedCallback(e => EditorPrefs.SetBool(HiddenKey, e.newValue));
            footer.Add(hidden);
            root.Add(footer);

            Rebuild();
        }

        void Rebuild()
        {
            if (content == null) return;
            content.Clear();

            var avatars = Scan();
            foreach (var a in avatars.Where(a => a.FaceTracking.Count + a.Fixes.Count > 0)) tracked.Add(a.Avatar.GetInstanceID());
            var shown = avatars.Where(a => tracked.Contains(a.Avatar.GetInstanceID())).ToList();

            if (shown.Count == 0)
            {
                content.Add(FTUI.Issue(FTKind.Ok, "No Pawlygon VRCFT 1.x setups in the open scenes.",
                    "Open a scene with an avatar set up with 1.x to upgrade it.", null));
                return;
            }

            foreach (var status in shown) AddAvatar(status);
        }

        void AddAvatar(AvatarStatus status)
        {
            var avatar = status.Avatar;
            var body = FTUI.Card(content, avatar.name, out var extras);
            var left = status.FaceTracking.Count + status.Fixes.Count + (status.HasNewPrefab ? 0 : 1);
            extras.Add(FTUI.Badge(status.Done ? "done" : $"{left} to do", status.Done ? FTKind.Ok : FTKind.Warning));

            // 1. and 2. The 1.x prefabs come off
            AddLegacy(body, status.FaceTracking, "1.x face tracking prefab",
                "It can't run together with native face tracking: both would drive the same blendshapes.");
            AddLegacy(body, status.Fixes, "1.x Blendshape Fixes",
                "The Blendshape Fixes component in the new prefab replaces it.");

            // 3. The new prefab goes on
            if (status.HasNewPrefab)
            {
                var component = FTAvatarUtils.GetBuildComponents<PawlygonFTComponent>(avatar.gameObject).First();
                body.Add(FTUI.Issue(FTKind.Ok, "Pawlygon Face Tracking prefab added.", null, new[] { ("Select", (System.Action)(() => Select(component.gameObject))) }));
            }
            else
            {
                body.Add(FTUI.Issue(FTKind.Warning, "Add the Pawlygon Face Tracking prefab.", "It's added inside the avatar. Undoable.",
                    new[] { ("Add Prefab", (System.Action)(() => AddPrefab(avatar))) }));
            }

            // 4. 1.x turned the eyes further than most descriptors do
            if (status.HasNewPrefab)
            {
                AddEyeGaze(body, avatar, FTAvatarUtils.GetBuildComponents<PawlygonFaceTracking>(avatar.gameObject).FirstOrDefault());
            }

            // 5. Check
            if (status.Done)
            {
                body.Add(FTUI.Issue(FTKind.Info, "Check the new components' inspectors.",
                    "Face Tracking shows every tracking key's mapping, Face Tracking Guards lists unguarded layers (Bake Guards), " +
                    "and Pupil Emulation offers Remove Fake Pupils for Face Tracking Extras and Custom Animations.", null));
            }
        }

        internal enum EyeGazeStep
        {
            // Eye gaze doesn't turn the eye bones on this avatar, so there's no range to pick
            None,
            // The eyes turn as far as the descriptor's Eye Look says
            Ask,
            // Face Tracking uses the 1.x range for the rig
            Rig,
        }

        /// <summary>
        /// Where the avatar stands on eye gaze. 1.x turned the eye bones from an animator layer, by the rig's humanoid
        /// eye limits. With native face tracking VRChat turns them, as far as the descriptor's Eye Look rotations go.
        /// </summary>
        internal static EyeGazeStep EyeGazeStepFor(VRCAvatarDescriptor avatar, PawlygonFaceTracking component)
        {
            if (avatar == null || component == null) return EyeGazeStep.None;

            var settings = avatar.customEyeLookSettings;
            var gaze = FaceTrackingSetupPlan.Resolve(component, avatar).GazeMode ?? settings.eyeGazeMode;
            var rotatesBones = gaze == EyeGazeMode.Both || gaze == EyeGazeMode.BoneRotation;
            var hasEyeBones = avatar.enableEyeLook && settings.leftEye != null && settings.rightEye != null;
            if (!rotatesBones || !hasEyeBones) return EyeGazeStep.None;

            return component.eyeRotationRange == FTEyeRotationRange.HumanoidRig ? EyeGazeStep.Rig : EyeGazeStep.Ask;
        }

        /// <summary>
        /// The eye gaze step: what changed from 1.x, how far the eyes turn now and how far they would with the rig's
        /// range, and the two ways to settle it. Once Humanoid Rig is picked it's one line like the other finished steps.
        /// </summary>
        void AddEyeGaze(VisualElement body, VRCAvatarDescriptor avatar, PawlygonFaceTracking component)
        {
            var step = EyeGazeStepFor(avatar, component);
            if (step == EyeGazeStep.None) return;

            var rig = FTLegacyEyeRange.Find(avatar, out var problem);
            if (step == EyeGazeStep.Rig)
            {
                var back = new[] { ("Keep Existing", (System.Action)(() => SetEyeRange(component, FTEyeRotationRange.KeepExisting))) };
                if (rig != null)
                {
                    body.Add(FTUI.Issue(FTKind.Ok, $"Eye gaze turns as far as with 1.x (Humanoid Rig: {FTLegacyEyeRange.Describe(avatar, rig)}).",
                        "Face Tracking's Eyes > Rotation is Humanoid Rig: the uploaded avatar gets the range 1.x had for this rig. " +
                        "Keep Existing goes back to the descriptor's own Eye Look limits. Undoable.", back));
                }
                else
                {
                    body.Add(FTUI.Issue(FTKind.Warning, "Eye Rotation is Humanoid Rig, but the rig's range couldn't be worked out.",
                        problem + " The descriptor's Eye Look limits are used. Keep Existing sets Eyes > Rotation back. Undoable.", back));
                }
                return;
            }

            var block = new VisualElement();
            block.AddToClassList("ft-issue");
            block.AddToClassList("ft-issue--info");
            block.AddToClassList("ft-step");

            var title = new VisualElement();
            title.AddToClassList("ft-step__title");
            var icon = new Image { image = FTUI.Icon(FTKind.Info) };
            icon.AddToClassList("ft-issue__icon");
            title.Add(icon);
            title.Add(FTUI.Text("Eye gaze: the eyes turn less than with 1.x", "ft-step__heading"));
            block.Add(title);

            var content = new VisualElement();
            content.AddToClassList("ft-step__body");
            block.Add(content);

            content.Add(FTUI.Text("With 1.x, an animator layer turned the eyes. With 2.0, VRChat turns them itself from your eye tracking, " +
                                  "and only as far as the Eye Look section of the avatar descriptor allows. Those limits are usually set " +
                                  "for people without eye tracking, so they're smaller.", "ft-step__text"));

            var ranges = new VisualElement();
            ranges.AddToClassList("ft-step__ranges");
            ranges.Add(RangeRow("Now, from the descriptor", FTLegacyEyeRange.Describe(avatar, null), false,
                "How far the eyes turn from Looking Straight with the descriptor's Looking Up, Down, Left and Right: up, down, and to the sides."));
            if (rig != null)
            {
                ranges.Add(RangeRow("With Humanoid Rig, like 1.x", FTLegacyEyeRange.Describe(avatar, rig), true,
                    "The rig's humanoid eye limits, times what 1.x's Eye Rotation layer used: up x2.8, down x3.5, in and out x2."));
            }
            content.Add(ranges);

            var buttons = new VisualElement();
            buttons.AddToClassList("ft-step__buttons");
            if (rig != null)
            {
                var useRig = new Button(() => SetEyeRange(component, FTEyeRotationRange.HumanoidRig))
                {
                    text = "Use Humanoid Rig",
                    tooltip = "Sets Face Tracking's Eyes > Rotation to Humanoid Rig. Only the uploaded avatar gets the wider limits. Undoable.",
                };
                useRig.AddToClassList("ft-step__button");
                useRig.AddToClassList("ft-button--primary");
                buttons.Add(useRig);
            }
            var open = new Button(() => Select(avatar.gameObject))
            {
                text = "Open Avatar Descriptor",
                tooltip = "Selects the avatar, to change Looking Up, Down, Left and Right in its Eye Look section yourself.",
            };
            open.AddToClassList("ft-step__button");
            buttons.Add(open);
            content.Add(buttons);

            content.Add(FTUI.Text(rig != null
                ? "Humanoid Rig also changes how far the eyes turn for people without eye tracking."
                : $"Humanoid Rig isn't available for this avatar. {problem}", "ft-step__note"));
            body.Add(block);
        }

        static VisualElement RangeRow(string label, string value, bool strong, string tooltip)
        {
            var row = new VisualElement { tooltip = tooltip };
            row.AddToClassList("ft-step__range");
            row.Add(FTUI.Text(label, "ft-step__range-label"));
            var number = FTUI.Text(value, "ft-step__range-value");
            number.EnableInClassList("ft-step__range-value--strong", strong);
            row.Add(number);
            return row;
        }

        static void AddLegacy(VisualElement body, List<LegacyOscDetector.Finding> findings, string what, string why)
        {
            if (findings.Count == 0)
            {
                body.Add(FTUI.Issue(FTKind.Ok, $"No {what}.", null, null));
                return;
            }

            foreach (var finding in findings)
            {
                var holder = finding.Holder;
                // Disabling the avatar root would hide the whole avatar, so only offer to select it
                var actions = holder != null && holder.transform.parent != null
                    ? new[] { ("Select", (System.Action)(() => Select(holder))), ("Disable", () => LegacyOscDetector.Disable(holder)) }
                    : new[] { ("Select", (System.Action)(() => Select(holder))) };
                body.Add(FTUI.Issue(FTKind.Warning, $"Remove the {what} on '{finding.Path}'.",
                    why + " Disable tags it EditorOnly (undoable), or delete it yourself.", actions));
            }
        }

        static void AddPrefab(VRCAvatarDescriptor avatar)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(PrefabGuid));
            if (prefab == null) return;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, avatar.transform);
            Undo.RegisterCreatedObjectUndo(instance, "Add Pawlygon Face Tracking");
            Select(instance);
        }

        void SetEyeRange(PawlygonFaceTracking component, FTEyeRotationRange range)
        {
            Undo.RecordObject(component, range == FTEyeRotationRange.HumanoidRig ? "Use Humanoid Rig Eye Rotation" : "Keep Existing Eye Rotation");
            component.eyeRotationRange = range;
            EditorUtility.SetDirty(component);
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            Rebuild();
        }

        static void Select(Object target)
        {
            if (target == null) return;
            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }
    }
}
