using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace SevenDashesToDie
{
    // ---------------------------------------------------------------------------------
    // The dash sound. The clip is a plain PCM .wav parsed by hand and turned into an
    // AudioClip, so no Unity asset bundle and no Unity Editor are needed.
    //
    // AudioListener.volume is driven by the game's master volume (GameOptionsManager sets
    // it from OptionsOverallAudioVolumeLevel), so the clip follows the audio options by
    // itself - do NOT multiply the pref in again or it scales twice.
    // ---------------------------------------------------------------------------------
    public static class DashSound
    {
        const string ClipFile = "Resources/dash1.wav";

        static AudioSource source;
        static AudioClip clip;
        static bool loadFailed;

        public static void Play(float volume)
        {
            if (loadFailed || volume <= 0f) return;

            if (clip == null)
            {
                string path = Path.Combine(SevenDashesMod.ModPath,
                                           ClipFile.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    clip = LoadWav(path, "SevenDashesDash");
                }
                catch (Exception e)
                {
                    loadFailed = true;
                    Log.Error(SevenDashesMod.LogPrefix + "could not load " + path + ": " + e.Message);
                    return;
                }
            }

            if (source == null)
            {
                GameObject go = new GameObject("SevenDashesAudio");
                UnityEngine.Object.DontDestroyOnLoad(go);
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f; // 2D - it is the player's own ability
                source.priority = 0;
            }

            // A dash can retrigger before the previous clip ends (rank 5, two air dashes).
            // PlayOneShot layers rather than cutting off, which is what we want here.
            source.PlayOneShot(clip, volume);
        }

        static AudioClip LoadWav(string path, string name)
        {
            byte[] d = File.ReadAllBytes(path);
            if (d.Length < 44 ||
                Encoding.ASCII.GetString(d, 0, 4) != "RIFF" ||
                Encoding.ASCII.GetString(d, 8, 4) != "WAVE")
                throw new Exception("not a RIFF/WAVE file");

            int channels = 0, sampleRate = 0, bits = 0, format = 0;
            int dataAt = -1, dataLen = 0;

            // Walk the chunks rather than assuming a 44-byte header: encoders happily put a
            // LIST/INFO chunk between fmt and data (ffmpeg does, and dash1.wav has one).
            int pos = 12;
            while (pos + 8 <= d.Length)
            {
                string id = Encoding.ASCII.GetString(d, pos, 4);
                int size = BitConverter.ToInt32(d, pos + 4);
                int body = pos + 8;
                if (size < 0 || body + size > d.Length) size = d.Length - body;

                if (id == "fmt " && size >= 16)
                {
                    format = BitConverter.ToInt16(d, body);
                    channels = BitConverter.ToInt16(d, body + 2);
                    sampleRate = BitConverter.ToInt32(d, body + 4);
                    bits = BitConverter.ToInt16(d, body + 14);
                }
                else if (id == "data")
                {
                    dataAt = body;
                    dataLen = size;
                }
                pos = body + size + (size & 1); // chunks are word-aligned
            }

            if (dataAt < 0 || channels <= 0 || sampleRate <= 0)
                throw new Exception("missing fmt/data chunk");
            if (format != 1 && !(format == 3 && bits == 32))
                throw new Exception("unsupported wav format " + format + " (only PCM / 32-bit float)");

            int bytesPerSample = bits / 8;
            if (bytesPerSample <= 0) throw new Exception("unsupported bit depth " + bits);
            int total = dataLen / bytesPerSample;
            float[] samples = new float[total];

            for (int i = 0; i < total; i++)
            {
                int o = dataAt + i * bytesPerSample;
                switch (bits)
                {
                    case 8: samples[i] = (d[o] - 128) / 128f; break;
                    case 16: samples[i] = BitConverter.ToInt16(d, o) / 32768f; break;
                    case 24: samples[i] = ((d[o] | (d[o + 1] << 8) | ((sbyte)d[o + 2] << 16))) / 8388608f; break;
                    case 32:
                        samples[i] = format == 3
                            ? BitConverter.ToSingle(d, o)
                            : BitConverter.ToInt32(d, o) / 2147483648f;
                        break;
                    default: throw new Exception("unsupported bit depth " + bits);
                }
            }

            // AudioClip.Create wants the sample count PER CHANNEL; SetData wants the
            // interleaved array. Mixing those up changes pitch and length.
            AudioClip c = AudioClip.Create(name, total / channels, channels, sampleRate, false);
            c.SetData(samples, 0);
            Log.Out(SevenDashesMod.LogPrefix + "dash clip loaded (" + channels + "ch " + sampleRate + "Hz " +
                    bits + "bit, " + (total / channels / (float)sampleRate).ToString("0.00") + "s)");
            return c;
        }
    }
}
