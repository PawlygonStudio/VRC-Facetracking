using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using BlendshapesSet = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingBlendshapesSet;

namespace Pawlygon.FaceTracking.Editor.Tests
{
    public class TogglesTests
    {
        TestAvatar avatar;
        PawlygonFTToggles component;

        [SetUp]
        public void SetUp()
        {
            avatar = new TestAvatar();
            component = avatar.Add<PawlygonFTToggles>();
        }

        [TearDown]
        public void TearDown() => avatar.Dispose();

        static BlendshapesSet SetWithVisemes(string name, float weight = 1f)
        {
            var set = FTBuildContext.CreateSet(name, weight);
            set.faceTrackingBlendshapeNames[FTKeys.KeyByName("JawOpen")] = "JawOpen";
            set.visemeBlendshapeNames[(int)VRC_AvatarDescriptor.Viseme.aa] = "vrc.v_aa";
            set.visemeBlendshapeNames[(int)VRC_AvatarDescriptor.Viseme.oh] = "vrc.v_oh";
            set.visemeBlendshapeMultipliers[(int)VRC_AvatarDescriptor.Viseme.oh] = 0.5f;
            return set;
        }

        [Test]
        public void DefaultsAddTheToggleWithVisemesPlaying()
        {
            Assert.That(component.visemesToggle, Is.True);
            Assert.That(component.visemesDisabledByDefault, Is.False);
            Assert.That(component.menuName, Is.EqualTo("Face Tracking"));
        }

        [Test]
        public void PlansAVisemeSetForTheBaseSet()
        {
            var plan = FTTogglesPlan.Resolve(component, new[] { SetWithVisemes(PawlygonFaceTracking.BaseSetName) }, true, true);

            Assert.That(plan.AddsVisemesToggle, Is.True);
            Assert.That(plan.VisemesSkipped, Is.Null);
            Assert.That(plan.SyncedBits, Is.EqualTo(1));
            var set = plan.VisemeSets.Single();
            Assert.That(set.Name, Is.EqualTo(FTTogglesPlan.VisemeSetName));
            Assert.That(set.Source, Is.EqualTo(PawlygonFaceTracking.BaseSetName));
            Assert.That(set.Visemes, Is.EqualTo(2));
        }

        [Test]
        public void ToggleOffPlansNothing()
        {
            component.visemesToggle = false;
            var plan = FTTogglesPlan.Resolve(component, new[] { SetWithVisemes(PawlygonFaceTracking.BaseSetName) }, true, true);

            Assert.That(plan.AddsVisemesToggle, Is.False);
            Assert.That(plan.VisemesSkipped, Is.Null);
            Assert.That(plan.SyncedBits, Is.EqualTo(0));
        }

        [Test]
        public void SkippedWithoutFaceTrackingVisemes()
        {
            var sets = new[] { SetWithVisemes(PawlygonFaceTracking.BaseSetName) };
            Assert.That(FTTogglesPlan.Resolve(component, sets, false, true).VisemesSkipped, Is.Not.Null);

            var noVisemes = FTBuildContext.CreateSet(PawlygonFaceTracking.BaseSetName, 1f);
            var plan = FTTogglesPlan.Resolve(component, new[] { noVisemes }, true, true);
            Assert.That(plan.AddsVisemesToggle, Is.False);
            Assert.That(plan.VisemesSkipped, Is.Not.Null);
        }

        [Test]
        public void SkippedWithoutVrcfuryOrModularAvatar()
        {
            var plan = FTTogglesPlan.Resolve(component, new[] { SetWithVisemes(PawlygonFaceTracking.BaseSetName) }, true, false);

            Assert.That(plan.AddsVisemesToggle, Is.False);
            Assert.That(plan.VisemesSkipped, Is.Not.Null);
        }

        [Test]
        public void SplitMovesVisemesAndKeepsTheKeys()
        {
            var sets = new List<BlendshapesSet> { SetWithVisemes(PawlygonFaceTracking.BaseSetName) };
            var plan = FTTogglesPlan.Resolve(component, sets, true, true);
            plan.SplitVisemes(sets);

            Assert.That(sets.Select(s => s.setName), Is.EqualTo(new[] { PawlygonFaceTracking.BaseSetName, FTTogglesPlan.VisemeSetName }));

            var baseSet = sets[0];
            Assert.That(baseSet.visemeBlendshapeNames.All(string.IsNullOrEmpty), Is.True);
            Assert.That(baseSet.faceTrackingBlendshapeNames[FTKeys.KeyByName("JawOpen")], Is.EqualTo("JawOpen"));

            var visemes = sets[1];
            Assert.That(visemes.setWeight, Is.EqualTo(1f));
            Assert.That(visemes.faceTrackingBlendshapeNames.All(string.IsNullOrEmpty), Is.True);
            Assert.That(visemes.visemeBlendshapeNames[(int)VRC_AvatarDescriptor.Viseme.aa], Is.EqualTo("vrc.v_aa"));
            Assert.That(visemes.visemeBlendshapeMultipliers[(int)VRC_AvatarDescriptor.Viseme.oh], Is.EqualTo(0.5f));
            Assert.That(visemes.visemeBlendshapeNames.Length, Is.EqualTo(FTKeys.VisemeCount));
        }

