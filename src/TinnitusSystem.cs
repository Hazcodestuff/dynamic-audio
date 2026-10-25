using UnityEngine;

namespace DynamicAudio
{
    /// <summary>
    /// Ear ringing (off by default). No longer creates its own AudioLowPassFilter on the listener
    /// (it used to fight the shockwave filter); it exposes MuffleCutoff and the listener system applies it.
    /// </summary>
    public sealed class TinnitusSystem
    {
        private GameObject host;
        private AudioSource tone;
        private float ringTime;
        private float ringDuration = 1f;
        private float ringPeak;
        private float exposure;
        private readonly float[] sampleBuf = new float[1024];
        private float sensitivity = 1f;
        private float ringVolume = 0.35f;
        private float muffleAmount = 0.6f;

        public float MuffleCutoff = 22000f;

        public bool Ringing { get { return ringTime > 0f; } }
        public float Exposure01 { get { return Mathf.Clamp01(exposure); } }

        public void SetHost(GameObject go)
        {
            host = go;
        }

        public void SetParams(float sens, float vol, float muffleAmt)
        {
            sensitivity = sens;
            ringVolume = vol;
            muffleAmount = muffleAmt;
        }

        public void TriggerExplosion(float severity01)
        {
            if (severity01 < 0.2f) return;
            StartRing(Mathf.Lerp(4f, 14f, Mathf.Clamp01(severity01)), Mathf.Clamp01(severity01));
        }

        private void StartRing(float duration, float severity)
        {
            ringDuration = Mathf.Max(1f, duration);
            ringTime = ringDuration;
            ringPeak = ringVolume * Mathf.Lerp(0.6f, 1f, severity);
        }

        public void AddExposure(float amount)
        {
            if (ringTime > 0f) return;
            exposure += amount;
            if (exposure >= 1f)
            {
                float over = Mathf.Clamp(exposure - 1f, 0f, 1f);
                exposure = 0f;
                StartRing(Mathf.Lerp(3f, 10f, over), Mathf.Lerp(0.5f, 1f, over));
            }
        }

        public void SampleLoudness(float enclosure)
        {
            if (ringTime > 0f) return;
            try
            {
                AudioListener.GetOutputData(sampleBuf, 0);
            }
            catch (System.Exception)
            {
                return;
            }
            float sum = 0f;
            for (int i = 0; i < sampleBuf.Length; i++) sum += sampleBuf[i] * sampleBuf[i];
            float rms = Mathf.Sqrt(sum / sampleBuf.Length);
            float enc = 1f + 0.8f * Mathf.Clamp01(enclosure);
            if (rms > 0.03f) AddExposure((rms - 0.02f) * 3f * sensitivity * enc * 0.1f);
            else exposure = Mathf.Max(0f, exposure - 0.002f);
        }

        public void Update(float dt, bool enabled)
        {
            if (!enabled)
            {
                if (ringTime > 0f || exposure > 0f || MuffleCutoff < 21000f || (tone != null && tone.isPlaying)) Shutdown();
                return;
            }
            if (ringTime <= 0f)
            {
                MuffleCutoff = 22000f;
                return;
            }

            ringTime -= dt;
            EnsureAudio();
            float elapsed = ringDuration - ringTime;
            float remaining = Mathf.Clamp01(ringTime / ringDuration);
            float env = elapsed < 0.25f ? elapsed / 0.25f : Mathf.Pow(remaining, 0.6f);

            if (tone != null)
            {
                if (!tone.isPlaying)
                {
                    tone.volume = 0f;
                    tone.Play();
                }
                tone.volume = Mathf.Clamp(ringPeak * env, 0f, 1f);
                if (ringTime <= 0f)
                {
                    tone.Stop();
                    tone.volume = 0f;
                }
            }
            MuffleCutoff = ringTime > 0f ? Mathf.Lerp(16000f, 4000f, muffleAmount * env) : 22000f;
        }

        public void Shutdown()
        {
            if (tone != null)
            {
                tone.Stop();
                tone.volume = 0f;
            }
            ringTime = 0f;
            exposure = 0f;
            ringPeak = 0f;
            MuffleCutoff = 22000f;
        }

        private void EnsureAudio()
        {
            if (tone != null) return;
            GameObject go = new GameObject("DynamicAudioTinnitusTone");
            if (host != null) go.transform.SetParent(host.transform, false);
            tone = go.AddComponent<AudioSource>();
            tone.spatialBlend = 0f;
            tone.loop = true;
            tone.playOnAwake = false;
            tone.volume = 0f;
            tone.priority = 0;
            tone.clip = SynthesizeTone();
        }

        private static AudioClip SynthesizeTone()
        {
            const int sampleRate = 44100;
            const int length = 88200;   // 2 s: every component below completes a whole number of cycles
            float[] data = new float[length];
            for (int i = 0; i < length; i++)
            {
                float t = (float)i / sampleRate;
                float wobble = 0.8f + 0.2f * Mathf.Sin(6.2831853f * t);                  // 1 Hz
                float s = Mathf.Sin(26389.378f * t) * 0.55f      // 4200 Hz
                        + Mathf.Sin(39584.07f * t) * 0.2f        // 6300 Hz
                        + Mathf.Sin(52778.758f * t) * 0.1f;      // 8400 Hz
                data[i] = s * wobble * 0.85f;
            }
            AudioClip clip = AudioClip.Create("DynamicAudioTinnitusTone", length, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
