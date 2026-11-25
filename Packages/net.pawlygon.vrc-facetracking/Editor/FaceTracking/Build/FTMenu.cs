using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>Where the components' menu controls go in the avatar's menu.</summary>
    internal static class FTMenu
    {
        const string IconsFolder = "Packages/net.pawlygon.vrc-facetracking/Icons/";

        public static Texture2D Icon(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>(IconsFolder + file);

        /// <summary>
        /// The generated menu for controls that go in the submenu named <paramref name="menuName"/> (the main menu
        /// when it's empty), or null when the avatar's own menu already has a control for <paramref name="parameter"/> there.
        /// </summary>
        public static VRCExpressionsMenu ControlsMenu(string menuName, FTBuildContext context, string parameter)
        {
            menuName = menuName?.Trim() ?? "";
            var existing = menuName.Length == 0
                ? context.Descriptor.expressionsMenu
                : FTAvatarAssets.FindSubmenu(context.Descriptor.expressionsMenu, menuName)?.subMenu;
            if (HasControl(existing, parameter, 1)) return null;

            var submenu = context.Generated.GetSubmenu(menuName, Icon("FaceTrackingIcon.png"));
            return HasControl(submenu, parameter, 1) ? null : submenu;
        }

        // The order the controls have in the menu, whichever component adds its own first
        static readonly string[] ControlOrder = { "Disable Gestures", FTVisemesPlan.VisemesMenuName, "Pupil Tracking", "Pupil Emulation" };

        /// <summary>Puts the generated controls in their fixed order, in the menu and in the submenu they share.</summary>
        public static void Order(VRCExpressionsMenu menu, int depth = 1)
        {
            if (menu == null) return;
            int Rank(VRCExpressionsMenu.Control control)
            {
                var rank = System.Array.IndexOf(ControlOrder, control.name);
                return rank < 0 ? ControlOrder.Length : rank;
            }
            menu.controls = menu.controls.Select((control, index) => (control, index))
                .OrderBy(x => Rank(x.control)).ThenBy(x => x.index).Select(x => x.control).ToList();
            EditorUtility.SetDirty(menu);

            if (depth <= 0) return;
            foreach (var control in menu.controls.Where(c => c.subMenu != null)) Order(control.subMenu, depth - 1);
        }

        /// <summary>The menu as inspectors call it: "the main menu", or "the Face Tracking menu".</summary>
        public static string Describe(string menuName)
        {
            menuName = menuName?.Trim() ?? "";
            return menuName.Length == 0 ? "the main menu" : $"the {menuName} menu";
        }

        static bool HasControl(VRCExpressionsMenu menu, string parameter, int depth)
        {
            if (menu == null) return false;
            return menu.controls.Any(c => c.parameter?.name == parameter || (depth > 0 && HasControl(c.subMenu, parameter, depth - 1)));
        }
    }
}
