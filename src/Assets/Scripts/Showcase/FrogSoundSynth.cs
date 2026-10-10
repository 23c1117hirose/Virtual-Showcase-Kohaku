using UnityEngine;

namespace VirtualShowcase.Showcase
{
    /// <summary>
    ///     Small sound effects for the frog, made from noise and tones at run time (no audio files needed):
    ///     <list type="bullet">
    ///         <item><see cref="Pecha" />: the light, wet "pecha" of a small, damp frog landing on the ground.</item>
    ///         <item><see cref="Rustle" />: a short rustle of grass when the frog leaves it or dives back in.</item>
    ///         <item><see cref="Thump" />: the earlier heavy "dosun" (kept for comparison only).</item>
    ///     </list>
    ///     The sample arrays are public so that they can be written to WAV files and inspected.
    /// </summary>
    public static class FrogSoundSynth
    {
        public const int SampleRate = 44100;

        public static AudioClip PechaClip(int seed = 11)
        {
            return ToClip("FrogPecha", Pecha(seed));
        }

        public static AudioClip RustleClip(int seed = 5)
        {
            return ToClip("GrassRustle", Rustle(seed));
        }

        public static AudioClip ThumpClip()
        {
            return ToClip("FrogThump", Thump());
        }

        private static AudioClip ToClip(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>
        ///     A wet slap: a very short bright burst (skin on the ground), a soft mid body, and a small "plop"
        ///     whose pitch drops quickly (like a bubble), followed by a tiny second plop.
        /// </summary>
        public static float[] Pecha(int seed)
        {
            const float duration = 0.16f;
            int count = Mathf.RoundToInt(SampleRate * duration);
            var data = new float[count];
            var rng = new System.Random(seed);

            float highLow = 0f;   // low-pass state used to make a high-pass
            float bodyLow1 = 0f;  // two low-passes make a band-pass for the body
            float bodyLow2 = 0f;
            float alphaHigh = OnePole(1500f);
            float alphaSlapTop = OnePole(4000f);
            float slapTop1 = 0f;  // two low-passes take the hiss off the top of the slap
            float slapTop2 = 0f;
            float alphaBody1 = OnePole(1500f);
            float alphaBody2 = OnePole(450f);
            float phase1 = 0f;
            float phase2 = 0f;

            for (var i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);

                // bright slap
                highLow += alphaHigh * (noise - highLow);
                slapTop1 += alphaSlapTop * ((noise - highLow) - slapTop1);
                slapTop2 += alphaSlapTop * (slapTop1 - slapTop2);
                float slap = slapTop2 * Mathf.Exp(-t * 75f) * 1.1f;

                // soft mid body
                bodyLow1 += alphaBody1 * (noise - bodyLow1);
                bodyLow2 += alphaBody2 * (noise - bodyLow2);
                float body = (bodyLow1 - bodyLow2) * Mathf.Exp(-t * 38f) * 0.9f;

                // bubble-like plop: pitch glides from about 900 Hz down to 380 Hz
                float frequency1 = 380f + 520f * Mathf.Exp(-t * 55f);
                phase1 += 2f * Mathf.PI * frequency1 / SampleRate;
                float plop = Mathf.Sin(phase1) * Mathf.Exp(-t * 40f) * (1f - Mathf.Exp(-t * 700f)) * 0.38f;

                // tiny second plop a little later
                float t2 = t - 0.045f;
                float plop2 = 0f;
                if (t2 > 0f)
                {
                    float frequency2 = 300f + 330f * Mathf.Exp(-t2 * 60f);
                    phase2 += 2f * Mathf.PI * frequency2 / SampleRate;
                    plop2 = Mathf.Sin(phase2) * Mathf.Exp(-t2 * 55f) * (1f - Mathf.Exp(-t2 * 700f)) * 0.17f;
                }

                data[i] = slap + body + plop + plop2;
            }

            FadeOutAndNormalize(data, 0.7f, 0.012f);
            return data;
        }

        /// <summary>
        ///     Grass rustle: bright filtered noise shaped by many short crackles at random moments on top of a soft swell.
        /// </summary>
        public static float[] Rustle(int seed)
        {
            const float duration = 0.5f;
            int count = Mathf.RoundToInt(SampleRate * duration);
            var data = new float[count];
            var rng = new System.Random(seed);

            // crackles
            var envelope = new float[count];
            for (var k = 0; k < 26; k++)
            {
                float center = 0.03f + (float)rng.NextDouble() * 0.40f;
                float strength = 0.25f + 0.75f * (float)rng.NextDouble();
                float tau = 0.006f + 0.016f * (float)rng.NextDouble();
                int start = Mathf.RoundToInt(center * SampleRate);
                int length = Mathf.RoundToInt(tau * 6f * SampleRate);
                for (var j = 0; j < length && start + j < count; j++)
                {
                    envelope[start + j] += strength * Mathf.Exp(-(float)j / SampleRate / tau);
                }
            }

            float lowHigh = 0f;
            float lowTop1 = 0f;
            float lowTop2 = 0f;
            float alphaHigh = OnePole(1500f);
            float alphaTop = OnePole(5000f);

            for (var i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);

                lowHigh += alphaHigh * (noise - lowHigh);
                float highPassed = noise - lowHigh;
                lowTop1 += alphaTop * (highPassed - lowTop1);
                lowTop2 += alphaTop * (lowTop1 - lowTop2);

                float swell = 0.25f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / duration)), 2f);
                data[i] = lowTop2 * (envelope[i] * 0.8f + swell);
            }

            FadeOutAndNormalize(data, 0.6f, 0.04f);
            return data;
        }

        /// <summary>The earlier heavy landing sound (a low tone around 85 Hz).</summary>
        public static float[] Thump()
        {
            const float duration = 0.18f;
            int count = Mathf.RoundToInt(SampleRate * duration);
            var data = new float[count];
            var rng = new System.Random(3);

            for (var i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float tone = Mathf.Sin(2f * Mathf.PI * 85f * t * (1f + 0.6f * Mathf.Exp(-t * 20f))) * Mathf.Exp(-t * 28f);
                float noise = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 60f);
                data[i] = tone * 0.8f + noise * 0.15f;
            }

            return data;
        }

        /// <summary>Coefficient of a one-pole low-pass filter with the given cut-off frequency.</summary>
        private static float OnePole(float cutoffHz)
        {
            return 1f - Mathf.Exp(-2f * Mathf.PI * cutoffHz / SampleRate);
        }

        private static void FadeOutAndNormalize(float[] data, float peak, float fadeSeconds)
        {
            float max = 0f;
            foreach (float value in data)
            {
                max = Mathf.Max(max, Mathf.Abs(value));
            }

            float gain = max > 1e-6f ? peak / max : 1f;
            int fade = Mathf.Max(1, Mathf.RoundToInt(fadeSeconds * SampleRate));
            for (var i = 0; i < data.Length; i++)
            {
                float fadeFactor = i >= data.Length - fade ? (float)(data.Length - i) / fade : 1f;
                data[i] *= gain * fadeFactor;
            }
        }
    }
}
