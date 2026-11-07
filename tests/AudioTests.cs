using System;
using System.IO;

namespace WildfireAudioEditor
{
    internal static class AudioTests
    {
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length != 1 || Directory.Exists(args[0])) throw new ArgumentException("Provide a new test directory.");
                NativeAudio.Initialize(false);
                try { SelfTest.Run(args[0]); }
                finally { NativeAudio.BASS_Free(); }
                Console.WriteLine(File.ReadAllText(Path.Combine(args[0], "results.txt")));
                return 0;
            }
            catch (Exception error) { Console.WriteLine(error); return 1; }
        }
    }
}
