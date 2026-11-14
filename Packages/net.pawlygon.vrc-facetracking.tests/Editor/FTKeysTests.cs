using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VRC.SDKBase;
using Key = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.FaceTrackingKey;

namespace Pawlygon.FaceTracking.Editor.Tests
{
    public class FTKeysTests
    {
        [Test]
        public void SdkAutoFillIsAvailable()
        {
            // FTKeys calls the SDK's internal auto-fill by reflection. If this fails after an SDK update, the SDK moved
            // or renamed it, and matching would fall back to exact Unified Expressions names.
            Assert.That(FTKeys.AutoFillAvailable, Is.True);
        }

        [Test]
        public void FindsUnifiedExpressionsName()
        {
            Assert.That(FTKeys.FindKey(new[] { "Other", "JawOpen" }, (int)Key.JawOpen), Is.EqualTo(1));
        }

        [TestCase("jawOpen")]
        [TestCase("Jaw_Open")]
        public void FindsAlias(string blendshape)
        {
            Assert.That(FTKeys.FindKey(new[] { blendshape }, (int)Key.JawOpen), Is.EqualTo(0));
        }

        [Test]
        public void UnifiedExpressionsNameWinsOverAlias()
        {
            Assert.That(FTKeys.FindKey(new[] { "jawOpen", "JawOpen" }, (int)Key.JawOpen), Is.EqualTo(1));
        }

        [Test]
        public void UnknownBlendshapeMatchesNothing()
        {
            Assert.That(FTKeys.FindKey(new[] { "Smile", "Angry" }, (int)Key.JawOpen), Is.EqualTo(-1));
        }

        [Test]
        public void ArkitSmileStandsForCornerPull()
        {
            Assert.That(FTKeys.KeysForName("mouthSmileLeft"), Does.Contain((int)Key.MouthCornerPullLeft));
        }

        [Test]
        public void ArkitNamesAreCaseSensitive()
        {
            Assert.That(FTKeys.IsArkitName("mouthSmileLeft"), Is.True);
            Assert.That(FTKeys.IsArkitName("MouthSmileLeft"), Is.False);
        }

        [Test]
        public void KeyByNameRoundTrips()
        {
            for (var key = 0; key < FTKeys.KeyCount; key++)
            {
                Assert.That(FTKeys.KeyByName(FTKeys.KeyName(key)), Is.EqualTo(key));
            }
            Assert.That(FTKeys.KeyByName("NotAKey"), Is.EqualTo(-1));
            Assert.That(FTKeys.KeyByName(""), Is.EqualTo(-1));
        }

        [Test]
        public void SuggestsMisspelling()
        {
            var suggestion = FTKeys.SuggestFor((int)Key.JawOpen, new[] { "Smile", "JawOpne" }, new List<string>());
            Assert.That(suggestion, Is.EqualTo("JawOpne"));
        }

        [Test]
        public void DoesNotSuggestAnotherKeysBlendshape()
        {
            // 2.0.7: TongueOut is two letters off TongueUp, but it's TongueOut's blendshape
            var suggestion = FTKeys.SuggestFor((int)Key.TongueUp, new[] { "TongueOut" }, new List<string>());
            Assert.That(suggestion, Is.Null);
        }

        [Test]
        public void DoesNotSuggestUsedBlendshape()
        {
            var suggestion = FTKeys.SuggestFor((int)Key.JawOpen, new[] { "JawOpne" }, new List<string> { "JawOpne" });
            Assert.That(suggestion, Is.Null);
        }

        [TestCase("vrc.v_aa")]
        [TestCase("v_aa")]
        [TestCase("VRC.V_AA")]
        [TestCase("aa")]
        public void FindsVisemeByExactName(string blendshape)
        {
            Assert.That(FTKeys.FindViseme(new[] { "Smile", blendshape }, (int)VRC_AvatarDescriptor.Viseme.aa), Is.EqualTo(1));
        }

        [Test]
        public void VisemesAreNotMatchedBySubstring()
        {
            // The SDK's own auto-fill would match "e" inside these
            Assert.That(FTKeys.FindViseme(new[] { "Smile", "EyeClosedLeft" }, (int)VRC_AvatarDescriptor.Viseme.E), Is.EqualTo(-1));
        }

        [Test]
        public void PupilKeys()
        {
            var pupils = Enumerable.Range(0, FTKeys.KeyCount).Where(FTKeys.IsPupilKey).Select(FTKeys.KeyName).ToList();
            Assert.That(pupils, Is.EquivalentTo(new[] { "EyeDilationLeft", "EyeDilationRight", "EyeConstrictLeft", "EyeConstrictRight" }));
        }
    }
}
