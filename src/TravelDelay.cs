// Experimental speed-of-sound delay.
//
// V6 delayed sounds with an OnAudioFilterRead ring buffer on sources that were ALREADY playing. That
// cannot work: the voice ends when the clip ends, so the start was silenced and the delayed tail was cut
// off (distant gunshots vanished), and every filter allocated several MB on the audio thread.
//
// This version delays the START of a sound instead, by hooking AudioSource.Play / PlayOneShot with Harmony
// (bundled with BepInEx 5) and starting a temporary copy of the source later. It is OFF by default.
// Define NO_HARMONY in the project to compile it out.
#if !NO_HARMONY
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DynamicAudio
{
    /// <summary>
    /// The delayed sound is NOT replayed on the game's own (pooled, constantly reused) AudioSource. Instead the
    /// call is cancelled and a short-lived copy of the source is created at the shot's position and started
    /// when the sound "arrives". The game's source is never touched afterwards, so pooled sources being
    /// recycled, moved or re-volumed during the delay can no longer cause loud/misplaced/doubled sounds.
    /// The timing uses game time, so it freezes with the pause menu.
    /// </summary>
    internal static class TravelDelay
    {
        private sealed class Pending
        {
            public GameObject Go;
            public AudioSource Source;
            public float DueTime;
            public float DestroyAt;     // set once started
            public bool Started;
        }

        private const int MaxPending = 40;
        private static readonly List<Pending> queue = new List<Pending>(MaxPending);
        private static Harmony harmony;
        private static bool bypass;

        public static bool Installed { get; private set; }
        public static bool Failed { get; private set; }

        public static void EnsureInstalled(ManualLogSource log)
        {
            if (Installed || Failed) return;
            try
            {
                harmony = new Harmony("lexge.dynamicaudio.traveldelay");
                BindingFlags self = BindingFlags.Public | BindingFlags.Static;

                MethodInfo play = typeof(AudioSource).GetMethod("Play", Type.EmptyTypes);
                MethodInfo one1 = typeof(AudioSource).GetMethod("PlayOneShot", new Type[] { typeof(AudioClip) });
                MethodInfo one2 = typeof(AudioSource).GetMethod("PlayOneShot", new Type[] { typeof(AudioClip), typeof(float) });
                if (play == null || one2 == null) throw new InvalidOperationException("AudioSource methods not found");

                harmony.Patch(play, new HarmonyMethod(typeof(TravelDelay).GetMethod("PlayPrefix", self)));
                harmony.Patch(one2, new HarmonyMethod(typeof(TravelDelay).GetMethod("OneShotPrefix2", self)));
                if (one1 != null) harmony.Patch(one1, new HarmonyMethod(typeof(TravelDelay).GetMethod("OneShotPrefix1", self)));

                Installed = true;
                if (log != null) log.LogInfo("Travel delay hook installed (experimental).");
            }
            catch (Exception e)
            {
                Failed = true;
                if (log != null) log.LogWarning("Travel delay hook could not be installed, feature disabled: " + e.Message);
                try { if (harmony != null) harmony.UnpatchSelf(); } catch (Exception) { }
            }
        }

        public static void Uninstall()
        {
            Flush();
            try { if (harmony != null) harmony.UnpatchSelf(); } catch (Exception) { }
            Installed = false;
        }

        private static bool ShouldDelay(AudioSource src, out float delay, out SoundCategory cat)
        {
            delay = 0f;
            cat = SoundCategory.Other;
            DynamicAudioPlugin p = DynamicAudioPlugin.Instance;
            if (p == null || !p.TravelDelayActive) return false;
            if (src == null || !src.isActiveAndEnabled || src.spatialBlend < 0.5f || src.loop) return false;
            if (queue.Count >= MaxPending) return false;
            if (p.IsDelayExempt(src)) return false;        // own gun / own vehicle / reload sounds
            Transform ear = p.Ear;
            if (ear == null) return false;

            float d = Vector3.Distance(ear.position, src.transform.position);
            if (d <= p.cfgSoundDelayMinDistance.Value) return false;

            cat = p.ClassifyCached(src);
            // Only the big, distant bangs are delayed: shots and explosions.
            if (cat != SoundCategory.Gunshot && cat != SoundCategory.Explosion) return false;
            if (!p.CategoryEnabled(cat)) return false;

            float speed = Mathf.Max(50f, p.cfgSoundSpeed.Value);
            delay = Mathf.Min(d / speed, p.cfgSoundDelayMax.Value);
            return delay >= 0.04f;
        }

        public static bool PlayPrefix(AudioSource __instance)
        {
            try
            {
                if (bypass || __instance == null) return true;
                AudioClip clip = __instance.clip;
                if (clip == null) return true;
                float delay;
                SoundCategory cat;
                if (!ShouldDelay(__instance, out delay, out cat)) return true;
                return !Defer(__instance, clip, 1f, delay, cat);
            }
            catch (Exception)
            {
                return true;
            }
        }

        public static bool OneShotPrefix2(AudioSource __instance, AudioClip __0, float __1)
        {
            return Enqueue(__instance, __0, __1);
        }

        public static bool OneShotPrefix1(AudioSource __instance, AudioClip __0)
        {
            return Enqueue(__instance, __0, 1f);
        }

        // Returns true to let the original run, false when the sound was deferred.
        private static bool Enqueue(AudioSource src, AudioClip clip, float volume)
        {
            try
            {
                if (bypass || clip == null || src == null) return true;
                float delay;
                SoundCategory cat;
                if (!ShouldDelay(src, out delay, out cat)) return true;
                return !Defer(src, clip, volume, delay, cat);
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>Creates the delayed copy. Returns false (nothing happened) if anything goes wrong.</summary>
        private static bool Defer(AudioSource src, AudioClip clip, float volume, float delay, SoundCategory cat)
        {
            GameObject go = null;
            try
            {
                go = new GameObject("DynamicAudioDelayed");
                go.transform.position = src.transform.position;
                AudioSource a = go.AddComponent<AudioSource>();
                a.playOnAwake = false;
                a.loop = false;
                a.clip = clip;
                a.volume = Mathf.Clamp01(src.volume * Mathf.Clamp(volume, 0f, 4f));
                a.pitch = src.pitch;
                a.outputAudioMixerGroup = src.outputAudioMixerGroup;
                a.spatialBlend = src.spatialBlend;
                a.spread = src.spread;
                a.dopplerLevel = src.dopplerLevel;
                a.minDistance = src.minDistance;
                a.maxDistance = src.maxDistance;
                a.rolloffMode = src.rolloffMode;
                a.priority = src.priority;
                a.reverbZoneMix = src.reverbZoneMix;
                a.bypassEffects = src.bypassEffects;
                a.bypassListenerEffects = src.bypassListenerEffects;
                a.bypassReverbZones = src.bypassReverbZones;
                a.ignoreListenerPause = src.ignoreListenerPause;
                if (src.rolloffMode == AudioRolloffMode.Custom)
                {
                    AnimationCurve c = src.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
                    if (c != null) a.SetCustomCurve(AudioSourceCurveType.CustomRolloff, c);
                }

                DynamicAudioPlugin p = DynamicAudioPlugin.Instance;
                if (p != null) p.RegisterCategory(a, cat);

                go.SetActive(false);      // dormant until the sound "arrives"
                Pending pd = new Pending();
                pd.Go = go;
                pd.Source = a;
                pd.DueTime = Time.time + delay;
                queue.Add(pd);
                return true;
            }
            catch (Exception)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                return false;
            }
        }

        public static void Tick(float now)
        {
            if (queue.Count == 0) return;
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                Pending p = queue[i];
                if (p.Go == null)
                {
                    queue.RemoveAt(i);
                    continue;
                }
                if (!p.Started)
                {
                    if (now < p.DueTime) continue;
                    p.Started = true;
                    float len = (p.Source.clip != null ? p.Source.clip.length : 1f) / Mathf.Max(0.1f, Mathf.Abs(p.Source.pitch));
                    p.DestroyAt = now + len + 0.5f;
                    bypass = true;
                    try
                    {
                        p.Go.SetActive(true);
                        p.Source.Play();
                    }
                    catch (Exception) { }
                    finally { bypass = false; }
                }
                else if (now >= p.DestroyAt)
                {
                    UnityEngine.Object.Destroy(p.Go);
                    queue.RemoveAt(i);
                }
            }
        }

        /// <summary>Drops everything pending (master switch off, pause, shutdown). Stale sounds are discarded, not played late.</summary>
        public static void Flush()
        {
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                if (queue[i].Go != null) UnityEngine.Object.Destroy(queue[i].Go);
            }
            queue.Clear();
        }
    }
}
#else
using BepInEx.Logging;

namespace DynamicAudio
{
    internal static class TravelDelay
    {
        public static bool Installed { get { return false; } }
        public static bool Failed { get { return true; } }
        public static void EnsureInstalled(ManualLogSource log) { }
        public static void Uninstall() { }
        public static void Tick(float now) { }
        public static void Flush() { }
    }
}
#endif
