using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using InputFlow.Core;

namespace InputFlow.App
{
    internal sealed class ProfileDraft
    {
        public string ProfileId { get; set; } = string.Empty;
        public InputProfile? InstalledProfile { get; set; }
        public string? EnterMode { get; set; }
    }

    internal sealed class ProfileDialog : Form
    {
        private readonly TextBox _idTextBox;
        private readonly ComboBox _installedProfileComboBox;
        private readonly ComboBox _enterModeComboBox;
        private readonly Label _errorLabel;
        private readonly bool _allowIdEdit;
        private string _lastSuggestedId = string.Empty;

        public ProfileDialog(IReadOnlyList<SetupInstalledProfileOption> installedProfiles, ProfileDraft? initialDraft = null, bool allowIdEdit = true)
        {
            _allowIdEdit = allowIdEdit;
            Text = initialDraft == null ? "Add Profile" : "Edit Profile";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new System.Drawing.Size(620, 220);

            var profileItems = installedProfiles
                .Select(profile => new InstalledProfileItem(profile.Profile, profile.DisplayName))
                .ToList();

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                Padding = new Padding(12)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++)
            {
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            }
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _idTextBox = new TextBox { Dock = DockStyle.Fill, Enabled = allowIdEdit, AccessibleName = "Profile ID", TabIndex = 0 };
            _installedProfileComboBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Windows profile", TabIndex = 1 };
            foreach (var item in profileItems)
            {
                _installedProfileComboBox.Items.Add(item);
            }
            _installedProfileComboBox.SelectedIndexChanged += (_, _) => SuggestProfileId();

            _enterModeComboBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Enter mode", TabIndex = 2 };
            _enterModeComboBox.Items.Add(new EnterModeItem(null, "(none)"));
            _enterModeComboBox.Items.Add(new EnterModeItem("hangul", "Korean Hangul"));
            _enterModeComboBox.SelectedIndex = 0;

            _errorLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = System.Drawing.Color.Firebrick,
                AutoSize = false
            };

            AddRow(root, 0, "Profile ID", _idTextBox);
            AddRow(root, 1, "Windows profile", _installedProfileComboBox);
            AddRow(root, 2, "Enter mode", _enterModeComboBox);
            root.Controls.Add(_errorLabel, 0, 3);
            root.SetColumnSpan(_errorLabel, 2);
            var buttons = CreateButtonRow();
            root.Controls.Add(buttons, 0, 4);
            root.SetColumnSpan(buttons, 2);

            Controls.Add(root);
            ApplyInitialDraft(initialDraft);
        }

        public ProfileDraft Draft { get; private set; } = new ProfileDraft();

        private Control CreateButtonRow()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };

            var saveButton = new Button { Text = "Save", Width = 100, Height = 30, TabIndex = 3 };
            saveButton.Click += (_, _) => Save();

            var cancelButton = new Button { Text = "Cancel", Width = 100, Height = 30, TabIndex = 4 };
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

            if (string.IsNullOrWhiteSpace(_idTextBox.Text))
            {
                _errorLabel.Text = "Profile ID is required.";
                return;
            }

            if (_installedProfileComboBox.SelectedItem is not InstalledProfileItem selected)
            {
                _errorLabel.Text = "Windows profile is required.";
                return;
            }

            var enterMode = _enterModeComboBox.SelectedItem as EnterModeItem;
            Draft = new ProfileDraft
            {
                ProfileId = _idTextBox.Text.Trim(),
                InstalledProfile = selected.Profile,
                EnterMode = enterMode?.Value
            };

            DialogResult = DialogResult.OK;
            Close();
        }

        private void ApplyInitialDraft(ProfileDraft? draft)
        {
            if (draft != null)
            {
                _idTextBox.Text = draft.ProfileId;
                SelectInstalledProfile(draft.InstalledProfile);
                SelectEnterMode(draft.EnterMode);
                return;
            }

            if (_installedProfileComboBox.Items.Count > 0)
            {
                _installedProfileComboBox.SelectedIndex = 0;
            }
        }

        private void SelectInstalledProfile(InputProfile? profile)
        {
            if (profile != null)
            {
                for (int i = 0; i < _installedProfileComboBox.Items.Count; i++)
                {
                    if (_installedProfileComboBox.Items[i] is InstalledProfileItem item &&
                        string.Equals(item.Profile.KLID, profile.KLID, StringComparison.OrdinalIgnoreCase))
                    {
                        _installedProfileComboBox.SelectedIndex = i;
                        return;
                    }
                }
            }

            if (_installedProfileComboBox.Items.Count > 0)
            {
                _installedProfileComboBox.SelectedIndex = 0;
            }
        }

        private void SelectEnterMode(string? enterMode)
        {
            for (int i = 0; i < _enterModeComboBox.Items.Count; i++)
            {
                if (_enterModeComboBox.Items[i] is EnterModeItem item &&
                    string.Equals(item.Value ?? string.Empty, enterMode ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    _enterModeComboBox.SelectedIndex = i;
                    return;
                }
            }
        }

        private void SuggestProfileId()
        {
            if (!_allowIdEdit || _installedProfileComboBox.SelectedItem is not InstalledProfileItem item)
            {
                return;
            }

            string suggestion = CreateProfileId(item.Profile);
            if (string.IsNullOrWhiteSpace(_idTextBox.Text) || string.Equals(_idTextBox.Text, _lastSuggestedId, StringComparison.OrdinalIgnoreCase))
            {
                _idTextBox.Text = suggestion;
                _lastSuggestedId = suggestion;
            }
        }

        private static string CreateProfileId(InputProfile profile)
        {
            string source = !string.IsNullOrWhiteSpace(profile.LanguageTag)
                ? profile.LanguageTag
                : !string.IsNullOrWhiteSpace(profile.FriendlyName)
                    ? profile.FriendlyName
                    : profile.KLID;

            string slug = Slugify(source);
            return string.IsNullOrWhiteSpace(slug) ? $"profile-{profile.KLID.ToLowerInvariant()}" : slug;
        }

        private static string Slugify(string value)
        {
            var builder = new StringBuilder(value.Length);
            bool previousWasSeparator = false;

            foreach (char ch in value.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(ch);
                    previousWasSeparator = false;
                }
                else if (!previousWasSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                    previousWasSeparator = true;
                }
            }

            return builder.ToString().Trim('-');
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

        private sealed class InstalledProfileItem
        {
            public InstalledProfileItem(InputProfile profile, string label)
            {
                Profile = profile;
                Label = label;
            }

            public InputProfile Profile { get; }
            private string Label { get; }
            public override string ToString() => Label;
        }

        private sealed class EnterModeItem
        {
            public EnterModeItem(string? value, string label)
            {
                Value = value;
                Label = label;
            }

            public string? Value { get; }
            private string Label { get; }
            public override string ToString() => Label;
        }
    }
}
