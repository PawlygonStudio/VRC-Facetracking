using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Pawlygon.FaceTracking.Editor
{
    [CustomEditor(typeof(PawlygonFTParameters))]
    internal sealed class PawlygonFTParametersEditor : FTComponentEditor<PawlygonFTParameters>
    {
        const int SearchThreshold = 6;

        readonly FTDynamic detectedBadge = new FTDynamic();
        readonly FTDynamic detectedList = new FTDynamic();
        readonly FTDynamic manualBadge = new FTDynamic();
        readonly FTDynamic summary = new FTDynamic();
        readonly List<Image> manualStates = new List<Image>();
        VisualElement manualRows;
        ToolbarSearchField search;
        int renderedManualCount = -1;
        bool useAllTrackingKeys = true;

        SerializedProperty ParametersProperty => serializedObject.FindProperty(nameof(PawlygonFTParameters.parameters));

        protected override string Title => "Face Tracking Parameters";
        protected override string Subtitle => "Forwards face tracking to animator parameters.";

        protected override string Details =>
            "When the avatar is built, forwards native face tracking values to animator parameters, so animators made for " +
            "OSC face tracking (FT/v2/..., OSCm/Proxy/FT/v2/...) keep working. Detected parameters come from the avatar's " +
            "animators, including the ones VRCFury and Modular Avatar merge. Manual entries replace detected ones with the same name.";

        protected override void BuildBody(VisualElement body)
        {
            var detected = FTUI.Card(body, "Detected", out var detectedExtras);
            var autoDetect = new Toggle { text = "Auto-detect", tooltip = "Forward every known face tracking parameter used by the avatar's animators." };
            autoDetect.AddToClassList("ft-toggle");
            autoDetect.BindProperty(serializedObject.FindProperty(nameof(PawlygonFTParameters.autoDetect)));
            detectedExtras.Add(autoDetect);
            detectedExtras.Add(detectedBadge.Element);

            search = new ToolbarSearchField();
            search.AddToClassList("ft-search");
            search.style.width = StyleKeyword.Auto;
            search.RegisterValueChangedCallback(_ => RefreshNow());
            detected.Add(search);
            detected.Add(detectedList.Element);

            var manual = FTUI.Card(body, "Manual", out var manualExtras);
            manualExtras.Add(manualBadge.Element);
            manualRows = new VisualElement();
            manual.Add(manualRows);
            var add = new Button(() => AddManual("", "")) { text = "Add Parameter" };
            add.AddToClassList("ft-add-button");
            manual.Add(add);

            body.Add(summary.Element);
        }

        protected override void Refresh(VRCAvatarDescriptor avatar, FTIssues issues)
        {
            var setup = FTAvatarUtils.GetBuildComponents<PawlygonFaceTracking>(avatar.gameObject).FirstOrDefault();
            if (setup == null && !avatar.enableFaceTracking)
            {
                issues.Error("Face tracking isn't enabled on this avatar.",
                    "Add a Pawlygon Face Tracking component, or enable face tracking on the avatar descriptor.");
            }
            useAllTrackingKeys = setup != null ? setup.useAllTrackingKeys : avatar.useAllTrackingKeys;

            var plan = FTForwardingPlan.ForPreview(Component, avatar, useAllTrackingKeys);
            foreach (var warning in plan.Warnings) issues.Warning(warning);
            if (plan.Unread.Count > 0)
            {
                issues.Info($"{plan.Unread.Count} face tracking {(plan.Unread.Count == 1 ? "parameter isn't" : "parameters aren't")} forwarded: no layer reads {(plan.Unread.Count == 1 ? "it" : "them")}.",
                    "Their controllers declare them but never use them, so forwarding them would only cost performance rank: " + string.Join(", ", plan.Unread));
            }

            var duplicate = Component.parameters.Where(p => !string.IsNullOrWhiteSpace(p.parameter))
                .GroupBy(p => p.parameter.Trim()).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null) issues.Warning($"'{duplicate.Key}' is in the manual list twice, the last one wins.");

            // Without a setup component, forwards already on the descriptor are kept
            var existing = setup != null ? 0 : (avatar.faceTrackingParameterBehaviors ?? new List<VRCAvatarDescriptor.FaceTrackingParameterBehavior>())
                .Count(b => b.targetMode == VRCAvatarDescriptor.ParameterBehaviorTargetMode.ForwardToAnimator
                            && plan.Entries.All(e => e.Parameter != b.outputParameterName));

            UpdateDetected(plan);
            UpdateManual();
            UpdateSummary(plan.Entries.Count + existing, existing);
        }

        void UpdateDetected(FTForwardingPlan plan)
        {
            var detected = plan.Entries.Where(e => !e.IsManual).ToList();
            detectedBadge.Update(detected.Count.ToString(), e => e.Add(FTUI.Badge(detected.Count.ToString(), FTKind.Muted)));
            search.style.display = detected.Count > SearchThreshold ? DisplayStyle.Flex : DisplayStyle.None;

            var filter = search.value?.Trim() ?? "";
            var shown = detected.Where(e => filter.Length == 0
                                            || e.Parameter.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                                            || e.Expression.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            var key = $"{Component.autoDetect}|{filter}|" + string.Join("|", shown.Select(e => e.Parameter + e.Expression + e.Source));
            detectedList.Update(key, container =>
            {
                if (!Component.autoDetect)
                {
                    container.Add(FTUI.Text("Auto-detect is off.", "ft-muted"));
                    return;
                }
                if (detected.Count == 0)
                {
                    container.Add(FTUI.Text("No known face tracking parameters in this avatar's animators.", "ft-muted"));
                    return;
                }
                if (shown.Count == 0)
                {
                    container.Add(FTUI.Text("No match.", "ft-muted"));
                    return;
                }

                foreach (var group in shown.GroupBy(e => e.Source))
                {
                    container.Add(FTUI.Text(group.Key, "ft-group-title"));
                    var index = 0;
                    foreach (var entry in group) container.Add(ParameterRow(entry, index++ % 2 == 1));
                }
            });
        }

        VisualElement ParameterRow(FTForwardingPlan.Entry entry, bool alternate)
        {
            var row = new VisualElement { tooltip = entry.Parameter };
            row.AddToClassList("ft-param");
            if (alternate) row.AddToClassList("ft-param--alt");

            var text = new VisualElement();
            text.AddToClassList("ft-param__text");

            var split = entry.Parameter.LastIndexOf('/') + 1;
            var name = new VisualElement();
            name.AddToClassList("ft-param__name");
            if (split > 0) name.Add(FTUI.Text(entry.Parameter.Substring(0, split), "ft-param__prefix"));
            name.Add(FTUI.Text(entry.Parameter.Substring(split), "ft-param__base"));
            text.Add(name);
            text.Add(FTUI.Text("= " + entry.Expression, "ft-param__expression"));
            row.Add(text);

            var parameter = entry.Parameter;
            var expression = entry.Expression;
            var customize = new Button(() => AddManual(parameter, expression))
            {
                text = "Override",
                tooltip = "Copy to the manual list to change its expression.",
            };
            customize.AddToClassList("ft-param__action");
            row.Add(customize);
            return row;
        }

        void UpdateManual()
        {
            var property = ParametersProperty;
            manualBadge.Update(property.arraySize.ToString(), e => e.Add(FTUI.Badge(property.arraySize.ToString(), FTKind.Muted)));

            // Rows are only rebuilt when entries are added or removed, so typing keeps focus
            if (property.arraySize != renderedManualCount) RebuildManualRows();

            for (var i = 0; i < manualStates.Count && i < Component.parameters.Count; i++)
            {
                var entry = Component.parameters[i];
                string problem;
                var kind = FTKind.Ok;
                if (string.IsNullOrWhiteSpace(entry.parameter))
                {
                    problem = "Enter a parameter name.";
                    kind = FTKind.Error;
                }
                else
                {
                    problem = FTExpressionCheck.Validate(entry.expression, useAllTrackingKeys, out var isWarning);
                    if (problem != null) kind = isWarning ? FTKind.Warning : FTKind.Error;
                }
                manualStates[i].image = FTUI.Icon(kind);
                manualStates[i].tooltip = problem ?? "Looks good.";
            }
        }

        void RebuildManualRows()
        {
            var property = ParametersProperty;
            renderedManualCount = property.arraySize;
            manualRows.Clear();
            manualStates.Clear();

            if (property.arraySize == 0)
            {
                manualRows.Add(FTUI.Text("No manual parameters.", "ft-muted"));
                return;
            }

            for (var i = 0; i < property.arraySize; i++)
            {
                var element = property.GetArrayElementAtIndex(i);
                var index = i;
                var row = new VisualElement();
                row.AddToClassList("ft-manual");

                var name = new TextField { tooltip = "Animator parameter that receives the value." };
                name.AddToClassList("ft-manual__name");
                name.BindProperty(element.FindPropertyRelative(nameof(FTForwardedParameter.parameter)));
                row.Add(name);
                row.Add(FTUI.Text("=", "ft-manual__equals"));

                var expressionProperty = element.FindPropertyRelative(nameof(FTForwardedParameter.expression));
                var expression = new TextField { tooltip = "Tracking keys combined with + - * / abs() min() max() clamp(). The result is clamped to -1..1." };
                expression.AddToClassList("ft-manual__expression");
                expression.BindProperty(expressionProperty);
                row.Add(expression);

                // Where the cursor was, so the buttons insert there instead of at the end. -1 until the field had focus.
                var cursor = -1;
                expression.RegisterCallback<KeyUpEvent>(_ => cursor = expression.cursorIndex);
                expression.RegisterCallback<PointerUpEvent>(_ => cursor = expression.cursorIndex);
                expression.RegisterCallback<FocusOutEvent>(_ => cursor = expression.cursorIndex);

                var state = new Image();
                state.AddToClassList("ft-manual__state");
                manualStates.Add(state);
                row.Add(state);

                var pick = new Button { text = "+", tooltip = "Insert a tracking key. Type to search, also by ARKit and other names." };
                pick.clicked += () => new FTInputPicker(input => cursor = InsertInput(expression, cursor, input)).Show(pick.worldBound);
                pick.AddToClassList("ft-manual__button");
                row.Add(pick);

                var functions = new Button { text = "ƒ", tooltip = "Insert a function or operator" };
                functions.clicked += () => FTExpressionCheck.FunctionMenu((text, caret) => cursor = Insert(expression, cursor, text, caret, !text.StartsWith(" ")))
                    .DropDown(functions.worldBound);
                functions.AddToClassList("ft-manual__button");
                row.Add(functions);

                var remove = new Button(() => RemoveManual(index)) { text = "✕", tooltip = "Remove" };
                remove.AddToClassList("ft-manual__button");
                row.Add(remove);

                manualRows.Add(row);
            }
        }

        void AddManual(string parameter, string expression)
        {
            var property = ParametersProperty;
            property.arraySize++;
            var element = property.GetArrayElementAtIndex(property.arraySize - 1);
            element.FindPropertyRelative(nameof(FTForwardedParameter.parameter)).stringValue = parameter;
            element.FindPropertyRelative(nameof(FTForwardedParameter.expression)).stringValue = expression;
            serializedObject.ApplyModifiedProperties();
            RefreshNow();
        }

        void RemoveManual(int index)
        {
            var property = ParametersProperty;
            if (index >= property.arraySize) return;
            property.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            RefreshNow();
        }

        /// <summary>Inserts an input at the cursor, with a + in front when it would follow another value.</summary>
        static int InsertInput(TextField field, int at, string input) => Insert(field, at, input, input.Length, true);

        /// <summary>
        /// Inserts <paramref name="text"/> where the cursor was (the end when <paramref name="at"/> is -1) and puts the
        /// cursor <paramref name="caret"/> characters into it. A value or function goes after a + when it would follow
        /// another value, so the expression stays valid. Returns the new cursor position.
        /// </summary>
        static int Insert(TextField field, int at, string text, int caret, bool isValue)
        {
            var value = field.value ?? "";
            var index = at < 0 || at > value.Length ? value.Length : at;
            var before = value.Substring(0, index).TrimEnd();
            var last = before.Length > 0 ? before[before.Length - 1] : ' ';

            var prefix = "";
            if (isValue && (char.IsLetterOrDigit(last) || last == '_' || last == ')' || last == '.')) prefix = " + ";
            else if (isValue && index > 0 && !char.IsWhiteSpace(value[index - 1]) && value[index - 1] != '(') prefix = " ";

            field.value = value.Substring(0, index) + prefix + text + value.Substring(index);
            var cursor = index + prefix.Length + caret;
            field.schedule.Execute(() =>
            {
                field.Focus();
                field.SelectRange(cursor, cursor);
            });
            return cursor;
        }

        void UpdateSummary(int total, int existing)
        {
            var rank = FTForwardingPlan.RankFor(total);
            var kind = total <= 10 ? FTKind.Ok : total <= 20 ? FTKind.Info : total <= 30 ? FTKind.Warning : FTKind.Error;
            summary.Update($"{total}|{existing}", e =>
            {
                var tooltip = "Forwarded parameters count towards the avatar's performance rank: Excellent up to 10, Good 20, Medium 30, Poor 40." +
                              (existing > 0 ? $" Includes {existing} already on the descriptor." : "");
                var row = FTUI.Row("Forwarded", $"{total} parameter{(total == 1 ? "" : "s")}", tooltip);
                row.Add(FTUI.Badge(rank, kind, tooltip));
                row.style.marginBottom = 4;
                e.Add(row);
            });
        }
    }
}
