using UnityEngine;

namespace RaidRogue
{
    // The existing controller moves a transform on a flat arena. Sweep a world-sized capsule
    // for voluntary movement; keep rescue/knockback's established destination rules intact.
    public sealed class PlayerMovementCollision
    {
        private const float Skin = 0.025f;
        private readonly Collider[] _overlaps = new Collider[32];
        private readonly RaycastHit[] _hits = new RaycastHit[32];

        public Vector3 Move(PlayerController player, Vector3 direction, float distance, out bool blocked)
            => Move(player.transform, player.transform.position, player.combatRadius, player.arenaBoundaryRadius, direction, distance, out blocked);

        public Vector3 Move(Transform owner, Vector3 start, float combatRadius, float arenaRadius, Vector3 direction, float distance, out bool blocked)
        {
            direction.y = 0;
            direction.Normalize();
            float allowed = (float)DashGeometry.ArenaTravel(start.x, start.z, direction.x, direction.z,
                distance, arenaRadius);
            blocked = allowed + 0.0001f < distance;
            if (allowed <= 0) return start;

            float radius = Mathf.Clamp(combatRadius, 0.05f, 0.95f);
            float halfSegment = Mathf.Max(0, 1f - radius - Skin);
            Vector3 bottom = start - Vector3.up * halfSegment;
            Vector3 top = start + Vector3.up * halfSegment;
            Physics.SyncTransforms();

            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, _overlaps,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // Never silently drop an obstacle in a dense scene.
            Collider[] overlaps = count == _overlaps.Length
                ? Physics.OverlapCapsule(bottom, top, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) : _overlaps;
            if (overlaps != _overlaps) count = overlaps.Length;
            for (int i = 0; i < count; i++)
            {
                if (!BlocksMovement(overlaps[i], owner)) continue;
                // Casts do not report initial penetrations. Stop instead of tunnelling through one.
                blocked = true;
                return start;
            }

            count = Physics.CapsuleCastNonAlloc(bottom, top, radius, direction, _hits, allowed + Skin,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            RaycastHit[] hits = count == _hits.Length
                ? Physics.CapsuleCastAll(bottom, top, radius, direction, allowed + Skin,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) : _hits;
            if (hits != _hits) count = hits.Length;
            for (int i = 0; i < count; i++)
            {
                if (!BlocksMovement(hits[i].collider, owner)) continue;
                float travel = Mathf.Max(0, hits[i].distance - Skin);
                if (travel >= allowed) continue;
                allowed = travel;
                blocked = true;
            }
            return start + direction * allowed;
        }

        private static bool BlocksMovement(Collider collider, Transform owner)
        {
            if (collider == null || collider.isTrigger || collider.transform.IsChildOf(owner)) return false;
            // Unit colliders exist for click targeting, not physical body blocking in this project.
            return collider.GetComponentInParent<NetworkRaidAvatar>() == null &&
                   collider.GetComponentInParent<PlayerController>() == null &&
                   collider.GetComponentInParent<PartyBotAI>() == null &&
                   collider.GetComponentInParent<BossController>() == null &&
                   collider.GetComponentInParent<BossAddMinion>() == null;
        }
    }
}
