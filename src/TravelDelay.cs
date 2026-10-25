// Experimental speed-of-sound delay.
//
// V6 delayed sounds with an OnAudioFilterRead ring buffer on sources that were ALREADY playing. That
// cannot work: the voice ends when the clip ends, so the start was silenced and the delayed tail was cut
// off (distant gunshots vanished), and every filter allocated several MB on the audio thread.
//
// This version delays the START of a sound instead, by hooking AudioSource.Play / PlayOneShot with Harmony
// (bundled with BepInEx 5). It is OFF by default. Define NO_HARMONY in the project to compile it out.
#if !NO_HARMONY
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DynamicAudio
{
    internal static class TravelDelay
    {
        private struct Pending
        {
            public AudioSource Source;
            public AudioClip Clip;
            public float Volume;
            public float DueTime;
        }

        private const int MaxPending = 96;
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

        private static bool ShouldDelay(AudioSource src, out float delay)
        {
            delay = 0f;
            DynamicAudioPlugin p = DynamicAudioPlugin.Instance;
            if (p == null || !p.TravelDelayActive) return false;
            if (src == null || !src.isActiveAndEnabled || src.spatialBlend < 0.5f) return false;
            if (p.IsDelayExempt(src)) return false;        // own gun / own vehicle / reload sounds
            Transform ear = p.Ear;
            if (ear == null) return false;

            float d = Vector3.Distance(ear.position, src.transform.position);
            if (d <= p.cfgSoundDelayMinDistance.Value) return false;

            SoundCategory cat = p.ClassifyCached(src);
            if (cat == SoundCategory.Footstep || cat == SoundCategory.Environment || cat == SoundCategory.Other) return false;
            if (!p.CategoryEnabled(cat)) return false;

            float speed = Mathf.Max(50f, p.cfgSoundSpeed.Value);
            delay = Mathf.Min(d / speed, p.cfgSoundDelayMax.Value);
            return delay >= 0.04f;
        }

        public static bool PlayPrefix(AudioSource __instance)
        {
            try
            {
                if (bypass) return true;
                float delay;
                if (!ShouldDelay(__instance, out delay)) return true;
                bypass = true;
                try { __instance.PlayDelayed(delay); }
                finally { bypass = false; }
                return false;
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

        // Returns true to let the original run, false when the sound was queued for later.
        private static bool Enqueue(AudioSource src, AudioClip clip, float volume)
        {
            try
            {
                if (bypass || clip == null) return true;
                if (queue.Count >= MaxPending) return true;
                float delay;
                if (!ShouldDelay(src, out delay)) return true;
                Pending p = new Pending();
                p.Source = src;
                p.Clip = clip;
                p.Volume = volume;
                p.DueTime = Time.time + delay;
                queue.Add(p);
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        public static void Tick(float now)
        {
            if (queue.Count == 0) return;
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                Pending p = queue[i];
                if (now < p.DueTime) continue;
                queue.RemoveAt(i);
                PlayNow(p);
            }
        }

        public static void Flush()
        {
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                Pending p = queue[i];
                queue.RemoveAt(i);
                PlayNow(p);
            }
        }

        private static void PlayNow(Pending p)
        {
            if (p.Source == null || p.Clip == null || !p.Source.isActiveAndEnabled) return;
            bypass = true;
            try { p.Source.PlayOneShot(p.Clip, p.Volume); }
            catch (Exception) { }
            finally { bypass = false; }
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
