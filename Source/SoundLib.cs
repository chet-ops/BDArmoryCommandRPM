// BDArmoryCommandRPM - avionics sound synthesis (no audio files).
// Enveloped tones, sweeps, "warble" alternations, filtered noise and a "cockpit speaker"
// filter so they don't sound like pure lab waveforms.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BDArmoryCommandRPM
{
    public static class SoundLib
    {
        public const int Rate = 44100;
        private static readonly System.Random rng = new System.Random(1234);

        public enum Wave { Sine, Soft, Square }

        private static float Osc(Wave w, double phase)
        {
            double s = Math.Sin(phase);
            switch (w)
            {
                case Wave.Soft: return (float)(s + Math.Sin(3 * phase) / 4.0 + Math.Sin(5 * phase) / 10.0) * 0.75f;
                case Wave.Square: return s >= 0 ? 0.6f : -0.6f;
                default: return (float)s;
            }
        }

        public static float[] Tone(float freq, float dur, float vol = 0.6f, Wave w = Wave.Soft, float attack = 0.004f, float release = 0.02f)
        {
            return Sweep(freq, freq, dur, vol, w, attack, release);
        }

        public static float[] Sweep(float f0, float f1, float dur, float vol = 0.6f, Wave w = Wave.Soft, float attack = 0.004f, float release = 0.02f)
        {
            int n = Mathf.Max(1, (int)(dur * Rate));
            float[] d = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float f = Mathf.Lerp(f0, f1, t);
                phase += 2 * Math.PI * f / Rate;
                d[i] = Osc(w, phase) * vol * Envelope(i, n, attack, release);
            }
            return d;
        }

        /// <summary>Tone that decays like a chime.</summary>
        public static float[] Chime(float freq, float dur, float vol = 0.6f)
        {
            int n = (int)(dur * Rate);
            float[] d = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate;
                double env = Math.Exp(-t * 5.0) * Math.Min(1.0, t / 0.003);
                d[i] = (float)((Math.Sin(2 * Math.PI * freq * t) + 0.3 * Math.Sin(2 * Math.PI * freq * 2.01 * t)) * env) * vol * 0.8f;
            }
            return d;
        }

        public static float[] Warble(float fa, float fb, float step, float dur, float vol = 0.6f, Wave w = Wave.Soft)
        {
            List<float[]> parts = new List<float[]>();
            bool a = true;
            for (float t = 0; t < dur; t += step)
            {
                parts.Add(Tone(a ? fa : fb, step, vol, w, 0.002f, 0.004f));
                a = !a;
            }
            return Concat(parts.ToArray());
        }

        public static float[] Noise(float dur, float vol, float smooth)
        {
            int n = (int)(dur * Rate);
            float[] d = new float[n];
            float y = 0;
            for (int i = 0; i < n; i++)
            {
                float x = (float)(rng.NextDouble() * 2 - 1);
                y += (x - y) * smooth; // simple low-pass
                d[i] = y * vol * Envelope(i, n, 0.001f, 0.004f);
            }
            return d;
        }

        public static float[] Silence(float dur)
        {
            return new float[Mathf.Max(1, (int)(dur * Rate))];
        }

        public static float[] Concat(params float[][] parts)
        {
            int n = 0;
            foreach (float[] p in parts) n += p.Length;
            float[] d = new float[n];
            int o = 0;
            foreach (float[] p in parts)
            {
                Array.Copy(p, 0, d, o, p.Length);
                o += p.Length;
            }
            return d;
        }

        public static float[] Mix(params float[][] parts)
        {
            int n = 0;
            foreach (float[] p in parts) n = Math.Max(n, p.Length);
            float[] d = new float[n];
            foreach (float[] p in parts)
                for (int i = 0; i < p.Length; i++) d[i] += p[i];
            return d;
        }

        public static float[] Repeat(float[] part, float gap, int times)
        {
            List<float[]> parts = new List<float[]>();
            for (int i = 0; i < times; i++)
            {
                parts.Add(part);
                if (i < times - 1) parts.Add(Silence(gap));
            }
            return Concat(parts.ToArray());
        }

        private static float Envelope(int i, int n, float attack, float release)
        {
            float a = attack * Rate, r = release * Rate;
            float e = 1f;
            if (a > 0 && i < a) e = i / a;
            if (r > 0 && i > n - r) e = Mathf.Min(e, (n - i) / r);
            return Mathf.Clamp01(e);
        }

        /// <summary>Cockpit speaker filter: cuts extreme lows and highs and saturates a little.</summary>
        public static float[] Speaker(float[] d) { return Speaker(d, 1f, 0.85f); }

        /// <summary>drive: how hard the speaker is pushed (louder and a bit harsher); ceiling: maximum peak.</summary>
        public static float[] Speaker(float[] d, float drive, float ceiling)
        {
            float lp = 0, hpIn = 0, hpOut = 0;
            const float lpA = 0.35f;   // ~4 kHz
            const float hpA = 0.985f;  // ~100 Hz
            for (int i = 0; i < d.Length; i++)
            {
                lp += (d[i] - lp) * lpA;
                float hp = hpA * (hpOut + lp - hpIn);
                hpIn = lp;
                hpOut = hp;
                d[i] = (float)Math.Tanh(hp * 1.3f * drive) * ceiling;
            }
            return d;
        }

        public static AudioClip Make(string name, float[] data, bool speaker = true, bool alarm = false)
        {
            // alarms come out of the speaker at full blast, like in a real fighter: more level and some saturation
            if (speaker) data = alarm ? Speaker(data, 2.2f, 0.97f) : Speaker(data);
            AudioClip clip = AudioClip.Create("BDArmoryCommandRPM_" + name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Seamless loop for the camera servo (1 s, integer frequencies, periodic noise).</summary>
        public static float[] ServoLoop()
        {
            int n = Rate;
            float[] white = new float[n];
            for (int i = 0; i < n; i++) white[i] = (float)(rng.NextDouble() * 2 - 1);
            // filter the noise twice around so the filter state is periodic
            float[] noise = new float[n];
            float y = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < n; i++)
                {
                    y += (white[i] - y) * 0.08f;
                    noise[i] = y;
                }
            float[] d = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / Rate;
                d[i] = (float)(0.22 * Math.Sin(2 * Math.PI * 180 * t) + 0.12 * Math.Sin(2 * Math.PI * 360 * t)
                             + 0.05 * Math.Sin(2 * Math.PI * 1440 * t)) + noise[i] * 0.35f;
            }
            return d;
        }
    }
}
