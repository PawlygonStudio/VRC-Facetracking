using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// Reads and replaces the avatar's FX controller, expression parameters and menu. Anything changed
    /// is copied into the build folder first, so the originals are never touched.
    /// </summary>
    internal static class FTAvatarAssets
    {
        public static RuntimeAnimatorController GetFX(VRCAvatarDescriptor descriptor)
        {
            if (!descriptor.customizeAnimationLayers || descriptor.baseAnimationLayers == null) return null;
            var layer = descriptor.baseAnimationLayers.FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX);
            return layer.isDefault ? null : layer.animatorController;
        }

        public static void SetFX(VRCAvatarDescriptor descriptor, RuntimeAnimatorController controller)
        {
            var layers = descriptor.baseAnimationLayers;
            var index = layers == null ? -1 : System.Array.FindIndex(layers, l => l.type == VRCAvatarDescriptor.AnimLayerType.FX);
            if (index < 0) throw new FTBuildException("The avatar descriptor has no FX playable layer slot.");

            layers[index].animatorController = controller;
            layers[index].isDefault = false;
            descriptor.baseAnimationLayers = layers;
            descriptor.customizeAnimationLayers = true;
            EditorUtility.SetDirty(descriptor);
        }

        /// <summary>
        /// An editable copy of the FX controller in <paramref name="folder"/>, or a new empty one.
        /// For an override controller the base is copied and a new override points at it.
        /// </summary>
        public static AnimatorController CopyFX(RuntimeAnimatorController source, string folder, out RuntimeAnimatorController assign)
        {
            var path = folder + "/FX.controller";
            if (source == null)
            {
                var created = AnimatorController.CreateAnimatorControllerAtPath(path);
                assign = created;
                return created;
            }

            var overrideController = source as AnimatorOverrideController;
            var baseController = overrideController != null ? overrideController.runtimeAnimatorController : source;
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(baseController), path))
            {
                throw new FTBuildException($"Couldn't copy the FX controller '{baseController.name}'.");
            }
            var copy = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);

            if (overrideController == null)
            {
                assign = copy;
                return copy;
            }

            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            overrideController.GetOverrides(overrides);
            var overrideCopy = new AnimatorOverrideController(copy) { name = overrideController.name };
            overrideCopy.ApplyOverrides(overrides);
            AssetDatabase.CreateAsset(overrideCopy, folder + "/FX Override.overrideController");
            assign = overrideCopy;
            return copy;
        }

        /// <summary>An editable copy of the expression parameters, assigned to the descriptor.</summary>
        public static VRCExpressionParameters CopyParameters(VRCAvatarDescriptor descriptor, string folder)
        {
            if (IsInFolder(descriptor.expressionParameters, folder)) return descriptor.expressionParameters;

            var copy = descriptor.expressionParameters != null
                ? Object.Instantiate(descriptor.expressionParameters)
                : ScriptableObject.CreateInstance<VRCExpressionParameters>();
            if (copy.parameters == null) copy.parameters = new VRCExpressionParameters.Parameter[0];
            copy.name = "Expression Parameters";
            AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(folder + "/Expression Parameters.asset"));

            descriptor.customExpressions = true;
            descriptor.expressionParameters = copy;
            if (descriptor.expressionsMenu == null) descriptor.expressionsMenu = CreateMenu(folder, "Menu");
            EditorUtility.SetDirty(descriptor);
            return copy;
        }

        /// <summary>Adds a parameter unless one with that name exists. Returns false if it already existed.</summary>
        public static bool AddParameter(VRCExpressionParameters parameters, string name, VRCExpressionParameters.ValueType type,
            float defaultValue, bool saved, bool synced)
        {
            if (parameters.parameters.Any(p => p.name == name)) return false;
            parameters.parameters = parameters.parameters.Append(new VRCExpressionParameters.Parameter
            {
                name = name,
                valueType = type,
                defaultValue = defaultValue,
                saved = saved,
                networkSynced = synced,
            }).ToArray();
            EditorUtility.SetDirty(parameters);
            return true;
        }

        /// <summary>
        /// The submenu named <paramref name="submenuName"/> in the avatar's main menu, copied so it can be
        /// edited. Created if missing. Null when the main menu is full.
        /// </summary>
        public static VRCExpressionsMenu GetEditableSubmenu(VRCAvatarDescriptor descriptor, string folder, string submenuName, Texture2D icon)
        {
            var root = descriptor.expressionsMenu;
            if (root == null)
            {
                root = CreateMenu(folder, "Menu");
            }
            else if (!IsInFolder(root, folder))
            {
                root = Object.Instantiate(root);
                root.name = "Menu";
                AssetDatabase.CreateAsset(root, AssetDatabase.GenerateUniqueAssetPath(folder + "/Menu.asset"));
            }
            descriptor.customExpressions = true;
            descriptor.expressionsMenu = root;

            var control = root.controls.FirstOrDefault(c => c.type == VRCExpressionsMenu.Control.ControlType.SubMenu && c.name == submenuName);
            if (control != null)
            {
                var copy = control.subMenu != null ? Object.Instantiate(control.subMenu) : ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                copy.name = submenuName;
                AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{SafeName(submenuName)} Menu.asset"));
                control.subMenu = copy;
                EditorUtility.SetDirty(root);
                return copy;
            }

            if (root.controls.Count >= VRCExpressionsMenu.MAX_CONTROLS) return null;

            var submenu = CreateMenu(folder, $"{SafeName(submenuName)} Menu");
            var submenuControl = CreateControl(submenuName, VRCExpressionsMenu.Control.ControlType.SubMenu, "", icon);
            submenuControl.subMenu = submenu;
            root.controls.Add(submenuControl);
            EditorUtility.SetDirty(root);
            return submenu;
        }

        /// <summary>
        /// A menu control with every list filled in like the SDK's inspector does. Tools like Av3Emulator
        /// and Gesture Manager expect them and fail to draw the menu when they're null.
        /// </summary>
        public static VRCExpressionsMenu.Control CreateControl(string name, VRCExpressionsMenu.Control.ControlType type, string parameter, Texture2D icon)
        {
            return new VRCExpressionsMenu.Control
            {
                name = name,
                type = type,
                icon = icon,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = parameter ?? "" },
                subParameters = new VRCExpressionsMenu.Control.Parameter[0],
                labels = new VRCExpressionsMenu.Control.Label[0],
                value = 1,
            };
        }

        static VRCExpressionsMenu CreateMenu(string folder, string name)
        {
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.name = name;
            menu.controls = new List<VRCExpressionsMenu.Control>();
            AssetDatabase.CreateAsset(menu, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.asset"));
            return menu;
        }

        static bool IsInFolder(Object asset, string folder)
        {
            return asset != null && AssetDatabase.GetAssetPath(asset).StartsWith(folder + "/");
        }

        static string SafeName(string name) => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' ? c : '_'));
    }
}
