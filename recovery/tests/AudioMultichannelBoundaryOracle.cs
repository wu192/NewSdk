using System;
using System.IO;
using System.Reflection;

// Deliberate unrecovered-input boundaries, not original rejection parity.
internal static class AudioMultichannelBoundaryOracle
{
    private static MethodInfo read;
    private static int count;
    private static void Reject(string name, string path)
    {
        try { read.Invoke(null, new object[] { File.ReadAllBytes(path), false }); }
        catch (TargetInvocationException error) {
            if (error.InnerException is NotSupportedException) { ++count; return; }
            throw;
        }
        throw new Exception("Unrecovered multichannel input accepted: " + name);
    }
    private static int Main(string[] args)
    {
        try {
            Assembly library = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            read = library.GetType("BinaryAssetBuilder.AudioCompiler.AudioPcmInputReader", true).GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic);
            Directory.CreateDirectory(args[1]); string path = Path.Combine(args[1], "multichannel-boundary.wav");
            foreach (int channels in new[] { 3, 5, 7 }) foreach (int bits in new[] { 8, 16, 24 }) foreach (bool ext in new[] { false, true }) {
                AudioMultichannelCases.Write(path, bits, channels, 129, ext, -1); Reject("WAVE" + channels + ":" + bits + ":" + ext, path);
            }
            foreach (int channels in new[] { 3,5,8 }) foreach (int bits in new[] { 8,16,24 }) {
                AudioInputFormatCases.Write(path, true, bits, channels, 129); Reject("AIFF" + channels + ":" + bits, path);
                AudioAifcInputCases.Write(path, bits, channels, 129, "empty", "NONE"); Reject("AIFC/NONE" + channels + ":" + bits, path);
            }
            foreach (int channels in new[] { 4,6 }) foreach (int bits in new[] { 8,16,24 }) {
                AudioAdp4InputCases.Write(path, bits, channels, 129, 3, 129); Reject("ADP4" + channels + ":" + bits, path);
            }
            if (count != 42) throw new Exception("Incomplete multichannel refusal set: " + count);
            Console.WriteLine("PASS 42 multichannel boundaries: WAVE3/5/7 and AIFF/AIFC NONE3/5/8 and ADP4 above stereo remain unsupported"); return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
