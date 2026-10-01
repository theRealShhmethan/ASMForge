using ASMForge.Core.Execution;
using System.Text;

namespace ASMForge.Core.Memory;

/// <summary>
/// Sparse byte-addressable MIPS memory. MARS defaults to little-endian memory and
/// uses widely separated text/data/heap/stack ranges, so a single contiguous array
/// is not an appropriate model.
/// </summary>
public sealed class MipsMemory
{
    public const uint DataBase = 0x10010000;
    public const uint HeapBase = 0x10040000;
    public const uint StackTop = 0x7fffeffc;

    private readonly Dictionary<uint, byte> _bytes = new();

    /// <summary>While set, every byte write is recorded for the current step.</summary>
    internal StepJournal? Journal { get; set; }

    public void Reset() => _bytes.Clear();

    public void Load(IReadOnlyDictionary<uint, byte> image)
    {
        foreach (var (address, value) in image)
            _bytes[address] = value;
    }

    public byte ReadByte(uint address) => _bytes.TryGetValue(address, out var value) ? value : (byte)0;
    public sbyte ReadSByte(uint address) => unchecked((sbyte)ReadByte(address));
    public void WriteByte(uint address, byte value)
    {
        Journal?.Memory.Add(new MemoryChange(address, ReadByte(address), value));
        _bytes[address] = value;
    }

    public ushort ReadHalf(uint address)
    {
        EnsureAligned(address, 2, "halfword");
        return (ushort)(ReadByte(address) | (ReadByte(address + 1) << 8));
    }

    public short ReadSignedHalf(uint address) => unchecked((short)ReadHalf(address));

    public void WriteHalf(uint address, ushort value)
    {
        EnsureAligned(address, 2, "halfword");
        WriteByte(address, (byte)value);
        WriteByte(address + 1, (byte)(value >> 8));
    }

    public int ReadWord(uint address)
    {
        EnsureAligned(address, 4, "word");
        return unchecked((int)ReadWordUnsigned(address));
    }

    public uint ReadWordUnsigned(uint address)
    {
        EnsureAligned(address, 4, "word");
        return (uint)(ReadByte(address)
            | (ReadByte(address + 1) << 8)
            | (ReadByte(address + 2) << 16)
            | (ReadByte(address + 3) << 24));
    }

    public void WriteWord(uint address, int value) => WriteWord(address, unchecked((uint)value));

    public void WriteWord(uint address, uint value)
    {
        EnsureAligned(address, 4, "word");
        WriteByte(address, (byte)value);
        WriteByte(address + 1, (byte)(value >> 8));
        WriteByte(address + 2, (byte)(value >> 16));
        WriteByte(address + 3, (byte)(value >> 24));
    }

    public ulong ReadDoubleWord(uint address)
    {
        EnsureAligned(address, 8, "doubleword");
        return ReadWordUnsigned(address) | ((ulong)ReadWordUnsigned(address + 4) << 32);
    }

    public void WriteDoubleWord(uint address, ulong value)
    {
        EnsureAligned(address, 8, "doubleword");
        WriteWord(address, (uint)value);
        WriteWord(address + 4, (uint)(value >> 32));
    }

    public string ReadCString(uint address)
    {
        var bytes = new List<byte>();
        for (var i = 0; i < 1_000_000; i++)
        {
            var value = ReadByte(address++);
            if (value == 0)
                return Encoding.UTF8.GetString(bytes.ToArray());
            bytes.Add(value);
        }
        throw new InvalidOperationException("Unterminated string.");
    }

    public void WriteCString(uint address, string value, int maxBytes = int.MaxValue)
    {
        if (maxBytes <= 0) return;
        var bytes = Encoding.UTF8.GetBytes(value);
        var count = Math.Min(bytes.Length, Math.Max(0, maxBytes - 1));
        for (var i = 0; i < count; i++) WriteByte(address + (uint)i, bytes[i]);
        WriteByte(address + (uint)count, 0);
    }

    private static void EnsureAligned(uint address, uint alignment, string kind)
    {
        if (address % alignment != 0)
            throw new InvalidOperationException($"Unaligned {kind} address 0x{address:X8}.");
    }
}
