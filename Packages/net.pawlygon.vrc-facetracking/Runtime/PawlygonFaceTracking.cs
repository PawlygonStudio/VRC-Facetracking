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

    // Mirrors VRCAvatarDescriptor.VisemeDampenMode so this assembly doesn't depend on beta SDK types
    public enum FTVisemeDampenMode
    {
        None,
        DampenTracking,
        DampenVisemes,
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

        [Tooltip("Face Tracking Visemes switches lip sync to the face tracking viseme mode, reusing the avatar's viseme blendshapes.")]
        public FTLipSyncMode lipSync = FTLipSyncMode.FaceTrackingVisemes;

        [Tooltip("Face Tracking drives the eyelids with the EyeClosed blendshapes.")]
        public FTEyelidMode eyelids = FTEyelidMode.FaceTracking;

        [Tooltip("Auto uses Both when the descriptor has eye bones set up in Eye Look, otherwise Face Tracking.")]
        public FTGazeMode eyeGaze = FTGazeMode.Auto;

        [Tooltip("How visemes and mouth tracking are blended. Needs face tracking visemes and all tracking keys.")]
        public FTVisemeDampenMode visemeDampenMode = FTVisemeDampenMode.None;

        [Range(0f, 1f)]
        public float visemeDampenAmount = 0.5f;
    }
}
