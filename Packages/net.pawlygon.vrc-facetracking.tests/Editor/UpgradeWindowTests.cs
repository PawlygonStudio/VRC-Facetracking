using NUnit.Framework;
using UnityEngine;

namespace Pawlygon.FaceTracking.Editor.Tests
{
    /// <summary>When the Upgrade to 2.0 window brings up eye gaze.</summary>
    public class UpgradeWindowTests
    {
        TestAvatar avatar;
        PawlygonFaceTracking component;

        [SetUp]
        public void SetUp()
        {
            avatar = new TestAvatar();
            avatar.AddMesh("Body", "JawOpen");
            component = avatar.Add<PawlygonFaceTracking>();
        }

        [TearDown]
        public void TearDown() => avatar.Dispose();

        void AddEyeBones()
        {
            Transform Bone(string name)
            {
                var bone = new GameObject(name);
                bone.transform.SetParent(avatar.Root.transform, false);
                return bone.transform;
            }
            avatar.Descriptor.enableEyeLook = true;
            avatar.Descriptor.customEyeLookSettings.leftEye = Bone("LeftEye");
            avatar.Descriptor.customEyeLookSettings.rightEye = Bone("RightEye");
        }

        [Test]
        public void NoStepWithoutEyeBonesToTurn()
        {
            Assert.That(FTUpgradeWindow.EyeGazeStepFor(avatar.Descriptor, component), Is.EqualTo(FTUpgradeWindow.EyeGazeStep.None));
            Assert.That(FTUpgradeWindow.EyeGazeStepFor(avatar.Descriptor, null), Is.EqualTo(FTUpgradeWindow.EyeGazeStep.None));
        }

        [Test]
        public void AsksWhileTheDescriptorsLimitsAreUsed()
        {
            AddEyeBones();
            Assert.That(FTUpgradeWindow.EyeGazeStepFor(avatar.Descriptor, component), Is.EqualTo(FTUpgradeWindow.EyeGazeStep.Ask));
        }

        [Test]
        public void SettledOnceEyeRotationIsHumanoidRig()
        {
            AddEyeBones();
            component.eyeRotationRange = FTEyeRotationRange.HumanoidRig;
            Assert.That(FTUpgradeWindow.EyeGazeStepFor(avatar.Descriptor, component), Is.EqualTo(FTUpgradeWindow.EyeGazeStep.Rig));
        }

        [Test]
        public void NoStepWhenGazeOnlyUsesBlendshapes()
        {
            AddEyeBones();
            component.eyeGaze = FTGazeMode.FaceTracking;
            Assert.That(FTUpgradeWindow.EyeGazeStepFor(avatar.Descriptor, component), Is.EqualTo(FTUpgradeWindow.EyeGazeStep.None));
        }
    }
}
