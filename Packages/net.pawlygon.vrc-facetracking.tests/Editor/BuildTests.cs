using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using TargetMode = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.ParameterBehaviorTargetMode;

namespace Pawlygon.FaceTracking.Editor.Tests
{
    /// <summary>
    /// Whole builds on an avatar in memory. Only components that write to the descriptor are used: the ones that add
    /// animator layers save assets and hand them to VRCFury or Modular Avatar, which Build Test Copy covers.
    /// </summary>
    public class BuildTests
    {
        TestAvatar avatar;

        [SetUp]
        public void SetUp() => avatar = new TestAvatar();

        [TearDown]
        public void TearDown() => avatar.Dispose();

        void AddFullFace()
        {
            avatar.AddMesh("Body", TestAvatar.AllKeyNames.Concat(new[] { "vrc.v_aa", "vrc.v_oh", "JawOpen-EyeClosedLeft" }).ToArray());
        }

        [Test]
        public void DescriptorPassWritesTheSetup()
        {
            AddFullFace();
            avatar.Add<PawlygonFaceTracking>();
            avatar.Add<PawlygonFTBlendshapeFixes>();
            avatar.Add<PawlygonFTEmulation>();

            FTBuildRunner.Run(avatar.Root, FTPass.Descriptor);

            var descriptor = avatar.Descriptor;
            Assert.That(descriptor.enableFaceTracking, Is.True);
            Assert.That(descriptor.useAllTrackingKeys, Is.True);
            Assert.That(descriptor.faceTrackingMeshes.Count, Is.EqualTo(1));
            Assert.That(descriptor.lipSync, Is.EqualTo(VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShapeFaceTracking));
            Assert.That(descriptor.customEyeLookSettings.eyelidType, Is.EqualTo(VRCAvatarDescriptor.EyelidType.FaceTracking));

            var set = descriptor.faceTrackingBlendshapesSetList.Single();
            Assert.That(set.setName, Is.EqualTo(PawlygonFaceTracking.BaseSetName));
            Assert.That(set.faceTrackingBlendshapeNames[FTKeys.KeyByName("JawLeft")], Is.EqualTo("JawLeft"));
            // Rewritten by Face Tracking Emulation, so its expression drives it instead of the set
            Assert.That(set.faceTrackingBlendshapeNames[FTKeys.KeyByName("JawOpen")], Is.Empty);

            var driven = descriptor.faceTrackingParameterBehaviors.Where(b => b.targetMode == TargetMode.ApplyToBlendshape)
                .Select(b => b.targetBlendshapeName).ToList();
            Assert.That(driven, Does.Contain("JawOpen"));
            Assert.That(driven, Does.Contain("JawOpen-EyeClosedLeft"));
            Assert.That(driven.Distinct().Count(), Is.EqualTo(driven.Count), "A blendshape is driven by two behaviors.");
        }

        [Test]
        public void GeneratedExpressionsPassTheSdkValidator()
        {
            AddFullFace();
            avatar.Add<PawlygonFaceTracking>();
            avatar.Add<PawlygonFTBlendshapeFixes>();
            var emulation = avatar.Add<PawlygonFTEmulation>();
            // Every rule on is the most expressions an avatar can get
            emulation.angryBrows = emulation.sadBrows = emulation.limitLipsByFunnel = emulation.limitJawByMouthSideways =
                emulation.limitFrownByMouthSideways = emulation.limitLipSuckByMouthClosed = true;

            FTBuildRunner.Run(avatar.Root, FTPass.Descriptor);

            Assert.That(avatar.Descriptor.faceTrackingParameterBehaviors, Is.Not.Empty);
            Assert.That(FaceTrackingExpressionValidator.CollectWarnings(avatar.Descriptor), Is.Empty);
        }

        [Test]
        public void TongueStepsAreDrivenByExpressions()
        {
            avatar.AddMesh("Body", "JawOpen", "Tongue_LongStep1", "Tongue_LongStep2");
            avatar.Add<PawlygonFaceTracking>();

            FTBuildRunner.Run(avatar.Root, FTPass.Descriptor);

            var steps = avatar.Descriptor.faceTrackingParameterBehaviors.ToDictionary(b => b.targetBlendshapeName, b => b.customExpression);
            Assert.That(steps["Tongue_LongStep1"], Is.EqualTo("clamp(TongueOut * 2, 0, 1)"));
            Assert.That(steps["Tongue_LongStep2"], Is.EqualTo("clamp(TongueOut * 2 - 1, 0, 1)"));
            Assert.That(FaceTrackingExpressionValidator.CollectWarnings(avatar.Descriptor), Is.Empty);
        }

