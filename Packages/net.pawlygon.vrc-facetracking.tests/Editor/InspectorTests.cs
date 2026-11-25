using NUnit.Framework;
using UnityEngine.UIElements;

namespace Pawlygon.FaceTracking.Editor.Tests
{
    /// <summary>The parts of the inspectors that decide where a problem shows.</summary>
    public class InspectorTests
    {
        [Test]
        public void AMarkShowsItsWorstProblemAndGoesAwayWhenItIsNotSetAgain()
        {
            var mark = new FTMark();
            Assert.That(mark.style.display.value, Is.EqualTo(DisplayStyle.None));

            var issues = new FTIssues();
            issues.Mark(mark, FTKind.Warning, "First.");
            issues.Mark(mark, FTKind.Error, "Second.", "Why.");
            issues.Mark(mark, FTKind.Warning, "Third.");
            Assert.That(mark.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(mark.ClassListContains("ft-mark--error"), Is.True);
            Assert.That(mark.ClassListContains("ft-mark--warning"), Is.False);
            Assert.That(mark.tooltip, Does.Contain("First.").And.Contain("Second.").And.Contain("Why.").And.Contain("Third."));
            Assert.That(issues.Marks, Does.Contain(mark));

            // The next refresh starts the mark over
            var next = new FTIssues();
            next.Mark(mark, FTKind.Warning, "Only this.");
            Assert.That(mark.tooltip, Is.EqualTo("Only this."));
            Assert.That(mark.ClassListContains("ft-mark--error"), Is.False);

            mark.Hide();
            Assert.That(mark.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(mark.ClassListContains("ft-mark--warning"), Is.False);
        }

        [Test]
        public void SetupWarningsSayWhichSettingTheyAreAbout()
        {
            using (var avatar = new TestAvatar())
            {
                avatar.AddMesh("Body", "JawOpen");
                var component = avatar.Add<PawlygonFaceTracking>();
                component.visemeDampenMode = FTVisemeDampenMode.DampenTracking;
                component.keyOverrides.Add(new FTKeyOverride { key = "JawOpen", blendshape = "Gone" });
                var plan = FaceTrackingSetupPlan.Resolve(component, avatar.Descriptor);

                FTSetupArea Area(string part) => plan.AreaOf(plan.Warnings.Find(w => w.Contains(part)));
                Assert.That(Area("dampening"), Is.EqualTo(FTSetupArea.Dampening));
                Assert.That(Area("Gone"), Is.EqualTo(FTSetupArea.Keys));
                Assert.That(Area("viseme blendshapes"), Is.EqualTo(FTSetupArea.LipSync));
                Assert.That(Area("EyeClosedLeft"), Is.EqualTo(FTSetupArea.Eyelids));
                Assert.That(plan.AreaOf("not one of its warnings"), Is.EqualTo(FTSetupArea.General));
            }
        }
    }
}
