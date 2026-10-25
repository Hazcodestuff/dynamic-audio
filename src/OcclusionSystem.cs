using UnityEngine;

namespace DynamicAudio
{
    /// <summary>
    /// Wall occlusion by raycast. Every ray now ENDS near the emitter (V6 fanned rays out by angle, so
    /// the outer rays ended in the ground/sky and counted terrain as a wall), uses RaycastNonAlloc and
    /// looks past ignorable colliders instead of stopping at the first one.
    /// </summary>
    public sealed class OcclusionSystem
    {
        /// <summary>Mask Ravenfield itself uses for its own sound line-of-sight test (ExplodingProjectile.Explode).</summary>
        public const int GameLineOfSightMask = 8392705;

        private const int MaxHits = 32;
        private const float EndMargin = 0.6f;

        private static readonly Vector2[] Pattern = new Vector2[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(-1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(0f, -1f),
            new Vector2(0.7071f, 0.7071f),
            new Vector2(-0.7071f, 0.7071f),
            new Vector2(0.7071f, -0.7071f),
            new Vector2(-0.7071f, -0.7071f)
        };

        private readonly RaycastHit[] hits = new RaycastHit[MaxHits];
        private int rayCount = 5;
        private float spread = 0.25f;
        private int mask = GameLineOfSightMask;

        public void Configure(int rays, float spreadFactor)
        {
            rayCount = Mathf.Clamp(rays, 1, Pattern.Length);
            spread = Mathf.Clamp(spreadFactor, 0f, 1f);
        }

        public void SetMask(int m)
        {
            mask = m;
        }

        /// <summary>Fallback mask: everything except actor/hitbox/editor/etc. layers.</summary>
        public static int BuildExclusionMask()
        {
            string[] names = new string[]
            {
                "Ignore Raycast", "HUD", "Hitbox", "Player", "Ragdoll", "Seat", "Vehicle", "Throwable", "Actor",
                "NoActorCollision", "SeatedHitbox", "ActorCollision", "FirstPerson", "AirAvoidance",
                "AiVisionOccluder", "Usable", "MapEditorSelectable", "MapEditorGizmo", "PostProcessing",
                "PostProcessing_AO"
            };
            int m = -1;
            for (int i = 0; i < names.Length; i++)
            {
                int layer = LayerMask.NameToLayer(names[i]);
                if (layer >= 0) m &= ~(1 << (layer & 31));
            }
            return m;
        }

        private bool FirstBlocker(Vector3 origin, Vector3 dir, float len, Transform ignoreRoot, out float entry, out Collider col)
        {
            entry = float.MaxValue;
            col = null;
            int count = Physics.RaycastNonAlloc(origin, dir, hits, len, mask, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < count; k++)
            {
                Collider c = hits[k].collider;
                if (c == null) continue;
                if (ignoreRoot != null && c.transform.IsChildOf(ignoreRoot)) continue;
                float d = hits[k].distance;
                if (d < entry)
                {
                    entry = d;
                    col = c;
                }
            }
            return col != null;
        }

        private float EstimateThickness(Vector3 listenerPos, Vector3 aim, float entry, Transform ignoreRoot)
        {
            Vector3 back = listenerPos - aim;
            float len = back.magnitude;
            if (len < 0.1f) return 0.3f;
            back /= len;
            int count = Physics.RaycastNonAlloc(aim, back, hits, len, mask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int k = 0; k < count; k++)
            {
                Collider c = hits[k].collider;
                if (c == null) continue;
                if (ignoreRoot != null && c.transform.IsChildOf(ignoreRoot)) continue;
                if (hits[k].distance < nearest) nearest = hits[k].distance;
            }
            if (nearest == float.MaxValue) return 0.3f;
            float exitFromListener = len - nearest;
            return Mathf.Clamp(exitFromListener - entry, 0.1f, 6f);
        }

        public OcclusionResult Probe(Vector3 listenerPos, Vector3 sourcePos, Transform sourceTop, int rays)
        {
            OcclusionResult res = new OcclusionResult();
            res.Ratio = 0f;
            res.ThicknessM = 0f;
            res.DominantMaterial = AcousticMaterial.Generic;
            res.MaterialHfLoss = 1f;

            Vector3 toSrc = sourcePos - listenerPos;
            float dist = toSrc.magnitude;
            if (dist < 1.5f) return res;

            Vector3 fwd = toSrc / dist;
            // Aim slightly above the emitter so ground-hugging sources do not graze the terrain.
            Vector3 aim = sourcePos + Vector3.up * Mathf.Min(0.6f, dist * 0.05f);
            Vector3 right = Vector3.Cross(fwd, Vector3.up);
            if (right.sqrMagnitude < 0.001f) right = Vector3.right;
            else right.Normalize();
            Vector3 up = Vector3.Cross(right, fwd).normalized;

            float radius = Mathf.Clamp(dist * spread * 0.15f, 0.3f, 2.5f);
            int n = Mathf.Clamp(rays, 1, Pattern.Length);
            int blocked = 0;
            float centerEntry = 0f;
            Collider centerCollider = null;
            bool centerBlocked = false;

            for (int i = 0; i < n; i++)
            {
                Vector3 target = aim + right * (Pattern[i].x * radius) + up * (Pattern[i].y * radius);
                Vector3 d = target - listenerPos;
                float len = d.magnitude;
                float castLen = len - EndMargin;
                if (castLen <= 0.05f) continue;
                d /= len;

                float entry;
                Collider col;
                if (FirstBlocker(listenerPos, d, castLen, sourceTop, out entry, out col))
                {
                    blocked++;
                    if (i == 0)
                    {
                        centerBlocked = true;
                        centerEntry = entry;
                        centerCollider = col;
                    }
                }
            }

            res.Ratio = (float)blocked / (float)n;

            if (blocked > 0)
            {
                RaycastHit roof;
                res.SourceCovered = Physics.Raycast(sourcePos + Vector3.up * 0.3f, Vector3.up, out roof, 25f, mask, QueryTriggerInteraction.Ignore);
            }

            if (centerBlocked)
            {
                res.ThicknessM = EstimateThickness(listenerPos, aim, centerEntry, sourceTop);
                res.DominantMaterial = MaterialSystem.Guess(centerCollider);
                res.MaterialHfLoss = MaterialSystem.Get(res.DominantMaterial).HfLoss;
            }
            else if (blocked > 0)
            {
                res.ThicknessM = 0.3f;
            }
            return res;
        }
    }
}
