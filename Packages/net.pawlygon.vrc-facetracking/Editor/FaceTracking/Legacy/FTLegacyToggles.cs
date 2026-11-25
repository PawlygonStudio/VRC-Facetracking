using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// The Face Tracking Toggles component of 2.1 held the Visemes menu and pupil toggle settings, which are part of
    /// Face Tracking and Pupil Emulation since 2.2. An avatar that still has one keeps building the way it was set up:
    /// its settings are used in place of the new ones, for the build and the inspectors alike, until Move Settings
    /// copies them over and removes it.
    /// </summary>
    internal static class FTLegacyToggles
    {
        internal struct VisemeSettings
        {
            public bool Menu;
            public FTVisemesLevel Default;
            public string MenuName;
            /// <summary>The 2.1 component the settings come from, or null when they're Face Tracking's own.</summary>
            public PawlygonFTToggles Legacy;
        }

        internal struct PupilSettings
        {
            public bool TrackingToggle;
            public bool EmulationToggle;
            public string MenuName;
            /// <summary>The 2.1 component the settings come from, or null when they're Pupil Emulation's own.</summary>
            public PawlygonFTToggles Legacy;
        }

        public static PawlygonFTToggles Find(GameObject avatarRoot)
        {
            return avatarRoot != null ? FTAvatarUtils.GetBuildComponents<PawlygonFTToggles>(avatarRoot).FirstOrDefault() : null;
        }

        public static VisemeSettings Visemes(PawlygonFaceTracking component, GameObject avatarRoot)
        {
            var legacy = Find(avatarRoot);
            if (legacy != null)
            {
                return new VisemeSettings { Menu = legacy.visemesMenu, Default = legacy.visemesDefault, MenuName = legacy.menuName, Legacy = legacy };
            }
            return new VisemeSettings { Menu = component.visemesMenu, Default = component.visemesDefault, MenuName = component.menuName };
        }

        public static PupilSettings Pupils(PawlygonFTPupilEmulation component, GameObject avatarRoot)
        {
            var legacy = Find(avatarRoot);
            if (legacy != null)
            {
                return new PupilSettings
                {
                    TrackingToggle = legacy.pupilTrackingToggle, EmulationToggle = legacy.pupilEmulationToggle, MenuName = legacy.menuName, Legacy = legacy,
                };
            }
            return new PupilSettings { TrackingToggle = component.pupilTrackingToggle, EmulationToggle = component.pupilEmulationToggle, MenuName = component.menuName };
        }

        /// <summary>
        /// Copies the 2.1 component's settings to the avatar's Face Tracking and Pupil Emulation and removes it, as
        /// one undo step.
        /// </summary>
        public static void MoveSettings(PawlygonFTToggles legacy, GameObject avatarRoot)
        {
            if (legacy == null || avatarRoot == null) return;

            Undo.SetCurrentGroupName("Move Face Tracking Toggles Settings");
            foreach (var tracking in FTAvatarUtils.GetBuildComponents<PawlygonFaceTracking>(avatarRoot))
            {
                Undo.RecordObject(tracking, "Move Face Tracking Toggles Settings");
                tracking.visemesMenu = legacy.visemesMenu;
                tracking.visemesDefault = legacy.visemesDefault;
                tracking.menuName = legacy.menuName;
                Saved(tracking);
            }
            foreach (var pupils in FTAvatarUtils.GetBuildComponents<PawlygonFTPupilEmulation>(avatarRoot))
            {
                Undo.RecordObject(pupils, "Move Face Tracking Toggles Settings");
                pupils.pupilTrackingToggle = legacy.pupilTrackingToggle;
                pupils.pupilEmulationToggle = legacy.pupilEmulationToggle;
                pupils.menuName = legacy.menuName;
                Saved(pupils);
            }

            // An object that only held the component goes with it
            var holder = legacy.gameObject;
            var onlyThis = holder != avatarRoot && holder.transform.childCount == 0 && holder.GetComponents<Component>().Length == 2
                           && !PrefabUtility.IsPartOfPrefabInstance(holder);
            if (onlyThis) Undo.DestroyObjectImmediate(holder);
            else Undo.DestroyObjectImmediate(legacy);
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        }

        static void Saved(Object component)
        {
            EditorUtility.SetDirty(component);
            if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }
    }
}
