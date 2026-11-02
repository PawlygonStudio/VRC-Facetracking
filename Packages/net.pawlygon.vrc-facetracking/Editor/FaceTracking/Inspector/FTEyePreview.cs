using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using EyeRotations = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.CustomEyeLookSettings.EyeRotations;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Turns the avatar's eye bones in the scene while a preview button is held, and puts them back on release.
    /// Only while held, so the eyes are never left turned in the scene or in an upload.
    /// </summary>
    internal sealed class FTEyePreview
    {
        Transform left, right;
        Quaternion savedLeft, savedRight;

        public bool Active => left != null;

        /// <summary>A button that turns the eyes to the rotation from <paramref name="rotations"/> while held.</summary>
        public Button HoldButton(string text, string tooltip, Func<(Transform left, Transform right, EyeRotations rotations)?> rotations)
        {
            var button = new Button { text = text, tooltip = tooltip };
            button.AddToClassList("ft-eye-preview__button");
            // Button eats the pointer events it uses for clicks, so these listen on the way down
            button.RegisterCallback<PointerDownEvent>(_ =>
            {
                var target = rotations();
                if (target.HasValue) Start(target.Value.left, target.Value.right, target.Value.rotations);
            }, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerUpEvent>(_ => Stop(), TrickleDown.TrickleDown);
            button.RegisterCallback<PointerLeaveEvent>(_ => Stop());
            button.RegisterCallback<PointerCaptureOutEvent>(_ => Stop());
            button.RegisterCallback<DetachFromPanelEvent>(_ => Stop());
            return button;
        }

        void Start(Transform leftEye, Transform rightEye, EyeRotations rotations)
        {
            if (leftEye == null || rightEye == null) return;
            Stop();
            left = leftEye;
            right = rightEye;
            savedLeft = left.localRotation;
            savedRight = right.localRotation;
            left.localRotation = rotations.left;
            right.localRotation = rotations.right;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            SceneView.RepaintAll();
        }

        public void Stop()
        {
            if (!Active) return;
            left.localRotation = savedLeft;
            if (right != null) right.localRotation = savedRight;
            left = right = null;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            SceneView.RepaintAll();
        }

        void OnPlayModeChanged(PlayModeStateChange _) => Stop();
    }
}
