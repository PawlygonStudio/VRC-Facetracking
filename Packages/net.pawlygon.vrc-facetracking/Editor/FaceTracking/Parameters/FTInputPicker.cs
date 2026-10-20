using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Pawlygon.FaceTracking.Editor
{
    /// <summary>
    /// A searchable list of the inputs a custom expression can use: the tracking keys by face region, Voice and the
    /// visemes. The keys' other names (ARKit, SRanipal...) are listed too, so searching "MouthSmile" finds
    /// MouthCornerPull. Picking one gives the SDK name.
    /// </summary>
    internal sealed class FTInputPicker : AdvancedDropdown
    {
        readonly Action<string> onPick;
        readonly List<string> values = new List<string>();
        int groupIds;

        public FTInputPicker(Action<string> onPick) : base(new AdvancedDropdownState())
        {
            this.onPick = onPick;
            minimumSize = new Vector2(300, 380);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            values.Clear();
            groupIds = 0;
            var root = Group("Inputs");
            var groups = new Dictionary<string, AdvancedDropdownItem>();
            var aliases = Group("Other names");

            for (var key = 0; key < FTKeys.KeyCount; key++)
            {
                var name = FTKeys.KeyName(key);
                var region = FTExpressionCheck.Region(name);
                if (!groups.TryGetValue(region, out var group))
                {
                    group = Group(region);
                    groups[region] = group;
                    root.AddChild(group);
                }
                group.AddChild(Item(name, name));
                foreach (var alias in FTKeys.AliasesFor(key)) aliases.AddChild(Item($"{alias}  →  {name}", name));
            }

            root.AddChild(Item("Voice", "Voice"));
            var visemes = Group("Visemes");
            foreach (var viseme in FTExpressionCheck.VisemeInputs) visemes.AddChild(Item(viseme, viseme));
            root.AddChild(visemes);
            if (aliases.children.Any()) root.AddChild(aliases);
            return root;
        }

        // Groups get negative ids, so they never match an input's
        AdvancedDropdownItem Group(string label) => new AdvancedDropdownItem(label) { id = -1 - ++groupIds };

        AdvancedDropdownItem Item(string label, string value)
        {
            values.Add(value);
            return new AdvancedDropdownItem(label) { id = values.Count - 1 };
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item.id >= 0 && item.id < values.Count) onPick(values[item.id]);
        }
    }
}
