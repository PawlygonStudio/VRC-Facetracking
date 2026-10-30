using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// The FX layers, expression parameters and menu controls the components add, built in the descriptor pass as
    /// new assets. VRCFury or Modular Avatar merges them into the avatar like any other prefab's, as both ask other
    /// tools to (<see href="https://vrcfury.com/api/">VRCFury's guidance</see>), so one of them is required.
    /// </summary>
    internal sealed class FTGeneratedAssets
    {
        readonly FTBuildContext context;
        readonly FTMerger merger;

        AnimatorController fx;
        VRCExpressionParameters parameters;
        VRCExpressionsMenu menu;

        public FTGeneratedAssets(FTBuildContext context)
        {
            this.context = context;
            merger = FTMerger.Find();
        }

        public bool HasFX => fx != null;

        /// <summary>A controller for the components' own FX layers.</summary>
        public AnimatorController FX
        {
            get
            {
                if (fx != null) return fx;
                RequireMerger();
                fx = AnimatorController.CreateAnimatorControllerAtPath(AssetDatabase.GenerateUniqueAssetPath(context.BuildFolder + "/Face Tracking FX.controller"));
                // Without the empty default layer, which would be merged too
                fx.RemoveLayer(0);
                DeclareSharedParameters(fx);
                return fx;
            }
        }

        /// <summary>Expression parameters for the components' own layers.</summary>
        public VRCExpressionParameters Parameters
        {
            get
            {
                if (parameters != null) return parameters;
                RequireMerger();
                parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                parameters.name = "Face Tracking Parameters";
                parameters.parameters = new VRCExpressionParameters.Parameter[0];
                AssetDatabase.CreateAsset(parameters, AssetDatabase.GenerateUniqueAssetPath(context.BuildFolder + "/Face Tracking Parameters.asset"));
                return parameters;
            }
        }

        /// <summary>
        /// The submenu of the main menu named <paramref name="name"/>, to add controls to. Merged with an existing
        /// one of that name. An empty name is the main menu itself.
        /// </summary>
        public VRCExpressionsMenu GetSubmenu(string name, Texture2D icon)
        {
            RequireMerger();
            if (menu == null) menu = FTAvatarAssets.CreateMenu(context.BuildFolder, "Face Tracking Menu");
            if (string.IsNullOrEmpty(name)) return menu;
            var control = FTAvatarAssets.FindSubmenu(menu, name);
            if (control == null)
            {
                control = FTAvatarAssets.CreateControl(name, VRCExpressionsMenu.Control.ControlType.SubMenu, "", icon);
                control.subMenu = FTAvatarAssets.CreateMenu(context.BuildFolder, $"{FTAvatarAssets.SafeName(name)} Menu");
                menu.controls.Add(control);
                EditorUtility.SetDirty(menu);
            }
            return control.subMenu;
        }

        /// <summary>Hands the assets to the merging tool.</summary>
        public void Install()
        {
            if (fx == null && parameters == null && menu == null) return;
            merger.Install(context.AvatarRoot, fx, parameters, menu);
            AssetDatabase.SaveAssets();
        }

        void RequireMerger()
        {
            if (merger == null) throw new FTBuildException(FTMerger.MissingMessage);
        }

        /// <summary>
        /// Parameters the avatar's own controllers may share with ours, declared with their type there (Unity Tools'
        /// guarded FX controllers use EyeTrackingActive as a Bool, for example), so merging doesn't have to pick one.
        /// </summary>
        void DeclareSharedParameters(AnimatorController controller)
        {
            var sources = FTAvatarAssets.GetSourceFXControllers(context.Descriptor);
            foreach (var name in FTGuardDriver.SharedParameters)
            {
                var existing = sources.SelectMany(s => s.controller.parameters)
                    .FirstOrDefault(p => p.name == name && p.type != AnimatorControllerParameterType.Trigger);
                if (existing != null) FXGuardWriter.EnsureParameter(controller, name, existing.type);
            }
        }
    }

    /// <summary>
    /// Merges the generated assets into the avatar through another tool. Implemented by the optional integration
    /// assemblies, which only compile with their tool installed. The lowest <see cref="Priority"/> wins.
    /// </summary>
    internal abstract class FTMerger
    {
        public const string MissingMessage = "It needs VRCFury (1.935.0 or newer) or Modular Avatar (1.9.0 or newer) to add its animator " +
                                             "layers, parameters and menu to the avatar. Install one of them with the VRChat Creator Companion.";

        public abstract string Name { get; }
        public abstract int Priority { get; }

        /// <summary>Adds the components that merge the assets. Any of them can be null.</summary>
        public abstract void Install(GameObject avatarRoot, AnimatorController fx, VRCExpressionParameters parameters, VRCExpressionsMenu menu);

        /// <summary>Runs in the animator pass, after the tool merged the assets.</summary>
        public virtual void CleanUp(GameObject avatarRoot) { }

        static FTMerger found;
        static bool searched;

        public static FTMerger Find()
        {
            if (searched) return found;
            searched = true;
            found = TypeCache.GetTypesDerivedFrom<FTMerger>()
                .Where(t => !t.IsAbstract)
                .Select(t => (FTMerger)Activator.CreateInstance(t, true))
                .OrderBy(m => m.Priority)
                .FirstOrDefault();
            return found;
        }
    }
}
