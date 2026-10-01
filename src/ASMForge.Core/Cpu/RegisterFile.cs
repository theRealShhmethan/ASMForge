namespace ASMForge.Core.Cpu;
public sealed class RegisterFile {
    public const int Count=32; private readonly int[] _r=new int[Count];
    public int this[int i] { get => i==0?0:_r[i]; set { if(i!=0)_r[i]=value; } }
    public int HI {get;set;} public int LO {get;set;}
    public void Reset(){Array.Clear(_r);HI=LO=0;}
    public static readonly string[] Names={"$zero","$at","$v0","$v1","$a0","$a1","$a2","$a3","$t0","$t1","$t2","$t3","$t4","$t5","$t6","$t7","$s0","$s1","$s2","$s3","$s4","$s5","$s6","$s7","$t8","$t9","$k0","$k1","$gp","$sp","$fp","$ra"};
    public static int Parse(string s){s=s.Trim(); if(int.TryParse(s.TrimStart('$'),out var n)&&n>=0&&n<32)return n; for(int i=0;i<Names.Length;i++)if(Names[i].Equals(s,StringComparison.OrdinalIgnoreCase))return i; throw new ArgumentException($"Unknown register {s}");}
}
