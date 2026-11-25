using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using BlendshapesSet = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingBlendshapesSet;

namespace Pawlygon.FaceTracking.Editor.Tests
{
    /// <summary>The Visemes menu of Face Tracking and the menu toggles of Pupil Emulation.</summary>
    public class VisemesMenuTests
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
        public void DefaultsAddTheMenuWithFullVisemesAndBothPupilToggles()
        {
            Assert.That(component.visemesMenu, Is.True);
            Assert.That(component.visemesDefault, Is.EqualTo(FTVisemesLevel.Full));
            Assert.That(component.menuName, Is.EqualTo("Face Tracking"));

            var pupils = avatar.Add<PawlygonFTPupilEmulation>();
            Assert.That(pupils.pupilTrackingToggle, Is.True);
            Assert.That(pupils.pupilEmulationToggle, Is.True);
            Assert.That(pupils.menuName, Is.EqualTo("Face Tracking"));
        }

        [Test]
        public void A21TogglesComponentIsUsedUntilItsSettingsAreMoved()
        {
            var pupils = avatar.Add<PawlygonFTPupilEmulation>();
            Assert.That(FTLegacyToggles.Visemes(component, avatar.Root).Legacy, Is.Null);
            Assert.That(FTLegacyToggles.Visemes(component, avatar.Root).Menu, Is.True);

            var legacy = avatar.Add<PawlygonFTToggles>();
            legacy.visemesMenu = false;
            legacy.visemesDefault = FTVisemesLevel.Off;
            legacy.pupilTrackingToggle = false;
            legacy.menuName = "Settings";

            var visemes = FTLegacyToggles.Visemes(component, avatar.Root);
            Assert.That((visemes.Menu, visemes.Default, visemes.MenuName, visemes.Legacy), Is.EqualTo((false, FTVisemesLevel.Off, "Settings", legacy)));
            var toggles = FTLegacyToggles.Pupils(pupils, avatar.Root);
            Assert.That((toggles.TrackingToggle, toggles.EmulationToggle, toggles.MenuName, toggles.Legacy), Is.EqualTo((false, true, "Settings", legacy)));

            FTLegacyToggles.MoveSettings(legacy, avatar.Root);

            Assert.That(avatar.Root.GetComponentsInChildren<PawlygonFTToggles>(true), Is.Empty);
            Assert.That((component.visemesMenu, component.visemesDefault, component.menuName), Is.EqualTo((false, FTVisemesLevel.Off, "Settings")));
            Assert.That((pupils.pupilTrackingToggle, pupils.pupilEmulationToggle, pupils.menuName), Is.EqualTo((false, true, "Settings")));
            Assert.That(FTLegacyToggles.Visemes(component, avatar.Root).Legacy, Is.Null);
        }

        [Test]
        public void LevelsAreTwoBitsWithFullAsZero()
        {
            // Full is the menu parameter's 0, so turning a level's toggle off in the menu goes back to full visemes
            Assert.That((int)FTVisemesLevel.Full, Is.EqualTo(0));
            Assert.That(FTVisemesPlan.Levels, Is.EqualTo(new[] { FTVisemesLevel.Full, FTVisemesLevel.Medium, FTVisemesLevel.Low, FTVisemesLevel.Off }));
            Assert.That(FTVisemesPlan.Levels.Select(l => (FTVisemesPlan.BitA(l), FTVisemesPlan.BitB(l))).Distinct().Count(), Is.EqualTo(4));
            Assert.That((FTVisemesPlan.BitA(FTVisemesLevel.Full), FTVisemesPlan.BitB(FTVisemesLevel.Full)), Is.EqualTo((false, false)));

            Assert.That(FTVisemesPlan.LevelWeight(FTVisemesLevel.Full), Is.EqualTo(1f));
            Assert.That(FTVisemesPlan.LevelWeight(FTVisemesLevel.Off), Is.EqualTo(0f));
            Assert.That(FTVisemesPlan.LevelWeight(FTVisemesLevel.Medium), Is.GreaterThan(FTVisemesPlan.LevelWeight(FTVisemesLevel.Low)));
            Assert.That(FTVisemesPlan.LevelWeight(FTVisemesLevel.Low), Is.GreaterThan(0f));
        }

