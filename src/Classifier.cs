using System.Collections.Generic;
using UnityEngine;

namespace DynamicAudio
{
    internal static class SoundClassifier
    {
        public static int Priority(SoundCategory c)
        {
            switch (c)
            {
                case SoundCategory.Gunshot: return 5;
                case SoundCategory.Explosion: return 6;
                case SoundCategory.Impact: return 3;
                case SoundCategory.Vehicle: return 3;
                case SoundCategory.Footstep: return 1;
                case SoundCategory.Environment: return 1;
                default: return 0;
            }
        }

        private static bool HasAny(string s, string a, string b, string c, string d, string e)
        {
            return s.Contains(a) || s.Contains(b) || (c != null && s.Contains(c)) || (d != null && s.Contains(d)) || (e != null && s.Contains(e));
        }

        public static SoundCategory Classify(AudioSource src)
        {
            if (src == null) return SoundCategory.Other;
            GameObject go = src.gameObject;
            AudioClip clip = src.clip;

            // Component-based hints first: they are exact and survive PlayOneShot (clip == null).
            if (go.GetComponentInParent<Weapon>() != null) return SoundCategory.Gunshot;
            if (go.GetComponentInParent<WeaponImposter>() != null) return SoundCategory.Gunshot;
            if (go.GetComponentInParent<ExplodingProjectile>() != null) return SoundCategory.Explosion;
            if (go.GetComponentInParent<PlayExplosionSound>() != null) return SoundCategory.Explosion;
            if (go.GetComponentInParent<FootstepAudio>() != null) return SoundCategory.Footstep;
            if (go.GetComponentInParent<Vehicle>() != null) return SoundCategory.Vehicle;
            if (go.GetComponentInParent<MeleeWeapon>() != null) return SoundCategory.Impact;

            string n = string.Empty;
            if (clip != null) n = clip.name;
            if (n.Length == 0) n = go.name;
            n = n.ToLowerInvariant();

            if (HasAny(n, "explo", "grenade", "bomb", "artillery", "tankshell")) return SoundCategory.Explosion;
            if (n.Contains("campfire") || n.Contains("fireplace")) return SoundCategory.Environment;
            if (HasAny(n, "fire", "shot", "gun", "rifle", "cannon") || n.Contains("sniper")) return SoundCategory.Gunshot;
            if (HasAny(n, "engine", "motor", "rotor", "tread", "hover")) return SoundCategory.Vehicle;
            if (HasAny(n, "footstep", "step", "foley", null, null)) return SoundCategory.Footstep;
            if (HasAny(n, "impact", "hit", "ricochet", "whiz", "flyby")) return SoundCategory.Impact;
            return SoundCategory.Environment;
        }
    }

    internal static class MaterialSystem
    {
        public struct MaterialAcoustics
        {
            public float Reflection;
            public float Absorption;
            public float HfLoss;
        }

        private static readonly Dictionary<AcousticMaterial, MaterialAcoustics> Table = BuildTable();

        private static MaterialAcoustics M(float refl, float abs, float hf)
        {
            MaterialAcoustics m = new MaterialAcoustics();
            m.Reflection = refl;
            m.Absorption = abs;
            m.HfLoss = hf;
            return m;
        }

        private static Dictionary<AcousticMaterial, MaterialAcoustics> BuildTable()
        {
            Dictionary<AcousticMaterial, MaterialAcoustics> t = new Dictionary<AcousticMaterial, MaterialAcoustics>();
            t[AcousticMaterial.Concrete] = M(0.90f, 0.10f, 1.0f);
            t[AcousticMaterial.Metal] = M(0.95f, 0.05f, 0.8f);
            t[AcousticMaterial.Rock] = M(0.80f, 0.25f, 1.1f);
            t[AcousticMaterial.Wood] = M(0.50f, 0.45f, 1.4f);
            t[AcousticMaterial.Glass] = M(0.70f, 0.15f, 0.6f);
            t[AcousticMaterial.Dirt] = M(0.20f, 0.75f, 1.6f);
            t[AcousticMaterial.Vegetation] = M(0.10f, 0.85f, 1.8f);
            t[AcousticMaterial.Vehicle] = M(0.60f, 0.30f, 1.2f);
            t[AcousticMaterial.Water] = M(0.30f, 0.60f, 1.3f);
            t[AcousticMaterial.Generic] = M(0.60f, 0.30f, 1.0f);
            return t;
        }

        public static MaterialAcoustics Get(AcousticMaterial m)
        {
            MaterialAcoustics v;
            if (Table.TryGetValue(m, out v)) return v;
            return Table[AcousticMaterial.Generic];
        }

        public static AcousticMaterial Guess(Collider c)
        {
            if (c == null) return AcousticMaterial.Generic;
            PhysicMaterial pm = c.sharedMaterial;
            if (pm != null) return GuessByName(pm.name);
            if (c is TerrainCollider) return AcousticMaterial.Dirt;
            return GuessByName(c.name);
        }

        private static bool Has(string n, string a, string b, string c, string d)
        {
            return n.Contains(a) || (b != null && n.Contains(b)) || (c != null && n.Contains(c)) || (d != null && n.Contains(d));
        }

        public static AcousticMaterial GuessByName(string n)
        {
            if (string.IsNullOrEmpty(n)) return AcousticMaterial.Generic;
            n = n.ToLowerInvariant();
            if (Has(n, "concrete", "brick", "cement", "bunker")) return AcousticMaterial.Concrete;
            if (Has(n, "metal", "steel", "iron", "container")) return AcousticMaterial.Metal;
            if (Has(n, "wood", "plank", "timber", null)) return AcousticMaterial.Wood;
            if (Has(n, "glass", "window", null, null)) return AcousticMaterial.Glass;
            if (Has(n, "rock", "stone", "cliff", null)) return AcousticMaterial.Rock;
            if (Has(n, "dirt", "sand", "ground", "terrain") || n.Contains("mud")) return AcousticMaterial.Dirt;
            if (Has(n, "tree", "bush", "leaf", "grass") || n.Contains("forest")) return AcousticMaterial.Vegetation;
            if (Has(n, "vehicle", "car", "tank", "hull") || n.Contains("apc")) return AcousticMaterial.Vehicle;
            if (Has(n, "water", "river", "lake", "ocean")) return AcousticMaterial.Water;
            return AcousticMaterial.Generic;
        }
    }
}
