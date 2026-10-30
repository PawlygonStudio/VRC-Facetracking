using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Walks an avatar from Pawlygon VRCFT 1.x to 2.0: removing the 1.x prefabs, adding the new one, and testing. Opens
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

            // 4. Check and test
            if (status.Done)
            {
                body.Add(FTUI.Issue(FTKind.Info, "Check the new components' inspectors, then build a test copy.",
                    "Face Tracking shows every tracking key's mapping, Face Tracking Guards lists unguarded layers (Bake Guards), " +
                    "and Pupil Emulation offers Remove Fake Pupils for Face Tracking Extras and Custom Animations.",
                    new[] { ("Build Test Copy", (System.Action)(() => FTTestCopy.Build(avatar))) }));
            }
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

        static void Select(Object target)
        {
            if (target == null) return;
            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }
    }
}
