using System;

namespace FauFau.Util
{
    // A v2 entity guid, a variation of MySQL's UUID_SHORT(), see https://gist.github.com/SilentCLD/881839a9f45578f1618db012fc789a71
    //   ServerId:  1 byte, region and probably the database node
    //   Timestamp: 3 bytes, server boot time in Unix seconds without its lowest 8 bits
    //   Counter:   3 bytes, increments per created entity
    //   Type:      1 byte, the entity type
    // Guids starting with 0x7F are v1 guids with an unknown layout. Guids starting with 0xFF belong to entities that only live
    // as long as the map (NPCs, deployables, markers) and have no timestamp.
    public readonly struct EntityGuid : IEquatable<EntityGuid>
    {
        public byte ServerId { get; }

        // Unix seconds, the lowest 8 bits are always 0
        public uint Timestamp { get; }

        public uint Counter { get; }
        public byte Type { get; }

        public ulong Full => ((ulong)ServerId << 56) | ((ulong)(Timestamp >> 8) << 32) | ((ulong)Counter << 8) | Type;

        public bool IsV1 => ServerId == 0x7F;
        public bool IsMapEntity => ServerId == 0xFF;

        public EntityGuid(byte serverId, uint timestamp, uint counter, byte type)
        {
            ServerId = serverId;
            Timestamp = timestamp & 0xFFFFFF00;
            Counter = counter & 0x00FFFFFF;
            Type = type;
        }

        public static EntityGuid Parse(ulong guid)
        {
            return new EntityGuid((byte)(guid >> 56), (uint)((guid >> 32) & 0x00FFFFFF) << 8, (uint)(guid >> 8) & 0x00FFFFFF, (byte)guid);
        }

        public bool Equals(EntityGuid other) => Full == other.Full;
        public override bool Equals(object obj) => obj is EntityGuid other && Equals(other);
        public override int GetHashCode() => Full.GetHashCode();
        public static bool operator ==(EntityGuid left, EntityGuid right) => left.Equals(right);
        public static bool operator !=(EntityGuid left, EntityGuid right) => !left.Equals(right);

        public override string ToString() => $"0x{Full:X16}";
    }
}