        [Test]
        public void EverySetWithVisemesGetsItsOwnVisemeSet()
        {
            var sets = new List<BlendshapesSet> { SetWithVisemes("Face", 1f), SetWithVisemes("Mask", 0.5f), FTBuildContext.CreateSet("Empty", 1f) };
            var plan = FTTogglesPlan.Resolve(component, sets, true, true);
            plan.SplitVisemes(sets);

            Assert.That(plan.VisemeSets.Select(s => s.Name), Is.EqualTo(new[] { "Face Visemes", "Mask Visemes" }));
            Assert.That(sets.Count, Is.EqualTo(5));
            Assert.That(sets.Single(s => s.setName == "Mask Visemes").setWeight, Is.EqualTo(0.5f));
            Assert.That(sets.Select(s => s.setName).Distinct().Count(), Is.EqualTo(5));
        }

        [Test]
        public void VisemeSetNameAvoidsExistingSets()
        {
            var sets = new List<BlendshapesSet> { SetWithVisemes(PawlygonFaceTracking.BaseSetName), FTBuildContext.CreateSet(FTTogglesPlan.VisemeSetName, 1f) };
            var plan = FTTogglesPlan.Resolve(component, sets, true, true);

            Assert.That(plan.VisemeSets.Single().Name, Is.Not.EqualTo(FTTogglesPlan.VisemeSetName));
        }

        [Test]
        public void DriverMutesOnlyWhileTrackingWithTheToggleOn()
        {
            var controller = avatar.NewController("FX");
            FTToggleLayers.AddVisemesDriver(controller);

            var layer = controller.layers.Single();
            Assert.That(layer.name, Is.EqualTo(FTToggleLayers.DriverLayer));
            Assert.That(FXGuardAnalysis.IsOwnLayer(layer.name), Is.True);

            var machine = layer.stateMachine;
            Assert.That(machine.defaultState.name, Is.EqualTo("Visemes Playing"));

            // Playing -> Muted needs both: face tracking and the Disable Visemes toggle on
            var mute = machine.defaultState.transitions.Single();
            Assert.That(mute.conditions.Select(c => (c.parameter, c.mode)), Is.EquivalentTo(new[]
            {
                (FTGuardDriver.UserFaceTracking, UnityEditor.Animations.AnimatorConditionMode.If),
                (FTTogglesPlan.DisableVisemesParameter, UnityEditor.Animations.AnimatorConditionMode.If),
            }));

            // Muted -> Playing on either: the toggle turned off, or face tracking stopped
            var muted = mute.destinationState;
            Assert.That(muted.transitions.Select(t => t.conditions.Single()).Select(c => (c.parameter, c.mode)), Is.EquivalentTo(new[]
            {
                (FTTogglesPlan.DisableVisemesParameter, UnityEditor.Animations.AnimatorConditionMode.IfNot),
                (FTGuardDriver.UserFaceTracking, UnityEditor.Animations.AnimatorConditionMode.IfNot),
            }));

            var driver = muted.behaviours.OfType<VRCAvatarParameterDriver>().Single();
            Assert.That(driver.localOnly, Is.True);
            Assert.That(driver.parameters.Single().name, Is.EqualTo(FTTogglesPlan.VisemesMutedParameter));
            Assert.That(driver.parameters.Single().value, Is.EqualTo(1f));
        }

        [Test]
        public void WeightLayerFadesTheVisemeSets()
        {
            var controller = avatar.NewController("FX");
            var sets = new[]
            {
                new FTTogglesPlan.VisemeSet { Source = "Face", Name = "Face Visemes", Weight = 1f },
                new FTTogglesPlan.VisemeSet { Source = "Mask", Name = "Mask Visemes", Weight = 0.5f },
            };
            FTToggleLayers.AddVisemesWeightLayer(controller, sets);

            var machine = controller.layers.Single().stateMachine;
            var playing = machine.defaultState;
            var muted = playing.transitions.Single().destinationState;
            Assert.That(playing.transitions.Single().conditions.Single().parameter, Is.EqualTo(FTTogglesPlan.VisemesMutedParameter));

            List<(string, float)> Weights(UnityEditor.Animations.AnimatorState state) => state.behaviours
                .OfType<VRC_AnimatorFaceTrackingBlendshapesSet>().Single().blendSets
                .Select(s => (s.blendshapesSetName, s.weight)).ToList();

            Assert.That(Weights(playing), Is.EqualTo(new[] { ("Face Visemes", 1f), ("Mask Visemes", 0.5f) }));
            Assert.That(Weights(muted), Is.EqualTo(new[] { ("Face Visemes", 0f), ("Mask Visemes", 0f) }));
        }
    }
}
