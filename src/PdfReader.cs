using System;
using System.Drawing;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SimplePdf
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [STAThread]
        static void Main(string[] args)
        {
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ReaderWindow(args));
        }
    }

    sealed class ReaderWindow : Form
    {
        WebView2 viewer;
        readonly Panel viewerHost = new Panel();
        readonly ComboBox languageSelector = new ComboBox();
        Dictionary<string, string> translations;
        string language;
        bool changingLanguage;
        readonly string settingsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Simple-PDF");
        readonly Label fileLabel = new Label();
        readonly Label status = new Label();
        readonly Button openButton;
        readonly Button printButton;
        readonly Button fullScreenButton;
        bool isFullScreen;
        Rectangle windowBoundsBeforeFullScreen;
        FormWindowState windowStateBeforeFullScreen;
        FormBorderStyle borderStyleBeforeFullScreen;
        readonly string[] arguments;
        readonly bool testing;
        string currentFile;
        string expectedUri;
        string welcomeUri;
        bool ready;
        bool loaded;
        bool testRunning;
        bool welcomeTested;

        public ReaderWindow(string[] args)
        {
            arguments = args;
            testing = args.Length >= 3 && args[0] == "--self-test";
            language = ReadLanguage(args);
            LoadTranslations();
            Text = T("appTitle");
            Size = new Size(1180, 850);
            MinimumSize = new Size(700, 520);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10F);
            BackColor = Color.FromArgb(245, 247, 248);
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (testing)
            {
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Location = new Point(-16000, -16000);
                Opacity = 0;
            }

            var toolbar = new TableLayoutPanel {
                Dock = DockStyle.Top, Height = 76, BackColor = Color.White,
                ColumnCount = 6, RowCount = 1, Padding = new Padding(22, 15, 22, 15)
            };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
            var logo = new Label {
                Text = "PDF", ForeColor = Color.White, BackColor = Color.FromArgb(24, 110, 92),
                Font = new Font("Segoe UI", 11, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0), AccessibleName = "Simple-PDF"
            };
            fileLabel.Text = T("appLabel");
            fileLabel.AutoEllipsis = true;
            fileLabel.Dock = DockStyle.Fill;
            fileLabel.TextAlign = ContentAlignment.MiddleLeft;
            fileLabel.ForeColor = Color.FromArgb(37, 47, 48);
            openButton = MakeButton(T("open"), Color.FromArgb(24, 110, 92), Color.White);
            printButton = MakeButton(T("print"), Color.FromArgb(237, 241, 240), Color.FromArgb(37, 47, 48));
            fullScreenButton = MakeButton(T("enterFullScreen"), Color.FromArgb(237, 241, 240), Color.FromArgb(37, 47, 48));
            openButton.Enabled = false;
            printButton.Enabled = false;
            fullScreenButton.Enabled = false;
            openButton.AccessibleName = T("openAccessible");
            printButton.AccessibleName = T("printAccessible");
            openButton.Click += delegate { ChooseFile(); };
            printButton.Click += delegate { ShowPrint(); };
            fullScreenButton.Click += delegate { ToggleFullScreen(); };
            fullScreenButton.AccessibleName = T("enterFullScreen");
            toolbar.Controls.Add(logo, 0, 0);
            toolbar.Controls.Add(fileLabel, 1, 0);
            languageSelector.DropDownStyle = ComboBoxStyle.DropDownList;
            languageSelector.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            languageSelector.Items.Add(T("languageEnglish"));
            languageSelector.Items.Add(T("languageChinese"));
            languageSelector.SelectedIndex = language == "en" ? 0 : 1;
            languageSelector.AccessibleName = T("languageAccessible");
            languageSelector.Enabled = false;
            languageSelector.SelectedIndexChanged += async delegate { await SwitchLanguage(); };
            toolbar.Controls.Add(languageSelector, 2, 0);
            toolbar.Controls.Add(openButton, 3, 0);
            toolbar.Controls.Add(printButton, 4, 0);
            toolbar.Controls.Add(fullScreenButton, 5, 0);

            status.Dock = DockStyle.Bottom;
            status.Height = 34;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.Padding = new Padding(22, 0, 12, 0);
            status.ForeColor = Color.FromArgb(98, 111, 113);
            status.Font = new Font("Segoe UI", 9F);
            status.Text = T("preparing");
            viewerHost.Dock = DockStyle.Fill;
            Controls.Add(viewerHost);
            Controls.Add(status);
            Controls.Add(toolbar);
            Shown += async delegate { await InitializeViewer(); };
        }

        protected override bool ShowWithoutActivation { get { return testing; } }

        string T(string key) { return translations[key]; }

        string ReadLanguage(string[] args)
        {
            foreach (string arg in args)
                if (arg == "--language=en" || arg == "--language=zh-CN") return arg.Substring(11);
            if (testing) return "en";
            try
            {
                string saved = File.ReadAllText(Path.Combine(settingsFolder, "language.txt")).Trim();
                if (saved == "zh-CN") return saved;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return "en";
        }

        [DllImport("kernel32.dll")] static extern ushort SetThreadUILanguage(ushort languageId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool SetThreadPreferredUILanguages(uint flags, string languages, out uint count);
        void LoadTranslations()
        {
            using (var stream = typeof(Program).Assembly.GetManifestResourceStream("strings." + language + ".json"))
            using (var reader = new StreamReader(stream))
                translations = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
            string cultureName = language == "en" ? "en-US" : "zh-CN";
            System.Threading.Thread.CurrentThread.CurrentUICulture = new CultureInfo(cultureName);
            uint count;
            SetThreadPreferredUILanguages(8, cultureName + "\0\0", out count);
            SetThreadUILanguage((ushort)(language == "en" ? 1033 : 2052));
        }

        async Task SwitchLanguage()
        {
            if (changingLanguage) return;
            string selected = languageSelector.SelectedIndex == 0 ? "en" : "zh-CN";
            if (selected == language) return;
            changingLanguage = true;
            ExitFullScreen();
            language = selected;
            LoadTranslations();
            Text = currentFile == null ? T("appTitle") : Path.GetFileName(currentFile) + " - Simple-PDF";
            if (currentFile == null) fileLabel.Text = T("appLabel");
            openButton.Text = T("open");
            printButton.Text = T("print");
            openButton.AccessibleName = T("openAccessible");
            printButton.AccessibleName = T("printAccessible");
            languageSelector.AccessibleName = T("languageAccessible");
            fullScreenButton.Text = T("enterFullScreen");
            fullScreenButton.AccessibleName = T("enterFullScreen");
            try
            {
                if (!testing)
                {
                    Directory.CreateDirectory(settingsFolder);
                    File.WriteAllText(Path.Combine(settingsFolder, "language.txt"), language);
                }
            }
            catch (IOException) { MessageBox.Show(this, T("settingsFailed"), "Simple-PDF"); }
            catch (UnauthorizedAccessException) { MessageBox.Show(this, T("settingsFailed"), "Simple-PDF"); }
            try { await InitializeViewer(); }
            finally { changingLanguage = false; }
        }

        static Button MakeButton(string text, Color background, Color foreground)
        {
            var button = new Button {
                Text = text, BackColor = background, ForeColor = foreground,
                Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
                Margin = new Padding(8, 0, 0, 0), Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        void ToggleFullScreen()
        {
            if (isFullScreen) { ExitFullScreen(); return; }
            if (!ready || !loaded) return;
            windowBoundsBeforeFullScreen = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            windowStateBeforeFullScreen = WindowState;
            borderStyleBeforeFullScreen = FormBorderStyle;
            Rectangle screenBounds = Screen.FromControl(this).Bounds;
            SuspendLayout();
            isFullScreen = true;
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = screenBounds;
            // Keep the native toolbar visible so the exit control cannot be hidden by the PDF renderer.
            fullScreenButton.Text = T("exitFullScreen");
            fullScreenButton.AccessibleName = T("exitFullScreen");
            status.Text = T("fullScreenHint");
            ResumeLayout(true);
        }

        void ExitFullScreen()
        {
            if (!isFullScreen) return;
            SuspendLayout();
            isFullScreen = false;
            WindowState = FormWindowState.Normal;
            FormBorderStyle = borderStyleBeforeFullScreen;
            Bounds = windowBoundsBeforeFullScreen;
            WindowState = windowStateBeforeFullScreen;
            fullScreenButton.Text = T("enterFullScreen");
            fullScreenButton.AccessibleName = T("enterFullScreen");
            status.Text = loaded ? T("documentReady") : T("ready");
            ResumeLayout(true);
        }

        bool HandleReaderShortcut(Keys keyData)
        {
            if (keyData == Keys.Escape && isFullScreen) { ExitFullScreen(); return true; }
            if (keyData == Keys.F11 && (isFullScreen || (ready && loaded))) { ToggleFullScreen(); return true; }
            if (keyData == (Keys.Control | Keys.O)) { BeginInvoke(new Action(ChooseFile)); return true; }
            if (keyData == (Keys.Control | Keys.P)) { BeginInvoke(new Action(ShowPrint)); return true; }
            return false;
        }

        void ViewerKeyDown(object sender, KeyEventArgs e)
        {
            Keys keys = e.KeyData;
            bool handled = (keys == Keys.Escape && isFullScreen)
                || (keys == Keys.F11 && (isFullScreen || (ready && loaded)))
                || keys == (Keys.Control | Keys.O) || keys == (Keys.Control | Keys.P);
            if (!handled) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            // Resize the host after the synchronous WebView2 keyboard callback has returned.
            BeginInvoke(new Action(delegate { HandleReaderShortcut(keys); }));
        }

        async Task InitializeViewer()
        {
            try
            {
                ready = false;
                loaded = false;
                ExitFullScreen();
                openButton.Enabled = false;
                printButton.Enabled = false;
                fullScreenButton.Enabled = false;
                languageSelector.Enabled = false;
                status.Text = T("preparing");
                if (viewer != null) { viewerHost.Controls.Remove(viewer); viewer.Dispose(); }
                viewer = new WebView2();
                viewer.Dock = DockStyle.Fill;
                viewer.AllowExternalDrop = false;
                viewer.DefaultBackgroundColor = Color.FromArgb(245, 247, 248);
                viewerHost.Controls.Add(viewer);
                string dataFolder = testing ? Path.Combine(arguments[2], "Profile-" + language) : Path.Combine(settingsFolder, "Profile-" + language);
                var options = new CoreWebView2EnvironmentOptions();
                options.Language = language == "en" ? "en-US" : "zh-CN";
                var environment = await CoreWebView2Environment.CreateAsync(null, dataFolder, options);
                await viewer.EnsureCoreWebView2Async(environment);
                var settings = viewer.CoreWebView2.Settings;
                settings.AreDevToolsEnabled = false;
                settings.AreDefaultContextMenusEnabled = false;
                settings.IsStatusBarEnabled = false;
                settings.IsGeneralAutofillEnabled = false;
                settings.IsPasswordAutosaveEnabled = false;
                settings.HiddenPdfToolbarItems = CoreWebView2PdfToolbarItems.Save | CoreWebView2PdfToolbarItems.SaveAs | CoreWebView2PdfToolbarItems.MoreSettings | CoreWebView2PdfToolbarItems.FullScreen;
                viewer.CoreWebView2.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e) {
                    if (e.Uri != "about:blank" && e.Uri != welcomeUri && !String.Equals(e.Uri.Split('#')[0], expectedUri, StringComparison.OrdinalIgnoreCase))
                        e.Cancel = true;
                };
                viewer.CoreWebView2.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
                viewer.CoreWebView2.DownloadStarting += delegate(object sender, CoreWebView2DownloadStartingEventArgs e) { e.Cancel = true; };
                viewer.CoreWebView2.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Deny; };
                viewer.CoreWebView2.WebMessageReceived += delegate(object sender, CoreWebView2WebMessageReceivedEventArgs e) {
                    if (currentFile == null && (e.Source == "about:blank" || e.Source == welcomeUri) && e.TryGetWebMessageAsString() == "open") ChooseFile();
                };
                viewer.CoreWebView2.NavigationCompleted += NavigationCompleted;
                viewer.CoreWebView2.ProcessFailed += delegate {
                    loaded = false;
                    ExitFullScreen();
                    printButton.Enabled = false;
                    fullScreenButton.Enabled = false;
                    status.Text = T("processFailed");
                };
                viewer.KeyDown += ViewerKeyDown;
                ready = true;
                openButton.Enabled = true;
                languageSelector.Enabled = true;
                status.Text = T("ready");
                if (testing && !welcomeTested)
                {
                    welcomeTested = true;
                    Directory.CreateDirectory(arguments[2]);
                    ShowWelcome();
                    await Task.Delay(2500);
                    string welcomeText = await viewer.CoreWebView2.ExecuteScriptAsync("document.body ? document.body.innerText : ''");
                    File.WriteAllText(Path.Combine(arguments[2], "welcome-text.json"), welcomeText);
                    if (!welcomeText.Contains("Simple-PDF")) throw new Exception("Welcome screen did not load.");
                    using (var welcome = File.Create(Path.Combine(arguments[2], "welcome.png")))
                        await viewer.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, welcome);
                    OpenPdf(arguments[1]);
                }
                else if (currentFile != null) OpenPdf(currentFile);
                else if (arguments.Length > 0 && !arguments[0].StartsWith("--")) OpenPdf(arguments[0]);
                else ShowWelcome();
            }
            catch (Exception ex)
            {
                languageSelector.Enabled = true;
                status.Text = T("startFailed");
                if (testing) FinishTest("FAIL: " + ex);
                else MessageBox.Show(this, T("runtimeError") + ex.Message, T("startFailedTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        string ReadWelcome()
        {
            using (var stream = typeof(Program).Assembly.GetManifestResourceStream("welcome." + language + ".html"))
            using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
        }

        void ShowWelcome()
        {
            string html = ReadWelcome();
            welcomeUri = "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(html));
            viewer.NavigateToString(html);
        }

        void ChooseFile()
        {
            if (!ready) return;
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = T("dialogTitle");
                dialog.Filter = T("pdfFilter");
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                var downloads = GetDownloadsFolder();
                dialog.InitialDirectory = currentFile != null ? Path.GetDirectoryName(currentFile) : downloads;
                if (dialog.ShowDialog(this) == DialogResult.OK) OpenPdf(dialog.FileName);
            }
        }

        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
        static string GetDownloadsFolder()
        {
            IntPtr path;
            if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out path) == 0)
            {
                try { return Marshal.PtrToStringUni(path); }
                finally { Marshal.FreeCoTaskMem(path); }
            }
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        internal string ValidatePdf(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath)) throw new FileNotFoundException(T("missingFile"), fullPath);
            if (!String.Equals(Path.GetExtension(fullPath), ".pdf", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(T("wrongExtension"));
            using (var stream = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] header = new byte[Math.Min(1024, stream.Length)];
                stream.Read(header, 0, header.Length);
                if (!System.Text.Encoding.ASCII.GetString(header).Contains("%PDF-"))
                    throw new InvalidDataException(T("invalidFile"));
            }
            return fullPath;
        }

        void OpenPdf(string path)
        {
            try
            {
                string fullPath = ValidatePdf(path);
                currentFile = fullPath;
                loaded = false;
                printButton.Enabled = false;
                fullScreenButton.Enabled = isFullScreen;
                fileLabel.Text = Path.GetFileName(fullPath);
                Text = Path.GetFileName(fullPath) + " - Simple-PDF";
                status.Text = T("loading");
                expectedUri = new Uri(fullPath).AbsoluteUri;
                viewer.CoreWebView2.Navigate(expectedUri);
            }
            catch (Exception ex)
            {
                if (testing) FinishTest("FAIL: " + ex);
                else MessageBox.Show(this, ex.Message, T("openFailed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        async void NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (currentFile == null || !viewer.CoreWebView2.Source.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return;
            loaded = e.IsSuccess;
            printButton.Enabled = loaded;
            fullScreenButton.Enabled = loaded || isFullScreen;
            status.Text = loaded ? T(isFullScreen ? "fullScreenHint" : "documentReady") : T("navigationFailed");
            if (!testing || testRunning) return;
            testRunning = true;
            try
            {
                if (!loaded) throw new Exception("Navigation failed: " + e.WebErrorStatus);
                // Let the embedded PDF renderer paint before checking its output.
                await Task.Delay(3000);
                Directory.CreateDirectory(arguments[2]);
                using (var image = File.Create(Path.Combine(arguments[2], "pdf-render-" + language + ".png")))
                    await viewer.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
                string frames = await viewer.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.getFrameTree", "{}");
                File.WriteAllText(Path.Combine(arguments[2], "frames.json"), frames);
                File.WriteAllText(Path.Combine(arguments[2], "dom.json"), await viewer.CoreWebView2.CallDevToolsProtocolMethodAsync("DOM.getDocument", "{\"depth\":-1,\"pierce\":true}"));
                CheckRejectedFiles();
                await CheckFullScreen();
                if (Array.IndexOf(arguments, "--test-switch") >= 0 && language == "en")
                {
                    File.WriteAllText(Path.Combine(arguments[2], "before-switch.txt"), currentFile);
                    testRunning = false;
                    BeginInvoke(new Action(delegate { languageSelector.SelectedIndex = 1; }));
                    return;
                }
                if (Array.IndexOf(arguments, "--test-switch") >= 0)
                {
                    if (language != "zh-CN" || currentFile != File.ReadAllText(Path.Combine(arguments[2], "before-switch.txt")))
                        throw new Exception("Language switching did not preserve the open document.");
                    if (openButton.Text != T("open") || printButton.Text != T("print"))
                        throw new Exception("Toolbar strings were not updated.");
                }
                viewer.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
                await Task.Delay(2000);
                using (var preview = File.Create(Path.Combine(arguments[2], "print-preview.png")))
                    await viewer.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, preview);
                FinishTest("PASS [" + language + "]: welcome screen, PDF navigation, fullscreen button/Esc/F11 and window restoration, invalid/missing file validation, and print-preview API. Runtime " + viewer.CoreWebView2.Environment.BrowserVersionString);
            }
            catch (Exception ex) { FinishTest("FAIL: " + ex); }
        }

        void CheckRejectedFiles()
        {
            string invalid = Path.Combine(arguments[2], "invalid.pdf");
            File.WriteAllText(invalid, "not a PDF");
            bool rejected = false;
            try { ValidatePdf(invalid); } catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Invalid file was not rejected.");
            rejected = false;
            try { ValidatePdf(Path.Combine(arguments[2], "missing.pdf")); } catch (FileNotFoundException) { rejected = true; }
            if (!rejected) throw new Exception("Missing file was not rejected.");
        }

        async Task CheckFullScreen()
        {
            Rectangle originalBounds = Bounds;
            FormBorderStyle originalBorder = FormBorderStyle;
            string originalSource = viewer.CoreWebView2.Source;
            if ((viewer.CoreWebView2.Settings.HiddenPdfToolbarItems & CoreWebView2PdfToolbarItems.FullScreen) == 0)
                throw new Exception("The renderer's fullscreen control was not replaced.");
            fullScreenButton.PerformClick();
            if (!isFullScreen || FormBorderStyle != FormBorderStyle.None || !fullScreenButton.Visible || fullScreenButton.Text != T("exitFullScreen"))
                throw new Exception("Fullscreen entry did not retain a visible exit button.");
            using (var toolbarImage = new Bitmap(fullScreenButton.Parent.Width, fullScreenButton.Parent.Height))
            {
                fullScreenButton.Parent.DrawToBitmap(toolbarImage, fullScreenButton.Parent.ClientRectangle);
                toolbarImage.Save(Path.Combine(arguments[2], "fullscreen-toolbar-" + language + ".png"));
            }
            fullScreenButton.PerformClick();
            if (isFullScreen || Bounds != originalBounds || FormBorderStyle != originalBorder)
                throw new Exception("The exit button did not restore the normal window.");
            fullScreenButton.PerformClick();
            Message escape = new Message();
            if (!ProcessCmdKey(ref escape, Keys.Escape) || isFullScreen || Bounds != originalBounds)
                throw new Exception("Escape did not exit fullscreen and restore the window.");
            Message toggle = new Message();
            ProcessCmdKey(ref toggle, Keys.F11);
            if (!isFullScreen) throw new Exception("F11 did not enter fullscreen.");
            ProcessCmdKey(ref toggle, Keys.F11);
            if (isFullScreen) throw new Exception("F11 did not exit fullscreen.");
            fullScreenButton.PerformClick();
            var viewerEscape = new KeyEventArgs(Keys.Escape);
            ViewerKeyDown(viewer, viewerEscape);
            await Task.Delay(150);
            if (!viewerEscape.Handled || !viewerEscape.SuppressKeyPress || isFullScreen)
                throw new Exception("Escape from the focused PDF did not exit fullscreen.");
            ViewerKeyDown(viewer, new KeyEventArgs(Keys.F11));
            await Task.Delay(150);
            if (!isFullScreen) throw new Exception("F11 from the focused PDF did not enter fullscreen.");
            ViewerKeyDown(viewer, new KeyEventArgs(Keys.F11));
            await Task.Delay(150);
            if (isFullScreen) throw new Exception("F11 from the focused PDF did not exit fullscreen.");
            WindowState = FormWindowState.Maximized;
            fullScreenButton.PerformClick();
            fullScreenButton.PerformClick();
            if (isFullScreen || WindowState != FormWindowState.Maximized)
                throw new Exception("Fullscreen exit did not restore the maximized window.");
            WindowState = FormWindowState.Normal;
            Bounds = originalBounds;
            if (viewer.CoreWebView2.Source != originalSource || !loaded || !printButton.Enabled)
                throw new Exception("Fullscreen switching interrupted the open document.");
        }

        void FinishTest(string result)
        {
            Directory.CreateDirectory(arguments[2]);
            File.WriteAllText(Path.Combine(arguments[2], "result.txt"), result);
            Environment.ExitCode = result.StartsWith("PASS") ? 0 : 1;
            Close();
        }

        void ShowPrint()
        {
            if (!ready || !loaded) return;
            try { viewer.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); }
            catch (Exception ex) { MessageBox.Show(this, T("printFailed") + ex.Message, T("print"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (HandleReaderShortcut(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
