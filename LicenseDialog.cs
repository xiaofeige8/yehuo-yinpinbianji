using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WildfireAudioEditor
{
    internal sealed class LicenseDialog : Form
    {
        private readonly OnlineLicense license;
        private readonly TextBox code = new TextBox();
        private readonly Label state = new Label();
        private readonly Button activate = Design.Button("激活 / 校验", true);
        private readonly Button refresh = Design.Button("重新校验", false);
        private readonly Button replace = Design.Button("更换卡密", false);
        private readonly Button close = Design.Button("完成", false);
        private bool working;

        public LicenseDialog(OnlineLicense service)
        {
            license = service;
            Text = "小程序卡密授权";
            ClientSize = new Size(620, 355);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9);
            BackColor = Design.Background;
            Label heading = Design.Label("使用 GG 卡密激活", 16, true, Design.Text);
            heading.Location = new Point(24, 22);
            Label hint = Design.Label("与 yuansheng（原声工坊 / 野火音乐台）共用授权，无需另购卡头。", 9, false, Design.Muted);
            hint.Location = new Point(25, 66);
            Label plans = Design.Label("支持：体验卡（24小时）、日卡、月卡、年卡、永久卡。", 9, false, Design.Muted);
            plans.Location = new Point(25, 94);
            code.SetBounds(26, 132, 565, 32);
            code.Font = new Font("Consolas", 13);
            code.AccessibleName = "GG 卡密";
            code.MaxLength = 64;
            activate.SetBounds(26, 179, 136, 36);
            refresh.SetBounds(175, 179, 120, 36);
            replace.SetBounds(308, 179, 120, 36);
            close.SetBounds(480, 293, 110, 36);
            state.SetBounds(26, 230, 565, 57);
            state.ForeColor = Design.Text;
            Label footer = Design.Label("期限以小程序后台签发结果为准；音频在本机处理。", 8.5f, false, Design.Muted);
            footer.Location = new Point(26, 308);
            Controls.AddRange(new Control[] { heading, hint, plans, code, activate, refresh, replace, state, close, footer });
            activate.Click += async delegate { await Run(async () => await license.ActivateAsync(code.Text, false)); };
            refresh.Click += async delegate { await Run(async () => await license.VerifyAsync(true)); };
            replace.Click += async delegate
            {
                try { OnlineLicense.NormalizeCode(code.Text); }
                catch (LicenseException error) { state.Text = error.Message; return; }
                if (MessageBox.Show(this, "更换会将本机 GG 授权切换到输入的新卡，野火音乐台也会沿用该授权。确定继续？", "更换 GG 卡密", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                await Run(async () => await license.ActivateAsync(code.Text, true));
            };
            close.Click += delegate { Close(); };
            FormClosing += delegate(object sender, FormClosingEventArgs args) { if (working) args.Cancel = true; };
            ShowState(license.Snapshot());
        }

        private async Task Run(Func<Task<LicenseState>> operation)
        {
            if (working) return;
            working = true;
            activate.Enabled = refresh.Enabled = replace.Enabled = close.Enabled = code.Enabled = false;
            state.Text = "正在连接小程序授权服务，请稍候…";
            try
            {
                ShowState(await operation());
                code.Clear();
            }
            catch (Exception error) { state.ForeColor = Color.Firebrick; state.Text = error is LicenseException ? error.Message : "授权校验未完成，请稍后重试。"; }
            finally { working = false; activate.Enabled = refresh.Enabled = replace.Enabled = close.Enabled = code.Enabled = true; }
        }

        private void ShowState(LicenseState current)
        {
            state.ForeColor = current.Valid ? Color.FromArgb(35, 119, 76) : Color.Firebrick;
            state.Text = current.Valid ? "已授权 · " + Describe(current) : current.Message;
        }

        internal static string Describe(LicenseState current)
        {
            return current.Label + (current.ExpiresAt.HasValue ? " · 到期：" + new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(current.ExpiresAt.Value).ToLocalTime().ToString("yyyy-MM-dd HH:mm") : " · 长期有效");
        }
    }
}
