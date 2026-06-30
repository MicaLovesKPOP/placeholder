using System;
using System.Linq;
using System.Windows.Forms;
using InputFlow.Core;

namespace InputFlow.App
{
    internal sealed class SetupStatusForm : Form
    {
        private readonly Label _recoveryStatusLabel;
        private readonly ListView _configuredProfilesList;
        private readonly ListView _installedProfilesList;
        private readonly ListView _workflowsList;
        private readonly ListView _excludedProcessesList;
        private readonly Action _copyDiagnostics;
        private readonly Action _openConfig;
        private readonly Action _addProfile;
        private readonly Action<string> _editProfile;
        private readonly Action<string> _removeProfile;
        private readonly Action _addWorkflow;
        private readonly Action<string> _editWorkflow;
        private readonly Action<string> _removeWorkflow;
        private readonly Action _addExcludedProcess;
        private readonly Action<string> _removeExcludedProcess;

        public SetupStatusForm(
            Action copyDiagnostics,
            Action openConfig,
            Action addProfile,
            Action<string> editProfile,
            Action<string> removeProfile,
            Action addWorkflow,
            Action<string> editWorkflow,
            Action<string> removeWorkflow,
            Action addExcludedProcess,
            Action<string> removeExcludedProcess)
        {
            _copyDiagnostics = copyDiagnostics;
            _openConfig = openConfig;
            _addProfile = addProfile;
            _editProfile = editProfile;
            _removeProfile = removeProfile;
            _addWorkflow = addWorkflow;
            _editWorkflow = editWorkflow;
            _removeWorkflow = removeWorkflow;
            _addExcludedProcess = addExcludedProcess;
            _removeExcludedProcess = removeExcludedProcess;

            Text = "InputFlow Setup Status";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new System.Drawing.Size(1080, 680);
            Size = new System.Drawing.Size(1160, 740);
            KeyPreview = true;
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Close();
                    e.Handled = true;
                }
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));

            _recoveryStatusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                AccessibleName = "Config recovery status"
            };

            _configuredProfilesList = CreateListView("Profile ID", "Health", "Matched profile", "Enter mode", "Summary");
            _configuredProfilesList.AccessibleName = "Configured profiles";
            _configuredProfilesList.AccessibleDescription = "Profiles InputFlow can use for switching. Press Enter to edit the selected profile, or Delete to remove it.";
            _configuredProfilesList.DoubleClick += (_, _) => EditSelectedProfile();
            _configuredProfilesList.KeyDown += (_, e) => HandleConfiguredProfileKeyDown(e);

            _installedProfilesList = CreateListView("Installed profile", "Configured as");
            _installedProfilesList.AccessibleName = "Installed profile options";
            _installedProfilesList.AccessibleDescription = "Windows input profiles detected by InputFlow.";

            _workflowsList = CreateListView("Workflow", "ID", "Mode", "Status", "Triggers", "Targets", "Fallback", "Blocking reasons");
            _workflowsList.AccessibleName = "Workflow readiness";
            _workflowsList.AccessibleDescription = "Configured workflows and registration status. Press Enter to edit the selected workflow, or Delete to remove it.";
            _workflowsList.DoubleClick += (_, _) => EditSelectedWorkflow();
            _workflowsList.KeyDown += (_, e) => HandleWorkflowKeyDown(e);

            _excludedProcessesList = CreateListView("Process name");
            _excludedProcessesList.AccessibleName = "Excluded processes";
            _excludedProcessesList.AccessibleDescription = "Processes where InputFlow ignores triggers. Press Delete to remove the selected exclusion.";
            _excludedProcessesList.DoubleClick += (_, _) => RemoveSelectedExcludedProcess();
            _excludedProcessesList.KeyDown += (_, e) => HandleExcludedProcessKeyDown(e);

            root.Controls.Add(_recoveryStatusLabel, 0, 0);
            root.Controls.Add(CreateGroup("Configured profiles", _configuredProfilesList), 0, 1);
            root.Controls.Add(CreateGroup("Installed profile options", _installedProfilesList), 0, 2);
            root.Controls.Add(CreateGroup("Workflow readiness", _workflowsList), 0, 3);
            root.Controls.Add(CreateExcludedProcessesGroup(), 0, 4);
            root.Controls.Add(CreateButtonRow(), 0, 5);

            Controls.Add(root);
        }

        public void RefreshModel(InputFlowSetupModel model, string recoveryStatus)
        {
            _recoveryStatusLabel.Text = recoveryStatus;
            _configuredProfilesList.BeginUpdate();
            _installedProfilesList.BeginUpdate();
            _workflowsList.BeginUpdate();
            _excludedProcessesList.BeginUpdate();
            try
            {
                _configuredProfilesList.Items.Clear();
                foreach (var profile in model.ConfiguredProfiles)
                {
                    var item = new ListViewItem(new[]
                    {
                        profile.ProfileId,
                        profile.Health.ToString().ToLowerInvariant(),
                        profile.MatchedProfile == null ? "" : InputProfileManager.FormatProfile(profile.MatchedProfile),
                        profile.EnterMode ?? "",
                        profile.Summary
                    })
                    {
                        Tag = profile.ProfileId
                    };
                    _configuredProfilesList.Items.Add(item);
                }

                _installedProfilesList.Items.Clear();
                foreach (var profile in model.InstalledProfiles)
                {
                    _installedProfilesList.Items.Add(new ListViewItem(new[]
                    {
                        profile.DisplayName,
                        FormatList(profile.ConfiguredProfileIds)
                    }));
                }

                _workflowsList.Items.Clear();
                foreach (var workflow in model.Workflows)
                {
                    var item = new ListViewItem(new[]
                    {
                        workflow.DisplayName,
                        workflow.WorkflowId,
                        workflow.Mode,
                        workflow.CanRegister ? "ready" : "blocked",
                        FormatList(workflow.TriggerKeys),
                        FormatList(workflow.TargetProfileIds),
                        workflow.FallbackProfileId ?? "",
                        FormatList(workflow.BlockingReasons)
                    })
                    {
                        Tag = workflow.WorkflowId
                    };
                    _workflowsList.Items.Add(item);
                }

                _excludedProcessesList.Items.Clear();
                foreach (string process in model.ExcludedProcesses)
                {
                    _excludedProcessesList.Items.Add(new ListViewItem(new[] { process }) { Tag = process });
                }
            }
            finally
            {
                AutoResizeColumns(_configuredProfilesList);
                AutoResizeColumns(_installedProfilesList);
                AutoResizeColumns(_workflowsList);
                AutoResizeColumns(_excludedProcessesList);
                _configuredProfilesList.EndUpdate();
                _installedProfilesList.EndUpdate();
                _workflowsList.EndUpdate();
                _excludedProcessesList.EndUpdate();
            }
        }

        private Control CreateButtonRow()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(4),
                AutoScroll = true
            };

            var closeButton = new Button { Text = "Close", Width = 100, Height = 30 };
            closeButton.Click += (_, _) => Close();

            var copyButton = new Button { Text = "Copy Diagnostics", Width = 130, Height = 30 };
            copyButton.Click += (_, _) => _copyDiagnostics();

            var configButton = new Button { Text = "Open Config", Width = 100, Height = 30 };
            configButton.Click += (_, _) => _openConfig();

            var addProfileButton = new Button { Text = "Add Profile", Width = 100, Height = 30 };
            addProfileButton.Click += (_, _) => _addProfile();

            var editProfileButton = new Button { Text = "Edit Profile", Width = 100, Height = 30 };
            editProfileButton.Click += (_, _) => EditSelectedProfile();

            var removeProfileButton = new Button { Text = "Remove Profile", Width = 115, Height = 30 };
            removeProfileButton.Click += (_, _) => RemoveSelectedProfile();

            var addWorkflowButton = new Button { Text = "Add Workflow", Width = 110, Height = 30 };
            addWorkflowButton.Click += (_, _) => _addWorkflow();

            var editWorkflowButton = new Button { Text = "Edit Workflow", Width = 110, Height = 30 };
            editWorkflowButton.Click += (_, _) => EditSelectedWorkflow();

            var removeWorkflowButton = new Button { Text = "Remove Workflow", Width = 125, Height = 30 };
            removeWorkflowButton.Click += (_, _) => RemoveSelectedWorkflow();

            panel.Controls.Add(removeWorkflowButton);
            panel.Controls.Add(editWorkflowButton);
            panel.Controls.Add(addWorkflowButton);
            panel.Controls.Add(removeProfileButton);
            panel.Controls.Add(editProfileButton);
            panel.Controls.Add(addProfileButton);
            panel.Controls.Add(configButton);
            panel.Controls.Add(copyButton);
            panel.Controls.Add(closeButton);
            return panel;
        }

        private void EditSelectedProfile()
        {
            string? profileId = GetSelectedProfileId();
            if (profileId == null)
            {
                return;
            }

            _editProfile(profileId);
        }

        private void RemoveSelectedProfile()
        {
            string? profileId = GetSelectedProfileId();
            if (profileId == null)
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                $"Remove configured profile '{profileId}'?",
                "InputFlow",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (result == DialogResult.OK)
            {
                _removeProfile(profileId);
            }
        }

        private void EditSelectedWorkflow()
        {
            string? workflowId = GetSelectedWorkflowId();
            if (workflowId == null)
            {
                return;
            }

            _editWorkflow(workflowId);
        }

        private void RemoveSelectedWorkflow()
        {
            string? workflowId = GetSelectedWorkflowId();
            if (workflowId == null)
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                $"Remove workflow '{workflowId}'?",
                "InputFlow",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (result == DialogResult.OK)
            {
                _removeWorkflow(workflowId);
            }
        }

        private void RemoveSelectedExcludedProcess()
        {
            if (_excludedProcessesList.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Select an excluded process first.", "InputFlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string? process = _excludedProcessesList.SelectedItems[0].Tag as string;
            if (string.IsNullOrWhiteSpace(process))
            {
                MessageBox.Show(this, "The selected exclusion has no process name.", "InputFlow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var result = MessageBox.Show(
                this,
                $"Remove exclusion '{process}'?",
                "InputFlow",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (result == DialogResult.OK)
            {
                _removeExcludedProcess(process);
            }
        }

        private void HandleConfiguredProfileKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                EditSelectedProfile();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Delete)
            {
                RemoveSelectedProfile();
                e.Handled = true;
            }
        }

        private void HandleWorkflowKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                EditSelectedWorkflow();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Delete)
            {
                RemoveSelectedWorkflow();
                e.Handled = true;
            }
        }

        private void HandleExcludedProcessKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                RemoveSelectedExcludedProcess();
                e.Handled = true;
            }
        }

        private string? GetSelectedProfileId()
        {
            if (_configuredProfilesList.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Select a configured profile first.", "InputFlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            string? profileId = _configuredProfilesList.SelectedItems[0].Tag as string;
            if (!string.IsNullOrWhiteSpace(profileId))
            {
                return profileId;
            }

            MessageBox.Show(this, "The selected profile has no ID.", "InputFlow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        private string? GetSelectedWorkflowId()
        {
            if (_workflowsList.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Select a workflow first.", "InputFlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            string? workflowId = _workflowsList.SelectedItems[0].Tag as string;
            if (!string.IsNullOrWhiteSpace(workflowId))
            {
                return workflowId;
            }

            MessageBox.Show(this, "The selected workflow has no ID.", "InputFlow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        private static GroupBox CreateGroup(string title, Control content)
        {
            var group = new GroupBox
            {
                Text = title,
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };
            group.Controls.Add(content);
            return group;
        }

        private GroupBox CreateExcludedProcessesGroup()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(8, 4, 0, 0)
            };

            var addButton = new Button { Text = "Add", Width = 120, Height = 28 };
            addButton.Click += (_, _) => _addExcludedProcess();

            var removeButton = new Button { Text = "Remove", Width = 120, Height = 28 };
            removeButton.Click += (_, _) => RemoveSelectedExcludedProcess();

            buttons.Controls.Add(addButton);
            buttons.Controls.Add(removeButton);
            root.Controls.Add(_excludedProcessesList, 0, 0);
            root.Controls.Add(buttons, 1, 0);

            return CreateGroup("Excluded processes", root);
        }

        private static ListView CreateListView(params string[] columns)
        {
            var list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false
            };

            foreach (string column in columns)
            {
                list.Columns.Add(column, 140);
            }

            return list;
        }

        private static void AutoResizeColumns(ListView list)
        {
            foreach (ColumnHeader column in list.Columns)
            {
                column.Width = -2;
                if (column.Width < 110)
                {
                    column.Width = 110;
                }
            }
        }

        private static string FormatList(System.Collections.Generic.IEnumerable<string> values)
        {
            var filtered = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            return filtered.Count == 0 ? "(none)" : string.Join(", ", filtered);
        }
    }
}
