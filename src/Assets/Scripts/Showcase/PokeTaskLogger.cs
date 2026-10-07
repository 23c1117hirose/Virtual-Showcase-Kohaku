using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace VirtualShowcase.Showcase
{
    /// <summary>
    ///     One finished trial of the poke task. NaN means "not measurable" (e.g. no hand tracked)
    ///     and is written as an empty CSV cell.
    /// </summary>
    public class PokeTrialRecord
    {
        public string Participant;
        public string Condition;
        public int Sequence;
        public bool IsPractice;
        public int Trial;
        public int Patch;
        public int Clip;
        public bool Hit;

        public float ReactionFromAppear = float.NaN;
        public float ReactionFromLanding = float.NaN;
        public float ReactionFromCue = float.NaN;
        public float HitError = float.NaN;

        public float TipToPatchAtCue = float.NaN;
        public float TipToPatchAtAppear = float.NaN;
        public float TipToPatchMinBeforeAppear = float.NaN;

        public int WrongPatchEntries;
        public float HandTrackedRatio = float.NaN;
        public bool EarlyTouch;
    }

    /// <summary>
    ///     Writes <see cref="PokeTrialRecord" />s to a CSV in Application.persistentDataPath/PokeTaskLogs.
    ///     Flushed after every trial so an aborted session keeps its data.
    /// </summary>
    public sealed class PokeTaskLogger : System.IDisposable
    {
        private const string Header =
            "timestamp,participant,condition,sequence,phase,trial,patch,clip,result," +
            "rt_from_appear_s,rt_from_landing_s,rt_from_cue_s,hit_error_cm," +
            "tip_to_patch_at_cue_cm,tip_to_patch_at_appear_cm,tip_to_patch_min_before_appear_cm," +
            "wrong_patch_entries,hand_tracked_ratio,early_touch";

        private readonly StreamWriter _writer;

        public string FilePath { get; }

        public PokeTaskLogger(string participant, string condition)
        {
            string directory = Path.Combine(Application.persistentDataPath, "PokeTaskLogs");
            Directory.CreateDirectory(directory);

            string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            FilePath = Path.Combine(directory, $"{Sanitize(participant)}_{Sanitize(condition)}_{stamp}.csv");

            _writer = new StreamWriter(FilePath, false, new UTF8Encoding(true));
            _writer.WriteLine(Header);
            _writer.Flush();
        }

        public void Write(PokeTrialRecord record)
        {
            string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var line = new StringBuilder();
            line.Append(timestamp).Append(',')
                .Append(Sanitize(record.Participant)).Append(',')
                .Append(Sanitize(record.Condition)).Append(',')
                .Append(record.Sequence).Append(',')
                .Append(record.IsPractice ? "practice" : "main").Append(',')
                .Append(record.Trial).Append(',')
                .Append(record.Patch).Append(',')
                .Append(record.Clip).Append(',')
                .Append(record.Hit ? "hit" : "miss").Append(',')
                .Append(Format(record.ReactionFromAppear)).Append(',')
                .Append(Format(record.ReactionFromLanding)).Append(',')
                .Append(Format(record.ReactionFromCue)).Append(',')
                .Append(Format(record.HitError)).Append(',')
                .Append(Format(record.TipToPatchAtCue)).Append(',')
                .Append(Format(record.TipToPatchAtAppear)).Append(',')
                .Append(Format(record.TipToPatchMinBeforeAppear)).Append(',')
                .Append(record.WrongPatchEntries).Append(',')
                .Append(Format(record.HandTrackedRatio)).Append(',')
                .Append(record.EarlyTouch ? 1 : 0);

            _writer.WriteLine(line.ToString());
            _writer.Flush();
        }

        public void Dispose()
        {
            _writer.Dispose();
        }

        private static string Format(float value)
        {
            return float.IsNaN(value) ? string.Empty : value.ToString("F3", CultureInfo.InvariantCulture);
        }

        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "unknown";
            }

            var builder = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                builder.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            }

            return builder.ToString();
        }
    }
}
