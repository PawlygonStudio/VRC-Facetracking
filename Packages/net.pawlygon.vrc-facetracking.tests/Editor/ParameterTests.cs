using System.Linq;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;

namespace Pawlygon.FaceTracking.Editor.Tests
{
    public class ParameterTests
    {
        // Every combined VRCFaceTracking v2 parameter the catalog rebuilds from tracking keys
        static readonly string[] CombinedParameters =
        {
            "EyeLeftX", "EyeRightX", "EyeY", "EyeLidLeft", "EyeLidRight", "PupilDilation", "BrowExpressionLeft", "BrowExpressionRight",
            "CheekPuffSuck", "CheekPuffSuckLeft", "CheekPuffSuckRight", "NoseSneer", "JawX", "LipFunnel", "LipPucker", "LipSuckUpper",
            "LipSuckLower", "MouthUpperUp", "MouthLowerDown", "MouthX", "MouthPress", "SmileFrownLeft", "SmileFrownRight",
            "MouthTightenerStretchLeft", "MouthTightenerStretchRight", "TongueX", "TongueY",
        };

        [TestCase("FT/v2/JawOpen", "JawOpen")]
        [TestCase("v2/JawOpen", "JawOpen")]
        [TestCase("OSCm/Proxy/FT/v2/JawOpen", "JawOpen")]
        [TestCase("VF12_FT/v2/JawOpen", "JawOpen")]
        [TestCase("FT/v2/JawX", "JawRight - JawLeft")]
        public void KnownParameterHasExpression(string parameter, string expression)
        {
            Assert.That(FTParameterCatalog.GetExpression(parameter), Is.EqualTo(expression));
        }

        [TestCase("JawOpen")]
        [TestCase("FT/v2/NotAKey")]
        [TestCase("FT/v1/JawOpen")]
        [TestCase("GestureLeft")]
        public void OtherParameterHasNone(string parameter)
        {
            Assert.That(FTParameterCatalog.GetExpression(parameter), Is.Null);
        }

        [Test]
        public void CombinedExpressionsAreValid()
        {
            // Catches tracking keys the SDK renamed or removed
            foreach (var name in CombinedParameters)
            {
                var expression = FTParameterCatalog.GetExpression("FT/v2/" + name);
                Assert.That(expression, Is.Not.Null, name);
                Assert.That(FTExpressionCheck.Validate(expression, true, out _), Is.Null, $"{name}: {expression}");
            }
        }

        [Test]
        public void ReferencedKeysLeaveOutFunctionsAndVoice()
        {
            Assert.That(FTParameterCatalog.GetReferencedKeys("max(JawOpen, Voice) * 0.5 + JawOpen - JawLeft"), Is.EquivalentTo(new[] { "JawOpen", "JawLeft" }));
        }

        [TestCase("JawOpen")]
        [TestCase("(JawOpen + JawLeft) / 2")]
        [TestCase("clamp(Voice * 2, 0, 1)")]
        [TestCase("max(abs(JawRight - JawLeft), min(JawOpen, .5))")]
        public void ValidExpression(string expression)
        {
            // 2.0.1 flagged every closing parenthesis
            Assert.That(FTExpressionCheck.Validate(expression, true, out _), Is.Null);
        }

        [TestCase("")]
        [TestCase("(JawOpen")]
        [TestCase("JawOpen)")]
        [TestCase("jawopen")]
        [TestCase("JawOpen $ 2")]
        public void InvalidExpression(string expression)
        {
            Assert.That(FTExpressionCheck.Validate(expression, true, out var isWarning), Is.Not.Null);
            Assert.That(isWarning, Is.False);
        }

        [Test]
        public void HardwareKeyWithoutAllTrackingKeysIsAWarning()
        {
            Assert.That(FTExpressionCheck.Validate("JawOpen", false, out var isWarning), Is.Not.Null);
            Assert.That(isWarning, Is.True);
            Assert.That(FTExpressionCheck.Validate("EyeClosedLeft", false, out _), Is.Null);
        }

