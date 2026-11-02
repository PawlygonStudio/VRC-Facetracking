using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using EyeRotations = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.CustomEyeLookSettings.EyeRotations;
using Object = UnityEngine.Object;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// The eye rotation range Pawlygon VRCFT 1.x had. Its Eye Rotation layer played the humanoid eye muscles past their
    /// limits, so how far the eyes turned was each rig's own humanoid eye limits times the values below. They're
    /// sampled from the rig and turned into the descriptor's Looking Up/Down/Left/Right rotations.
    /// </summary>
    internal static class FTLegacyEyeRange
    {
        // The 1.x Eye_Look_*_Rot clips, played with Override blending, so they're absolute muscle values
        const float Up = 2.8f, Down = 3.5f, Out = 2f, In = 2f;

        public enum Direction { Up, Down, Left, Right }

        /// <summary>Each eye's turn away from looking straight, in its bone's local space, by <see cref="Direction"/>.</summary>
        internal sealed class Range
        {
            public readonly Quaternion[] Left = new Quaternion[4];
            public readonly Quaternion[] Right = new Quaternion[4];

            public float Degrees(Direction direction) =>
                (Quaternion.Angle(Quaternion.identity, Left[(int)direction]) + Quaternion.Angle(Quaternion.identity, Right[(int)direction])) / 2;
        }

        // Sampling builds a copy of the skeleton, so it's done once per rig
        static readonly Dictionary<Avatar, Range> cache = new Dictionary<Avatar, Range>();

        /// <summary>The 1.x range for the avatar's rig, or null with the reason.</summary>
        public static Range Find(VRCAvatarDescriptor descriptor, out string problem)
        {
            problem = null;
            var animator = descriptor.GetComponent<Animator>();
            var avatar = animator != null ? animator.avatar : null;
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                problem = "The avatar's rig isn't humanoid, so its eye range can't be worked out.";
                return null;
            }

            var human = avatar.humanDescription.human;
            var leftBone = human.FirstOrDefault(b => b.humanName == "LeftEye").boneName;
            var rightBone = human.FirstOrDefault(b => b.humanName == "RightEye").boneName;
            if (string.IsNullOrEmpty(leftBone) || string.IsNullOrEmpty(rightBone))
            {
                problem = "The humanoid rig has no eye bones, so its eye range can't be worked out.";
                return null;
            }

            var settings = descriptor.customEyeLookSettings;
            if (settings.leftEye == null || settings.rightEye == null || settings.leftEye.name != leftBone || settings.rightEye.name != rightBone)
            {
                problem = $"The descriptor's eye bones aren't the humanoid rig's eye bones ({leftBone}, {rightBone}).";
                return null;
            }

            if (!cache.TryGetValue(avatar, out var range))
            {
                range = Sample(animator.transform, avatar, leftBone, rightBone);
                cache[avatar] = range;
            }
            return range;
        }

        /// <summary>The descriptor's rotation for a direction, turned by the 1.x range from its Looking Straight.</summary>
        public static EyeRotations Apply(VRCAvatarDescriptor descriptor, Range range, Direction direction)
        {
            var settings = descriptor.customEyeLookSettings;
            var straight = settings.eyesLookingStraight;
            var left = Straight(straight.left, settings.leftEye) * range.Left[(int)direction];
            var right = Straight(straight.right, settings.rightEye) * range.Right[(int)direction];
            return new EyeRotations { linked = left == right, left = left, right = right };
        }

        /// <summary>The descriptor's own rotation for a direction.</summary>
        public static EyeRotations FromDescriptor(VRCAvatarDescriptor descriptor, Direction direction)
        {
            var settings = descriptor.customEyeLookSettings;
            switch (direction)
            {
                case Direction.Up: return settings.eyesLookingUp;
                case Direction.Down: return settings.eyesLookingDown;
                case Direction.Left: return settings.eyesLookingLeft;
                default: return settings.eyesLookingRight;
            }
        }

        /// <summary>How far a rotation turns the eyes from the descriptor's Looking Straight, averaged over both eyes.</summary>
        public static float Degrees(VRCAvatarDescriptor descriptor, EyeRotations rotations)
        {
            var settings = descriptor.customEyeLookSettings;
            var straight = settings.eyesLookingStraight;
            return (Quaternion.Angle(Straight(straight.left, settings.leftEye), rotations.left) +
                    Quaternion.Angle(Straight(straight.right, settings.rightEye), rotations.right)) / 2;
        }

        // An Eye Look section that was never set up has empty rotations
        static Quaternion Straight(Quaternion q, Transform eye)
        {
            return q.x == 0 && q.y == 0 && q.z == 0 && q.w == 0 ? eye.localRotation : q;
        }

        static Range Sample(Transform avatarRoot, Avatar avatar, string leftBone, string rightBone)
        {
            // A bare copy of the hierarchy, so posing it never touches the avatar
            var root = Mirror(avatarRoot, null);
            try
            {
                var leftEye = Find(root, leftBone);
                var rightEye = Find(root, rightBone);
                var muscles = HumanTrait.MuscleName;
                int Muscle(string name) => Array.IndexOf(muscles, name);
                var eyeMuscles = new[] { "Left Eye Down-Up", "Left Eye In-Out", "Right Eye Down-Up", "Right Eye In-Out" }.Select(Muscle).ToArray();

                var handler = new HumanPoseHandler(avatar, root);
                try
                {
                    var pose = new HumanPose();
                    handler.GetHumanPose(ref pose);

                    // Muscles past 1 turn on along the same axis, so the turn at 1 is scaled rather than posing past the limit
                    Quaternion Turn(Transform eye, string muscle, float value)
                    {
                        foreach (var i in eyeMuscles) pose.muscles[i] = 0;
                        handler.SetHumanPose(ref pose);
                        var neutral = eye.localRotation;
                        pose.muscles[Muscle(muscle)] = Mathf.Sign(value);
                        handler.SetHumanPose(ref pose);
                        var turn = Quaternion.Inverse(neutral) * eye.localRotation;
                        return Quaternion.SlerpUnclamped(Quaternion.identity, turn, Mathf.Abs(value));
                    }

                    var range = new Range();
                    range.Left[(int)Direction.Up] = Turn(leftEye, "Left Eye Down-Up", Up);
                    range.Right[(int)Direction.Up] = Turn(rightEye, "Right Eye Down-Up", Up);
                    range.Left[(int)Direction.Down] = Turn(leftEye, "Left Eye Down-Up", -Down);
                    range.Right[(int)Direction.Down] = Turn(rightEye, "Right Eye Down-Up", -Down);
                    // Looking left turns the left eye out and the right eye in
                    range.Left[(int)Direction.Left] = Turn(leftEye, "Left Eye In-Out", Out);
                    range.Right[(int)Direction.Left] = Turn(rightEye, "Right Eye In-Out", -In);
                    range.Left[(int)Direction.Right] = Turn(leftEye, "Left Eye In-Out", -In);
                    range.Right[(int)Direction.Right] = Turn(rightEye, "Right Eye In-Out", Out);
                    return range;
                }
                finally
                {
                    handler.Dispose();
                }
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        static Transform Mirror(Transform source, Transform parent)
        {
            var copy = new GameObject(source.name) { hideFlags = HideFlags.HideAndDontSave }.transform;
            if (parent != null)
            {
                copy.SetParent(parent, false);
                copy.localPosition = source.localPosition;
                copy.localRotation = source.localRotation;
                copy.localScale = source.localScale;
            }
            foreach (Transform child in source) Mirror(child, copy);
            return copy;
        }

        static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
