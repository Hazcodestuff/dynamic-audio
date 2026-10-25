using UnityEngine;

namespace DynamicAudio
{
    /// <summary>
    /// Tiny audio-thread gain stage. Replaces the old "divide/multiply AudioSource.volume" hack,
    /// which fought the game's own volume writes and left sounds stuck loud or silent.
    /// No allocations, no Unity API calls on the audio thread, per-block linear ramp (no clicks).
    /// </summary>
    public sealed class AcousticGainFilter : MonoBehaviour
    {
        private volatile float targetGain = 1f;
        private float currentGain = 1f;

        public float TargetGain
        {
            get { return targetGain; }
            set { targetGain = value; }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (data == null || channels < 1) return;

            float target = targetGain;
            if (float.IsNaN(target) || float.IsInfinity(target)) target = 1f;
            if (target < 0f) target = 0f;
            if (target > 1f) target = 1f;

            float g = currentGain;
            if (System.Math.Abs(g - target) < 0.0005f)
            {
                currentGain = target;
                if (target >= 0.9995f) return;               // identity
                for (int i = 0; i < data.Length; i++) data[i] *= target;
                return;
            }

            int frames = data.Length / channels;
            if (frames <= 0) return;
            float step = (target - g) / frames;
            int idx = 0;
            for (int f = 0; f < frames; f++)
            {
                g += step;
                for (int c = 0; c < channels; c++)
                {
                    data[idx] *= g;
                    idx++;
                }
            }
            currentGain = target;
        }
    }
}
