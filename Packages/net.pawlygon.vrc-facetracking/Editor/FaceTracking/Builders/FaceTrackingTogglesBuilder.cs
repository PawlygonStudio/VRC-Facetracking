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
        /// Splits the visemes into their own sets and adds the layers, parameters and menu before VRCFury and Modular
        /// Avatar run, so they merge them. Without a Visemes menu to add, the avatar builds exactly as it would without
        /// this component. The pupil toggles are added by Pupil Emulation's builder, which owns the layer they change.
        /// </summary>
        protected override void BuildDescriptor(PawlygonFTToggles component, FTBuildContext context)
        {
            var plan = FTTogglesPlan.Resolve(component, context.Sets, UsesFaceTrackingLipSync(context), FTMerger.Find() != null);
            if (plan.VisemesSkipped != null)
            {
                Debug.LogWarning($"[{FTErrorBoundary.Title}] {plan.VisemesSkipped}", component);
            }
            if (!plan.AddsVisemesMenu) return;

            plan.SplitVisemes(context.Sets);

            var fx = context.Generated.FX;
            FTToggleLayers.AddVisemesDriver(fx);
            FTToggleLayers.AddVisemesWeightLayer(fx, plan.VisemeSets);

            var parameters = context.Generated.Parameters;
            // Read by the owner's driver only, so it doesn't need syncing
            FTAvatarAssets.AddParameter(parameters, FTTogglesPlan.VisemesLevelParameter, VRCExpressionParameters.ValueType.Int,
                (int)component.visemesDefault, true, false);
            FTAvatarAssets.AddParameter(parameters, FTTogglesPlan.VisemesBitAParameter, VRCExpressionParameters.ValueType.Bool, 0, false, true);
            FTAvatarAssets.AddParameter(parameters, FTTogglesPlan.VisemesBitBParameter, VRCExpressionParameters.ValueType.Bool, 0, false, true);

            AddVisemesMenu(component, context);
        }

        /// <summary>Face Tracking has already applied its lip sync mode to the descriptor by now.</summary>
        static bool UsesFaceTrackingLipSync(FTBuildContext context)
        {
            return context.EnableFaceTracking && context.Descriptor.lipSync == VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShapeFaceTracking;
        }

        /// <summary>
        /// The generated menu the component's controls go in, or null when the avatar's own menu already has a control
        /// for <paramref name="parameter"/> there.
        /// </summary>
        internal static VRCExpressionsMenu ControlsMenu(PawlygonFTToggles component, FTBuildContext context, string parameter)
        {
            // Empty puts the controls in the main menu
            var menuName = component.menuName?.Trim() ?? "";
            var existing = menuName.Length == 0
                ? context.Descriptor.expressionsMenu
                : FTAvatarAssets.FindSubmenu(context.Descriptor.expressionsMenu, menuName)?.subMenu;
            if (HasControl(existing, parameter, 1)) return null;

            var submenu = context.Generated.GetSubmenu(menuName, AssetDatabase.LoadAssetAtPath<Texture2D>(IconsFolder + "FaceTrackingIcon.png"));
            return HasControl(submenu, parameter, 1) ? null : submenu;
        }

        internal static Texture2D Icon(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>(IconsFolder + file);

        static bool HasControl(VRCExpressionsMenu menu, string parameter, int depth)
        {
            if (menu == null) return false;
            return menu.controls.Any(c => c.parameter?.name == parameter || (depth > 0 && HasControl(c.subMenu, parameter, depth - 1)));
        }

        /// <summary>A Visemes submenu with one toggle per level, each setting the menu parameter to its level.</summary>
        static void AddVisemesMenu(PawlygonFTToggles component, FTBuildContext context)
        {
            var menu = ControlsMenu(component, context, FTTogglesPlan.VisemesLevelParameter);
            if (menu == null) return;

            var icon = Icon("Visemes_Icon.png");
            var levels = context.Generated.GetSubmenu(menu, FTTogglesPlan.VisemesMenuName, icon);
            foreach (var level in FTTogglesPlan.Levels)
            {
                var control = FTAvatarAssets.CreateControl(level.ToString(), VRCExpressionsMenu.Control.ControlType.Toggle,
                    FTTogglesPlan.VisemesLevelParameter, icon);
                control.value = (int)level;
                levels.controls.Add(control);
            }
            EditorUtility.SetDirty(levels);
        }
    }
}