        [Test]
        public void AnimatorPassForwardsParametersAndRemovesTheComponents()
        {
            AddFullFace();
            var fx = avatar.AddFX();
            fx.AddParameter("FT/v2/JawX", AnimatorControllerParameterType.Float);
            ParameterTests.AddLayerReading(fx, "FT/v2/JawX");
            avatar.Add<PawlygonFaceTracking>();
            avatar.Add<PawlygonFTParameters>();

            FTBuildRunner.Run(avatar.Root, FTPass.Descriptor);
            Assert.That(avatar.Root.GetComponentsInChildren<PawlygonFTComponent>(true), Is.Not.Empty);

            FTBuildRunner.Run(avatar.Root, FTPass.Animator);

            var forward = avatar.Descriptor.faceTrackingParameterBehaviors.Single(b => b.targetMode == TargetMode.ForwardToAnimator);
            Assert.That(forward.outputParameterName, Is.EqualTo("FT/v2/JawX"));
            Assert.That(forward.customExpression, Is.EqualTo("JawRight - JawLeft"));
            Assert.That(avatar.Root.GetComponentsInChildren<PawlygonFTComponent>(true), Is.Empty);
            // The objects that only held a component are gone too
            Assert.That(avatar.Root.transform.Find(nameof(PawlygonFaceTracking)), Is.Null);
        }

        [Test]
        public void TogglesWithoutAToggleChangeNothing()
        {
            AddFullFace();
            avatar.Add<PawlygonFaceTracking>();
            FTBuildRunner.Run(avatar.Root, FTPass.Descriptor);
            var expected = Describe(avatar.Descriptor);

            using (var other = new TestAvatar())
            {
                other.AddMesh("Body", TestAvatar.AllKeyNames.Concat(new[] { "vrc.v_aa", "vrc.v_oh", "JawOpen-EyeClosedLeft" }).ToArray());
                other.Add<PawlygonFaceTracking>();
                other.Add<PawlygonFTToggles>().visemesToggle = false;
                FTBuildRunner.Run(other.Root, FTPass.Descriptor);

                Assert.That(Describe(other.Descriptor), Is.EqualTo(expected));
            }
        }

        [Test]
        public void TwoFaceTrackingComponentsStopTheBuild()
        {
            AddFullFace();
            avatar.Add<PawlygonFaceTracking>();
            avatar.Add<PawlygonFaceTracking>();

            Assert.Throws<FTBuildException>(() => FTBuildRunner.Run(avatar.Root, FTPass.Descriptor));
        }

        [Test]
        public void EditorOnlyComponentsAreNotBuilt()
        {
            AddFullFace();
            avatar.Add<PawlygonFaceTracking>().gameObject.tag = "EditorOnly";

            FTBuildRunner.Run(avatar.Root, FTPass.Descriptor);

            Assert.That(avatar.Descriptor.enableFaceTracking, Is.False);
        }

        [Test]
        public void VersionMessagesMatchTheAssemblyDefinition()
        {
            // The asmdef decides which VRCFury and Avatar Optimizer versions count as keeping the blendshapes, and
            // the messages tell users which version to update to. They're written in two places.
            var asmdef = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Packages/net.pawlygon.vrc-facetracking/Editor/FaceTracking/Pawlygon.FaceTracking.Editor.asmdef");
            Assert.That(asmdef, Is.Not.Null);

            string Expression(string define)
            {
                var match = Regex.Match(asmdef.text, "\"expression\":\\s*\"([^\"]+)\",\\s*\"define\":\\s*\"" + define + "\"");
                Assert.That(match.Success, Is.True, define);
                return match.Groups[1].Value;
            }

            Assert.That(FTBlendshapeOptimizers.VrcfuryKeepsFaceTrackingFrom, Is.EqualTo(Expression("PAWLYGON_VRCFURY_KEEPS_FT")));
            Assert.That(FTBlendshapeOptimizers.AaoKeepsFaceTrackingFrom, Is.EqualTo(Expression("PAWLYGON_AAO_KEEPS_FT")));
        }

        /// <summary>The descriptor's face tracking setup as text, to compare two builds.</summary>
        static string Describe(VRCAvatarDescriptor descriptor)
        {
            var sets = descriptor.faceTrackingBlendshapesSetList.Select(JsonUtility.ToJson);
            var behaviors = descriptor.faceTrackingParameterBehaviors.Select(JsonUtility.ToJson);
            return string.Join("\n", sets.Concat(behaviors)) + $"\n{descriptor.lipSync}|{descriptor.enableFaceTracking}|{descriptor.useAllTrackingKeys}";
        }
    }
}
