using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace Pawlygon.FaceTracking.Editor
{
    internal sealed class FaceTrackingTogglesBuilder : FTBuilder<PawlygonFTToggles>
    {
        const string IconsFolder = "Packages/net.pawlygon.vrc-facetracking/Icons/";

        // After Face Tracking has set up the blendshapes sets the visemes are taken from
        public override FTPhase Phase => FTPhase.Animator;

        /// <summary>
        /// Splits the visemes into their own sets and adds the layers, parameters and menu toggle before VRCFury and
        /// Modular Avatar run, so they merge them. Without a toggle to add, the avatar builds exactly as it would
        /// without this component.
        /// </summary>
        protected override void BuildDescriptor(PawlygonFTToggles component, FTBuildContext context)
        {
            var plan = FTTogglesPlan.Resolve(component, context.Sets, UsesFaceTrackingLipSync(context), FTMerger.Find() != null);
            if (plan.VisemesSkipped != null)
            {
                Debug.LogWarning($"[{FTErrorBoundary.Title}] {plan.VisemesSkipped}", component);
            }
            if (!plan.AddsVisemesToggle) return;

            plan.SplitVisemes(context.Sets);

            var fx = context.Generated.FX;
            FTToggleLayers.AddVisemesDriver(fx);
            FTToggleLayers.AddVisemesWeightLayer(fx, plan.VisemeSets);

            var parameters = context.Generated.Parameters;
            // Read by the owner's driver only, so it doesn't need syncing
            FTAvatarAssets.AddParameter(parameters, FTTogglesPlan.VisemesParameter, VRCExpressionParameters.ValueType.Bool,
                component.visemesOnByDefault ? 1 : 0, true, false);
            FTAvatarAssets.AddParameter(parameters, FTTogglesPlan.VisemesMutedParameter, VRCExpressionParameters.ValueType.Bool, 0, false, true);

            AddMenuToggle(component, context);
        }

        /// <summary>Face Tracking has already applied its lip sync mode to the descriptor by now.</summary>
        static bool UsesFaceTrackingLipSync(FTBuildContext context)
        {
            return context.EnableFaceTracking && context.Descriptor.lipSync == VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShapeFaceTracking;
        }

        static void AddMenuToggle(PawlygonFTToggles component, FTBuildContext context)
        {
            // Empty puts the toggle in the main menu
            var menuName = component.menuName?.Trim() ?? "";
            // The avatar's own menu may have one already
            var existing = menuName.Length == 0
                ? context.Descriptor.expressionsMenu
                : FTAvatarAssets.FindSubmenu(context.Descriptor.expressionsMenu, menuName)?.subMenu;
            if (existing != null && existing.controls.Any(c => c.parameter?.name == FTTogglesPlan.VisemesParameter)) return;

            var submenu = context.Generated.GetSubmenu(menuName, AssetDatabase.LoadAssetAtPath<Texture2D>(IconsFolder + "FaceTrackingIcon.png"));
            if (submenu.controls.Any(c => c.parameter?.name == FTTogglesPlan.VisemesParameter)) return;

            submenu.controls.Add(FTAvatarAssets.CreateControl("Visemes", VRCExpressionsMenu.Control.ControlType.Toggle,
                FTTogglesPlan.VisemesParameter, AssetDatabase.LoadAssetAtPath<Texture2D>(IconsFolder + "Visemes_Icon.png")));
            EditorUtility.SetDirty(submenu);
        }
    }
}
