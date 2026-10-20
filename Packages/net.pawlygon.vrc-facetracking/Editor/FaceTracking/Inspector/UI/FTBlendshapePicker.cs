using System;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>A searchable list of blendshape names, with a None entry at the top.</summary>
    internal sealed class FTBlendshapePicker : AdvancedDropdown
    {
        public const string None = "(None)";

        readonly string title;
        readonly string[] names;
        readonly Action<string> onPick;

        /// <param name="onPick">Gets the picked name, or an empty string for None.</param>
        public FTBlendshapePicker(string title, string[] names, Action<string> onPick) : base(new AdvancedDropdownState())
        {
            this.title = title;
            this.names = names;
            this.onPick = onPick;
            minimumSize = new Vector2(280, 360);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem(title);
            root.AddChild(new AdvancedDropdownItem(None) { id = -1 });
            for (var i = 0; i < names.Length; i++) root.AddChild(new AdvancedDropdownItem(names[i]) { id = i });
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            onPick(item.id >= 0 && item.id < names.Length ? names[item.id] : "");
        }
    }
}
