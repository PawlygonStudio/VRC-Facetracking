using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>Merges the generated assets with Modular Avatar components, when VRCFury isn't installed.</summary>
    internal sealed class FTModularAvatarMerger : FTMerger
    {
        public const string HolderName = "Pawlygon Face Tracking (Modular Avatar)";

        public override string Name => "Modular Avatar";
        public override int Priority => 100;

        public override void Install(GameObject avatarRoot, AnimatorController fx, VRCExpressionParameters parameters, VRCExpressionsMenu menu)
        {
            // An object of its own: MA Parameters allows one per object, and the avatar root may have one
            var holder = new GameObject(HolderName);
            holder.transform.SetParent(avatarRoot.transform, false);

            if (fx != null)
            {
                var merge = holder.AddComponent<ModularAvatarMergeAnimator>();
                merge.animator = fx;
                merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
                merge.pathMode = MergeAnimatorPathMode.Absolute;
                // After the avatar's own layers, like the layers added without a merging tool
                merge.layerPriority = 1000;
            }

            if (parameters != null && parameters.parameters.Length > 0)
            {
                var declared = holder.AddComponent<ModularAvatarParameters>();
                declared.parameters = parameters.parameters.Select(p => new ParameterConfig
                {
                    nameOrPrefix = p.name,
                    remapTo = "",
                    syncType = SyncType(p.valueType),
                    localOnly = !p.networkSynced,
                    defaultValue = p.defaultValue,
                    saved = p.saved,
                }).ToList();
            }

            if (menu != null) InstallMenu(holder, avatarRoot, menu);
        }

        /// <summary>Modular Avatar removed its components by now, so the empty holder goes too.</summary>
        public override void CleanUp(GameObject avatarRoot)
        {
            var holder = avatarRoot.transform.Find(HolderName);
            if (holder != null && holder.childCount == 0 && holder.GetComponents<Component>().Length == 1) Object.DestroyImmediate(holder.gameObject);
        }

        /// <summary>
        /// Submenus the avatar's main menu already has go into that submenu, so it doesn't end up with two of the
        /// same name. Everything else goes into the main menu.
        /// </summary>
        static void InstallMenu(GameObject holder, GameObject avatarRoot, VRCExpressionsMenu menu)
        {
            var mainMenu = avatarRoot.GetComponent<VRCAvatarDescriptor>().expressionsMenu;
            foreach (var control in menu.controls)
            {
                var existing = FTAvatarAssets.FindSubmenu(mainMenu, control.name)?.subMenu;
                if (control.type != VRCExpressionsMenu.Control.ControlType.SubMenu || existing == null) continue;

                var installer = holder.AddComponent<ModularAvatarMenuInstaller>();
                installer.menuToAppend = control.subMenu;
                installer.installTargetMenu = existing;
            }

            var rest = menu.controls.Where(c => c.type != VRCExpressionsMenu.Control.ControlType.SubMenu
                                                || FTAvatarAssets.FindSubmenu(mainMenu, c.name)?.subMenu == null).ToList();
            if (rest.Count == 0) return;

            menu.controls = rest;
            holder.AddComponent<ModularAvatarMenuInstaller>().menuToAppend = menu;
        }

        static ParameterSyncType SyncType(VRCExpressionParameters.ValueType type)
        {
            switch (type)
            {
                case VRCExpressionParameters.ValueType.Int: return ParameterSyncType.Int;
                case VRCExpressionParameters.ValueType.Float: return ParameterSyncType.Float;
                default: return ParameterSyncType.Bool;
            }
        }
    }
}
