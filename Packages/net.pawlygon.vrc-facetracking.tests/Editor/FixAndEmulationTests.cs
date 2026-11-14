using System.Linq;
using NUnit.Framework;

namespace Pawlygon.FaceTracking.Editor.Tests
{
    public class FixAndEmulationTests
    {
        TestAvatar avatar;

        [SetUp]
        public void SetUp() => avatar = new TestAvatar();

        [TearDown]
        public void TearDown() => avatar.Dispose();

        FaceTrackingSetupPlan Setup(params string[] blendshapes)
        {
            avatar.AddMesh("Body", blendshapes);
            return FaceTrackingSetupPlan.Resolve(avatar.Add<PawlygonFaceTracking>(), avatar.Descriptor);
        }

        [Test]
        public void DetectsFixNamedAfterTheShapesItFixes()
        {
            var setup = Setup("JawOpen", "EyeClosedLeft", "JawOpen-EyeClosedLeft", "Not-AFix");
            var plan = FTFixPlan.Resolve(avatar.Add<PawlygonFTBlendshapeFixes>(), setup.Meshes, new[] { setup.Set });

            Assert.That(plan.Entries.Count, Is.EqualTo(1));
            var fix = plan.Entries[0];
            Assert.That(fix.Blendshape, Is.EqualTo("JawOpen-EyeClosedLeft"));
            Assert.That(fix.Problem, Is.Null);
            Assert.That(fix.Expression, Is.EqualTo("(JawOpen + EyeClosedLeft) / 2"));
            Assert.That(fix.WeightSet, Is.EqualTo(PawlygonFaceTracking.BaseSetName));
        }

        [TestCase(FTFixCombine.Minimum, "min(JawOpen, EyeClosedLeft)")]
        [TestCase(FTFixCombine.Maximum, "max(JawOpen, EyeClosedLeft)")]
        [TestCase(FTFixCombine.Multiply, "JawOpen * EyeClosedLeft")]
        public void CombineModes(FTFixCombine combine, string expression)
        {
            var setup = Setup("JawOpen", "EyeClosedLeft", "JawOpen-EyeClosedLeft");
            var component = avatar.Add<PawlygonFTBlendshapeFixes>();
            component.combine = combine;

            Assert.That(FTFixPlan.Resolve(component, setup.Meshes, new[] { setup.Set }).Entries[0].Expression, Is.EqualTo(expression));
        }

        [Test]
        public void ExcludedFixIsListedButMarked()
        {
            var setup = Setup("JawOpen", "EyeClosedLeft", "JawOpen-EyeClosedLeft");
            var component = avatar.Add<PawlygonFTBlendshapeFixes>();
            component.excludedFixes.Add("JawOpen-EyeClosedLeft");

            Assert.That(FTFixPlan.Resolve(component, setup.Meshes, new[] { setup.Set }).Entries[0].Excluded, Is.True);
        }

        [Test]
        public void ManualFixWithUndrivenSourceHasAProblem()
        {
            var setup = Setup("JawOpen", "MyFix");
            var component = avatar.Add<PawlygonFTBlendshapeFixes>();
            component.fixes.Add(new FTBlendshapeFix { blendshape = "MyFix", sources = "JawOpen, Hat" });

            var fix = FTFixPlan.Resolve(component, setup.Meshes, new[] { setup.Set }).Entries.Single();
            Assert.That(fix.IsManual, Is.True);
            Assert.That(fix.Problem, Does.Contain("Hat"));
        }

        [Test]
        public void FixesStayOnTheirSetWhenVisemesAreSplitOff()
        {
            // The viseme set has no key mappings, so it must never become a fix's weight set
            var setup = Setup("JawOpen", "EyeClosedLeft", "JawOpen-EyeClosedLeft", "vrc.v_aa");
            var sets = new[] { setup.Set }.ToList();
            var toggles = FTTogglesPlan.Resolve(avatar.Add<PawlygonFTToggles>(), sets, true, true);
            toggles.SplitVisemes(sets);

            var plan = FTFixPlan.Resolve(avatar.Add<PawlygonFTBlendshapeFixes>(), setup.Meshes, sets);
            Assert.That(plan.Entries.Single().WeightSet, Is.EqualTo(PawlygonFaceTracking.BaseSetName));
        }

        [Test]
        public void EmulationNeedsAllTrackingKeys()
        {
            var setup = Setup(TestAvatar.AllKeyNames);
            var plan = FTEmulationPlan.Resolve(avatar.Add<PawlygonFTEmulation>(), new[] { setup.Set }, false);

            Assert.That(plan.NeedsAllTrackingKeys, Is.True);
            Assert.That(plan.Shapes, Is.Empty);
        }

        [Test]
        public void EmulationRewritesTheShapesOfItsRules()
        {
            var setup = Setup(TestAvatar.AllKeyNames);
            var plan = FTEmulationPlan.Resolve(avatar.Add<PawlygonFTEmulation>(), new[] { setup.Set }, true);

            var jaw = plan.Shapes.Single(s => s.Blendshape == "JawOpen");
            Assert.That(jaw.Expression, Is.EqualTo("max(JawOpen, TongueOut * 0.25)"));
            Assert.That(jaw.Set, Is.EqualTo(PawlygonFaceTracking.BaseSetName));
            Assert.That(plan.SkippedRules, Is.Empty);
        }

        [Test]
        public void EmulationSkipsRulesWithoutTheirBlendshape()
        {
            // No brow shapes, so the brow rules have nothing to rewrite
            var setup = Setup("JawOpen", "TongueOut", "EyeWideLeft", "EyeWideRight");
            var plan = FTEmulationPlan.Resolve(avatar.Add<PawlygonFTEmulation>(), new[] { setup.Set }, true);

            Assert.That(plan.Shapes.Any(s => s.Blendshape.StartsWith("Brow")), Is.False);
            Assert.That(plan.Shapes.Any(s => s.Blendshape == "JawOpen"), Is.True);
        }

        [Test]
        public void UnmapTakesRewrittenShapesOutOfTheSet()
        {
            var setup = Setup(TestAvatar.AllKeyNames);
            var plan = FTEmulationPlan.Resolve(avatar.Add<PawlygonFTEmulation>(), new[] { setup.Set }, true);
            plan.UnmapShapes(new[] { setup.Set });

            Assert.That(setup.Set.faceTrackingBlendshapeNames[FTKeys.KeyByName("JawOpen")], Is.Empty);
            Assert.That(setup.Set.faceTrackingBlendshapeNames[FTKeys.KeyByName("JawLeft")], Is.EqualTo("JawLeft"));
        }
    }
}
