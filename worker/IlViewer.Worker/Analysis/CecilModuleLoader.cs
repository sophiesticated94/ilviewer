using IlViewer.Worker.Models;
using Mono.Cecil;

namespace IlViewer.Worker.Analysis;

public sealed class CecilModuleLoader
{
    public ModuleDefinition LoadModule(ProjectArtifacts artifacts, string assemblyPath)
    {
        var resolver = new DefaultAssemblyResolver();
        foreach (var directory in artifacts.AssemblySearchDirectories)
        {
            if (Directory.Exists(directory))
            {
                resolver.AddSearchDirectory(directory);
            }
        }

        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        return ModuleDefinition.ReadModule(assemblyPath, new ReaderParameters
        {
            AssemblyResolver = resolver,
            ReadSymbols = File.Exists(pdbPath),
            ReadingMode = ReadingMode.Deferred
        });
    }
}
