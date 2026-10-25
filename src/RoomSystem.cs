using UnityEngine;

namespace DynamicAudio
{
    /// <summary>
    /// Estimates the room around the listener with 10 rays. Uses Physics.Raycast (closest hit) because
    /// RaycastNonAlloc with a tiny buffer returns arbitrary hits, not the nearest ones.
    /// </summary>
    public sealed class RoomSystem
    {
        private int mask;

        public RoomSystem(int layerMask)
        {
            mask = layerMask;
        }

        public void SetMask(int m)
        {
            mask = m;
        }

        private float DistanceTo(Vector3 origin, Vector3 dir, float range)
        {
            RaycastHit h;
            if (Physics.Raycast(origin, dir, out h, range, mask, QueryTriggerInteraction.Ignore)) return h.distance;
            return range;
        }

        public RoomEstimate Probe(Vector3 origin)
        {
            RoomEstimate r = new RoomEstimate();
            float left = DistanceTo(origin, Vector3.left, 60f);
            float right = DistanceTo(origin, Vector3.right, 60f);
            float fwd = DistanceTo(origin, Vector3.forward, 60f);
            float back = DistanceTo(origin, Vector3.back, 60f);
            float up = DistanceTo(origin, Vector3.up, 40f);
            float down = DistanceTo(origin, Vector3.down, 20f);
            float fr = DistanceTo(origin, (Vector3.forward + Vector3.right).normalized, 60f);
            float fl = DistanceTo(origin, (Vector3.forward + Vector3.left).normalized, 60f);
            float br = DistanceTo(origin, (Vector3.back + Vector3.right).normalized, 60f);
            float bl = DistanceTo(origin, (Vector3.back + Vector3.left).normalized, 60f);

            float width = (left >= 60f || right >= 60f) ? 120f : left + right;
            float length = (fwd >= 60f || back >= 60f) ? 120f : fwd + back;
            // If either vertical ray misses, treat it as open sky/floor: use the hit we DID get when it is
            // plausible, otherwise fall back to a generic tall-space estimate. (V1.5.0 had the redundant
            // condition "up < 40f && up < 25f" here and asymmetric range handling; fixed.)
            bool upHit = up < 40f;
            bool downHit = down < 20f;
            float height;
            if (upHit && downHit) height = up + down;
            else if (upHit) height = Mathf.Max(up * 2f, 3f);
            else if (downHit) height = Mathf.Max(down * 2f, 3f);
            else height = 40f;
            height = Mathf.Clamp(height, 1.5f, 60f);

            r.Width = width;
            r.Length = length;
            r.Height = height;
            // A ceiling exists only when the UP ray actually hit something within its range.
            r.HasCeiling = upHit;

            float[] d = new float[] { left, right, fwd, back, fr, fl, br, bl };
            int close = 0;
            for (int i = 0; i < d.Length; i++)
            {
                if (d[i] < 48f) close++;
            }
            r.Enclosure = (float)close / (float)d.Length;
            r.Volume = width * length * height;
            r.Type = Classify(ref r);
            return r;
        }

        public static RoomType Classify(ref RoomEstimate r)
        {
            float shortSide = Mathf.Min(r.Width, r.Length);
            float longSide = Mathf.Max(r.Width, r.Length);
            if (r.Enclosure < 0.3f) return RoomType.OutdoorOpen;
            if (!r.HasCeiling)
            {
                if (r.Enclosure > 0.65f) return RoomType.Courtyard;
                return RoomType.OutdoorNearSurfaces;
            }
            if (shortSide < 4f && longSide > shortSide * 2.5f) return RoomType.Hallway;
            if (longSide > 12f && shortSide < 6f && r.Height < 7f) return RoomType.Tunnel;
            if (r.Volume > 8000f && longSide > 25f) return RoomType.Warehouse;
            if (r.Volume > 1500f) return RoomType.LargeRoom;
            if (r.Volume > 150f) return RoomType.MediumRoom;
            return RoomType.SmallRoom;
        }

        public static string Describe(ref RoomEstimate r)
        {
            string vol = r.Volume < 1000f ? r.Volume.ToString("F0") : (r.Volume / 1000f).ToString("F1") + "k";
            return r.Type + "  " + r.Width.ToString("F1") + "x" + r.Length.ToString("F1") + "x" + r.Height.ToString("F1")
                + "m  vol~" + vol + "m3  enclosure " + (r.Enclosure * 100f).ToString("F0") + "%";
        }
    }
}
