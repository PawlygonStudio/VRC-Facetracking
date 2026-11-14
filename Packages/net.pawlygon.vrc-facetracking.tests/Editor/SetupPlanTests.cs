using System.Linq;
using NUnit.Framework;
using VRC.SDKBase;
using EyeGazeMode = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.CustomEyeLookSettings.EyeGazeMode;
using Key = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingKey;

namespace Pawlygon.FaceTracking.Editor.Tests
{
    public class SetupPlanTests
    {
        TestAvatar avatar;
        PawlygonFaceTracking component;

        [SetUp]
        public void SetUp()
        {
            avatar = new TestAvatar();
            component = avatar.Add<PawlygonFaceTracking>();
        }

        [TearDown]
        public void TearDown() => avatar.Dispose();

        FaceTrackingSetupPlan Resolve() => FaceTrackingSetupPlan.Resolve(component, avatar.Descriptor);

        static string Mapped(FaceTrackingSetupPlan plan, Key key) => plan.Set.faceTrackingBlendshapeNames[(int)key];

        [Test]
        public void MapsUnifiedExpressionsNames()
        {
            var body = avatar.AddMesh("Body", "JawOpen", "EyeClosedLeft", "EyeClosedRight", "Unrelated");
            var plan = Resolve();

            Assert.That(plan.Errors, Is.Empty);
            Assert.That(plan.Meshes, Is.EquivalentTo(new[] { body }));
            Assert.That(plan.Set.setName, Is.EqualTo(PawlygonFaceTracking.BaseSetName));
            Assert.That(Mapped(plan, Key.JawOpen), Is.EqualTo("JawOpen"));
            Assert.That(Mapped(plan, Key.EyeClosedLeft), Is.EqualTo("EyeClosedLeft"));
            Assert.That(plan.UnmappedKeys, Does.Contain((int)Key.TongueOut));
            Assert.That(plan.UseFaceTrackingEyelids, Is.True);
        }

        [Test]
        public void MeshesWithoutFaceTrackingBlendshapesAreLeftOut()
        {
            var body = avatar.AddMesh("Body", "JawOpen");
            avatar.AddMesh("Hair", "Long", "Short");

            Assert.That(Resolve().Meshes, Is.EquivalentTo(new[] { body }));
        }

        [Test]
        public void NoFaceTrackingMeshIsAnError()
        {
            avatar.AddMesh("Hair", "Long", "Short");
            Assert.That(Resolve().Errors, Is.Not.Empty);
        }

        [Test]
        public void MapsArkitOnlyAvatar()
        {
            avatar.AddMesh("Body", "jawOpen", "eyeBlinkLeft", "eyeBlinkRight", "mouthSmileLeft");
            var plan = Resolve();

            Assert.That(Mapped(plan, Key.JawOpen), Is.EqualTo("jawOpen"));
            Assert.That(Mapped(plan, Key.EyeClosedLeft), Is.EqualTo("eyeBlinkLeft"));
            Assert.That(Mapped(plan, Key.MouthCornerPullLeft), Is.EqualTo("mouthSmileLeft"));
            Assert.That(plan.UseFaceTrackingEyelids, Is.True);
        }

        [Test]
        public void UnifiedExpressionsComesBeforeArkit()
        {
            // 2.0.2: with the ARKit shapes listed first, the avatar smiled with mouthSmileRight
            avatar.AddMesh("Body", "mouthSmileRight", "browDownRight", "MouthSmileRight", "BrowDownRight");
            var plan = Resolve();

            Assert.That(Mapped(plan, Key.MouthCornerPullRight), Is.EqualTo("MouthSmileRight"));
            Assert.That(Mapped(plan, Key.BrowLowererRight), Is.EqualTo("BrowDownRight"));
        }

