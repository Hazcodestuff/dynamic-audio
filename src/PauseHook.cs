#if !NO_HARMONY
using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace DynamicAudio
{
    /// <summary>
    /// Ravenfield pauses sound with AudioListener.pause = true (AudioManager.PausePlayback). Unity can leave
    /// custom/effect filters on a paused audio graph looping one stale 1024-sample buffer, which is heard as
    /// a monotone drone for as long as the pause menu is open. Removing the filters one frame AFTER the
    /// pause is too late, so this hook runs first: it takes our filters off the graph, waits a few frames,
    /// and only then lets the game's own pause happen.
    /// </summary>
    internal static class PauseHook
    {
        private static Harmony harmony;
        private static bool bypass;
        public static bool Installed { get; private set; }

        public static void Install(ManualLogSource log)
        {
            if (Installed) return;
            try
            {
                MethodInfo pause = typeof(AudioManager).GetMethod("PausePlayback", BindingFlags.Public | BindingFlags.Static);
                MethodInfo resume = typeof(AudioManager).GetMethod("ResumePlayback", BindingFlags.Public | BindingFlags.Static);
                if (pause == null || resume == null) throw new InvalidOperationException("AudioManager pause methods not found");
                harmony = new Harmony("lexge.dynamicaudio.pausehook");
                BindingFlags self = BindingFlags.Public | BindingFlags.Static;
                harmony.Patch(pause, new HarmonyMethod(typeof(PauseHook).GetMethod("PausePrefix", self)));
                harmony.Patch(resume, new HarmonyMethod(typeof(PauseHook).GetMethod("ResumePrefix", self)));
                Installed = true;
                if (log != null) log.LogInfo("Pause hook installed.");
            }
            catch (Exception e)
            {
                if (log != null) log.LogWarning("Pause hook not installed (filters are removed one frame after pausing instead): " + e.Message);
                try { if (harmony != null) harmony.UnpatchSelf(); } catch (Exception) { }
            }
        }

        public static void Uninstall()
        {
            try { if (harmony != null) harmony.UnpatchSelf(); } catch (Exception) { }
            Installed = false;
        }

        /// <summary>Called by the plugin a few frames later to perform the pause the game asked for.</summary>
        public static void ApplyPause()
        {
            bypass = true;
            try { AudioManager.PausePlayback(); }
            catch (Exception) { }
            finally { bypass = false; }
        }

        public static bool PausePrefix()
        {
            try
            {
                if (bypass) return true;
                DynamicAudioPlugin p = DynamicAudioPlugin.Instance;
                if (p == null) return true;
                return !p.RequestPause();       // true = we took over, skip the original for now
            }
            catch (Exception)
            {
                return true;
            }
        }

        public static bool ResumePrefix()
        {
            try
            {
                DynamicAudioPlugin p = DynamicAudioPlugin.Instance;
                if (p != null) p.CancelPendingPause();
            }
            catch (Exception) { }
            return true;
        }
    }
}
#else
using BepInEx.Logging;

namespace DynamicAudio
{
    internal static class PauseHook
    {
        public static bool Installed { get { return false; } }
        public static void Install(ManualLogSource log) { }
        public static void Uninstall() { }
        public static void ApplyPause() { }
    }
}
#endif
