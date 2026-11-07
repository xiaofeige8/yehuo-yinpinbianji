using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace WildfireAudioEditor
{
    public sealed class AudioSettings
    {
        public decimal Tempo = 100m;
        public decimal Pitch = 0m;
        public decimal Rate = 100m;
        public decimal Volume = 100m;
        public string Format = "MP3";
        public int Bitrate = 320;
        public bool Normalize;
        public bool Mono;
        public bool ReplaceExisting;
        public bool AutoParallel = true;
        public int Sequence;
        public int SeekWindow;
        public int Overlap = 8;

        public AudioSettings Copy() { return (AudioSettings)MemberwiseClone(); }

        public void Validate()
        {
            if (Tempo < 25 || Tempo > 300 || Rate < 25 || Rate > 300 || Pitch < -12 || Pitch > 12 || Volume < 0 || Volume > 100)
                throw new InvalidOperationException("参数超出可用范围，请重新设置。");
            if (!new[] { "MP3", "WAV", "OGG", "M4A" }.Contains(Format))
                throw new InvalidOperationException("请选择支持的输出格式。");
            if (!new[] { 128, 192, 256, 320 }.Contains(Bitrate))
                throw new InvalidOperationException("请选择有效的 MP3 码率。");
            if (Sequence < 0 || Sequence > 200 || SeekWindow < 0 || SeekWindow > 100 || Overlap < 4 || Overlap > 50)
                throw new InvalidOperationException("高级算法参数超出范围。");
        }
    }

    public sealed class AudioItem
    {
        public string Input;
        public string Root;
        public double Duration;
        public Metadata Tags = new Metadata();
        public string[] Lyrics = new string[0];
        public override string ToString() { return Path.GetFileName(Input); }
    }

    public sealed class Metadata
    {
        public string Title = "";
        public string Artist = "";
        public string Album = "";
        public string Year = "";
        public string Track = "";
        public string Comment = "";
        public Metadata Copy() { return (Metadata)MemberwiseClone(); }
    }

    public sealed class ProcessingResult
    {
        public string Output;
        public bool Skipped;
        public int LyricsCopied;
        public string Warning;
    }

    public static class NativeAudio
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string existing, string replacement, uint flags);

        public static void ReplaceFile(string source, string destination)
        {
            if (!MoveFileEx(source, destination, 9)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法替换结果文件，请检查文件是否被占用或只读。");
        }

        public const uint Decode = 0x200000;
        public const uint Unicode = 0x80000000;
        public const uint FreeSource = 0x10000;
        public const uint Prescan = 0x20000;
        public const uint Tempo = 0x10000;
        public const uint Pitch = 0x10001;
        public const uint Frequency = 0x10002;
        public static int Device;
        public static bool CanPlay;

        [StructLayout(LayoutKind.Sequential)]
        public struct ChannelInfo
        {
            public uint Frequency;
            public uint Channels;
            public uint Flags;
            public uint Type;
            public uint OriginalResolution;
            public uint Plugin;
            public uint Sample;
            public IntPtr Filename;
        }

        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern bool BASS_Init(int device, uint frequency, uint flags, IntPtr window, IntPtr clsid);
        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int BASS_GetDevice();
        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern bool BASS_SetDevice(int device);
        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint BASS_GetVersion();
        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern bool BASS_Free();
        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int BASS_ErrorGetCode();
        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        public static extern uint BASS_PluginLoad(string filename, uint flags);
        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        public static extern uint BASS_StreamCreateFile(bool memory, string filename, long offset, long length, uint flags);
        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern bool BASS_StreamFree(uint handle);
        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern bool BASS_ChannelGetInfo(uint handle, out ChannelInfo information);
        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern long BASS_ChannelGetLength(uint handle, uint mode);
        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern long BASS_ChannelGetPosition(uint handle, uint mode);
        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern double BASS_ChannelBytes2Seconds(uint handle, long position);
        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int BASS_ChannelGetData(uint handle, byte[] buffer, int length);
        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern bool BASS_ChannelSetAttribute(uint handle, uint attribute, float value);
        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern bool BASS_ChannelPlay(uint handle, bool restart);
        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern bool BASS_ChannelStop(uint handle);
        [DllImport("bass_fx.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint BASS_FX_GetVersion();
        [DllImport("bass_fx.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint BASS_FX_TempoCreate(uint handle, uint flags);

        public static void Initialize(bool preview)
        {
            if ((BASS_GetVersion() >> 16) != 0x204 || (BASS_FX_GetVersion() >> 16) != 0x204)
                throw new InvalidOperationException("音频组件版本不匹配，请重新安装完整软件。");
            CanPlay = preview && BASS_Init(-1, 44100, 0, IntPtr.Zero, IntPtr.Zero);
            if (!CanPlay && !BASS_Init(0, 44100, 0, IntPtr.Zero, IntPtr.Zero))
                throw Failure("无法初始化音频引擎");
            Device = BASS_GetDevice();
            if (BASS_PluginLoad(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bass_aac.dll"), Unicode) == 0)
                throw Failure("无法加载 AAC/M4A 音频组件");
        }

        public static Exception Failure(string message)
        {
            return new InvalidOperationException(message + "（音频错误 " + BASS_ErrorGetCode() + "）");
        }

        public static void Apply(uint handle, AudioSettings settings, uint sampleRate)
        {
            if (!BASS_ChannelSetAttribute(handle, Tempo, (float)settings.Tempo - 100f) ||
                !BASS_ChannelSetAttribute(handle, Pitch, (float)settings.Pitch) ||
                !BASS_ChannelSetAttribute(handle, Frequency, sampleRate * (float)settings.Rate / 100f))
                throw Failure("无法应用变速或变调参数");
            if (!BASS_ChannelSetAttribute(handle, 0x10013, settings.Sequence) ||
                !BASS_ChannelSetAttribute(handle, 0x10014, settings.SeekWindow) ||
                !BASS_ChannelSetAttribute(handle, 0x10015, settings.Overlap))
                throw Failure("无法应用高级算法设置");
        }
    }

    public sealed class AudioEngine : IDisposable
    {
        private uint preview;
        private uint previewFrequency;

        public AudioItem Probe(string input, string root)
        {
            NativeAudio.BASS_SetDevice(NativeAudio.Device);
            uint stream = NativeAudio.BASS_StreamCreateFile(false, input, 0, 0, NativeAudio.Decode | NativeAudio.Unicode | NativeAudio.Prescan);
            if (stream == 0) throw NativeAudio.Failure("无法读取“" + Path.GetFileName(input) + "”");
            try
            {
                NativeAudio.ChannelInfo information;
                if (!NativeAudio.BASS_ChannelGetInfo(stream, out information)) throw NativeAudio.Failure("无法读取音频参数");
                if (information.Channels > 2) throw new InvalidOperationException("此版本支持单声道和立体声音频，请先将多声道素材转换为立体声。");
                return new AudioItem { Input = Path.GetFullPath(input), Root = root, Duration = NativeAudio.BASS_ChannelBytes2Seconds(stream, NativeAudio.BASS_ChannelGetLength(stream, 0)), Tags = ReadMetadata(input) };
            }
            finally { NativeAudio.BASS_StreamFree(stream); }
        }

        public void StartPreview(AudioItem item, AudioSettings settings)
        {
            if (!NativeAudio.CanPlay) throw new InvalidOperationException("未检测到可用的播放设备，仍可正常导出音频。");
            StopPreview();
            uint source = NativeAudio.BASS_StreamCreateFile(false, item.Input, 0, 0, NativeAudio.Decode | NativeAudio.Unicode | NativeAudio.Prescan);
            if (source == 0) throw NativeAudio.Failure("无法打开试听文件");
            preview = NativeAudio.BASS_FX_TempoCreate(source, NativeAudio.FreeSource);
            if (preview == 0) { NativeAudio.BASS_StreamFree(source); throw NativeAudio.Failure("无法创建试听音频"); }
            NativeAudio.ChannelInfo information;
            NativeAudio.BASS_ChannelGetInfo(source, out information);
            previewFrequency = information.Frequency;
            UpdatePreview(settings);
            if (!NativeAudio.BASS_ChannelPlay(preview, false)) throw NativeAudio.Failure("无法开始试听");
        }

        public void UpdatePreview(AudioSettings settings)
        {
            if (preview == 0) return;
            NativeAudio.Apply(preview, settings, previewFrequency);
            NativeAudio.BASS_ChannelSetAttribute(preview, 2, (float)settings.Volume / 100f);
        }

        public void StopPreview()
        {
            if (preview == 0) return;
            NativeAudio.BASS_ChannelStop(preview);
            NativeAudio.BASS_StreamFree(preview);
            preview = 0;
        }

        public ProcessingResult Process(AudioItem item, string destination, AudioSettings settings, CancellationToken cancellation, Action<double> progress)
        {
            settings.Validate();
            if (string.Equals(Path.GetFullPath(item.Input), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("结果不能覆盖原音频，请选择单独的结果文件夹。");
            NativeAudio.BASS_SetDevice(NativeAudio.Device);
            if (File.Exists(destination) && !settings.ReplaceExisting) return new ProcessingResult { Output = destination, Skipped = true };
            string scratch = Path.Combine(Path.GetTempPath(), "wildfire-audio-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            string published = null;
            try
            {
                string wave = Path.Combine(scratch, "audio.wav");
                Render(item.Input, wave, settings, cancellation, value => progress(value * 0.8));
                string processed = wave;
                if (settings.Format != "WAV")
                {
                    processed = Path.Combine(scratch, "audio." + settings.Format.ToLowerInvariant());
                    Encode(wave, processed, item.Tags, settings, cancellation);
                }
                cancellation.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                published = Path.Combine(Path.GetDirectoryName(destination), ".wildfire-" + Guid.NewGuid().ToString("N") + ".tmp");
                if (settings.Format == "MP3")
                {
                    using (FileStream target = new FileStream(published, FileMode.CreateNew))
                    {
                        byte[] tags = WriteMetadata(item.Tags);
                        target.Write(tags, 0, tags.Length);
                        using (FileStream audio = File.OpenRead(processed))
                        {
                            audio.Position = Id3Length(audio);
                            audio.CopyTo(target);
                        }
                    }
                }
                else File.Copy(processed, published, false);
                cancellation.ThrowIfCancellationRequested();
                if (File.Exists(destination))
                {
                    if (!settings.ReplaceExisting) return new ProcessingResult { Output = destination, Skipped = true };
                    NativeAudio.ReplaceFile(published, destination);
                }
                else File.Move(published, destination);
                published = null;
                ProcessingResult result = new ProcessingResult { Output = destination };
                foreach (string lyric in item.Lyrics)
                {
                    try
                    {
                        string target = Path.Combine(Path.GetDirectoryName(destination), Path.GetFileName(lyric));
                        if (string.Equals(Path.GetFullPath(lyric), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) continue;
                        if (!File.Exists(target)) { File.Copy(lyric, target, false); result.LyricsCopied++; }
                        else if (settings.ReplaceExisting)
                        {
                            string staged = Path.Combine(Path.GetDirectoryName(target), ".wildfire-" + Guid.NewGuid().ToString("N"));
                            try { File.Copy(lyric, staged); NativeAudio.ReplaceFile(staged, target); result.LyricsCopied++; }
                            finally { if (File.Exists(staged)) File.Delete(staged); }
                        }
                    }
                    catch (Exception error) { result.Warning = "歌词复制失败：" + error.Message; }
                }
                progress(1);
                return result;
            }
            finally
            {
                if (published != null && File.Exists(published)) File.Delete(published);
                if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
            }
        }

        public void Render(string input, string output, AudioSettings settings, CancellationToken cancellation, Action<double> progress)
        {
            settings.Validate();
            uint source = NativeAudio.BASS_StreamCreateFile(false, input, 0, 0, NativeAudio.Decode | NativeAudio.Unicode | NativeAudio.Prescan);
            if (source == 0) throw NativeAudio.Failure("无法打开音频");
            uint transformed = 0;
            try
            {
                NativeAudio.ChannelInfo info;
                if (!NativeAudio.BASS_ChannelGetInfo(source, out info)) throw NativeAudio.Failure("无法读取音频参数");
                transformed = NativeAudio.BASS_FX_TempoCreate(source, NativeAudio.Decode);
                if (transformed == 0) throw NativeAudio.Failure("无法创建音频处理流");
                NativeAudio.Apply(transformed, settings, info.Frequency);
                int inputChannels = (int)info.Channels;
                int outputChannels = settings.Mono ? 1 : inputChannels;
                long inputLength = NativeAudio.BASS_ChannelGetLength(source, 0);
                byte[] buffer = new byte[65536 - 65536 % (inputChannels * 2)];
                int peak = 0;
                using (FileStream file = new FileStream(output, FileMode.CreateNew, FileAccess.ReadWrite))
                using (BinaryWriter writer = new BinaryWriter(file))
                {
                    WaveHeader(writer, (int)info.Frequency, outputChannels, 0);
                    while (true)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        int count = NativeAudio.BASS_ChannelGetData(transformed, buffer, buffer.Length);
                        if (count < 0)
                        {
                            if (NativeAudio.BASS_ErrorGetCode() == 45) break;
                            throw NativeAudio.Failure("音频处理失败");
                        }
                        if (count == 0) break;
                        using (MemoryStream converted = new MemoryStream(count))
                        using (BinaryWriter samples = new BinaryWriter(converted))
                        {
                            for (int position = 0; position + inputChannels * 2 <= count; position += inputChannels * 2)
                            {
                                for (int channel = 0; channel < outputChannels; channel++)
                                {
                                    double value;
                                    if (settings.Mono)
                                    {
                                        int sum = 0;
                                        for (int sourceChannel = 0; sourceChannel < inputChannels; sourceChannel++) sum += BitConverter.ToInt16(buffer, position + sourceChannel * 2);
                                        value = (double)sum / inputChannels;
                                    }
                                    else value = BitConverter.ToInt16(buffer, position + channel * 2);
                                    short sample = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(value * (double)settings.Volume / 100)));
                                    peak = Math.Max(peak, Math.Abs((int)sample));
                                    samples.Write(sample);
                                }
                            }
                            writer.Write(converted.GetBuffer(), 0, (int)converted.Length);
                        }
                        if (file.Length > uint.MaxValue - 44) throw new InvalidOperationException("音频过长，超出了 WAV 文件大小限制，请分段处理。");
                        if (inputLength > 0) progress(Math.Min(0.99, (double)NativeAudio.BASS_ChannelGetPosition(source, 0) / inputLength));
                    }
                    long size = file.Length - 44;
                    if (size == 0) throw new InvalidOperationException("音频处理结果为空。");
                    if (settings.Normalize && peak > 0) Normalize(file, peak, cancellation);
                    file.Position = 0;
                    WaveHeader(writer, (int)info.Frequency, outputChannels, (uint)size);
                }
            }
            finally
            {
                if (transformed != 0) NativeAudio.BASS_StreamFree(transformed);
                NativeAudio.BASS_StreamFree(source);
            }
        }

        private static void Normalize(FileStream file, int peak, CancellationToken cancellation)
        {
            double gain = 32000.0 / peak;
            byte[] buffer = new byte[65536];
            long position = 44;
            while (position < file.Length)
            {
                cancellation.ThrowIfCancellationRequested();
                file.Position = position;
                int count = file.Read(buffer, 0, (int)Math.Min(buffer.Length, file.Length - position));
                for (int index = 0; index + 1 < count; index += 2)
                {
                    short sample = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(BitConverter.ToInt16(buffer, index) * gain)));
                    buffer[index] = (byte)(sample & 255);
                    buffer[index + 1] = (byte)((sample >> 8) & 255);
                }
                file.Position = position;
                file.Write(buffer, 0, count);
                position += count;
            }
        }

        private static void WaveHeader(BinaryWriter writer, int rate, int channels, uint size)
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(size + 36);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)channels); writer.Write(rate);
            writer.Write(rate * channels * 2); writer.Write((short)(channels * 2)); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(size);
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "").Replace("\r", " ").Replace("\n", " ") + "\"";
        }

        private static void Encode(string wave, string output, Metadata tags, AudioSettings settings, CancellationToken cancellation)
        {
            string encoder;
            string arguments;
            if (settings.Format == "MP3")
            {
                encoder = "lame.exe";
                arguments = "--silent --cbr -b " + settings.Bitrate + " -q 2 " + Quote(wave) + " " + Quote(output);
            }
            else if (settings.Format == "OGG")
            {
                encoder = "oggenc.exe";
                arguments = "-Q -q 6 -o " + Quote(output) + " " + Quote(wave);
            }
            else
            {
                encoder = "faac.exe";
                arguments = "-w -q 100 -o " + Quote(output) + " " + Quote(wave);
            }
            using (Process process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, encoder), arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                StringBuilder errors = new StringBuilder();
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args) { if (args.Data != null) lock (errors) { if (errors.Length < 8192) errors.AppendLine(args.Data); } };
                process.OutputDataReceived += delegate { };
                if (!process.Start()) throw new InvalidOperationException("无法启动音频编码器。");
                process.BeginErrorReadLine(); process.BeginOutputReadLine();
                try
                {
                    while (!process.WaitForExit(150)) cancellation.ThrowIfCancellationRequested();
                    process.WaitForExit();
                    cancellation.ThrowIfCancellationRequested();
                    if (process.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length == 0)
                        throw new InvalidOperationException("编码失败：" + errors.ToString().Trim());
                }
                finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
            }
        }

        private static long Id3Length(Stream file)
        {
            byte[] header = new byte[10];
            file.Position = 0;
            if (file.Read(header, 0, 10) != 10 || Encoding.ASCII.GetString(header, 0, 3) != "ID3") return 0;
            long size = (header[6] & 127) * 2097152L + (header[7] & 127) * 16384L + (header[8] & 127) * 128L + (header[9] & 127);
            return Math.Min(file.Length, size + 10 + ((header[3] == 4 && (header[5] & 16) != 0) ? 10 : 0));
        }

        public static Metadata ReadMetadata(string filename)
        {
            Metadata tags = new Metadata();
            if (!string.Equals(Path.GetExtension(filename), ".mp3", StringComparison.OrdinalIgnoreCase)) return tags;
            try
            {
                using (FileStream file = File.OpenRead(filename))
                {
                    long end = Id3Length(file);
                    if (end < 10 || end > 16 * 1024 * 1024) return tags;
                    file.Position = 3;
                    int version = file.ReadByte();
                    if (version != 3 && version != 4) return tags;
                    file.Position = 5;
                    if ((file.ReadByte() & 0xC0) != 0) return tags;
                    file.Position = 10;
                    while (file.Position + 10 <= end)
                    {
                        byte[] header = new byte[10]; file.Read(header, 0, 10);
                        string identity = Encoding.ASCII.GetString(header, 0, 4);
                        if (header[0] == 0) break;
                        int length = version == 4 ? (header[4] << 21) | (header[5] << 14) | (header[6] << 7) | header[7] : (header[4] << 24) | (header[5] << 16) | (header[6] << 8) | header[7];
                        if (length <= 0 || file.Position + length > end) break;
                        byte[] content = new byte[length]; file.Read(content, 0, length);
                        if (identity == "COMM")
                        {
                            if (content.Length < 5) continue;
                            int start = 4;
                            int step = content[0] == 1 || content[0] == 2 ? 2 : 1;
                            for (; start + step <= content.Length; start += step)
                                if (content[start] == 0 && (step == 1 || content[start + 1] == 0)) { start += step; break; }
                            if (start < content.Length)
                            {
                                Encoding commentEncoding = content[0] == 3 ? Encoding.UTF8 : content[0] == 2 ? Encoding.BigEndianUnicode : content[0] == 1 ? Encoding.Unicode : Encoding.GetEncoding(28591);
                                tags.Comment = commentEncoding.GetString(content, start, content.Length - start).Trim('\0', '\uFEFF', '\uFFFE');
                            }
                            continue;
                        }
                        if (!identity.StartsWith("T")) continue;
                        Encoding encoding = content[0] == 3 ? Encoding.UTF8 : content[0] == 2 ? Encoding.BigEndianUnicode : content[0] == 1 ? Encoding.Unicode : Encoding.GetEncoding(28591);
                        string value = encoding.GetString(content, 1, content.Length - 1).Trim('\0', '\uFEFF', '\uFFFE');
                        if (identity == "TIT2") tags.Title = value;
                        else if (identity == "TPE1") tags.Artist = value;
                        else if (identity == "TALB") tags.Album = value;
                        else if (identity == "TYER" || identity == "TDRC") tags.Year = value;
                        else if (identity == "TRCK") tags.Track = value;
                    }
                }
            }
            catch (IOException) { }
            return tags;
        }

        private static byte[] WriteMetadata(Metadata tags)
        {
            using (MemoryStream frames = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(frames))
            {
                string[] identities = { "TIT2", "TPE1", "TALB", "TYER", "TRCK", "COMM" };
                string[] values = { tags.Title, tags.Artist, tags.Album, tags.Year, tags.Track, tags.Comment };
                for (int index = 0; index < identities.Length; index++)
                {
                    if (string.IsNullOrWhiteSpace(values[index])) continue;
                    byte[] text = Encoding.Unicode.GetBytes("\uFEFF" + values[index]);
                    byte[] prefix = index == 5 ? new byte[] { 1, 101, 110, 103, 255, 254, 0, 0 } : new byte[] { 1 };
                    int length = text.Length + prefix.Length;
                    writer.Write(Encoding.ASCII.GetBytes(identities[index]));
                    writer.Write(new byte[] { (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length, 0, 0 });
                    writer.Write(prefix); writer.Write(text);
                }
                int size = (int)frames.Length;
                using (MemoryStream result = new MemoryStream())
                {
                    result.Write(new byte[] { 73, 68, 51, 3, 0, 0, (byte)((size >> 21) & 127), (byte)((size >> 14) & 127), (byte)((size >> 7) & 127), (byte)(size & 127) }, 0, 10);
                    frames.Position = 0; frames.CopyTo(result);
                    return result.ToArray();
                }
            }
        }

        public void Dispose() { StopPreview(); }
    }
}