        [Test]
        public void OnlyParametersALayerReadsAreForwarded()
        {
            using (var avatar = new TestAvatar())
            {
                var fx = avatar.AddFX();
                fx.AddParameter("FT/v2/JawOpen", AnimatorControllerParameterType.Float);
                fx.AddParameter("FT/v2/JawX", AnimatorControllerParameterType.Float);
                fx.AddParameter("Unrelated", AnimatorControllerParameterType.Float);
                AddLayerReading(fx, "FT/v2/JawOpen");

                var component = avatar.Add<PawlygonFTParameters>();
                var plan = FTForwardingPlan.ForBuild(component, avatar.Descriptor, true);

                Assert.That(plan.Entries.Select(e => e.Parameter), Is.EquivalentTo(new[] { "FT/v2/JawOpen" }));
                Assert.That(plan.Entries[0].Expression, Is.EqualTo("JawOpen"));
                Assert.That(plan.Unread, Is.EquivalentTo(new[] { "FT/v2/JawX" }));
            }
        }

        [Test]
        public void ManualEntryReplacesTheDetectedOne()
        {
            using (var avatar = new TestAvatar())
            {
                var fx = avatar.AddFX();
                fx.AddParameter("FT/v2/JawOpen", AnimatorControllerParameterType.Float);
                AddLayerReading(fx, "FT/v2/JawOpen");

                var component = avatar.Add<PawlygonFTParameters>();
                component.parameters.Add(new FTForwardedParameter { parameter = "FT/v2/JawOpen", expression = "JawOpen * 0.5" });
                component.parameters.Add(new FTForwardedParameter { parameter = "My/Param", expression = "JawLeft" });
                var plan = FTForwardingPlan.ForBuild(component, avatar.Descriptor, true);

                Assert.That(plan.Entries.Count, Is.EqualTo(2));
                var jaw = plan.Entries.Single(e => e.Parameter == "FT/v2/JawOpen");
                Assert.That(jaw.Expression, Is.EqualTo("JawOpen * 0.5"));
                Assert.That(jaw.IsManual, Is.True);
            }
        }

        [Test]
        public void AutoDetectOffForwardsOnlyManualEntries()
        {
            using (var avatar = new TestAvatar())
            {
                var fx = avatar.AddFX();
                fx.AddParameter("FT/v2/JawOpen", AnimatorControllerParameterType.Float);
                AddLayerReading(fx, "FT/v2/JawOpen");

                var component = avatar.Add<PawlygonFTParameters>();
                component.autoDetect = false;

                Assert.That(FTForwardingPlan.ForBuild(component, avatar.Descriptor, true).Entries, Is.Empty);
            }
        }

        [Test]
        public void RankFollowsTheForwardedCount()
        {
            Assert.That(FTForwardingPlan.RankFor(0), Is.EqualTo("Excellent"));
            Assert.That(FTForwardingPlan.RankFor(10), Is.EqualTo("Excellent"));
            Assert.That(FTForwardingPlan.RankFor(11), Is.EqualTo("Good"));
            Assert.That(FTForwardingPlan.RankFor(41), Is.EqualTo("Very Poor"));
        }

        /// <summary>A layer with a transition that waits for the parameter, which counts as reading it.</summary>
        internal static void AddLayerReading(AnimatorController controller, string parameter, string layerName = "Reader")
        {
            controller.AddLayer(layerName);
            var machine = controller.layers[controller.layers.Length - 1].stateMachine;
            var idle = machine.AddState("Idle");
            var active = machine.AddState("Active");
            machine.defaultState = idle;

            var type = controller.parameters.First(p => p.name == parameter).type;
            var transition = idle.AddTransition(active);
            if (type == AnimatorControllerParameterType.Bool) transition.AddCondition(AnimatorConditionMode.If, 0, parameter);
            else transition.AddCondition(AnimatorConditionMode.Greater, 0.5f, parameter);
        }
    }
}