        [Test]
        public void ArkitFillInThatOverlapsIsLeftUnmapped()
        {
            // mouthSmileRight would fill in MouthCornerSlantRight, but it also moves the corner pull the avatar already has
            avatar.AddMesh("Body", "MouthCornerPullRight", "mouthSmileRight");
            var plan = Resolve();

            Assert.That(Mapped(plan, Key.MouthCornerPullRight), Is.EqualTo("MouthCornerPullRight"));
            Assert.That(Mapped(plan, Key.MouthCornerSlantRight), Is.Empty);
            Assert.That(plan.ArkitOverlaps.Keys, Does.Contain((int)Key.MouthCornerSlantRight));
        }

        [Test]
        public void KeyOverrideReplacesTheMatch()
        {
            avatar.AddMesh("Body", "JawOpen", "MyJaw");
            component.keyOverrides.Add(new FTKeyOverride { key = "JawOpen", blendshape = "MyJaw" });
            var plan = Resolve();

            Assert.That(Mapped(plan, Key.JawOpen), Is.EqualTo("MyJaw"));
            Assert.That(plan.AutoMatches[(int)Key.JawOpen], Is.EqualTo("JawOpen"));
            Assert.That(plan.OverriddenKeys, Does.Contain((int)Key.JawOpen));
        }

        [Test]
        public void EmptyKeyOverrideUnmapsTheKey()
        {
            avatar.AddMesh("Body", "JawOpen", "EyeClosedLeft");
            component.keyOverrides.Add(new FTKeyOverride { key = "JawOpen", blendshape = "" });
            var plan = Resolve();

            Assert.That(Mapped(plan, Key.JawOpen), Is.Empty);
            Assert.That(plan.UnmappedKeys, Does.Contain((int)Key.JawOpen));
        }

        [Test]
        public void KeyOverrideToMissingBlendshapeFallsBackWithWarning()
        {
            avatar.AddMesh("Body", "JawOpen");
            component.keyOverrides.Add(new FTKeyOverride { key = "JawOpen", blendshape = "Gone" });
            var plan = Resolve();

            Assert.That(Mapped(plan, Key.JawOpen), Is.EqualTo("JawOpen"));
            Assert.That(plan.Warnings.Any(w => w.Contains("Gone")), Is.True);
        }

        [Test]
        public void BasicKeysOnlyWithoutAllTrackingKeys()
        {
            avatar.AddMesh("Body", "JawOpen", "EyeClosedLeft", "EyeClosedRight");
            component.useAllTrackingKeys = false;
            var plan = Resolve();

            Assert.That(Mapped(plan, Key.EyeClosedLeft), Is.EqualTo("EyeClosedLeft"));
            Assert.That(Mapped(plan, Key.JawOpen), Is.Empty);
        }

        [Test]
        public void TongueStepsAreUsedWhenThereIsNoTongueOut()
        {
            avatar.AddMesh("Body", "JawOpen", "Tongue_LongStep1", "Tongue_LongStep2");
            var plan = Resolve();

            Assert.That(plan.TongueSteps, Is.Not.Null);
            Assert.That(plan.TongueSteps.Step1, Is.EqualTo("Tongue_LongStep1"));
            Assert.That(plan.TongueSteps.Step2, Is.EqualTo("Tongue_LongStep2"));
            // The steps are driven by expressions, so the key itself maps nothing
            Assert.That(Mapped(plan, Key.TongueOut), Is.Empty);
            Assert.That(plan.MappedKeys, Does.Contain((int)Key.TongueOut));
        }

        [Test]
        public void TongueOutWinsOverStepsInAuto()
        {
            avatar.AddMesh("Body", "Tongue_LongStep1", "Tongue_LongStep2", "TongueOut");
            var plan = Resolve();

            Assert.That(plan.TongueSteps, Is.Null);
            Assert.That(plan.FoundTongueSteps, Is.Not.Null);
            Assert.That(Mapped(plan, Key.TongueOut), Is.EqualTo("TongueOut"));
            Assert.That(plan.TongueConflict, Is.Not.Null);
        }

        [Test]
        public void StepsModeUsesStepsNextToTongueOut()
        {
            avatar.AddMesh("Body", "Tongue_LongStep1", "Tongue_LongStep2", "TongueOut");
            component.tongueMode = FTTongueMode.Steps;
            var plan = Resolve();

            Assert.That(plan.TongueSteps, Is.Not.Null);
            Assert.That(Mapped(plan, Key.TongueOut), Is.Empty);
        }

