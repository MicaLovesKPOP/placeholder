using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using InputFlow.Core;

namespace InputFlow.App
{
    internal sealed class WorkflowDraft
    {
        public string Name { get; set; } = string.Empty;
        public string Mode { get; set; } = "toggle";
        public List<string> Triggers { get; set; } = new();
        public string? TargetProfileId { get; set; }
        public List<string> TargetProfileIds { get; set; } = new();
        public string? FallbackProfileId { get; set; }
        public string ReturnBehavior { get; set; } = "lastNonTarget";
    }

    internal sealed class WorkflowDialog : Form
    {
        private readonly TextBox _nameTextBox;
        private readonly ComboBox _modeComboBox;
        private readonly TextBox _triggersTextBox;
        private readonly ComboBox _targetComboBox;
        private readonly ComboBox _fallbackComboBox;
        private readonly ComboBox _returnBehaviorComboBox;
        private readonly CheckedListBox _cycleTargetsList;
        private readonly Label _targetLabel;
        private readonly Label _cycleTargetsLabel;
        private readonly Label _fallbackLabel;
        private readonly Label _returnBehaviorLabel;
        private readonly Label _errorLabel;

        public WorkflowDialog(IReadOnlyList<SetupConfiguredProfileOption> profiles, WorkflowDraft? initialDraft = null)
        {
            Text = initialDraft == null ? "Add Workflow" : "Edit Workflow";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new System.Drawing.Size(560, 460);

            var switchableProfiles = profiles
                .Where(profile => profile.CanUseForSwitching)
                .Select(profile => new ProfileItem(profile.ProfileId, FormatProfileLabel(profile)))
                .ToList();

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 9,
                Padding = new Padding(12)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _nameTextBox = new TextBox { Dock = DockStyle.Fill, Text = "Language workflow", AccessibleName = "Workflow name", TabIndex = 0 };
            _modeComboBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Workflow mode", TabIndex = 1 };
            _modeComboBox.Items.Add(new ModeItem("toggle", "Toggle"));
            _modeComboBox.Items.Add(new ModeItem("switchTo", "Direct switch"));
            _modeComboBox.Items.Add(new ModeItem("cycle", "Cycle"));
            _modeComboBox.Items.Add(new ModeItem("previous", "Previous profile"));
            _modeComboBox.SelectedIndex = 0;
            _modeComboBox.SelectedIndexChanged += (_, _) => UpdateModeVisibility();

            _triggersTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Text = "Ctrl+Shift+Space",
                AccessibleName = "Triggers",
                TabIndex = 2,
                Multiline = true,
                AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical
            };
            _targetComboBox = CreateProfileComboBox(switchableProfiles);
            _targetComboBox.AccessibleName = "Target profile";
            _targetComboBox.TabIndex = 3;
            _fallbackComboBox = CreateProfileComboBox(new[] { new ProfileItem("", "(none)") }.Concat(switchableProfiles).ToList());
            _fallbackComboBox.AccessibleName = "Fallback profile";
            _fallbackComboBox.TabIndex = 4;
            _returnBehaviorComboBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Return behavior", TabIndex = 5 };
            _returnBehaviorComboBox.Items.Add(new ReturnBehaviorItem("lastNonTarget", "Last non-target profile"));
            _returnBehaviorComboBox.Items.Add(new ReturnBehaviorItem("alwaysSpecificLayout", "Always fallback profile"));
            _returnBehaviorComboBox.Items.Add(new ReturnBehaviorItem("manualOnly", "Manual return only"));
            _returnBehaviorComboBox.SelectedIndex = 0;

            _cycleTargetsList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, AccessibleName = "Cycle targets", TabIndex = 6 };
            foreach (var profile in switchableProfiles)
            {
                _cycleTargetsList.Items.Add(profile);
            }

