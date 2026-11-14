using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Pawlygon.FaceTracking.Editor.Tests
{
    public class GuardTests
    {
        TestAvatar avatar;

        [SetUp]
        public void SetUp() => avatar = new TestAvatar();

        [TearDown]
        public void TearDown() => avatar.Dispose();

        /// <summary>A gesture layer: Idle, and a Fist state reached while GestureLeft is 1, which plays the clip.</summary>
        static AnimatorStateMachine AddGestureLayer(AnimatorController controller, AnimationClip clip, bool writeDefaults = true)
        {
            if (controller.parameters.All(p => p.name != FXGuardAnalysis.GestureLeft))
            {
                controller.AddParameter(FXGuardAnalysis.GestureLeft, AnimatorControllerParameterType.Int);
            }
            controller.AddLayer("Left Hand");
            var machine = controller.layers[controller.layers.Length - 1].stateMachine;
            var idle = machine.AddState("Idle");
            var fist = machine.AddState("Fist");
            fist.motion = clip;
            idle.writeDefaultValues = fist.writeDefaultValues = writeDefaults;
            machine.defaultState = idle;

            idle.AddTransition(fist).AddCondition(AnimatorConditionMode.Equals, 1, FXGuardAnalysis.GestureLeft);
            fist.AddTransition(idle).AddCondition(AnimatorConditionMode.NotEqual, 1, FXGuardAnalysis.GestureLeft);
            return machine;
        }

        static AnimationClip BlendshapeClip(string blendshape, float value = 100f)
        {
            var clip = new AnimationClip { name = blendshape };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape." + blendshape),
                AnimationCurve.Constant(0, 1f / 60f, value));
            return clip;
        }

        static FXGuardWriter.Spec GestureSpec(AnimatorController controller)
        {
            return FXGuardWriter.CreateSpec(controller, FXGuardWriter.GestureGuardState, FXGuardWriter.GestureGuardParameter,
                AnimatorControllerParameterType.Bool, new Vector3(30, -80));
        }

        [Test]
        public void AnalysisFindsGestureLayerThatAnimatesTheFace()
        {
            var controller = avatar.NewController("FX");
            AddGestureLayer(controller, BlendshapeClip("Smile"));
            var body = avatar.AddMesh("Body", "Smile");

            var analysis = FXGuardAnalysis.Analyze(controller, new FXFaceMeshes(new[] { body }, avatar.Root.transform));

            Assert.That(analysis.GestureLayers.Count, Is.EqualTo(1));
            var layer = analysis.GestureLayers[0];
            Assert.That(layer.DrivesExpressions, Is.True);
            Assert.That(layer.ExpressionBlendshapes, Is.EquivalentTo(new[] { "Smile" }));
            Assert.That(layer.AlreadyGuarded, Is.False);
        }

        [Test]
        public void AnalysisIgnoresThisPackagesOwnLayers()
        {
            var controller = avatar.NewController("FX");
            FTGuardDriver.AddGestureDriver(controller, true);
            FTGuardDriver.AddBlinkDriver(controller);

            var analysis = FXGuardAnalysis.Analyze(controller, new FXFaceMeshes(new SkinnedMeshRenderer[0], avatar.Root.transform));

            Assert.That(controller.layers.All(l => FXGuardAnalysis.IsOwnLayer(l.name)), Is.True);
            Assert.That(analysis.GestureLayers, Is.Empty);
            Assert.That(analysis.BlinkLayers, Is.Empty);
        }

        [Test]
        public void OwnLayerIsRecognizedAfterVrcfuryPrefixedIt()
        {
            Assert.That(FXGuardAnalysis.IsOwnLayer("[VF12] Pawlygon FT - Gesture Guard"), Is.True);
            Assert.That(FXGuardAnalysis.CleanLayerName("[VF12] Left Hand"), Is.EqualTo("Left Hand"));
        }

        [Test]
        public void EnsureGuardAddsAGuardState()
        {
            var controller = avatar.NewController("FX");
            var machine = AddGestureLayer(controller, BlendshapeClip("Smile"));

            FXGuardWriter.EnsureGuard(controller, 0, GestureSpec(controller));

            Assert.That(FXGuardWriter.HasGuard(machine, FXGuardWriter.GestureGuardState), Is.True);
            Assert.That(controller.parameters.Single(p => p.name == FXGuardWriter.GestureGuardParameter).type, Is.EqualTo(AnimatorControllerParameterType.Bool));

            // First Any State transition, so nothing else in the layer wins while the guard is on
            var guard = machine.anyStateTransitions[0];
            Assert.That(guard.destinationState.name, Is.EqualTo(FXGuardWriter.GestureGuardState));
            Assert.That(guard.conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.If));

            // Back to the default state once it clears
            var exit = guard.destinationState.transitions.Single();
            Assert.That(exit.destinationState, Is.EqualTo(machine.defaultState));
            Assert.That(exit.conditions.Single().mode, Is.EqualTo(AnimatorConditionMode.IfNot));
        }

        [Test]
        public void EnsureGuardTwiceKeepsOneGuard()
        {
            var controller = avatar.NewController("FX");
            var machine = AddGestureLayer(controller, BlendshapeClip("Smile"));

            FXGuardWriter.EnsureGuard(controller, 0, GestureSpec(controller));
            FXGuardWriter.EnsureGuard(controller, 0, GestureSpec(controller));

            Assert.That(machine.anyStateTransitions.Length, Is.EqualTo(1));
            Assert.That(machine.states.Count(s => s.state.name == FXGuardWriter.GestureGuardState), Is.EqualTo(1));
        }

        [Test]
        public void GuardStateIsEmptyWithWriteDefaultsOn()
        {
            var controller = avatar.NewController("FX");
            var machine = AddGestureLayer(controller, BlendshapeClip("Smile"), true);

            FXGuardWriter.EnsureGuard(controller, 0, GestureSpec(controller));

            var guard = machine.anyStateTransitions[0].destinationState;
            Assert.That(guard.writeDefaultValues, Is.True);
            Assert.That(guard.motion, Is.Null);
        }

        [Test]
        public void GuardStateResetsBlendshapesWithWriteDefaultsOff()
        {
            var controller = avatar.NewController("FX");
            var machine = AddGestureLayer(controller, BlendshapeClip("Smile"), false);

            FXGuardWriter.EnsureGuard(controller, 0, GestureSpec(controller));

            var guard = machine.anyStateTransitions[0].destinationState;
            Assert.That(guard.writeDefaultValues, Is.False);
            var reset = guard.motion as AnimationClip;
            Assert.That(reset, Is.Not.Null);
            var binding = AnimationUtility.GetCurveBindings(reset).Single();
            Assert.That(binding.propertyName, Is.EqualTo("blendShape.Smile"));
            Assert.That(AnimationUtility.GetEditorCurve(reset, binding).Evaluate(0), Is.EqualTo(0f));
        }

        [Test]
        public void AnalysisSeesTheBakedGuard()
        {
            var controller = avatar.NewController("FX");
            AddGestureLayer(controller, BlendshapeClip("Smile"));
            FXGuardWriter.EnsureGuard(controller, 0, GestureSpec(controller));

            var analysis = FXGuardAnalysis.Analyze(controller, new FXFaceMeshes(new SkinnedMeshRenderer[0], avatar.Root.transform));
            Assert.That(analysis.GestureLayers.Single().AlreadyGuarded, Is.True);
        }

        [Test]
        public void DeclaredParameterAloneIsNotDriven()
        {
            // Controllers made for 1.x often declare the guard parameters without a layer that uses them
            var controller = avatar.NewController("FX");
            controller.AddParameter(FXGuardWriter.GestureGuardParameter, AnimatorControllerParameterType.Bool);
            AddGestureLayer(controller, BlendshapeClip("Smile"));

            Assert.That(FaceTrackingGuardsBuilder.UsesParameter(controller, FXGuardWriter.GestureGuardParameter), Is.False);
        }

        [Test]
        public void ParameterReadByAGuardIsDriven()
        {
            var controller = avatar.NewController("FX");
            AddGestureLayer(controller, BlendshapeClip("Smile"));
            FXGuardWriter.EnsureGuard(controller, 0, GestureSpec(controller));

            Assert.That(FaceTrackingGuardsBuilder.UsesParameter(controller, FXGuardWriter.GestureGuardParameter), Is.True);
            Assert.That(FaceTrackingGuardsBuilder.UsesParameter(controller, FXGuardWriter.BlinkGuardParameter), Is.False);
        }

        [Test]
        public void ReadParametersCoversConditionsAndBlendTrees()
        {
            var controller = avatar.NewController("FX");
            controller.AddParameter("Condition", AnimatorControllerParameterType.Float);
            controller.AddParameter("Blend", AnimatorControllerParameterType.Float);
            controller.AddParameter("Unused", AnimatorControllerParameterType.Float);
            ParameterTests.AddLayerReading(controller, "Condition");

            var machine = controller.layers[0].stateMachine;
            var tree = new BlendTree { name = "Tree", blendParameter = "Blend" };
            machine.AddState("Tree").motion = tree;

            var read = FTAnimatorReads.ReadParameters(controller.layers[0]).ToList();
            Assert.That(read, Is.EquivalentTo(new[] { "Condition", "Blend" }));
            Object.DestroyImmediate(tree);
        }
    }
}
