using System;
using System.Linq;
using System.Windows.Forms;
using InputFlow.Core;

namespace InputFlow.App
{
    internal sealed class SetupStatusForm : Form
    {
        private readonly ListView _configuredProfilesList;
        private readonly ListView _installedProfilesList;
        private readonly ListView _workflowsList;
        private readonly Action _copyDiagnostics;
        private readonly Action _openConfig;
        private readonly Action _addWorkflow;
        private readonly Action<string> _removeWorkflow;

        public SetupStatusForm(Action copyDiagnostics, Action openConfig, Action addWorkflow, Action<string> removeWorkflow)
        {
            _copyDiagnostics = copyDiagnostics;
            _openConfig = openConfig;
            _addWorkflow = addWorkflow;
            _removeWorkflow = removeWorkflow;

            Text = "InputFlow Setup Status";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new System.Drawing.Size(860, 560);
            Size = new System.Drawing.Size(980, 680);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 31));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 31));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            _configuredProfilesList = CreateListView("Profile ID", "Health", "Matched profile", "Enter mode", "Summary");
            _installedProfilesList = CreateListView("Installed profile", "Configured as");
            _workflowsList = CreateListView("Workflow", "ID", "Mode", "Status", "Triggers", "Targets", "Fallback", "Blocking reasons");

            root.Controls.Add(CreateGroup("Configured profiles", _configuredProfilesList), 0, 0);
            root.Controls.Add(CreateGroup("Installed profile options", _installedProfilesList), 0, 1);
            root.Controls.Add(CreateGroup("Workflow readiness", _workflowsList), 0, 2);
            root.Controls.Add(CreateButtonRow(), 0, 3);

            Controls.Add(root);
        }

        public void RefreshModel(InputFlowSetupModel model)
        {
            _configuredProfilesList.BeginUpdate();
            _installedProfilesList.BeginUpdate();
            _workflowsList.BeginUpdate();
            try
            {
                _configuredProfilesList.Items.Clear();
                foreach (var profile in model.ConfiguredProfiles)
                {
                    _configuredProfilesList.Items.Add(new ListViewItem(new[]
                    {
                        profile.ProfileId,
                        profile.Health.ToString().ToLowerInvariant(),
                        profile.MatchedProfile == null ? "" : InputProfileManager.FormatProfile(profile.MatchedProfile),
                        profile.EnterMode ?? "",
                        profile.Summary
                    }));
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
            }
            finally
            {
                AutoResizeColumns(_configuredProfilesList);
                AutoResizeColumns(_installedProfilesList);
                AutoResizeColumns(_workflowsList);
                _configuredProfilesList.EndUpdate();
                _installedProfilesList.EndUpdate();
                _workflowsList.EndUpdate();
            }
        }

        private Control CreateButtonRow()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };

            var closeButton = new Button { Text = "Close", Width = 110, Height = 30 };
            closeButton.Click += (_, _) => Close();

            var copyButton = new Button { Text = "Copy Diagnostics", Width = 140, Height = 30 };
            copyButton.Click += (_, _) => _copyDiagnostics();

            var configButton = new Button { Text = "Open Config", Width = 110, Height = 30 };
            configButton.Click += (_, _) => _openConfig();

            var addWorkflowButton = new Button { Text = "Add Workflow", Width = 120, Height = 30 };
            addWorkflowButton.Click += (_, _) => _addWorkflow();

            var removeWorkflowButton = new Button { Text = "Remove Workflow", Width = 130, Height = 30 };
            removeWorkflowButton.Click += (_, _) => RemoveSelectedWorkflow();

            panel.Controls.Add(closeButton);
            panel.Controls.Add(copyButton);
            panel.Controls.Add(configButton);
            panel.Controls.Add(addWorkflowButton);
            panel.Controls.Add(removeWorkflowButton);
            return panel;
        }

        private void RemoveSelectedWorkflow()
        {
            if (_workflowsList.SelectedItems.Count == 0)
            {
                MessageBox.Show(this, "Select a workflow first.", "InputFlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string? workflowId = _workflowsList.SelectedItems[0].Tag as string;
            if (string.IsNullOrWhiteSpace(workflowId))
            {
                MessageBox.Show(this, "The selected workflow cannot be removed because it has no ID.", "InputFlow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
