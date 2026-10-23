using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RaidRogue
{
    public struct RaidMoveInput
    {
        public uint Sequence;
        public float X, Z;
        public bool Dash;
        public bool Valid => Sequence > 0 && Finite(X) && Finite(Z) && Math.Abs(X) <= 1.001f && Math.Abs(Z) <= 1.001f;
        public static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
    }

    // One input per host tick, regardless of client packet rate. The sender's transport connection
    // selects the owner; no client supplied actor ID, position, time step, speed or cooldown is trusted.
    public sealed class RaidInputQueue
    {
        private readonly SortedDictionary<uint, RaidMoveInput> _pending = new SortedDictionary<uint, RaidMoveInput>();
        public uint Acknowledged { get; private set; }
        public bool Add(RaidMoveInput input)
        {
            if (!input.Valid || input.Sequence <= Acknowledged || input.Sequence - Acknowledged > 90 || _pending.Count >= 90) return false;
            if (_pending.ContainsKey(input.Sequence)) return false;
            _pending.Add(input.Sequence, input); return true;
        }
        public RaidMoveInput Next()
        {
            if (_pending.Count == 0) return default;
            var enumerator = _pending.GetEnumerator(); enumerator.MoveNext(); var input = enumerator.Current.Value;
            _pending.Remove(input.Sequence); Acknowledged = input.Sequence; return input;
        }
    }

    public sealed class RaidRoster
    {
        public sealed class Member { public int Id, Role; public string Name; }
        public readonly List<Member> Members = new List<Member>();
        private int _nextId = 1;
        public Member Join(string name)
        {
            if (Members.Count >= 4) return null;
            int role = 1; while (Members.Exists(m => m.Role == role)) role++;
            var member = new Member { Id = _nextId++, Role = role, Name = CleanName(name) };
            Members.Add(member); return member;
        }
        public bool SelectRole(int id, int role)
        {
            var member = Members.Find(m => m.Id == id);
            if (member == null || role < 1 || role > 4 || Members.Exists(m => m.Id != id && m.Role == role)) return false;
            member.Role = role; return true;
        }
        public void Remove(int id) => Members.RemoveAll(m => m.Id == id);
        public static string CleanName(string value)
        {
            var text = new StringBuilder();
            foreach (char c in value ?? "") if (!char.IsControl(c) && c != '<' && c != '>' && text.Length < 16) text.Append(c);
            return text.Length == 0 ? "Player" : text.ToString();
        }
    }

    public enum RaidPacketKind : byte { Hello = 1, Welcome, Input, Snapshot, Role, Notice }
    public sealed class RaidPose
    {
        public int Id, Role;
        public string Name;
        public uint Ack;
        public float X, Z, Yaw;
        public bool MovementLocked;
        public DashMotion.Snapshot Dash;
    }
    public sealed class RaidPacket
    {
        public RaidPacketKind Kind;
        public string Text = "";
        public int Id, Role;
        public uint Tick;
        public float Speed, Radius, Arena, TurnSpeed;
        public DashSettings Dash = new DashSettings();
        public readonly List<RaidMoveInput> Inputs = new List<RaidMoveInput>();
        public readonly List<RaidPose> Poses = new List<RaidPose>();
    }

    // Bounded binary protocol, deliberately below the UDP MTU. Every snapshot carries a complete
    // roster so late joins, lost snapshots and disconnects converge without scene/prefab replication.
    public static class RaidWire
    {
        public const int Version = 1, MaxBytes = 1100;
        public const float Step = 1f / 30f;
        public static byte[] Encode(RaidPacket packet)
        {
            using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Encoding.UTF8);
            w.Write((byte)Version); w.Write((byte)packet.Kind);
            switch (packet.Kind)
            {
                case RaidPacketKind.Hello: case RaidPacketKind.Notice: WriteText(w, packet.Text); break;
                case RaidPacketKind.Welcome:
                    w.Write(packet.Id); w.Write(packet.Speed); w.Write(packet.Radius); w.Write(packet.Arena); w.Write(packet.TurnSpeed);
                    w.Write(packet.Dash.distance); w.Write(packet.Dash.duration); w.Write(packet.Dash.cooldown); break;
                case RaidPacketKind.Role: w.Write(packet.Role); break;
                case RaidPacketKind.Input:
                    w.Write((byte)packet.Inputs.Count);
                    foreach (var input in packet.Inputs) { w.Write(input.Sequence); w.Write(input.X); w.Write(input.Z); w.Write(input.Dash); }
                    break;
                case RaidPacketKind.Snapshot:
                    w.Write(packet.Tick); w.Write((byte)packet.Poses.Count);
                    foreach (var pose in packet.Poses)
                    {
                        w.Write(pose.Id); w.Write(pose.Role); WriteText(w, pose.Name); w.Write(pose.Ack);
                        w.Write(pose.X); w.Write(pose.Z); w.Write(pose.Yaw); w.Write(pose.MovementLocked);
                        w.Write(pose.Dash.Active); w.Write(pose.Dash.Cooldown); w.Write(pose.Dash.CooldownTotal);
                        w.Write(pose.Dash.Time); w.Write(pose.Dash.Distance); w.Write(pose.Dash.Speed); w.Write(pose.Dash.X); w.Write(pose.Dash.Z);
                    }
                    break;
                default: throw new InvalidDataException();
            }
            if (stream.Length > MaxBytes) throw new InvalidDataException("Packet too large");
            return stream.ToArray();
        }
        public static bool Decode(byte[] bytes, out RaidPacket packet)
        {
            packet = null;
            if (bytes == null || bytes.Length < 2 || bytes.Length > MaxBytes) return false;
            try
            {
                using var stream = new MemoryStream(bytes); using var r = new BinaryReader(stream, Encoding.UTF8);
                if (r.ReadByte() != Version) return false;
                var p = new RaidPacket { Kind = (RaidPacketKind)r.ReadByte() };
                switch (p.Kind)
                {
                    case RaidPacketKind.Hello: case RaidPacketKind.Notice: p.Text = ReadText(r); break;
                    case RaidPacketKind.Welcome:
                        p.Id = r.ReadInt32(); p.Speed = Number(r); p.Radius = Number(r); p.Arena = Number(r); p.TurnSpeed = Number(r);
                        p.Dash.distance = Number(r); p.Dash.duration = Number(r); p.Dash.cooldown = Number(r);
                        if (p.Id <= 0 || p.Speed <= 0 || p.Radius <= 0 || p.Arena <= 0 || p.TurnSpeed <= 0 || p.Dash.distance <= 0 || p.Dash.duration <= 0 || p.Dash.cooldown < 0) return false;
                        break;
                    case RaidPacketKind.Role: p.Role = r.ReadInt32(); if (p.Role < 1 || p.Role > 4) return false; break;
                    case RaidPacketKind.Input:
                        int count = r.ReadByte(); if (count > 8) return false;
                        for (int i = 0; i < count; i++)
                        {
                            var input = new RaidMoveInput { Sequence = r.ReadUInt32(), X = Number(r), Z = Number(r), Dash = r.ReadBoolean() };
                            if (!input.Valid) return false; p.Inputs.Add(input);
                        }
                        break;
                    case RaidPacketKind.Snapshot:
                        p.Tick = r.ReadUInt32(); count = r.ReadByte(); if (count < 1 || count > 4) return false;
                        var ids = new HashSet<int>(); var roles = new HashSet<int>();
                        for (int i = 0; i < count; i++)
                        {
                            var pose = new RaidPose { Id = r.ReadInt32(), Role = r.ReadInt32(), Name = ReadText(r), Ack = r.ReadUInt32(), X = Number(r), Z = Number(r), Yaw = Number(r), MovementLocked = r.ReadBoolean() };
                            if (pose.Id <= 0 || pose.Role < 1 || pose.Role > 4 || !ids.Add(pose.Id) || !roles.Add(pose.Role)) return false;
                            pose.Dash = new DashMotion.Snapshot { Active = r.ReadBoolean(), Cooldown = Number(r), CooldownTotal = Number(r), Time = Number(r), Distance = Number(r), Speed = Number(r), X = Number(r), Z = Number(r) };
                            p.Poses.Add(pose);
                        }
                        break;
                    default: return false;
                }
                if (stream.Position != stream.Length) return false;
                packet = p; return true;
            }
            catch (IOException) { return false; }
            catch (ArgumentException) { return false; }
        }
        private static float Number(BinaryReader r) { float f = r.ReadSingle(); if (!RaidMoveInput.Finite(f)) throw new InvalidDataException(); return f; }
        private static void WriteText(BinaryWriter w, string text)
        {
            var data = Encoding.UTF8.GetBytes(text ?? ""); if (data.Length > 240) throw new InvalidDataException();
            w.Write((byte)data.Length); w.Write(data);
        }
        private static string ReadText(BinaryReader r)
        {
            int count = r.ReadByte(); if (count > 240) throw new InvalidDataException();
            var data = r.ReadBytes(count); if (data.Length != count) throw new EndOfStreamException();
            return new UTF8Encoding(false, true).GetString(data);
        }
    }
}
