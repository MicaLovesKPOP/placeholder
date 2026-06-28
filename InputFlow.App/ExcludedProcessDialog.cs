using System;
using System.IO;
using System.Windows.Forms;

namespace InputFlow.App
{
    internal sealed class ExcludedProcessDialog : Form
    {
        private readonly TextBox _processTextBox;
        private readonly Label _errorLabel;

        public ExcludedProcessDialog()
        {
            Text = "Add Excluded Process";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new System.Drawing.Size(420, 150);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                Padding = new Padding(12)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            _processTextBox = new TextBox { Dock = DockStyle.Fill, AccessibleName = "Process name", TabIndex = 0 };
            _errorLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = System.Drawing.Color.Firebrick,
                AutoSize = false
            };

            root.Controls.Add(CreateLabel("Process"), 0, 0);
            root.Controls.Add(_processTextBox, 1, 0);
            root.Controls.Add(_errorLabel, 0, 1);
            root.SetColumnSpan(_errorLabel, 2);
            var buttonRow = CreateButtonRow();
            root.Controls.Add(buttonRow, 0, 3);
            root.SetColumnSpan(buttonRow, 2);

            Controls.Add(root);
        }

        public string ProcessName { get; private set; } = string.Empty;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _processTextBox.Focus();
        }

        private Control CreateButtonRow()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };

            var saveButton = new Button { Text = "Save", Width = 100, Height = 30, TabIndex = 1 };
            saveButton.Click += (_, _) => Save();

            var cancelButton = new Button { Text = "Cancel", Width = 100, Height = 30, TabIndex = 2 };
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
            string process = NormalizeProcessName(_processTextBox.Text);
            if (string.IsNullOrWhiteSpace(process))
            {
                _errorLabel.Text = "Process name is required.";
                return;
            }

            ProcessName = process;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static string NormalizeProcessName(string value)
        {
            string process = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(process))
            {
                return string.Empty;
            }

            process = Path.GetFileName(process);
            if (string.IsNullOrWhiteSpace(process))
            {
                return string.Empty;
            }

            if (!process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                process += ".exe";
            }

            return process;
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
    }
}
