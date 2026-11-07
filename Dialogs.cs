using System;
using System.Drawing;
using System.Windows.Forms;

namespace WildfireAudioEditor
{
    public sealed class AdvancedDialog : Form
    {
        public AudioSettings Settings;
        public bool PreserveFolders;

        public AdvancedDialog(AudioSettings settings, bool preserveFolders)
        {
            Settings = settings.Copy();
            Text = "更多设置"; ClientSize = new Size(650, 600); Font = new Font("Microsoft YaHei UI", 9);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            BackColor = Design.Background;
            Label intro = Design.Label("日常使用保持默认即可。", 12, true, Design.Text); intro.Location = new Point(24, 20);
            NumericUpDown volume = Number(0, 100, settings.Volume, 2, 170, 65);
            Label volumeLabel = Design.Label("输出音量（%）", 9, false, Design.Text); volumeLabel.Location = new Point(24, 70);
            Label volumeHint = Design.Label("100% 保持原音量；80% 为原来的八成。", 8.5f, false, Design.Muted); volumeHint.Location = new Point(24, 103);
            CheckBox normalize = Check("统一峰值音量（开启后，音量比例由峰值统一处理覆盖）", settings.Normalize, 143);
            CheckBox mono = Check("转为单声道（适合语音；音乐建议关闭）", settings.Mono, 181);
            CheckBox parallel = Check("根据电脑配置自动并行处理（建议开启）", settings.AutoParallel, 219);
            CheckBox folders = Check("保留提交文件夹的子目录，减少同名歌曲冲突", preserveFolders, 257);
            CheckBox overwrite = Check("替换结果文件夹内已有的同名结果（默认跳过）", settings.ReplaceExisting, 295);
            Label algorithm = Design.Label("算法微调 · 一般无需修改", 10, true, Design.Text); algorithm.Location = new Point(24, 345);
            Label advice = Design.Label("片段/搜索设为 0 时自动选择；重叠默认 8 毫秒。\n先使用默认值试听；数值越大不代表效果一定越好。", 8.5f, false, Design.Muted); advice.Location = new Point(24, 375);
            NumericUpDown sequence = Number(0, 200, settings.Sequence, 0, 24, 449);
            NumericUpDown seek = Number(0, 100, settings.SeekWindow, 0, 230, 449);
            NumericUpDown overlap = Number(4, 50, settings.Overlap, 0, 436, 449);
            Label first = Design.Label("片段长度（毫秒）", 9, false, Design.Text); first.Location = new Point(24, 423);
            Label second = Design.Label("搜索窗口（毫秒）", 9, false, Design.Text); second.Location = new Point(230, 423);
            Label third = Design.Label("重叠长度（毫秒）", 9, false, Design.Text); third.Location = new Point(436, 423);
            Button reset = Design.Button("恢复默认", false); reset.Location = new Point(24, 536);
            Button cancel = Design.Button("取消", false); cancel.Location = new Point(426, 536); cancel.DialogResult = DialogResult.Cancel;
            Button save = Design.Button("保存设置", true); save.Location = new Point(528, 536);
            reset.Click += delegate { volume.Value = 100; normalize.Checked = false; mono.Checked = false; parallel.Checked = true; folders.Checked = true; overwrite.Checked = false; sequence.Value = seek.Value = 0; overlap.Value = 8; };
            save.Click += delegate
            {
                Settings.Volume = volume.Value; Settings.Normalize = normalize.Checked; Settings.Mono = mono.Checked;
                Settings.AutoParallel = parallel.Checked; PreserveFolders = folders.Checked; Settings.ReplaceExisting = overwrite.Checked;
                Settings.Sequence = (int)sequence.Value; Settings.SeekWindow = (int)seek.Value; Settings.Overlap = (int)overlap.Value;
                Settings.Validate(); DialogResult = DialogResult.OK;
            };
            Controls.AddRange(new Control[] { intro, volumeLabel, volume, volumeHint, normalize, mono, parallel, folders, overwrite, algorithm, advice, first, second, third, sequence, seek, overlap, reset, cancel, save });
            AcceptButton = save; CancelButton = cancel;
        }

        private static NumericUpDown Number(decimal minimum, decimal maximum, decimal value, int decimals, int left, int top)
        {
            return new NumericUpDown { Minimum = minimum, Maximum = maximum, Value = value, DecimalPlaces = decimals, Increment = decimals == 0 ? 1 : .01m, Location = new Point(left, top), Width = 125 };
        }
        private static CheckBox Check(string text, bool selected, int top)
        {
            return new CheckBox { Text = text, Checked = selected, Location = new Point(24, top), AutoSize = true };
        }
    }

    public sealed class MetadataDialog : Form
    {
        public Metadata Tags;
        public MetadataDialog(Metadata tags, int count)
        {
            Text = "编辑歌曲信息"; ClientSize = new Size(550, 460); Font = new Font("Microsoft YaHei UI", 9);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            Label hint = Design.Label("以下信息写入导出的 MP3，原文件不变。\n将统一应用到选中的 " + count + " 首歌曲；留空会清除对应字段。", 9, false, Design.Muted); hint.Location = new Point(24, 20);
            string[] labels = { "标题", "歌手", "专辑", "年份", "曲目编号", "备注" };
            string[] values = { tags.Title, tags.Artist, tags.Album, tags.Year, tags.Track, tags.Comment };
            TextBox[] fields = new TextBox[6];
            Controls.Add(hint);
            for (int index = 0; index < labels.Length; index++)
            {
                Label label = Design.Label(labels[index], 9, false, Design.Text); label.Location = new Point(24, 92 + index * 44);
                fields[index] = new TextBox { Text = values[index], Location = new Point(110, 87 + index * 44), Width = 410, MaxLength = 1000 };
                Controls.Add(label); Controls.Add(fields[index]);
            }
            Button cancel = Design.Button("取消", false); cancel.Location = new Point(320, 400); cancel.DialogResult = DialogResult.Cancel;
            Button save = Design.Button("应用", true); save.Location = new Point(426, 400);
            save.Click += delegate { Tags = new Metadata { Title = fields[0].Text, Artist = fields[1].Text, Album = fields[2].Text, Year = fields[3].Text, Track = fields[4].Text, Comment = fields[5].Text }; DialogResult = DialogResult.OK; };
            Controls.AddRange(new Control[] { cancel, save }); AcceptButton = save; CancelButton = cancel;
        }
    }
}
