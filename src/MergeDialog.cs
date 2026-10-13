using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SimplePdf
{
    sealed class MergeDialog : Form
    {
        readonly Func<string, string> translate;
        readonly string initialDirectory;
        readonly List<string> files = new List<string>();
        readonly ListBox fileList = new ListBox();
        readonly Button addButton, removeButton, upButton, downButton, mergeButton, cancelButton;
        readonly Label progressLabel = new Label();
        readonly CheckBox openResult = new CheckBox();
        CancellationTokenSource cancellation;
        bool busy;
        bool closeRequested;
        bool previewing;
        public string MergedPath { get; private set; }
        public bool OpenMergedFile { get { return openResult.Checked; } }
        protected override bool ShowWithoutActivation { get { return previewing; } }

        public MergeDialog(Func<string, string> translator, string downloadsDirectory, string currentFile)
        {
            translate = translator;
            initialDirectory = downloadsDirectory;
            Text = T("mergeTitle");
            ClientSize = new Size(840, 480);
            MinimumSize = new Size(720, 450);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10F);
            BackColor = Color.FromArgb(245, 247, 248);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), RowCount = 6, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.Controls.Add(new Label { Text = T("mergeDescription"), Dock = DockStyle.Fill, ForeColor = Color.FromArgb(70, 87, 80) }, 0, 0);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            addButton = MakeButton(T("mergeAdd"), 130);
            removeButton = MakeButton(T("mergeRemove"), 110);
            upButton = MakeButton(T("mergeUp"), 110);
            downButton = MakeButton(T("mergeDown"), 130);
            actions.Controls.AddRange(new Control[] { addButton, removeButton, upButton, downButton });
            layout.Controls.Add(actions, 0, 1);
            fileList.Dock = DockStyle.Fill;
            fileList.HorizontalScrollbar = true;
            fileList.IntegralHeight = false;
            fileList.AccessibleName = T("mergeList");
            fileList.SelectedIndexChanged += delegate { RefreshButtons(); };
            layout.Controls.Add(fileList, 0, 2);
            progressLabel.Dock = DockStyle.Fill;
            progressLabel.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(progressLabel, 0, 3);
            openResult.Text = T("mergeOpenResult");
            openResult.Checked = true;
            openResult.Dock = DockStyle.Fill;
            layout.Controls.Add(openResult, 0, 4);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            cancelButton = MakeButton(T("mergeCancel"), 110);
            mergeButton = MakeButton(T("mergeSave"), 165);
            mergeButton.BackColor = Color.FromArgb(24, 110, 92);
            mergeButton.ForeColor = Color.White;
            footer.Controls.AddRange(new Control[] { cancelButton, mergeButton });
            layout.Controls.Add(footer, 0, 5);
            Controls.Add(layout);
            addButton.Click += delegate { AddFiles(); };
            removeButton.Click += delegate { int selected = fileList.SelectedIndex; if (selected >= 0) { files.RemoveAt(selected); RefreshFiles(Math.Min(selected, files.Count - 1)); } };
            upButton.Click += delegate { MoveSelected(-1); };
            downButton.Click += delegate { MoveSelected(1); };
            mergeButton.Click += async delegate { await MergeAndSave(); };
            cancelButton.Click += delegate { if (busy) RequestCancel(); else Close(); };
            AcceptButton = mergeButton;
            CancelButton = cancelButton;
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { e.Cancel = true; RequestCancel(); } };
            if (currentFile != null && File.Exists(currentFile)) files.Add(currentFile);
            RefreshFiles(0);
        }

        string T(string key) { return translate(key); }
        static Button MakeButton(string text, int width)
        {
            var button = new Button { Text = text, Width = width, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(0, 0, 8, 0), Cursor = Cursors.Hand };
            button.FlatAppearance.BorderColor = Color.FromArgb(214, 226, 220);
            return button;
        }
        void RefreshFiles(int selected)
        {
            fileList.Items.Clear();
            for (int index = 0; index < files.Count; index++)
                fileList.Items.Add((index + 1) + ". " + Path.GetFileName(files[index]) + "   -   " + Path.GetDirectoryName(files[index]));
            if (selected >= 0 && selected < files.Count) fileList.SelectedIndex = selected;
            progressLabel.Text = String.Format(T("mergeCount"), files.Count);
            RefreshButtons();
        }
        void RefreshButtons()
        {
            int selected = fileList.SelectedIndex;
            addButton.Enabled = !busy;
            removeButton.Enabled = !busy && selected >= 0;
            upButton.Enabled = !busy && selected > 0;
            downButton.Enabled = !busy && selected >= 0 && selected < files.Count - 1;
            mergeButton.Enabled = !busy && files.Count >= 2;
            fileList.Enabled = !busy;
            openResult.Enabled = !busy;
        }
        void AddFiles()
        {
            using (var picker = new OpenFileDialog { Title = T("mergeAdd"), Filter = T("pdfFilter"), Multiselect = true, CheckFileExists = true, InitialDirectory = initialDirectory })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                foreach (string file in picker.FileNames)
                    if (!files.Exists(delegate(string item) { return String.Equals(item, file, StringComparison.OrdinalIgnoreCase); })) files.Add(file);
                RefreshFiles(files.Count - 1);
            }
        }
        void MoveSelected(int direction)
        {
            int selected = fileList.SelectedIndex;
            int target = selected + direction;
            if (selected < 0 || target < 0 || target >= files.Count) return;
            string file = files[selected];
            files.RemoveAt(selected);
            files.Insert(target, file);
            RefreshFiles(target);
        }

        internal void CheckLayoutAndOrdering(string directory)
        {
            string first = files[0];
            string second = Path.Combine(directory, "second-paper.pdf"), third = Path.Combine(directory, "third-paper.pdf");
            File.Copy(first, second, true);
            File.Copy(first, third, true);
            files.Add(second);
            files.Add(third);
            RefreshFiles(2);
            MoveSelected(-1);
            if (files[1] != third || fileList.SelectedIndex != 1) throw new Exception("Move up did not reorder the merge list.");
            MoveSelected(1);
            if (files[2] != third || fileList.SelectedIndex != 2) throw new Exception("Move down did not reorder the merge list.");
            files.RemoveAt(2);
            RefreshFiles(1);
            if (!mergeButton.Enabled || !upButton.Enabled || downButton.Enabled) throw new Exception("Merge list buttons have incorrect states.");
            previewing = true;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-16000, -16000);
            Opacity = 0;
            Show();
            PerformLayout();
            using (var image = new Bitmap(Width, Height))
            {
                DrawToBitmap(image, new Rectangle(0, 0, Width, Height));
                image.Save(Path.Combine(directory, "merge-dialog.png"));
            }
            Close();
        }
        void RequestCancel()
        {
            closeRequested = true;
            cancellation.Cancel();
            progressLabel.Text = T("mergeCanceling");
            cancelButton.Enabled = false;
        }
        async Task MergeAndSave()
        {
            string destination;
            using (var picker = new SaveFileDialog { Title = T("mergeSave"), Filter = T("pdfFilter"), FileName = "merged.pdf", DefaultExt = "pdf", AddExtension = true, OverwritePrompt = false, InitialDirectory = initialDirectory })
            {
                while (true)
                {
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    if (!File.Exists(picker.FileName)) { destination = picker.FileName; break; }
                    MessageBox.Show(this, T("mergeOutputExists"), T("mergeTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            busy = true;
            cancellation = new CancellationTokenSource();
            RefreshButtons();
            var progress = new Progress<int>(delegate(int count) { if (busy && !closeRequested) progressLabel.Text = String.Format(T("mergeProgress"), count, files.Count); });
            progressLabel.Text = String.Format(T("mergeProgress"), 0, files.Count);
            try
            {
                var inputs = files.ToArray();
                var result = await Task.Run(delegate { return PdfMergeService.Merge(inputs, destination, cancellation.Token, progress); });
                MergedPath = result.Path;
                MessageBox.Show(this, String.Format(T("mergeSuccess"), result.PageCount, files.Count), T("mergeTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException) { }
            catch (MergeException ex) { MessageBox.Show(this, String.Format(T(ex.MessageKey), ex.FileName), T("mergeFailed"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            catch (Exception ex) { MessageBox.Show(this, T("mergeSaveFailed") + "\n\n" + ex.Message, T("mergeFailed"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally
            {
                busy = false;
                cancellation.Dispose();
                cancelButton.Enabled = true;
                RefreshFiles(fileList.SelectedIndex);
                if (MergedPath != null) { DialogResult = DialogResult.OK; Close(); }
                else if (closeRequested) Close();
            }
        }
    }
}
