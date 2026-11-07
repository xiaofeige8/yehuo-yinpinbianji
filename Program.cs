using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("野火音频编辑")]
[assembly: AssemblyProduct("野火音频编辑")]
[assembly: AssemblyVersion("2.0.2.0")]
[assembly: AssemblyFileVersion("2.0.2.0")]

namespace WildfireAudioEditor
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] arguments)
        {
            try
            {
                NativeAudio.Initialize(true);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(arguments, new OnlineLicense()));
                return 0;
            }
            catch (Exception error)
            {
                MessageBox.Show("启动或处理失败：" + error.Message, "野火音频编辑", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
