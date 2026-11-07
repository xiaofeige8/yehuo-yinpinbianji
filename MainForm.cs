using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace WildfireAudioEditor
{
    public static class Design
    {
        public static readonly Color Background = Color.FromArgb(245, 246, 248);
        public static readonly Color Text = Color.FromArgb(37, 43, 54);
        public static readonly Color Muted = Color.FromArgb(107, 115, 129);
        public static readonly Color Accent = Color.FromArgb(231, 91, 45);
        public static readonly Color Line = Color.FromArgb(226, 230, 236);

        public static Button Button(string text, bool primary)
        {
            Button button = new Button { Text = text, Height = 34, AutoSize = true, MinimumSize = new Size(84, 34), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Padding = new Padding(9, 0, 9, 0), BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Text, Margin = new Padding(0, 0, 8, 0) };
            button.FlatAppearance.BorderColor = primary ? Accent : Line;
            return button;
        }

        public static Label Label(string text, float size, bool bold, Color color)
        {
            return new Label { Text = text, AutoSize = true, Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color, BackColor = Color.Transparent };
        }
    }

    public sealed class ParameterCard : Panel
    {
        private readonly NumericUpDown number;
        private readonly TrackBar slider;
        private readonly Label title;
        private readonly Label description;
        private readonly Label hint;
        private readonly Label unit;
        private readonly decimal original;
        private bool updating;
        public event EventHandler ValueChanged;

        public decimal Value { get { return number.Value; } set { number.Value = Math.Max(number.Minimum, Math.Min(number.Maximum, value)); } }

        public ParameterCard(string heading, string explanation, string suggestion, string unitText, decimal minimum, decimal maximum, decimal initial)
        {
            original = initial;
            Height = 92;
            Dock = DockStyle.Top;
            BackColor = Color.White;
            Padding = new Padding(16);
            title = Design.Label(heading, 10, true, Design.Text);
            description = Design.Label(explanation, 8.5f, false, Design.Muted);
            hint = Design.Label(suggestion, 8.5f, false, Design.Muted);
            hint.AutoSize = false;
            number = new NumericUpDown { Minimum = minimum, Maximum = maximum, Value = initial, DecimalPlaces = 2, Increment = 0.01m, TextAlign = HorizontalAlignment.Center, Font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold), Width = 105, BorderStyle = BorderStyle.FixedSingle, ThousandsSeparator = false };
            unit = Design.Label(unitText, 9, false, Design.Muted);
            slider = new TrackBar { Minimum = (int)(minimum * 100), Maximum = (int)(maximum * 100), Value = (int)(initial * 100), TickStyle = TickStyle.None, SmallChange = 1, LargeChange = 100, Height = 28, AutoSize = false, BackColor = Color.White };
            Controls.AddRange(new Control[] { title, description, hint, number, unit, slider });
            number.ValueChanged += delegate
            {
                if (updating) return;
                updating = true; slider.Value = (int)Math.Round(number.Value * 100); updating = false;
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            };
            slider.ValueChanged += delegate
            {
                if (updating) return;
                updating = true; number.Value = slider.Value / 100m; updating = false;
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            };
            Resize += delegate { Arrange(); };
            Arrange();
        }

        private void Arrange()
        {
            title.Location = new Point(16, 10);
            number.Location = new Point(Math.Max(250, Width - 155), 5);
            unit.Location = new Point(Math.Max(360, Width - 43), 15);
            description.Location = new Point(16, 34);
            slider.SetBounds(9, 50, Math.Max(100, Width - 18), 20);
            hint.SetBounds(16, 72, Math.Max(100, Width - 26), 20);
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            using (Pen pen = new Pen(Design.Line)) args.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        public void ResetValue() { Value = original; }
    }

    public sealed class AppPreferences
    {
        public AudioSettings Parameters = new AudioSettings();
        public string Output = "";
        public bool PreserveFolders = true;
    }

    internal sealed class MainForm : Form
    {
        private readonly AudioEngine engine = new AudioEngine();
        private readonly OnlineLicense license;
        private readonly Button licenseButton = Design.Button("正在校验授权…", false);
        private readonly System.Windows.Forms.Timer licenseTimer = new System.Windows.Forms.Timer { Interval = 60000 };
        private bool checkingLicense;
        private readonly List<AudioItem> items = new List<AudioItem>();
        private readonly DataGridView grid = new DataGridView();
        private readonly ParameterCard tempo;
        private readonly ParameterCard pitch;
        private readonly ParameterCard rate;
        private readonly Label fileCount;
        private readonly Label estimate;
        private readonly Label status;
        private readonly Label profile;
        private readonly TextBox output = new TextBox();
        private readonly ComboBox format = new ComboBox();
        private readonly ComboBox bitrate = new ComboBox();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Button start;
        private readonly Button stop;
        private readonly Panel controls = new Panel();
        private readonly List<Control> busyDisabled = new List<Control>();
        private readonly List<string> records = new List<string>();
        private readonly string settingsFile;
        private readonly string logFile;
        private AudioSettings settings = new AudioSettings();
        private bool preserveFolders = true;
        private bool busy;
        private bool importing;
        private bool resetting;
        private CancellationTokenSource cancellation;
        private readonly HashSet<string> audioExtensions = new HashSet<string>(new[] { ".mp3", ".ogg", ".aac", ".mp4", ".m4a", ".m4b", ".wav", ".aif", ".aiff" }, StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> lyricExtensions = new HashSet<string>(new[] { ".lrc", ".txt", ".srt", ".ass", ".vtt" }, StringComparer.OrdinalIgnoreCase);

        public MainForm(string[] arguments, OnlineLicense licenseService)
        {
            license = licenseService;
            Text = "野火音频编辑";
            ClientSize = new Size(1200, 700);
            MinimumSize = new Size(1120, 700);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Design.Background;
            Font = new Font("Microsoft YaHei UI", 9);
            ForeColor = Design.Text;
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            string local = Environment.GetEnvironmentVariable("WILDFIRE_AUDIO_DATA_ROOT");
            if (string.IsNullOrWhiteSpace(local)) local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WildfireAudioEditor");
            Directory.CreateDirectory(local);
            settingsFile = Path.Combine(local, "settings.xml");
            string logs = Path.Combine(local, "logs"); Directory.CreateDirectory(logs);
            logFile = Path.Combine(logs, DateTime.Now.ToString("yyyy-MM-dd") + ".log");

            Panel header = new Panel { Dock = DockStyle.Top, Size = new Size(1200, 68), BackColor = Color.FromArgb(34, 39, 49) };
            Label brand = Design.Label("野火音频编辑", 18, true, Color.White); brand.Location = new Point(24, 6);
            Label subtitle = Design.Label("先添加歌曲，再调参数，最后一键导出。", 9, false, Color.FromArgb(187, 194, 207)); subtitle.Location = new Point(27, 42);
            Label version = Design.Label("2.0.2  ·  本地处理", 9, false, Color.FromArgb(220, 226, 234)); version.Anchor = AnchorStyles.Right | AnchorStyles.Top; version.Location = new Point(1050, 30);
            version.Cursor = Cursors.Hand;
            version.Click += delegate { ShowMessage("野火音频编辑 2.0\n本地音频处理程序\n\nMP3 编码使用 LAME 3.98.3（LGPL，许可全文见安装目录 Licenses 文件夹）。\n其他音频组件：BASS、BASS_FX、BASS_AAC、OggEnc 1.0.2、FAAC 1.28。\n组件来源及源代码链接见安装目录“第三方组件说明.txt”。"); };
            licenseButton.SetBounds(760, 18, 270, 34);
            licenseButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            licenseButton.AutoSize = false;
            licenseButton.Click += delegate { OpenLicense(); };
            header.Controls.AddRange(new Control[] { brand, subtitle, licenseButton, version });

            Panel footer = new Panel { Dock = DockStyle.Bottom, Size = new Size(1200, 128), BackColor = Color.White, Padding = new Padding(24, 15, 24, 12) };
            Label outputTitle = Design.Label("03  保存结果", 10, true, Design.Text); outputTitle.Location = new Point(24, 14);
            output.SetBounds(128, 12, 728, 28); output.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Button browse = Design.Button("选择位置", false); browse.SetBounds(870, 9, 100, 34); browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Button open = Design.Button("打开结果", false); open.SetBounds(982, 9, 100, 34); open.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Button history = Design.Button("处理记录", false); history.SetBounds(1090, 9, 86, 34); history.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Label outputNote = Design.Label("保持原文件名  ·  原文件不改动  ·  同名歌词随结果复制", 8.5f, false, Design.Muted); outputNote.Location = new Point(129, 46);
            progress.SetBounds(24, 75, 840, 8); progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            status = Design.Label("准备就绪，请先添加音频。", 9, false, Design.Muted); status.Location = new Point(24, 95); status.AutoSize = false; status.Size = new Size(840, 28); status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            start = Design.Button("开始处理", true); start.Font = new Font(Font, FontStyle.Bold); start.SetBounds(984, 68, 192, 46); start.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            stop = Design.Button("停止", false); stop.SetBounds(883, 72, 85, 39); stop.Anchor = AnchorStyles.Right | AnchorStyles.Top; stop.Enabled = false;
            footer.Controls.AddRange(new Control[] { outputTitle, output, browse, open, history, outputNote, progress, status, start, stop });

            TableLayoutPanel body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 12, 24, 12), ColumnCount = 2, RowCount = 1, BackColor = Design.Background };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            Panel left = new Panel { Dock = DockStyle.Fill, Size = new Size(536, 480), BackColor = Color.White, Margin = new Padding(0, 0, 16, 0), Padding = new Padding(15) };
            Label leftTitle = Design.Label("01  添加音频", 11, true, Design.Text); leftTitle.Location = new Point(15, 16);
            fileCount = Design.Label("0 首歌曲", 9, false, Design.Muted); fileCount.Location = new Point(360, 18); fileCount.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            FlowLayoutPanel toolbar = new FlowLayoutPanel { Location = new Point(15, 51), Height = 38, Width = 500, WrapContents = false, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            Button add = Design.Button("＋ 添加音频", true);
            Button folder = Design.Button("添加文件夹", false);
            Button remove = Design.Button("移出列表", false);
            toolbar.Controls.AddRange(new Control[] { add, folder, remove });
            Label dropHint = Design.Label("也可把音频或文件夹拖到这里。支持 MP3、WAV、OGG、M4A 等。", 8, false, Design.Muted); dropHint.Location = new Point(15, 95); dropHint.AutoSize = false; dropHint.Size = new Size(475, 32); dropHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            grid.SetBounds(15, 134, 506, 287); grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.ReadOnly = true; grid.RowHeadersVisible = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.MultiSelect = true; grid.BackgroundColor = Color.White; grid.BorderStyle = BorderStyle.None; grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal; grid.GridColor = Design.Line; grid.AutoGenerateColumns = false; grid.EnableHeadersVisualStyles = false; grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None; grid.ColumnHeadersHeight = 38; grid.RowTemplate.Height = 38;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Design.Background; grid.ColumnHeadersDefaultCellStyle.ForeColor = Design.Muted; grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Regular);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(255, 236, 227); grid.DefaultCellStyle.SelectionForeColor = Design.Text; grid.DefaultCellStyle.Padding = new Padding(5, 0, 0, 0);
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Song", HeaderText = "歌曲", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 130 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Duration", HeaderText = "时长", Width = 64 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "状态", Width = 82 });
            foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            Button metadata = Design.Button("编辑歌曲信息", false); metadata.SetBounds(15, 502, 140, 34); metadata.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Label scope = Design.Label("右侧参数统一应用于本批全部歌曲。", 8.5f, false, Design.Muted); scope.Location = new Point(166, 510); scope.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; scope.AutoSize = false; scope.Size = new Size(320, 24);
            left.Resize += delegate { grid.Height = Math.Max(120, left.Height - 193); metadata.Top = left.Height - 45; scope.Top = left.Height - 38; fileCount.Left = left.Width - 105; };
            left.Controls.AddRange(new Control[] { leftTitle, fileCount, toolbar, dropHint, grid, metadata, scope });

            Panel right = new Panel { Dock = DockStyle.Fill, Size = new Size(600, 480), Margin = new Padding(0), AutoScroll = true, BackColor = Design.Background };
            controls.Dock = DockStyle.Top; controls.Size = new Size(600, 470);
            Label rightTitle = Design.Label("02  调整参数", 11, true, Design.Text); rightTitle.Location = new Point(0, 0);
            profile = Design.Label("精度 0.01  ·  可直接输入数字", 8.5f, false, Design.Muted); profile.Location = new Point(230, 4);
            FlowLayoutPanel presets = new FlowLayoutPanel { Location = new Point(0, 32), Height = 38, Width = 580, WrapContents = false, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            AddPreset(presets, "原始", 100, 0, 100);
            AddPreset(presets, "轻度", 105, -1, 102);
            AddPreset(presets, "中度", 110, -1.5m, 104);
            AddPreset(presets, "重度", 115, -2, 105);
            tempo = new ParameterCard("速度 · 保持音调", "只改变快慢，适合加快节奏或放慢练习。", "100% 原速；轻快可试 103～108%；练习可试 85～95%。", "%", 25, 300, 100);
            pitch = new ParameterCard("音调 · 保持速度", "正数升调，负数降调，不改变歌曲时长。", "0 为原调；+1 升半音，-1 降半音；先从 ±0.5～2 试起。", "半音", -12, 12, 0);
            rate = new ParameterCard("播放速率 · 同时变速变调", "越快声音越高，越慢声音越低。", "通常保持 100%；需要整体变快、变高时，可先试 103%。", "%", 25, 300, 100);
            tempo.Dock = DockStyle.None; pitch.Dock = DockStyle.None; rate.Dock = DockStyle.None;
            tempo.SetBounds(0, 68, 600, 92); pitch.SetBounds(0, 166, 600, 92); rate.SetBounds(0, 264, 600, 92);
            tempo.Anchor = pitch.Anchor = rate.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            estimate = Design.Label("选中歌曲后显示预计输出时长。", 8.5f, false, Design.Muted); estimate.Location = new Point(0, 362); estimate.AutoSize = false; estimate.Size = new Size(565, 25);
            FlowLayoutPanel previewButtons = new FlowLayoutPanel { Location = new Point(0, 390), Height = 39, Width = 570, WrapContents = false };
            Button listen = Design.Button("试听选中歌曲", false); Button endPreview = Design.Button("停止试听", false); Button advanced = Design.Button("更多设置", false);
            previewButtons.Controls.AddRange(new Control[] { listen, endPreview, advanced });
            FlowLayoutPanel formats = new FlowLayoutPanel { Location = new Point(0, 434), Width = 580, Height = 34, WrapContents = false };
            Label formatLabel = Design.Label("输出格式", 9, false, Design.Muted); formatLabel.Margin = new Padding(0, 5, 10, 0);
            format.DropDownStyle = ComboBoxStyle.DropDownList; format.Width = 90; format.Items.AddRange(new object[] { "MP3", "WAV", "OGG", "M4A" }); format.SelectedIndex = 0;
            Label bitrateLabel = Design.Label("MP3 码率", 9, false, Design.Muted); bitrateLabel.Margin = new Padding(12, 5, 10, 0);
            bitrate.DropDownStyle = ComboBoxStyle.DropDownList; bitrate.Width = 108; bitrate.Items.AddRange(new object[] { "128 kbps", "192 kbps", "256 kbps", "320 kbps" }); bitrate.SelectedIndex = 3;
            formats.Controls.AddRange(new Control[] { formatLabel, format, bitrateLabel, bitrate });
            controls.Height = 470;
            controls.Controls.AddRange(new Control[] { rightTitle, profile, presets, tempo, pitch, rate, estimate, previewButtons, formats });
            right.Resize += delegate { controls.Width = right.ClientSize.Width - (right.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0); };
            right.Controls.Add(controls);
            body.Controls.Add(left, 0, 0); body.Controls.Add(right, 1, 0);
            Controls.Add(body); Controls.Add(footer); Controls.Add(header);

            busyDisabled.AddRange(new Control[] { add, folder, remove, metadata, controls, output, browse, start });
            add.Click += async delegate { using (OpenFileDialog dialog = new OpenFileDialog { Title = "添加音频", Multiselect = true, Filter = "音频文件|*.mp3;*.ogg;*.aac;*.mp4;*.m4a;*.m4b;*.wav;*.aif;*.aiff|所有文件|*.*" }) { if (dialog.ShowDialog(this) == DialogResult.OK) await AddInputs(dialog.FileNames); } };
            folder.Click += async delegate { using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "选择包含音频和歌词的文件夹" }) { if (dialog.ShowDialog(this) == DialogResult.OK) await AddInputs(new[] { dialog.SelectedPath }); } };
            remove.Click += delegate { foreach (DataGridViewRow row in grid.SelectedRows.Cast<DataGridViewRow>().ToArray()) { items.Remove((AudioItem)row.Tag); grid.Rows.Remove(row); } UpdateCount(); };
            metadata.Click += delegate { EditMetadata(); };
            browse.Click += delegate { using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "选择结果保存文件夹", SelectedPath = output.Text }) { if (dialog.ShowDialog(this) == DialogResult.OK) output.Text = dialog.SelectedPath; } };
            open.Click += delegate { if (Directory.Exists(output.Text)) Process.Start(new ProcessStartInfo(output.Text) { UseShellExecute = true }); else ShowMessage("结果文件夹尚未创建，请先处理音频。"); };
            history.Click += delegate { ShowRecords(); };
            tempo.ValueChanged += ParametersChanged; pitch.ValueChanged += ParametersChanged; rate.ValueChanged += ParametersChanged;
            format.SelectedIndexChanged += delegate { bitrate.Enabled = format.Text == "MP3"; };
            listen.Click += async delegate { try { AudioItem item = Selected(); if (item == null) { ShowMessage("请先选中一首歌曲。"); return; } if (!await CheckLicense(false)) { OpenLicense(); return; } engine.StartPreview(item, Snapshot()); status.Text = "正在试听：" + item + "；调整右侧参数可实时试听。"; } catch (Exception error) { ShowMessage(error.Message); } };
            endPreview.Click += delegate { engine.StopPreview(); status.Text = "试听已停止。"; };
            advanced.Click += delegate { using (AdvancedDialog dialog = new AdvancedDialog(Snapshot(), preserveFolders)) { if (dialog.ShowDialog(this) == DialogResult.OK) { settings = dialog.Settings; preserveFolders = dialog.PreserveFolders; ParametersChanged(null, EventArgs.Empty); } } };
            start.Click += async delegate { await StartBatch(); };
            stop.Click += delegate { if (cancellation != null) { cancellation.Cancel(); stop.Enabled = false; status.Text = "正在安全停止，已完成的结果会保留…"; } };
            grid.SelectionChanged += delegate { UpdateEstimate(); };
            AllowDrop = true;
            DragEnter += delegate(object sender, DragEventArgs args) { if (!busy && !importing && args.Data.GetDataPresent(DataFormats.FileDrop)) args.Effect = DragDropEffects.Copy; };
            DragDrop += async delegate(object sender, DragEventArgs args) { if (!busy && !importing) await AddInputs((string[])args.Data.GetData(DataFormats.FileDrop)); };
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (busy) { args.Cancel = true; ShowMessage("请先点击“停止”，等待当前处理结束后再关闭。"); return; }
                licenseTimer.Stop(); licenseTimer.Dispose(); SavePreferences(); engine.Dispose(); NativeAudio.BASS_Free();
            };
            LoadPreferences();
            Shown += async delegate
            {
                bool authorized = await CheckLicense(true);
                if (IsDisposed || Disposing) return;
                if (!authorized) OpenLicense();
                if (IsDisposed || Disposing) return;
                if (arguments.Length > 0) await AddInputs(arguments);
                licenseTimer.Start();
            };
            licenseTimer.Tick += async delegate { if (!checkingLicense) await CheckLicense(true); };
            Log("软件启动：GG 小程序授权，本地音频处理。");
        }

        private void OpenLicense()
        {
            if (checkingLicense || IsDisposed || Disposing) return;
            licenseTimer.Stop();
            using (LicenseDialog dialog = new LicenseDialog(license)) dialog.ShowDialog(this);
            UpdateLicense();
            licenseTimer.Start();
        }

        private void UpdateLicense()
        {
            LicenseState current = license.Snapshot();
            licenseButton.Text = current.Valid ? current.Label + " · 已授权" : "未授权 · 点击激活";
            licenseButton.AccessibleDescription = current.Valid ? LicenseDialog.Describe(current) : current.Message;
            if (!current.Valid)
            {
                engine.StopPreview();
                if (cancellation != null) cancellation.Cancel();
                status.Text = current.Message + " 点击顶部授权按钮处理。";
            }
        }

        private async Task<bool> CheckLicense(bool force)
        {
            if (checkingLicense) return false;
            checkingLicense = true;
            try { await license.VerifyAsync(force); }
            catch (Exception error) { Log("授权校验：" + (error is LicenseException ? error.Message : "校验未完成，请稍后重试。")); }
            finally { checkingLicense = false; }
            if (IsDisposed || Disposing) return false;
            UpdateLicense();
            return license.Snapshot().Valid;
        }

        private void AddPreset(FlowLayoutPanel panel, string name, decimal tempoValue, decimal pitchValue, decimal rateValue)
        {
            Button button = Design.Button(name, false); button.MinimumSize = new Size(0, 31); button.Height = 31; button.Padding = new Padding(6, 0, 6, 0); button.Margin = new Padding(0, 0, 6, 0);
            button.Click += delegate
            {
                resetting = true; tempo.Value = tempoValue; pitch.Value = pitchValue; rate.Value = rateValue; resetting = false; ParametersChanged(null, EventArgs.Empty);
            };
            panel.Controls.Add(button);
        }

        private AudioSettings Snapshot()
        {
            AudioSettings snapshot = settings.Copy();
            snapshot.Tempo = tempo.Value; snapshot.Pitch = pitch.Value; snapshot.Rate = rate.Value;
            snapshot.Format = format.Text; snapshot.Bitrate = int.Parse(bitrate.Text.Split(' ')[0]);
            snapshot.Validate();
            return snapshot;
        }

        private void ParametersChanged(object sender, EventArgs args)
        {
            if (resetting) return;
            UpdateEstimate();
            if (!license.Snapshot().Valid) { engine.StopPreview(); return; }
            try { engine.UpdatePreview(Snapshot()); } catch (Exception error) { engine.StopPreview(); Log("试听停止：" + error.Message); }
        }

        private AudioItem Selected()
        {
            return grid.SelectedRows.Count == 0 ? null : (AudioItem)grid.SelectedRows[0].Tag;
        }

        private void UpdateEstimate()
        {
            AudioItem item = Selected();
            estimate.Text = item == null ? "选中歌曲后显示预计输出时长。" : "原时长 " + Duration(item.Duration) + "  →  预计 " + Duration(item.Duration / (double)(tempo.Value * rate.Value / 10000m)) + "（音调单独调整不影响时长）";
        }

        private static string Duration(double seconds)
        {
            TimeSpan span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return span.TotalHours >= 1 ? ((int)span.TotalHours) + ":" + span.Minutes.ToString("00") + ":" + span.Seconds.ToString("00") : ((int)span.TotalMinutes) + ":" + span.Seconds.ToString("00");
        }

        private void UpdateCount()
        {
            fileCount.Text = items.Count + " 首歌曲";
            UpdateEstimate();
        }

        private IEnumerable<string> Scan(string directory)
        {
            foreach (string file in Directory.EnumerateFiles(directory)) yield return file;
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                string name = Path.GetFileName(child);
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0 || name.StartsWith(".wildfire-") || name == "处理结果" || name.EndsWith("_处理完成")) continue;
                foreach (string file in Scan(child)) yield return file;
            }
        }

        private async Task AddInputs(string[] paths)
        {
            if (busy || importing) return;
            importing = true; start.Enabled = false;
            int added = 0;
            try
            {
                HashSet<string> existing = new HashSet<string>(items.Select(item => item.Input), StringComparer.OrdinalIgnoreCase);
                foreach (string path in paths)
                {
                    string root = Directory.Exists(path) ? Path.GetFullPath(path) : null;
                    string[] files;
                    try { files = root == null ? new[] { path } : await Task.Run(() => Scan(root).ToArray()); }
                    catch (Exception error) { Log("文件夹读取失败：" + error.Message); continue; }
                    string[] lyrics = files.Where(file => lyricExtensions.Contains(Path.GetExtension(file))).ToArray();
                    foreach (string file in files.Where(file => audioExtensions.Contains(Path.GetExtension(file))))
                    {
                        if (!existing.Add(Path.GetFullPath(file))) continue;
                        try
                        {
                            AudioItem item = await Task.Run(() => engine.Probe(file, root));
                            string stem = Path.GetFileNameWithoutExtension(file);
                            item.Lyrics = root == null ? Directory.EnumerateFiles(Path.GetDirectoryName(file)).Where(candidate => lyricExtensions.Contains(Path.GetExtension(candidate)) && string.Equals(Path.GetFileNameWithoutExtension(candidate), stem, StringComparison.OrdinalIgnoreCase)).ToArray() : lyrics.Where(candidate => string.Equals(Path.GetFileNameWithoutExtension(candidate), stem, StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetDirectoryName(candidate), Path.GetDirectoryName(file), StringComparison.OrdinalIgnoreCase)).ToArray();
                            items.Add(item);
                            int row = grid.Rows.Add(Path.GetFileName(file), Duration(item.Duration), "待处理"); grid.Rows[row].Tag = item; grid.Rows[row].Cells[0].ToolTipText = item.Input; added++;
                        }
                        catch (Exception error) { Log(error.Message); }
                    }
                    if (string.IsNullOrWhiteSpace(output.Text) && files.Length > 0)
                        output.Text = root == null ? Path.Combine(Path.GetDirectoryName(path), "处理结果") : root.TrimEnd(Path.DirectorySeparatorChar) + "_处理完成";
                }
                UpdateCount();
                status.Text = "已添加 " + added + " 首音频。调好右侧参数后点击“开始处理”。";
                Log("添加音频：" + added + " 首；当前列表 " + items.Count + " 首。");
            }
            finally { importing = false; start.Enabled = true; }
        }

        private string Destination(AudioItem item, string directory, string extension, HashSet<string> protectedInputs, bool retainFolders)
        {
            string relative = retainFolders && item.Root != null ? item.Input.Substring(item.Root.TrimEnd(Path.DirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar) : Path.GetFileName(item.Input);
            string target = Path.GetFullPath(Path.Combine(directory, Path.ChangeExtension(relative, extension)));
            string outputRoot = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("结果路径超出保存文件夹。");
            if (protectedInputs.Contains(target)) target = Path.Combine(Path.GetDirectoryName(target), "处理结果", Path.GetFileName(target));
            if (protectedInputs.Contains(target)) throw new InvalidOperationException("结果位置与已提交的原文件冲突，请更换保存位置。");
            return target;
        }

        private async Task StartBatch()
        {
            if (busy || importing) return;
            if (items.Count == 0) { ShowMessage("请先添加音频或文件夹。"); return; }
            if (string.IsNullOrWhiteSpace(output.Text)) { ShowMessage("请选择结果保存位置。"); return; }
            if (!await CheckLicense(true)) { OpenLicense(); return; }
            if (busy || importing) return;
            AudioSettings snapshot;
            string directory;
            try { snapshot = Snapshot(); directory = Path.GetFullPath(output.Text); Directory.CreateDirectory(directory); }
            catch (Exception error) { ShowMessage(error.Message); return; }
            engine.StopPreview(); SavePreferences();
            AudioItem[] batch = items.Select(item => new AudioItem { Input = item.Input, Root = item.Root, Duration = item.Duration, Tags = item.Tags.Copy(), Lyrics = (string[])item.Lyrics.Clone() }).ToArray();
            HashSet<string> protectedInputs = new HashSet<string>(items.Select(item => item.Input), StringComparer.OrdinalIgnoreCase);
            string[] destinations = new string[batch.Length];
            try { for (int index = 0; index < batch.Length; index++) destinations[index] = Destination(batch[index], directory, "." + snapshot.Format.ToLowerInvariant(), protectedInputs, preserveFolders); }
            catch (Exception error) { ShowMessage(error.Message); return; }
            if (destinations.Distinct(StringComparer.OrdinalIgnoreCase).Count() != destinations.Length) { ShowMessage("列表中有重名歌曲会保存到同一位置。请在更多设置中保留目录结构，或分批处理。"); return; }
            busy = true; cancellation = new CancellationTokenSource();
            foreach (Control control in busyDisabled) control.Enabled = false;
            stop.Enabled = true;
            progress.Value = 0;
            int workers = snapshot.AutoParallel ? Capacity.Workers() : 1;
            workers = Math.Min(workers, batch.Length);
            Log(string.Format("开始批次：{0} 首，同时 {1} 首；速度 {2:0.00}%，音调 {3:+0.00;-0.00;0.00} 半音，播放速率 {4:0.00}%。", batch.Length, workers, snapshot.Tempo, snapshot.Pitch, snapshot.Rate));
            int completed = 0; int succeeded = 0; int skipped = 0; int failed = 0;
            double[] portions = new double[batch.Length];
            object sync = new object();
            Stopwatch batchWatch = Stopwatch.StartNew();
            SemaphoreSlim slots = new SemaphoreSlim(workers);
            try
            {
                Task[] tasks = batch.Select((item, index) => Task.Run(async delegate
                {
                    bool entered = false;
                    try
                    {
                        await slots.WaitAsync(cancellation.Token); entered = true;
                        cancellation.Token.ThrowIfCancellationRequested();
                        LicenseState authorization = await license.VerifyAsync(false);
                        if (!authorization.Valid) throw new LicenseException("license_required", authorization.Message);
                        cancellation.Token.ThrowIfCancellationRequested();
                        UI(delegate { grid.Rows[index].Cells[2].Value = "处理中"; });
                        Stopwatch watch = Stopwatch.StartNew();
                        ProcessingResult result = engine.Process(item, destinations[index], snapshot, cancellation.Token, value =>
                        {
                            if (!license.Snapshot().Valid) cancellation.Cancel();
                            int percent;
                            lock (sync) { portions[index] = value; percent = (int)Math.Round(portions.Sum() / batch.Length * 100); }
                            UI(delegate { progress.Value = Math.Max(progress.Value, Math.Min(100, percent)); });
                        });
                        lock (sync) { if (result.Skipped) skipped++; else succeeded++; }
                        UI(delegate
                        {
                            grid.Rows[index].Cells[2].Value = result.Skipped ? "已跳过" : "已完成";
                            Log((result.Skipped ? "跳过同名结果：" : "完成：") + item + "；耗时 " + watch.Elapsed.TotalSeconds.ToString("0.0") + " 秒" + (result.LyricsCopied > 0 ? "；歌词 " + result.LyricsCopied + " 个" : ""));
                            if (!string.IsNullOrEmpty(result.Warning)) Log(result.Warning);
                        });
                    }
                    catch (OperationCanceledException) { UI(delegate { grid.Rows[index].Cells[2].Value = "已停止"; }); }
                    catch (Exception error) { lock (sync) failed++; UI(delegate { grid.Rows[index].Cells[2].Value = "失败"; Log("失败：" + item + "；" + error.Message); }); }
                    finally
                    {
                        if (entered) slots.Release();
                        int count;
                        lock (sync) { completed++; portions[index] = 1; count = completed; }
                        UI(delegate { status.Text = "已处理 " + count + "/" + batch.Length + " 首 · 同时 " + workers + " 首"; });
                    }
                })).ToArray();
                await Task.WhenAll(tasks);
                progress.Value = cancellation.IsCancellationRequested ? progress.Value : 100;
                status.Text = string.Format("{0}：成功 {1} 首，跳过 {2} 首，失败 {3} 首；共用时 {4}。", cancellation.IsCancellationRequested ? "已停止" : "处理完成", succeeded, skipped, failed, Duration(batchWatch.Elapsed.TotalSeconds));
                Log(status.Text);
            }
            finally
            {
                busy = false; stop.Enabled = false; slots.Dispose(); cancellation.Dispose(); cancellation = null;
                foreach (Control control in busyDisabled) control.Enabled = true;
            }
        }

        private void UI(Action action)
        {
            if (IsDisposed || Disposing) return;
            try { BeginInvoke(action); } catch (InvalidOperationException) { }
        }

        private void Log(string text)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text;
            records.Add(line);
            if (records.Count > 3000) records.RemoveRange(0, 1000);
            try { File.AppendAllText(logFile, line + Environment.NewLine, Encoding.UTF8); } catch (IOException) { }
        }

        private void ShowRecords()
        {
            using (Form dialog = new Form { Text = "处理记录", Size = new Size(850, 520), StartPosition = FormStartPosition.CenterParent, Font = Font, MinimizeBox = false })
            {
                TextBox text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Text = string.Join(Environment.NewLine, records), BackColor = Color.White, BorderStyle = BorderStyle.None, Font = new Font("Microsoft YaHei UI", 9) };
                dialog.Controls.Add(text); dialog.ShowDialog(this);
            }
        }

        private void EditMetadata()
        {
            AudioItem[] chosen = grid.SelectedRows.Cast<DataGridViewRow>().Select(row => (AudioItem)row.Tag).ToArray();
            if (chosen.Length == 0) { ShowMessage("请先选中要编辑的歌曲。"); return; }
            using (MetadataDialog dialog = new MetadataDialog(chosen[0].Tags.Copy(), chosen.Length))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                foreach (AudioItem item in chosen) item.Tags = dialog.Tags.Copy();
                Log("已更新 " + chosen.Length + " 首歌曲的信息，导出 MP3 时写入；原文件保持不变。");
            }
        }

        private void LoadPreferences()
        {
            try
            {
                if (!File.Exists(settingsFile)) return;
                AppPreferences saved;
                using (Stream file = File.OpenRead(settingsFile)) saved = (AppPreferences)new XmlSerializer(typeof(AppPreferences)).Deserialize(file);
                saved.Parameters.Validate();
                settings = saved.Parameters;
                resetting = true; tempo.Value = settings.Tempo; pitch.Value = settings.Pitch; rate.Value = settings.Rate; resetting = false;
                format.SelectedItem = settings.Format; bitrate.SelectedItem = settings.Bitrate + " kbps";
                preserveFolders = saved.PreserveFolders;
                if (Directory.Exists(saved.Output)) output.Text = saved.Output;
            }
            catch (Exception error) { Log("设置已恢复默认值：" + error.Message); }
        }

        private void SavePreferences()
        {
            try
            {
                AppPreferences saved = new AppPreferences { Parameters = Snapshot(), Output = output.Text, PreserveFolders = preserveFolders };
                using (Stream file = File.Create(settingsFile + ".tmp")) new XmlSerializer(typeof(AppPreferences)).Serialize(file, saved);
                if (File.Exists(settingsFile)) NativeAudio.ReplaceFile(settingsFile + ".tmp", settingsFile); else File.Move(settingsFile + ".tmp", settingsFile);
            }
            catch (Exception error) { Log("保存设置失败：" + error.Message); }
        }

        private void ShowMessage(string message) { MessageBox.Show(this, message, "野火音频编辑", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }

    public static class Capacity
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            public uint Length;
            public uint Load;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPage;
            public ulong AvailablePage;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtended;
        }
        [DllImport("kernel32.dll")]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        public static int Workers()
        {
            MemoryStatus info = new MemoryStatus(); info.Length = (uint)Marshal.SizeOf(info);
            if (!GlobalMemoryStatusEx(ref info)) return 1;
            return Math.Min(8, Math.Min(Math.Max(1, Environment.ProcessorCount / 2), Math.Max(1, (int)(info.AvailablePhysical / (1024UL * 1024 * 1024)) - 1)));
        }
    }
}