        [Test]
        public void PupilTogglesNeedPupilShapesAndThePupilKeys()
        {
            avatar.AddMesh("Body", "EyeDilationLeft", "EyeDilationRight", "EyeConstrictLeft", "EyeConstrictRight");
            var meshes = avatar.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var noSets = new BlendshapesSet[0];

            var plan = FTPupilEmulationPlan.Resolve(meshes, noSets, true);
            plan.ResolveToggles(true, true);
            Assert.That(plan.Shapes, Is.Not.Empty);
            Assert.That((plan.TrackingToggle, plan.EmulationToggle, plan.ToggleBits), Is.EqualTo((true, true, 2)));

            // Left out in the component
            plan = FTPupilEmulationPlan.Resolve(meshes, noSets, true);
            plan.ResolveToggles(false, true);
            Assert.That((plan.TrackingToggle, plan.EmulationToggle, plan.ToggleBits), Is.EqualTo((false, true, 1)));

            // Without All Tracking Keys there's no pupil tracking to toggle
            plan = FTPupilEmulationPlan.Resolve(meshes, noSets, false);
            plan.ResolveToggles(true, true);
            Assert.That(plan.TrackingToggle, Is.EqualTo(plan.RealTracking));
            Assert.That(plan.EmulationToggle, Is.True);

            // Both left out, or no pupil blendshapes
            plan = FTPupilEmulationPlan.Resolve(meshes, noSets, true);
            plan.ResolveToggles(false, false);
            Assert.That(plan.ToggleBits, Is.EqualTo(0));

            plan = FTPupilEmulationPlan.Resolve(new SkinnedMeshRenderer[0], noSets, true);
            plan.ResolveToggles(true, true);
            Assert.That(plan.ToggleBits, Is.EqualTo(0));
        }

        [Test]
        public void PlansAVisemeSetForTheBaseSet()
        {
            var plan = FTVisemesPlan.Resolve(component.visemesMenu, new[] { SetWithVisemes(PawlygonFaceTracking.BaseSetName) }, true, true);

            Assert.That(plan.AddsVisemesMenu, Is.True);
            Assert.That(plan.VisemesSkipped, Is.Null);
            Assert.That(plan.SyncedBits, Is.EqualTo(2));
            var set = plan.VisemeSets.Single();
            Assert.That(set.Name, Is.EqualTo(FTVisemesPlan.VisemeSetName));
            Assert.That(set.Source, Is.EqualTo(PawlygonFaceTracking.BaseSetName));
            Assert.That(set.Visemes, Is.EqualTo(2));
        }

        [Test]
        public void ToggleOffPlansNothing()
        {
            component.visemesMenu = false;
            var plan = FTVisemesPlan.Resolve(component.visemesMenu, new[] { SetWithVisemes(PawlygonFaceTracking.BaseSetName) }, true, true);

            Assert.That(plan.AddsVisemesMenu, Is.False);
            Assert.That(plan.VisemesSkipped, Is.Null);
            Assert.That(plan.SyncedBits, Is.EqualTo(0));
        }

        [Test]
        public void SkippedWithoutFaceTrackingVisemes()
        {
            var sets = new[] { SetWithVisemes(PawlygonFaceTracking.BaseSetName) };
            Assert.That(FTVisemesPlan.Resolve(component.visemesMenu, sets, false, true).VisemesSkipped, Is.Not.Null);

            var noVisemes = FTBuildContext.CreateSet(PawlygonFaceTracking.BaseSetName, 1f);
            var plan = FTVisemesPlan.Resolve(component.visemesMenu, new[] { noVisemes }, true, true);
            Assert.That(plan.AddsVisemesMenu, Is.False);
            Assert.That(plan.VisemesSkipped, Is.Not.Null);
            // Expected on an avatar without visemes, so the build doesn't warn about it
            Assert.That(plan.NoVisemes, Is.True);
        }

