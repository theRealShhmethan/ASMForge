namespace ASMForge.Core.Assembly;
public sealed class SimpleAssembler {
 public AssemblyProgram Assemble(string source){var ins=new List<Instruction>();var labels=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);var lines=source.Replace("\r","").Split('\n');
  for(int i=0;i<lines.Length;i++){var raw=lines[i];var s=raw.Split('#')[0].Trim();if(s.Length==0||s.StartsWith("."))continue;while(s.Contains(':')){var p=s.IndexOf(':');var label=s[..p].Trim();if(label.Length>0)labels[label]=ins.Count;s=s[(p+1)..].Trim();if(s.Length==0)break;}if(s.Length==0)continue;var parts=s.Split(new[]{' ','\t'},2,StringSplitOptions.RemoveEmptyEntries);var args=parts.Length>1?parts[1].Split(',').Select(x=>x.Trim()).Where(x=>x.Length>0).ToArray():Array.Empty<string>();ins.Add(new(i+1,parts[0].ToLowerInvariant(),args,raw.Trim()));}
  return new(ins,labels);
 }
}
