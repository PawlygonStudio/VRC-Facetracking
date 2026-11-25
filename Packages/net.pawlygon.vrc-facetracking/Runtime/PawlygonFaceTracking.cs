using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pawlygon.FaceTracking
{
    public enum FTMeshMode
    {
        Auto,
        Manual,
    }

    public enum FTLipSyncMode
    {
        FaceTrackingVisemes,
        KeepExisting,
    }

    public enum FTEyelidMode
    {
        FaceTracking,
        KeepExisting,
    }

    public enum FTGazeMode
    {
        Auto,
        Both,
        FaceTracking,
        BoneRotation,
        KeepExisting,
    }

    public enum FTEyeRotationRange
    {
        KeepExisting,
        HumanoidRig,
    }

    public enum FTTongueMode
    {
        Auto,
        TongueOut,
        Steps,
    }

    // Mirrors VRCAvatarDescriptor.VisemeDampenMode so this assembly doesn't depend on beta SDK types
    public enum FTVisemeDampenMode
    {
        None,
        DampenTracking,
        DampenVisemes,
    }

    /// <summary>
    /// How much of the visemes plays while face tracking is active. The numbers are the menu parameter's values:
    /// Full is 0, so turning any other level's toggle off in the menu goes back to full visemes.
    /// </summary>
    public enum FTVisemesLevel
    {
        Full = 0,
        Medium = 1,
        Low = 2,
        Off = 3,
    }

    /// <summary>A tracking key mapped by hand.</summary>
    [Serializable]
    public class FTKeyOverride
    {
        [Tooltip("The tracking key's name, e.g. EyeConstrictLeft.")]
        public string key;

        [Tooltip("The blendshape it drives. Empty leaves the key unmapped.")]
        public string blendshape;
    }

    /// <summary>How strongly a tracking key moves its blendshape, when it isn't 1.</summary>
    [Serializable]
    public class FTKeyMultiplier
    {
        [Tooltip("The tracking key's name, e.g. EyeWideLeft.")]
        public string key;

        [Tooltip("1 is full strength. 0.8 stops the blendshape at 80%.")]
        public float multiplier = 1f;
    }

    /// <summary>
    /// Sets up VRChat native face tracking on the avatar descriptor at build time.
    /// Replaces any face tracking configuration already on the descriptor.
    /// </summary>
    [AddComponentMenu("Pawlygon/Pawlygon Face Tracking")]
    [DisallowMultipleComponent]
    [FTSingleInstance]
    public class PawlygonFaceTracking : PawlygonFTComponent
    {
        public const string BaseSetName = "Pawlygon Base";

        public override int LatestVersion => 1;

        [Tooltip("Auto picks every mesh on the avatar that has face tracking blendshapes.")]
        public FTMeshMode meshMode = FTMeshMode.Auto;

        [Tooltip("Meshes driven by face tracking when Mesh Mode is Manual (max 16).")]
        public List<SkinnedMeshRenderer> meshes = new List<SkinnedMeshRenderer>();

        [Tooltip("Map every tracking key. Off limits the avatar to eye look and eyelids.")]
        public bool useAllTrackingKeys = true;

        [Tooltip("Tracking keys mapped by hand, replacing the automatic match. Edited from the Tracking Keys list.")]
        public List<FTKeyOverride> keyOverrides = new List<FTKeyOverride>();

        [Tooltip("How strongly each tracking key moves its blendshape, for the keys where it isn't 1. Edited from the Tracking Keys list.")]
        public List<FTKeyMultiplier> keyMultipliers = new List<FTKeyMultiplier>();

        [Tooltip("Avatars with tongue step blendshapes (like Tongue_LongStep1 and Tongue_LongStep2). Steps plays them one after the other " +
                 "as the tongue comes out. TongueOut maps a single blendshape. Auto uses TongueOut when the avatar has that blendshape, and the steps otherwise.")]
        public FTTongueMode tongueMode = FTTongueMode.Auto;

        [Tooltip("Face Tracking Visemes switches lip sync to the face tracking viseme mode, reusing the avatar's viseme blendshapes.")]
        public FTLipSyncMode lipSync = FTLipSyncMode.FaceTrackingVisemes;

        [Tooltip("Face Tracking drives the eyelids with the EyeClosed blendshapes.")]
        public FTEyelidMode eyelids = FTEyelidMode.FaceTracking;

        [Tooltip("Auto: Both with EyeLook blendshapes and eye bones set up in the descriptor's Eye Look, Face Tracking with only the blendshapes, Bone Rotation with only the bones, unchanged with neither.")]
        public FTGazeMode eyeGaze = FTGazeMode.Auto;

        [Tooltip("How far the eye bones turn. Keep Existing uses the descriptor's Looking Up/Down/Left/Right. Humanoid Rig works them out from " +
                 "the rig's humanoid eye limits, times the multipliers Pawlygon VRCFT 1.x used: up x2.8, down x3.5, in and out x2. " +
                 "Only used when eye gaze rotates the eye bones. Also changes how far the eyes turn for people without eye tracking.")]
        public FTEyeRotationRange eyeRotationRange = FTEyeRotationRange.KeepExisting;

        [Tooltip("How visemes and mouth tracking are blended. Needs face tracking visemes and all tracking keys.")]
        public FTVisemeDampenMode visemeDampenMode = FTVisemeDampenMode.None;

        [Range(0f, 1f)]
        public float visemeDampenAmount = 0.5f;

        [Tooltip("Add a Visemes submenu to the avatar's menu, with Full, Medium, Low and Off. The level applies while face tracking is active, " +
                 "so visemes don't play at full strength on top of the tracked mouth. Needs face tracking visemes, and VRCFury or Modular Avatar.")]
        public bool visemesMenu = true;

        [Tooltip("The level the menu starts on. Full plays visemes as usual until another level is picked. The pick is saved.")]
        public FTVisemesLevel visemesDefault = FTVisemesLevel.Full;

        [Tooltip("Submenu of the avatar's main menu that gets the Visemes submenu. Created if missing. Leave empty to put it in the main menu.")]
        public string menuName = "Face Tracking";
    }
}