        [Test]
        public void SkippedWithoutVrcfuryOrModularAvatar()
        {
            var plan = FTVisemesPlan.Resolve(component.visemesMenu, new[] { SetWithVisemes(PawlygonFaceTracking.BaseSetName) }, true, false);

            Assert.That(plan.AddsVisemesMenu, Is.False);
            Assert.That(plan.VisemesSkipped, Is.Not.Null);
            Assert.That(plan.NoVisemes, Is.False);
        }

        [Test]
        public void SplitMovesVisemesAndKeepsTheKeys()
        {
            var sets = new List<BlendshapesSet> { SetWithVisemes(PawlygonFaceTracking.BaseSetName) };
            var plan = FTVisemesPlan.Resolve(component.visemesMenu, sets, true, true);
            plan.SplitVisemes(sets);

            Assert.That(sets.Select(s => s.setName), Is.EqualTo(new[] { PawlygonFaceTracking.BaseSetName, FTVisemesPlan.VisemeSetName }));

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
            var plan = FTVisemesPlan.Resolve(component.visemesMenu, sets, true, true);
            plan.SplitVisemes(sets);

            Assert.That(plan.VisemeSets.Select(s => s.Name), Is.EqualTo(new[] { "Face Visemes", "Mask Visemes" }));
            Assert.That(sets.Count, Is.EqualTo(5));
            Assert.That(sets.Single(s => s.setName == "Mask Visemes").setWeight, Is.EqualTo(0.5f));
            Assert.That(sets.Select(s => s.setName).Distinct().Count(), Is.EqualTo(5));
        }

        [Test]
        public void VisemeSetNameAvoidsExistingSets()
        {
            var sets = new List<BlendshapesSet> { SetWithVisemes(PawlygonFaceTracking.BaseSetName), FTBuildContext.CreateSet(FTVisemesPlan.VisemeSetName, 1f) };
            var plan = FTVisemesPlan.Resolve(component.visemesMenu, sets, true, true);

            Assert.That(plan.VisemeSets.Single().Name, Is.Not.EqualTo(FTVisemesPlan.VisemeSetName));
        }

