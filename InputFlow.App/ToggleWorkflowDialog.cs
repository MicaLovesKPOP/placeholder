using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using InputFlow.Core;

namespace InputFlow.App
{
    internal sealed class ToggleWorkflowDraft
    {
        public string Name { get; set; } = string.Empty;
        public string Trigger { get; set; } = string.Empty;
        public string TargetProfileId { get; set; } = string.Empty;
        public string? FallbackProfileId { get; set; }
        public string ReturnBehavior { get; set; } = "lastNonTarget";
    }

    internal sealed class ToggleWorkflowDialog : Form
    {
        private readonly TextBox _nameTextBox;
        private readonly TextBox _triggerTextBox;
        private readonly ComboBox _targetComboBox;
        private readonly ComboBox _fallbackComboBox;
        private readonly ComboBox _returnBehaviorComboBox;
        private readonly Label _errorLabel;

        public ToggleWorkflowDialog(IReadOnlyList<SetupConfiguredProfileOption> profiles)
        {
            Text = "Add Toggle Workflow";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new System.Drawing.Size(520, 290);

            var switchableProfiles = profiles
                .Where(profile => profile.CanUseForSwitching)
                .Select(profile => new ProfileItem(profile.ProfileId, FormatProfileLabel(profile)))
                .ToList();

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 7,
                Padding = new Padding(12)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 6; i++)
            {
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            }
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _nameTextBox = new TextBox { Dock = DockStyle.Fill, Text = "Language toggle" };
            _triggerTextBox = new TextBox { Dock = DockStyle.Fill, Text = "Ctrl+Shift+Space" };
            _targetComboBox = CreateProfileComboBox(switchableProfiles);
            _fallbackComboBox = CreateProfileComboBox(new[] { new ProfileItem("", "(none)") }.Concat(switchableProfiles).ToList());
            _returnBehaviorComboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _returnBehaviorComboBox.Items.Add(new ReturnBehaviorItem("lastNonTarget", "Last non-target profile"));
            _returnBehaviorComboBox.Items.Add(new ReturnBehaviorItem("alwaysSpecificLayout", "Always fallback profile"));
            _returnBehaviorComboBox.Items.Add(new ReturnBehaviorItem("manualOnly", "Manual return only"));
            _returnBehaviorComboBox.SelectedIndex = 0;

            _errorLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = System.Drawing.Color.Firebrick,
                AutoSize = false
            };

            AddRow(root, 0, "Name", _nameTextBox);
            AddRow(root, 1, "Trigger", _triggerTextBox);
            AddRow(root, 2, "Target", _targetComboBox);
            AddRow(root, 3, "Fallback", _fallbackComboBox);
            AddRow(root, 4, "Return behavior", _returnBehaviorComboBox);
            root.Controls.Add(_errorLabel, 0, 5);
            root.SetColumnSpan(_errorLabel, 2);
            var buttonRow = CreateButtonRow();
            root.Controls.Add(buttonRow, 0, 6);
            root.SetColumnSpan(buttonRow, 2);

            Controls.Add(root);

            if (_targetComboBox.Items.Count > 0)
            {
                _targetComboBox.SelectedIndex = 0;
            }
            _fallbackComboBox.SelectedIndex = 0;
        }

        public ToggleWorkflowDraft Draft { get; private set; } = new ToggleWorkflowDraft();

        private Control CreateButtonRow()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };

            var saveButton = new Button { Text = "Save", Width = 100, Height = 30 };
            saveButton.Click += (_, _) => Save();

            var cancelButton = new Button { Text = "Cancel", Width = 100, Height = 30 };
            cancelButton.Click += (_, _) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            panel.Controls.Add(saveButton);
            panel.Controls.Add(cancelButton);
            return panel;
        }

        private void Save()
        {
            _errorLabel.Text = "";

            if (string.IsNullOrWhiteSpace(_nameTextBox.Text))
            {
                _errorLabel.Text = "Name is required.";
                return;
            }

            if (string.IsNullOrWhiteSpace(_triggerTextBox.Text))
            {
                _errorLabel.Text = "Trigger is required.";
                return;
            }

            var parsedTrigger = InputFlowTriggerParser.Parse(_triggerTextBox.Text);
            if (!parsedTrigger.Success)
            {
                _errorLabel.Text = parsedTrigger.Error ?? "Trigger is invalid.";
                return;
            }

            if (parsedTrigger.IsSingleKeyTrigger)
            {
                var result = MessageBox.Show(
                    this,
                    $"'{parsedTrigger.NormalizedKeys}' is a single-key trigger. InputFlow will suppress that key while it is running.",
                    "InputFlow single-key trigger",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);
                if (result != DialogResult.OK)
                {
                    return;
                }
            }

            if (_targetComboBox.SelectedItem is not ProfileItem target || string.IsNullOrWhiteSpace(target.ProfileId))
            {
                _errorLabel.Text = "Target profile is required.";
                return;
            }

            var fallback = _fallbackComboBox.SelectedItem as ProfileItem;
            var returnBehavior = _returnBehaviorComboBox.SelectedItem as ReturnBehaviorItem;
            if (string.Equals(returnBehavior?.Value, "alwaysSpecificLayout", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(fallback?.ProfileId))
            {
                _errorLabel.Text = "Always fallback profile requires a fallback.";
                return;
            }

            Draft = new ToggleWorkflowDraft
            {
                Name = _nameTextBox.Text.Trim(),
                Trigger = parsedTrigger.NormalizedKeys,
                TargetProfileId = target.ProfileId,
                FallbackProfileId = string.IsNullOrWhiteSpace(fallback?.ProfileId) ? null : fallback.ProfileId,
                ReturnBehavior = returnBehavior?.Value ?? "lastNonTarget"
            };

            DialogResult = DialogResult.OK;
            Close();
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
            root.Controls.Add(new Label
            {
                Text = label,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill
            }, 0, row);
            root.Controls.Add(input, 1, row);
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
    }
}
