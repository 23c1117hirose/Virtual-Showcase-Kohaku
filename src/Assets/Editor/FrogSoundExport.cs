using System.IO;
using UnityEditor;
using UnityEngine;
using VirtualShowcase.Showcase;

/// <summary>
///     Writes the generated frog sounds to WAV files (Tools → Poke Task → Export Frog Sounds) so that they can be
///     listened to in any player, compared, or replaced by recorded sounds.
///     Batch: Unity -batchmode -nographics -executeMethod FrogSoundExport.RunBatch -- &lt;output folder&gt;
/// </summary>
public static class FrogSoundExport
{
    [MenuItem("Tools/Poke Task/Export Frog Sounds")]
    public static void RunFromMenu()
    {
        string folder = EditorUtility.OpenFolderPanel("Folder for the frog sound WAV files", string.Empty, string.Empty);
        if (!string.IsNullOrEmpty(folder))
        {
            Export(folder);
        }
    }

    public static void RunBatch()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        string folder = args.Length > 0 ? args[args.Length - 1] : "FrogSounds";
        Export(folder);
        EditorApplication.Exit(0);
    }

    private static void Export(string folder)
    {
        Directory.CreateDirectory(folder);
        Write(Path.Combine(folder, "frog_pecha.wav"), FrogSoundSynth.Pecha(11));
        Write(Path.Combine(folder, "grass_rustle.wav"), FrogSoundSynth.Rustle(5));
        Write(Path.Combine(folder, "old_thump.wav"), FrogSoundSynth.Thump());
        Debug.Log("[FrogSound] written to " + folder);
    }

    /// <summary>16-bit PCM mono WAV.</summary>
    private static void Write(string path, float[] samples)
    {
        using (var stream = new FileStream(path, FileMode.Create))
        using (var writer = new BinaryWriter(stream))
        {
            int dataBytes = samples.Length * 2;
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataBytes);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(FrogSoundSynth.SampleRate);
            writer.Write(FrogSoundSynth.SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataBytes);
            foreach (float sample in samples)
            {
                writer.Write((short)(Mathf.Clamp(sample, -1f, 1f) * 32767f));
            }
        }
    }
}
