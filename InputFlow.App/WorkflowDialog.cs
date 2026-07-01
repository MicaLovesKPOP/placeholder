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
        private readonly Control _cycleTargetsControl;
        private readonly Label _modeHelpLabel;
        private readonly Label _targetLabel;
        private readonly Label _cycleTargetsLabel;
        private readonly Label _fallbackLabel;
        private readonly Label _returnBehaviorLabel;
        private readonly Label _summaryLabel;
        private readonly Label _errorLabel;
        private readonly ToolTip _toolTip = new ToolTip();

        public WorkflowDialog(IReadOnlyList<SetupConfiguredProfileOption> profiles, WorkflowDraft? initialDraft = null)
        {
            Text = initialDraft == null ? "Add Workflow" : "Edit Workflow";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new System.Drawing.Size(680, 620);

            var switchableProfiles = profiles
                .Where(profile => profile.CanUseForSwitching)
                .Select(profile => new ProfileItem(profile.ProfileId, FormatProfileLabel(profile)))
                .ToList();
            switchableProfiles = OrderProfilesForDraft(switchableProfiles, initialDraft);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 11,
                Padding = new Padding(12)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _nameTextBox = new TextBox { Dock = DockStyle.Fill, Text = "Language workflow", AccessibleName = "Workflow name", TabIndex = 0 };
            _modeComboBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Workflow mode", TabIndex = 1 };
            _modeComboBox.Items.Add(new ModeItem("toggle", "Toggle"));
            _modeComboBox.Items.Add(new ModeItem("switchTo", "Direct switch"));
            _modeComboBox.Items.Add(new ModeItem("cycle", "Cycle"));
            _modeComboBox.Items.Add(new ModeItem("previous", "Previous profile"));
            _modeComboBox.SelectedIndex = 0;
            _modeComboBox.SelectedIndexChanged += (_, _) =>
            {
                UpdateModeVisibility();
                UpdateModeHelp();
                UpdateSummary();
            };
            var modeControl = CreateModeControl();

            _triggersTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Text = "Ctrl+Shift+Space",
                AccessibleName = "Triggers",
                AccessibleDescription = "Enter one trigger per line. Examples include Ctrl+Shift+Space, F13, and RightAlt.",
                TabIndex = 2,
                Multiline = true,
                AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical
            };
            _triggersTextBox.TextChanged += (_, _) => UpdateSummary();
            var triggersControl = CreateTriggersControl();
            _modeHelpLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                ForeColor = System.Drawing.SystemColors.GrayText
            };
            _targetComboBox = CreateProfileComboBox(switchableProfiles);
            _targetComboBox.AccessibleName = "Target profile";
            _targetComboBox.TabIndex = 3;
            _targetComboBox.SelectedIndexChanged += (_, _) => UpdateSummary();
            _fallbackComboBox = CreateProfileComboBox(new[] { new ProfileItem("", "(none)") }.Concat(switchableProfiles).ToList());
            _fallbackComboBox.AccessibleName = "Fallback profile";
            _fallbackComboBox.TabIndex = 4;
            _fallbackComboBox.SelectedIndexChanged += (_, _) => UpdateSummary();
            _returnBehaviorComboBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Return behavior", TabIndex = 5 };
            _returnBehaviorComboBox.Items.Add(new ReturnBehaviorItem("lastNonTarget", "Last non-target profile"));
            _returnBehaviorComboBox.Items.Add(new ReturnBehaviorItem("alwaysSpecificLayout", "Always fallback profile"));
            _returnBehaviorComboBox.Items.Add(new ReturnBehaviorItem("manualOnly", "Manual return only"));
            _returnBehaviorComboBox.SelectedIndex = 0;
            _returnBehaviorComboBox.SelectedIndexChanged += (_, _) => UpdateSummary();

            _cycleTargetsList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, AccessibleName = "Cycle targets", TabIndex = 6 };
            foreach (var profile in switchableProfiles)
            {
                _cycleTargetsList.Items.Add(profile);
            }
            _cycleTargetsList.ItemCheck += (_, _) => ScheduleSummaryUpdateAfterItemCheck();
            _cycleTargetsControl = CreateCycleTargetsControl();

            _targetLabel = CreateLabel("Target");
            _cycleTargetsLabel = CreateLabel("Cycle targets");
            _fallbackLabel = CreateLabel("Fallback");
            _returnBehaviorLabel = CreateLabel("Return behavior");
            _summaryLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                ForeColor = System.Drawing.SystemColors.ControlText,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(8, 0, 8, 0),
                AccessibleName = "Workflow summary"
            };
            _errorLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = System.Drawing.Color.Firebrick,
                AutoSize = false
            };

            AddRow(root, 0, "Name", _nameTextBox);
            AddRow(root, 1, "Mode", modeControl);
            root.Controls.Add(_modeHelpLabel, 0, 2);
            root.SetColumnSpan(_modeHelpLabel, 2);
            AddRow(root, 3, "Triggers", triggersControl);
            root.Controls.Add(_targetLabel, 0, 4);
            root.Controls.Add(_targetComboBox, 1, 4);
            root.Controls.Add(_fallbackLabel, 0, 5);
            root.Controls.Add(_fallbackComboBox, 1, 5);
            root.Controls.Add(_returnBehaviorLabel, 0, 6);
            root.Controls.Add(_returnBehaviorComboBox, 1, 6);
            root.Controls.Add(_cycleTargetsLabel, 0, 7);
            root.Controls.Add(_cycleTargetsControl, 1, 7);
            root.Controls.Add(_summaryLabel, 0, 8);
            root.SetColumnSpan(_summaryLabel, 2);
            root.Controls.Add(_errorLabel, 0, 9);
            root.SetColumnSpan(_errorLabel, 2);
            var buttonRow = CreateButtonRow();
            root.Controls.Add(buttonRow, 0, 10);
            root.SetColumnSpan(buttonRow, 2);

            Controls.Add(root);
            ConfigureToolTips();

            ApplyInitialDraft(initialDraft);
            UpdateModeVisibility();
            UpdateModeHelp();
            UpdateSummary();
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

        private Control CreateModeControl()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));

            var helpButton = new Button
            {
                Text = "?",
                Dock = DockStyle.Fill,
                AccessibleName = "Workflow mode help",
                TabIndex = 9
            };
            helpButton.Click += (_, _) => ShowModeHelp();

            root.Controls.Add(_modeComboBox, 0, 0);
            root.Controls.Add(helpButton, 1, 0);
            return root;
        }

        private Control CreateTriggersControl()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));

            var helpButton = new Button
            {
                Text = "?",
                Dock = DockStyle.Fill,
                AccessibleName = "Trigger help",
                TabIndex = 10
            };
            helpButton.Click += (_, _) => ShowTriggerHelp();

            root.Controls.Add(_triggersTextBox, 0, 0);
            root.Controls.Add(helpButton, 1, 0);
            return root;
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

            if (mode.Equals("toggle", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(target?.ProfileId) &&
                string.Equals(target.ProfileId, fallback?.ProfileId, StringComparison.OrdinalIgnoreCase))
            {
                _errorLabel.Text = "Fallback profile must be different from the target profile.";
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
            _cycleTargetsControl.Visible = isCycle;
            _fallbackLabel.Visible = isToggle;
            _fallbackComboBox.Visible = isToggle;
            _returnBehaviorLabel.Visible = isToggle;
            _returnBehaviorComboBox.Visible = isToggle;
        }

        private void UpdateModeHelp()
        {
            _modeHelpLabel.Text = GetSelectedMode() switch
            {
                "switchTo" => "Direct switch always switches to the target profile. Pressing the trigger again keeps switching to that same target.",
                "cycle" => "Cycle advances through the checked target profiles in list order. Use Move Up and Move Down to set the order.",
                "previous" => "Previous profile switches back to the last profile InputFlow saw before the current one.",
                _ => "Toggle switches to the target profile, then returns according to the selected return behavior."
            };
        }

        private void ShowModeHelp()
        {
            MessageBox.Show(
                this,
                "Toggle: switch to one target, then return using the selected return behavior.\n\nDirect switch: always switch to one target, with no automatic return.\n\nCycle: move through checked targets in list order. Use Move Up and Move Down to set the order.\n\nPrevious profile: jump back to the last profile InputFlow observed.",
                "InputFlow workflow modes",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void ShowTriggerHelp()
        {
            MessageBox.Show(
                this,
                "Enter one trigger per line. InputFlow accepts chords such as Ctrl+Shift+Space, Ctrl+Alt+K, and Ctrl+Shift+F12.\n\nFunction keys F1 through F24 are supported, so F13 works if your keyboard, keyboard software, or another tool sends it.\n\nSingle-key triggers such as RightAlt, LeftAlt, RightCtrl, LeftCtrl, RightShift, and LeftShift are supported. InputFlow suppresses a single-key trigger while it is running, so choose one only if you are comfortable giving that key to InputFlow.",
                "InputFlow trigger help",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void ConfigureToolTips()
        {
            _toolTip.SetToolTip(_nameTextBox, "A label for this workflow in Setup Status and logs.");
            _toolTip.SetToolTip(_modeComboBox, "Choose how InputFlow responds when the trigger is pressed.");
            _toolTip.SetToolTip(_triggersTextBox, "Enter one trigger per line, for example Ctrl+Shift+Space or F13.");
            _toolTip.SetToolTip(_targetComboBox, "The profile this workflow switches to.");
            _toolTip.SetToolTip(_fallbackComboBox, "Optional return profile for toggle workflows.");
            _toolTip.SetToolTip(_returnBehaviorComboBox, "Controls how toggle workflows return from the target profile.");
            _toolTip.SetToolTip(_cycleTargetsList, "Checked profiles are cycled in the order shown.");
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

        private Control CreateCycleTargetsControl()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(6, 0, 0, 0)
            };

            var moveUpButton = new Button { Text = "Move Up", Width = 86, Height = 28, TabIndex = 7 };
            moveUpButton.Click += (_, _) => MoveSelectedCycleTarget(-1);

            var moveDownButton = new Button { Text = "Move Down", Width = 86, Height = 28, TabIndex = 8 };
            moveDownButton.Click += (_, _) => MoveSelectedCycleTarget(1);

            buttons.Controls.Add(moveUpButton);
            buttons.Controls.Add(moveDownButton);

            root.Controls.Add(_cycleTargetsList, 0, 0);
            root.Controls.Add(buttons, 1, 0);
            return root;
        }

        private void MoveSelectedCycleTarget(int direction)
        {
            int index = _cycleTargetsList.SelectedIndex;
            int newIndex = index + direction;
            if (index < 0 || newIndex < 0 || newIndex >= _cycleTargetsList.Items.Count)
            {
                return;
            }

            object item = _cycleTargetsList.Items[index];
            bool isChecked = _cycleTargetsList.GetItemChecked(index);
            _cycleTargetsList.Items.RemoveAt(index);
            _cycleTargetsList.Items.Insert(newIndex, item);
            _cycleTargetsList.SetItemChecked(newIndex, isChecked);
            _cycleTargetsList.SelectedIndex = newIndex;
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            string triggerSummary = GetTriggerSummary();
            string mode = GetSelectedMode();
            string target = GetSelectedProfileLabel(_targetComboBox);
            string fallback = GetSelectedProfileLabel(_fallbackComboBox);
            string cycleTargets = GetCheckedCycleTargetSummary();
            string returnBehavior = _returnBehaviorComboBox.SelectedItem?.ToString() ?? "Last non-target profile";

            _summaryLabel.Text = mode switch
            {
                "switchTo" => $"Summary: {triggerSummary} -> switch directly to {target}.",
                "cycle" => $"Summary: {triggerSummary} -> cycle through {cycleTargets}.",
                "previous" => $"Summary: {triggerSummary} -> switch to the previous observed profile.",
                _ => $"Summary: {triggerSummary} -> switch to {target}; return behavior: {returnBehavior}; fallback: {fallback}."
            };
        }

        private void ScheduleSummaryUpdateAfterItemCheck()
        {
            if (IsHandleCreated)
            {
                BeginInvoke((Action)UpdateSummary);
            }
        }

        private string GetTriggerSummary()
        {
            var triggers = _triggersTextBox.Lines
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(3)
                .ToList();
            if (triggers.Count == 0)
            {
                return "trigger";
            }

            string suffix = _triggersTextBox.Lines.Count(line => !string.IsNullOrWhiteSpace(line)) > triggers.Count ? ", ..." : "";
            return string.Join(", ", triggers) + suffix;
        }

        private static string GetSelectedProfileLabel(ComboBox combo)
        {
            return combo.SelectedItem is ProfileItem profile && !string.IsNullOrWhiteSpace(profile.ProfileId)
                ? profile.ProfileId
                : "(none)";
        }

        private string GetCheckedCycleTargetSummary()
        {
            var targets = _cycleTargetsList.CheckedItems
                .OfType<ProfileItem>()
                .Select(item => item.ProfileId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();
            return targets.Count == 0 ? "(no targets selected)" : string.Join(" -> ", targets);
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

        private static List<ProfileItem> OrderProfilesForDraft(List<ProfileItem> profiles, WorkflowDraft? draft)
        {
            if (draft == null || draft.TargetProfileIds.Count == 0)
            {
                return profiles;
            }

            var profilesById = profiles.ToDictionary(profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase);
            var ordered = new List<ProfileItem>();
            var selectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string profileId in draft.TargetProfileIds)
            {
                if (profilesById.TryGetValue(profileId, out var profile) && selectedIds.Add(profile.ProfileId))
                {
                    ordered.Add(profile);
                }
            }

            ordered.AddRange(profiles.Where(profile => !selectedIds.Contains(profile.ProfileId)));
            return ordered;
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
