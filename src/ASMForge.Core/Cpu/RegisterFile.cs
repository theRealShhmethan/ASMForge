using ASMForge.Core.Execution;

namespace ASMForge.Core.Cpu;

public sealed class RegisterFile
{
    public const int Count = 32;
    public const int HiIndex = 32;
    public const int LoIndex = 33;
    private readonly int[] _r = new int[Count];
    private int _hi;
    private int _lo;

    /// <summary>While set, every register write is recorded for the current step.</summary>
    internal StepJournal? Journal { get; set; }

    public int this[int i]
    {
        get => i == 0 ? 0 : _r[i];
        set
        {
            if (i == 0) return;
            Journal?.Registers.Add(new RegisterChange(i, _r[i], value));
            _r[i] = value;
        }
    }

    public int HI
    {
        get => _hi;
        set
        {
            Journal?.Registers.Add(new RegisterChange(HiIndex, _hi, value));
            _hi = value;
        }
    }

    public int LO
    {
        get => _lo;
        set
        {
            Journal?.Registers.Add(new RegisterChange(LoIndex, _lo, value));
            _lo = value;
        }
    }

    public void Reset()
    {
        Array.Clear(_r);
        _hi = _lo = 0;
    }

    /// <summary>Reads a register by change index: 0-31, <see cref="HiIndex"/> or <see cref="LoIndex"/>.</summary>
    public int GetByIndex(int index) => index switch
    {
        HiIndex => _hi,
        LoIndex => _lo,
        _ => this[index]
    };

    internal void SetByIndex(int index, int value)
    {
        switch (index)
        {
            case HiIndex: HI = value; break;
            case LoIndex: LO = value; break;
            default: this[index] = value; break;
        }
    }

    public static string NameOf(int index) => index switch
    {
        HiIndex => "HI",
        LoIndex => "LO",
        _ => Names[index]
    };

    public static readonly string[] Names =
    {
        "$zero", "$at", "$v0", "$v1", "$a0", "$a1", "$a2", "$a3",
        "$t0", "$t1", "$t2", "$t3", "$t4", "$t5", "$t6", "$t7",
        "$s0", "$s1", "$s2", "$s3", "$s4", "$s5", "$s6", "$s7",
        "$t8", "$t9", "$k0", "$k1", "$gp", "$sp", "$fp", "$ra"
    };

    public static int Parse(string value)
    {
        var s = value.Trim();
        if (int.TryParse(s.TrimStart('$'), out var number) && number is >= 0 and < 32)
            return number;

        if (s.Equals("$s8", StringComparison.OrdinalIgnoreCase))
            return 30;

        for (var i = 0; i < Names.Length; i++)
            if (Names[i].Equals(s, StringComparison.OrdinalIgnoreCase))
                return i;

        throw new ArgumentException($"Unknown register {value}");
    }
}
