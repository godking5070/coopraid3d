using UnityEngine;

namespace RaidRogue
{
    // Used by host simulation and owner prediction/replay, with the same capsule sweep as single play.
    public sealed class RaidNetworkMotor
    {
        public Vector3 Position;
        public float Yaw;
        public readonly DashMotion Dash = new DashMotion();
        private readonly PlayerMovementCollision _collision = new PlayerMovementCollision();
        public void Step(RaidMoveInput input, Transform owner, RaidPacket config, bool allowed)
        {
            bool approach = RaidNetworkSession.Instance != null && RaidNetworkSession.Instance.BattlePhase == RaidBattlePhase.Approach;
            bool portal = RaidApproachField.Instance?.PortalActive == true;
            if (RaidNetworkSession.Instance != null && (RaidNetworkSession.Instance.BattlePhase == RaidBattlePhase.Lobby || approach || portal)) input.Dash = false;
            Dash.TickCooldown(RaidWire.Step);
            if (!allowed) { Dash.Cancel(); return; }
            var kit=owner.GetComponent<CharacterKit>();
            Vector3 direction = Vector3.ClampMagnitude(new Vector3(input.X, 0, input.Z), 1f);
            Vector3 facing = Quaternion.Euler(0, Yaw, 0) * Vector3.forward;
            var character = owner.GetComponent<CharacterActor>();
            if (input.Dash) { if (character != null) character.StartMovement(Dash, config.Dash, true, direction.x, direction.z, facing.x, facing.z); else Dash.TryStart(config.Dash, true, direction.x, direction.z, facing.x, facing.z); }
            var extensions=owner.GetComponent<AegisExtensions>();
            if(Dash.Active)extensions?.CancelChannel();else if(extensions?.Channeling==true)direction=Vector3.zero;
            float distance;
            if (Dash.Active)
            {
                character?.MarkDashMovement(character.MovementSettings(config.Dash).preserveCast);
                direction = new Vector3((float)Dash.DirectionX, 0, (float)Dash.DirectionZ);
                distance = (float)Dash.Step(RaidWire.Step);
            }
            else if(kit?.MovementBlocked==true)distance=0;
            else distance = config.Speed * RaidWire.Step * direction.magnitude;
            if (distance <= 0) return;
            Vector3 before = Position;
            var field = RaidApproachField.Instance;
            float arena = approach && field != null ? field.MovementRadius : field != null && field.Showing ? field.FieldRadius : config.Arena;
            Position = _collision.Move(owner, Position, config.Radius, arena, direction, distance, out bool blocked);
            if (blocked) Dash.Cancel();
            if ((Position - before).sqrMagnitude > 0.000001f && kit?.Has("aimLock")!=true)
                Yaw = Mathf.MoveTowardsAngle(Yaw, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, config.TurnSpeed * RaidWire.Step);
        }
        public void Restore(RaidPose pose) { Position = new Vector3(pose.X, 1, pose.Z); Yaw = pose.Yaw; Dash.Restore(pose.Dash); }
        public RaidPose Snapshot(RaidRoster.Member member, uint ack) => new RaidPose
        { Id = member.Id, Role = member.Role, Name = member.Name, Ack = ack, X = Position.x, Z = Position.z, Yaw = Yaw, Dash = Dash.Capture() };
    }
}
