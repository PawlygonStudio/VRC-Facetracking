using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>The 2.1 component, which only hands its settings over now.</summary>
    [CustomEditor(typeof(PawlygonFTToggles))]
    internal sealed class PawlygonFTTogglesEditor : FTComponentEditor<PawlygonFTToggles>
    {
        readonly FTDynamic settings = new FTDynamic();
        Button move;

        protected override string Title => "Face Tracking Toggles";
        protected override string Subtitle => "Replaced in 2.2 by settings in Face Tracking and Pupil Emulation.";

        protected override string Details =>
            "Since 2.2 the Visemes menu is set in Face Tracking, and the Pupil Tracking and Pupil Emulation toggles in Pupil Emulation. " +
            "This component is from 2.1. While it's on the avatar, its settings are used in place of theirs, so the avatar builds as before. " +
            "Move Settings copies them over and removes this component.";

        protected override void BuildBody(VisualElement body)
        {
            var card = FTUI.Card(body, "Settings From 2.1", out _);
            card.Add(settings.Element);
            move = new Button
            {
                text = "Move Settings",
                tooltip = "Copies these settings to the avatar's Face Tracking and Pupil Emulation and removes this component. Undoable.",
            };
            move.AddToClassList("ft-add-button");
            card.Add(move);
        }

        protected override void Refresh(VRCAvatarDescriptor avatar, FTIssues issues)
        {
            var component = Component;
            void Move() => FTLegacyToggles.MoveSettings(component, avatar.gameObject);
            move.clickable = new Clickable(Move);

            var hasTracking = FTAvatarUtils.GetBuildComponents<PawlygonFaceTracking>(avatar.gameObject).Any();
            var hasPupils = FTAvatarUtils.GetBuildComponents<PawlygonFTPupilEmulation>(avatar.gameObject).Any();
            issues.Info("This component was replaced in 2.2.",
                "The Visemes menu is set in Face Tracking and the pupil toggles in Pupil Emulation. Its settings are used until they're moved.",
                ("Move Settings", Move));
            if (!hasTracking)
            {
                issues.Warning("The avatar has no Face Tracking component, so there's no Visemes menu.",
                    "The Visemes menu is part of Face Tracking now. Add the component to get it back.");
            }

            var menu = FTMenu.Describe(component.menuName);
            settings.Update($"{component.visemesMenu}|{component.visemesDefault}|{component.pupilTrackingToggle}|{component.pupilEmulationToggle}|{menu}|{hasTracking}|{hasPupils}", container =>
            {
                container.Add(FTUI.Row("Visemes menu", component.visemesMenu ? $"on, starts at {component.visemesDefault}" : "off",
                    hasTracking ? "Goes to Face Tracking's Visemes Menu." : "There's no Face Tracking component to take it."));
                container.Add(FTUI.Row("Pupil Tracking toggle", component.pupilTrackingToggle ? "on" : "off",
                    hasPupils ? "Goes to Pupil Emulation's Menu Toggles." : "There's no Pupil Emulation component to take it."));
                container.Add(FTUI.Row("Pupil Emulation toggle", component.pupilEmulationToggle ? "on" : "off",
                    hasPupils ? "Goes to Pupil Emulation's Menu Toggles." : "There's no Pupil Emulation component to take it."));
                container.Add(FTUI.Row("In menu", menu));
            });
        }
    }
}
