namespace ASMForge.Projects;
public sealed record Project(string Name,string RootPath,IReadOnlyList<ProjectFile> Files); public sealed record ProjectFile(string RelativePath,string Language);