            _targetLabel = CreateLabel("Target");
            _cycleTargetsLabel = CreateLabel("Cycle targets");
            _fallbackLabel = CreateLabel("Fallback");
            _returnBehaviorLabel = CreateLabel("Return behavior");
            _errorLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = System.Drawing.Color.Firebrick,
                AutoSize = false
            };

            AddRow(root, 0, "Name", _nameTextBox);
            AddRow(root, 1, "Mode", _modeComboBox);
            AddRow(root, 2, "Triggers", _triggersTextBox);
            root.Controls.Add(_targetLabel, 0, 3);
            root.Controls.Add(_targetComboBox, 1, 3);
            root.Controls.Add(_fallbackLabel, 0, 4);
            root.Controls.Add(_fallbackComboBox, 1, 4);
            root.Controls.Add(_returnBehaviorLabel, 0, 5);
            root.Controls.Add(_returnBehaviorComboBox, 1, 5);
            root.Controls.Add(_cycleTargetsLabel, 0, 6);
            root.Controls.Add(_cycleTargetsList, 1, 6);
            root.Controls.Add(_errorLabel, 0, 7);
            root.SetColumnSpan(_errorLabel, 2);
            var buttonRow = CreateButtonRow();
            root.Controls.Add(buttonRow, 0, 8);
            root.SetColumnSpan(buttonRow, 2);

            Controls.Add(root);

            ApplyInitialDraft(initialDraft);
            UpdateModeVisibility();
        }

        public WorkflowDraft Draft { get; private set; } = new WorkflowDraft();

        private Control CreateButtonRow()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };

            var saveButton = new Button { Text = "Save", Width = 100, Height = 30, TabIndex = 7 };
            saveButton.Click += (_, _) => Save();

            var cancelButton = new Button { Text = "Cancel", Width = 100, Height = 30, TabIndex = 8 };
            cancelButton.Click += (_, _) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            panel.Controls.Add(saveButton);
            panel.Controls.Add(cancelButton);
            AcceptButton = saveButton;
            CancelButton = cancelButton;
            return panel;
        }

        private void Save()
        {
            _errorLabel.Text = "";

            string mode = GetSelectedMode();
            if (string.IsNullOrWhiteSpace(_nameTextBox.Text))
            {
                _errorLabel.Text = "Name is required.";
                return;
            }

            var triggers = ParseTriggers();
            if (!triggers.Success)
            {
                _errorLabel.Text = triggers.Error;
                return;
            }

            if (triggers.SingleKeyTriggers.Count > 0)
            {
                string triggerList = string.Join(", ", triggers.SingleKeyTriggers);
                var result = MessageBox.Show(
                    this,
                    $"The following triggers are single-key triggers: {triggerList}. InputFlow will suppress those keys while it is running.",
                    "InputFlow single-key trigger",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);
                if (result != DialogResult.OK)
                {
                    return;
                }
            }

            var fallback = _fallbackComboBox.SelectedItem as ProfileItem;
            var returnBehavior = _returnBehaviorComboBox.SelectedItem as ReturnBehaviorItem;
            var target = _targetComboBox.SelectedItem as ProfileItem;
            var cycleTargets = _cycleTargetsList.CheckedItems
                .OfType<ProfileItem>()
                .Select(item => item.ProfileId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();

            if (mode.Equals("cycle", StringComparison.OrdinalIgnoreCase))
            {
                if (cycleTargets.Count < 2)
                {
                    _errorLabel.Text = "Cycle workflows require at least two targets.";
                    return;
                }
            }
            else if (mode.Equals("previous", StringComparison.OrdinalIgnoreCase))
            {
                // Previous-profile workflows intentionally do not have a target.
            }
            else if (target == null || string.IsNullOrWhiteSpace(target.ProfileId))
            {
                _errorLabel.Text = "Target profile is required.";
                return;
            }

            if (mode.Equals("toggle", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(returnBehavior?.Value, "alwaysSpecificLayout", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(fallback?.ProfileId))
            {
                _errorLabel.Text = "Always fallback profile requires a fallback.";
                return;
            }

            Draft = new WorkflowDraft
            {
                Name = _nameTextBox.Text.Trim(),
                Mode = mode,
                Triggers = triggers.NormalizedTriggers,
                TargetProfileId = mode.Equals("cycle", StringComparison.OrdinalIgnoreCase) || mode.Equals("previous", StringComparison.OrdinalIgnoreCase) ? null : target?.ProfileId,
                TargetProfileIds = cycleTargets,
                FallbackProfileId = mode.Equals("toggle", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(fallback?.ProfileId) ? fallback.ProfileId : null,
                ReturnBehavior = mode.Equals("toggle", StringComparison.OrdinalIgnoreCase) ? returnBehavior?.Value ?? "lastNonTarget" : "lastNonTarget"
            };

            DialogResult = DialogResult.OK;
            Close();
        }

        private void UpdateModeVisibility()
        {
            string mode = GetSelectedMode();
            bool isCycle = mode.Equals("cycle", StringComparison.OrdinalIgnoreCase);
            bool isToggle = mode.Equals("toggle", StringComparison.OrdinalIgnoreCase);
            bool isPrevious = mode.Equals("previous", StringComparison.OrdinalIgnoreCase);

            _targetLabel.Visible = !isCycle && !isPrevious;
            _targetComboBox.Visible = !isCycle && !isPrevious;
            _cycleTargetsLabel.Visible = isCycle;
            _cycleTargetsList.Visible = isCycle;
            _fallbackLabel.Visible = isToggle;
            _fallbackComboBox.Visible = isToggle;
            _returnBehaviorLabel.Visible = isToggle;
            _returnBehaviorComboBox.Visible = isToggle;
        }

        private string GetSelectedMode()
        {
            return _modeComboBox.SelectedItem is ModeItem mode ? mode.Value : "toggle";
        }

        private void ApplyInitialDraft(WorkflowDraft? draft)
        {
            if (draft == null)
            {
                if (_targetComboBox.Items.Count > 0)
                {
                    _targetComboBox.SelectedIndex = 0;
                }
                _fallbackComboBox.SelectedIndex = 0;
                return;
            }

            _nameTextBox.Text = draft.Name;
            _triggersTextBox.Text = string.Join(Environment.NewLine, draft.Triggers);
            SelectMode(draft.Mode);
            SelectProfile(_targetComboBox, draft.TargetProfileId);
            SelectProfile(_fallbackComboBox, draft.FallbackProfileId);
            SelectReturnBehavior(draft.ReturnBehavior);
            SelectCycleTargets(draft.TargetProfileIds);
        }

        private TriggerParseResult ParseTriggers()
        {
            var lines = _triggersTextBox.Lines
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
            if (lines.Count == 0)
            {
                return TriggerParseResult.Failed("At least one trigger is required.");
            }

            var normalized = new List<string>();
            var singleKeyTriggers = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                var parsed = InputFlowTriggerParser.Parse(line);
                if (!parsed.Success)
                {
                    string error = parsed.Error ?? "Trigger is invalid.";
                    return TriggerParseResult.Failed($"Trigger '{line}' is invalid: {error}");
                }

                if (!seen.Add(parsed.NormalizedKeys))
                {
                    continue;
                }

                normalized.Add(parsed.NormalizedKeys);
                if (parsed.IsSingleKeyTrigger)
                {
                    singleKeyTriggers.Add(parsed.NormalizedKeys);
                }
            }

            return TriggerParseResult.Succeeded(normalized, singleKeyTriggers);
        }

        private void SelectMode(string mode)
        {
            for (int i = 0; i < _modeComboBox.Items.Count; i++)
            {
                if (_modeComboBox.Items[i] is ModeItem item && string.Equals(item.Value, mode, StringComparison.OrdinalIgnoreCase))
                {
                    _modeComboBox.SelectedIndex = i;
                    return;
                }
            }
        }

        private static void SelectProfile(ComboBox combo, string? profileId)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ProfileItem item && string.Equals(item.ProfileId, profileId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }

            if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }
        }

        private void SelectReturnBehavior(string returnBehavior)
        {
            for (int i = 0; i < _returnBehaviorComboBox.Items.Count; i++)
            {
                if (_returnBehaviorComboBox.Items[i] is ReturnBehaviorItem item && string.Equals(item.Value, returnBehavior, StringComparison.OrdinalIgnoreCase))
                {
                    _returnBehaviorComboBox.SelectedIndex = i;
                    return;
                }
            }
        }

        private void SelectCycleTargets(IReadOnlyList<string> targetIds)
        {
            var selected = targetIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _cycleTargetsList.Items.Count; i++)
            {
                bool isChecked = _cycleTargetsList.Items[i] is ProfileItem item && selected.Contains(item.ProfileId);
                _cycleTargetsList.SetItemChecked(i, isChecked);
            }
        }

        private static ComboBox CreateProfileComboBox(IReadOnlyList<ProfileItem> items)
        {
            var combo = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            foreach (var item in items)
            {
                combo.Items.Add(item);
            }

            return combo;
        }

        private static void AddRow(TableLayoutPanel root, int row, string label, Control input)
        {
            root.Controls.Add(CreateLabel(label), 0, row);
            root.Controls.Add(input, 1, row);
        }

        private static Label CreateLabel(string text)
        {
            return new Label
            {
                Text = text,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill
            };
        }

        private static string FormatProfileLabel(SetupConfiguredProfileOption profile)
        {
            string matched = profile.MatchedProfile == null
                ? profile.Summary
                : InputProfileManager.FormatProfile(profile.MatchedProfile);
            return $"{profile.ProfileId} - {matched}";
        }

        private sealed class ProfileItem
        {
            public ProfileItem(string profileId, string label)
            {
                ProfileId = profileId;
                Label = label;
            }

            public string ProfileId { get; }
            private string Label { get; }
            public override string ToString() => Label;
        }

        private sealed class ModeItem
        {
            public ModeItem(string value, string label)
            {
                Value = value;
                Label = label;
            }

            public string Value { get; }
            private string Label { get; }
            public override string ToString() => Label;
        }

        private sealed class ReturnBehaviorItem
        {
            public ReturnBehaviorItem(string value, string label)
            {
                Value = value;
                Label = label;
            }

            public string Value { get; }
            private string Label { get; }
            public override string ToString() => Label;
        }

        private sealed class TriggerParseResult
        {
            private TriggerParseResult(bool success, List<string> normalizedTriggers, List<string> singleKeyTriggers, string error)
            {
                Success = success;
                NormalizedTriggers = normalizedTriggers;
                SingleKeyTriggers = singleKeyTriggers;
                Error = error;
            }

            public bool Success { get; }
            public List<string> NormalizedTriggers { get; }
            public List<string> SingleKeyTriggers { get; }
            public string Error { get; }

            public static TriggerParseResult Succeeded(List<string> normalizedTriggers, List<string> singleKeyTriggers)
            {
                return new TriggerParseResult(true, normalizedTriggers, singleKeyTriggers, string.Empty);
            }

            public static TriggerParseResult Failed(string error)
            {
                return new TriggerParseResult(false, new List<string>(), new List<string>(), error);
            }
        }
    }
}