        [Test]
        public void VisemesAreMatchedByName()
        {
            avatar.AddMesh("Body", "JawOpen", "vrc.v_aa", "vrc.v_oh");
            var plan = Resolve();

            Assert.That(plan.UseFaceTrackingLipSync, Is.True);
            Assert.That(plan.MappedVisemes, Is.EqualTo(2));
            Assert.That(plan.VisemesFromDescriptor, Is.False);
            Assert.That(plan.Set.visemeBlendshapeNames[(int)VRC_AvatarDescriptor.Viseme.aa], Is.EqualTo("vrc.v_aa"));
        }

        [Test]
        public void VisemesAreTakenFromTheDescriptor()
        {
            var body = avatar.AddMesh("Body", "JawOpen", "MyAA", "vrc.v_aa");
            var visemes = Enumerable.Repeat("", FTKeys.VisemeCount).ToArray();
            visemes[(int)VRC_AvatarDescriptor.Viseme.aa] = "MyAA";
            avatar.Descriptor.lipSync = VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape;
            avatar.Descriptor.VisemeSkinnedMesh = body;
            avatar.Descriptor.VisemeBlendShapes = visemes;
            var plan = Resolve();

            Assert.That(plan.VisemesFromDescriptor, Is.True);
            Assert.That(plan.MappedVisemes, Is.EqualTo(1));
            Assert.That(plan.Set.visemeBlendshapeNames[(int)VRC_AvatarDescriptor.Viseme.aa], Is.EqualTo("MyAA"));
        }

        [Test]
        public void LipSyncIsLeftAloneWithoutVisemes()
        {
            avatar.AddMesh("Body", "JawOpen");
            var plan = Resolve();

            Assert.That(plan.UseFaceTrackingLipSync, Is.False);
            Assert.That(plan.Warnings, Is.Not.Empty);
        }

        [Test]
        public void KeepExistingLipSyncMapsNoVisemes()
        {
            avatar.AddMesh("Body", "JawOpen", "vrc.v_aa");
            component.lipSync = FTLipSyncMode.KeepExisting;
            var plan = Resolve();

            Assert.That(plan.UseFaceTrackingLipSync, Is.False);
            Assert.That(plan.MappedVisemes, Is.EqualTo(0));
        }

        [Test]
        public void GazeUsesBlendshapesWithoutEyeBones()
        {
            avatar.AddMesh("Body", "EyeLookUpLeft", "EyeLookUpRight", "EyeLookDownLeft", "EyeLookDownRight");
            Assert.That(Resolve().GazeMode, Is.EqualTo(EyeGazeMode.FaceTracking));
        }

        [Test]
        public void GazeIsLeftAloneWithoutBlendshapesOrBones()
        {
            avatar.AddMesh("Body", "JawOpen");
            Assert.That(Resolve().GazeMode, Is.Null);
        }

        [Test]
        public void EyelidsAreLeftAloneWithoutBothEyeClosedShapes()
        {
            avatar.AddMesh("Body", "JawOpen", "EyeClosedLeft");
            var plan = Resolve();

            Assert.That(plan.UseFaceTrackingEyelids, Is.False);
            Assert.That(plan.Warnings.Any(w => w.Contains("EyeClosed")), Is.True);
        }

        [Test]
        public void MoreThanSixteenMeshesIsAnError()
        {
            for (var i = 0; i < FTBuildContext.MaxMeshes + 1; i++) avatar.AddMesh($"Mesh {i}", "JawOpen");
            Assert.That(Resolve().Errors.Any(e => e.Contains("Manual")), Is.True);
        }

        [Test]
        public void ManualModeUsesTheListedMeshes()
        {
            avatar.AddMesh("Body", "JawOpen");
            var face = avatar.AddMesh("Face", "JawOpen");
            component.meshMode = FTMeshMode.Manual;
            component.meshes.Add(face);

            Assert.That(Resolve().Meshes, Is.EquivalentTo(new[] { face }));
        }
    }
}