        [Test]
        public void DriverFollowsThePickedLevelOnlyWhileTracking()
        {
            var controller = avatar.NewController("FX");
            FTVisemeLayers.AddVisemesDriver(controller);

            var layer = controller.layers.Single();
            Assert.That(layer.name, Is.EqualTo(FTVisemeLayers.DriverLayer));
            Assert.That(FXGuardAnalysis.IsOwnLayer(layer.name), Is.True);

            var machine = layer.stateMachine;
            Assert.That(machine.states.Select(s => s.state.name),
                Is.EquivalentTo(FTVisemesPlan.Levels.Select(FTVisemeLayers.StateName)));
            var full = machine.defaultState;
            Assert.That(full.name, Is.EqualTo(FTVisemeLayers.StateName(FTVisemesLevel.Full)));

            // Leaving Full needs both: face tracking, and that level picked in the menu
            Assert.That(full.transitions.Length, Is.EqualTo(3));
            foreach (var transition in full.transitions)
            {
                var level = FTVisemesPlan.Levels.Single(l => FTVisemeLayers.StateName(l) == transition.destinationState.name);
                Assert.That(transition.conditions.Select(c => (c.parameter, c.mode, c.threshold)), Is.EquivalentTo(new[]
                {
                    (FTGuardDriver.UserFaceTracking, UnityEditor.Animations.AnimatorConditionMode.If, 0f),
                    (FTVisemesPlan.VisemesLevelParameter, UnityEditor.Animations.AnimatorConditionMode.Equals, (float)(int)level),
                }));
            }

            // Every other level goes back to Full on either: Full picked, or face tracking stopped
            var off = machine.states.Select(s => s.state).Single(s => s.name == FTVisemeLayers.StateName(FTVisemesLevel.Off));
            var toFull = off.transitions.Where(t => t.destinationState == full).Select(t => t.conditions.Single()).ToList();
            Assert.That(toFull.Select(c => (c.parameter, c.mode)), Is.EquivalentTo(new[]
            {
                (FTVisemesPlan.VisemesLevelParameter, UnityEditor.Animations.AnimatorConditionMode.Equals),
                (FTGuardDriver.UserFaceTracking, UnityEditor.Animations.AnimatorConditionMode.IfNot),
            }));
            Assert.That(off.transitions.Length, Is.EqualTo(4));

            // Each state sets both synced bits to its level, on the owner only
            foreach (var level in FTVisemesPlan.Levels)
            {
                var state = machine.states.Select(s => s.state).Single(s => s.name == FTVisemeLayers.StateName(level));
                var driver = state.behaviours.OfType<VRCAvatarParameterDriver>().Single();
                Assert.That(driver.localOnly, Is.True);
                Assert.That(driver.parameters.Select(p => (p.name, p.value)), Is.EqualTo(new[]
                {
                    (FTVisemesPlan.VisemesBitAParameter, FTVisemesPlan.BitA(level) ? 1f : 0f),
                    (FTVisemesPlan.VisemesBitBParameter, FTVisemesPlan.BitB(level) ? 1f : 0f),
                }));
            }
        }

        [Test]
        public void WeightLayerScalesTheVisemeSetsByLevel()
        {
            var controller = avatar.NewController("FX");
            var sets = new[]
            {
                new FTVisemesPlan.VisemeSet { Source = "Face", Name = "Face Visemes", Weight = 1f },
                new FTVisemesPlan.VisemeSet { Source = "Mask", Name = "Mask Visemes", Weight = 0.5f },
            };
            FTVisemeLayers.AddVisemesWeightLayer(controller, sets);

            var machine = controller.layers.Single().stateMachine;
            Assert.That(machine.defaultState.name, Is.EqualTo(FTVisemeLayers.StateName(FTVisemesLevel.Full)));

            List<(string, float)> Weights(UnityEditor.Animations.AnimatorState state) => state.behaviours
                .OfType<VRC_AnimatorFaceTrackingBlendshapesSet>().Single().blendSets
                .Select(s => (s.blendshapesSetName, s.weight)).ToList();

            foreach (var level in FTVisemesPlan.Levels)
            {
                var state = machine.states.Select(s => s.state).Single(s => s.name == FTVisemeLayers.StateName(level));
                var scale = FTVisemesPlan.LevelWeight(level);
                Assert.That(Weights(state), Is.EqualTo(new[] { ("Face Visemes", 1f * scale), ("Mask Visemes", 0.5f * scale) }));

                // Reached from every other state, on exactly this level's two synced bits
                Assert.That(state.transitions.Length, Is.EqualTo(3));
                foreach (var transition in state.transitions)
                {
                    var to = FTVisemesPlan.Levels.Single(l => FTVisemeLayers.StateName(l) == transition.destinationState.name);
                    Assert.That(transition.conditions.Select(c => (c.parameter, c.mode)), Is.EquivalentTo(new[]
                    {
                        (FTVisemesPlan.VisemesBitAParameter, FTVisemesPlan.BitA(to) ? UnityEditor.Animations.AnimatorConditionMode.If : UnityEditor.Animations.AnimatorConditionMode.IfNot),
                        (FTVisemesPlan.VisemesBitBParameter, FTVisemesPlan.BitB(to) ? UnityEditor.Animations.AnimatorConditionMode.If : UnityEditor.Animations.AnimatorConditionMode.IfNot),
                    }));
                }
            }
        }
    }
}
