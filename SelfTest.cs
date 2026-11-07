using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WildfireAudioEditor
{
    internal static class SelfTest
    {
        private static void Require(bool valid, string message)
        {
            if (!valid) throw new Exception(message);
        }

        private static string Hash(string path)
        {
            using (FileStream file = File.OpenRead(path))
            using (SHA256 hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(file));
        }

        public static void Run(string directory)
        {
            Directory.CreateDirectory(directory);
            string input = Path.Combine(directory, "中文歌曲.wav");
            using (BinaryWriter writer = new BinaryWriter(File.Create(input)))
            {
                int length = 44100 * 5 * 2;
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(length + 36); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(44100); writer.Write(88200); writer.Write((short)2); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(length);
                for (int frame = 0; frame < 44100 * 5; frame++) writer.Write((short)(12000 * Math.Sin(2 * Math.PI * 440 * frame / 44100)));
            }
            string lyric = Path.ChangeExtension(input, ".lrc"); File.WriteAllText(lyric, "[00:00.00]中文歌词");
            string original = Hash(input);
            StringBuilder results = new StringBuilder();
            using (AudioEngine engine = new AudioEngine())
            {
                AudioItem item = engine.Probe(input, directory); item.Lyrics = new[] { lyric };
                item.Tags = new Metadata { Title = "测试歌曲", Artist = "野火测试", Comment = "测试备注" };
                foreach (string format in new[] { "WAV", "MP3", "OGG", "M4A" })
                {
                    string target = Path.Combine(directory, "导出", format, "中文歌曲." + format.ToLowerInvariant());
                    ProcessingResult result = engine.Process(item, target, new AudioSettings { Format = format }, CancellationToken.None, value => { });
                    Require(File.Exists(target) && new FileInfo(target).Length > 100, format + " output missing");
                    Require(result.LyricsCopied == 1, "lyrics not copied");
                    Require(Hash(Path.Combine(Path.GetDirectoryName(target), "中文歌曲.lrc")) == Hash(lyric), "lyrics mismatch");
                    if (format == "MP3") Require(AudioEngine.ReadMetadata(target).Title == "测试歌曲" && AudioEngine.ReadMetadata(target).Comment == "测试备注", "MP3 Unicode metadata mismatch");
                    Require(engine.Process(item, target, new AudioSettings { Format = format }, CancellationToken.None, value => { }).Skipped, "skip failed");
                    engine.Process(item, target, new AudioSettings { Format = format, ReplaceExisting = true }, CancellationToken.None, value => { });
                    results.AppendLine(format + ": export, lyrics, skip and replace passed");
                }
                AudioSettings[] variants = {
                    new AudioSettings { Format = "WAV", Tempo = 105.25m },
                    new AudioSettings { Format = "WAV", Pitch = .75m },
                    new AudioSettings { Format = "WAV", Rate = 102.35m },
                    new AudioSettings { Format = "WAV", Tempo = 105.25m, Pitch = .75m, Rate = 102.35m },
                    new AudioSettings { Format = "WAV", Volume = 50 },
                    new AudioSettings { Format = "WAV", Normalize = true }
                };
                for (int index = 0; index < variants.Length; index++)
                    engine.Process(item, Path.Combine(directory, "variant-" + index + ".wav"), variants[index], CancellationToken.None, value => { });
                Parallel.For(0, 10, new ParallelOptions { MaxDegreeOfParallelism = 3 }, index =>
                    engine.Process(item, Path.Combine(directory, "并行", "song-" + index + ".mp3"), new AudioSettings(), CancellationToken.None, value => { }));
                Require(Directory.GetFiles(Path.Combine(directory, "并行"), "*.mp3").Length == 10, "batch output missing");
                results.AppendLine("10 files, 3 workers: passed");
                string cancelledOutput = Path.Combine(directory, "cancelled.wav");
                using (CancellationTokenSource cancellation = new CancellationTokenSource())
                {
                    bool stopped = false;
                    try { engine.Process(item, cancelledOutput, new AudioSettings { Format = "WAV" }, cancellation.Token, value => cancellation.Cancel()); }
                    catch (OperationCanceledException) { stopped = true; }
                    Require(stopped && !File.Exists(cancelledOutput), "cancellation failed");
                }
                Require(!Directory.EnumerateFiles(directory, ".wildfire-*", SearchOption.AllDirectories).Any(), "temporary output remains");
                Require(Hash(input) == original, "original changed");
                bool sourceProtected = false;
                try { engine.Process(item, input, new AudioSettings { Format = "WAV", ReplaceExisting = true }, CancellationToken.None, value => { }); }
                catch (InvalidOperationException) { sourceProtected = true; }
                Require(sourceProtected, "source overwrite was allowed");
                results.AppendLine("cancellation, temporary cleanup and original hash: passed");
            }
            File.WriteAllText(Path.Combine(directory, "results.txt"), results.ToString(), Encoding.UTF8);
        }
    }
}
