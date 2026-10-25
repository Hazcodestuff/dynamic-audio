using UnityEngine;

namespace DynamicAudio
{
    public enum SoundCategory
    {
        Gunshot = 0,
        Explosion = 1,
        Impact = 2,
        Vehicle = 3,
        Footstep = 4,
        Environment = 5,
        Other = 6
    }

    public enum RoomType
    {
        OutdoorOpen = 0,
        OutdoorNearSurfaces = 1,
        Courtyard = 2,
        SmallRoom = 3,
        MediumRoom = 4,
        LargeRoom = 5,
        Warehouse = 6,
        Tunnel = 7,
        Hallway = 8
    }

    public enum AcousticMaterial
    {
        Generic = 0,
        Concrete = 1,
        Metal = 2,
        Wood = 3,
        Dirt = 4,
        Rock = 5,
        Glass = 6,
        Vegetation = 7,
        Vehicle = 8,
        Water = 9
    }

    public struct RoomEstimate
    {
        public float Width;
        public float Length;
        public float Height;
        public float Volume;
        public float Enclosure;
        public bool HasCeiling;
        public RoomType Type;
    }

    public struct OcclusionResult
    {
        public float Ratio;
        public float ThicknessM;
        public AcousticMaterial DominantMaterial;
        public float MaterialHfLoss;
        public bool SourceCovered;      // roofed over (only measured when something blocks the path)
    }

    /// <summary>
    /// Per-AudioSource simulation state. The mod never writes AudioSource.volume any more
    /// (Ravenfield rewrites it constantly through SoundBank.SetVolume/MoveVolume); gain and
    /// muffling are applied by helper filter components instead.
    /// </summary>
    public sealed class TrackedSound
    {
        public AudioSource Source;
        public Transform SourceTop;
        public SoundCategory Category;
        public AudioLowPassFilter LowPass;
        public AcousticGainFilter Gain;

        public float Distance;
        public float Occlusion;

        public float TargetGain = 1f;
        public float CurrentGain = 1f;
        public float TargetCutoff = 22000f;
        public float CurrentCutoff = 22000f;

        public float NextUpdateTime;
        public float TrackedSince;
        public float LastPlayingTime;
        public int RayLod;          // 0 = full ray count, 1 = single ray, -1 = no rays
        public bool Processed;
    }
}
