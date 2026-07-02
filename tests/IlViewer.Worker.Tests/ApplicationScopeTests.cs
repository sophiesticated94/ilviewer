using IlViewer.Worker.Analysis;
using IlViewer.Worker.Models;

namespace IlViewer.Worker.Tests;

public sealed class ApplicationScopeTests : WorkerTestBase
{
    [Fact]
    public async Task Analyze_ApplicationScope_IncludesProjectReferenceAssembly()
    {
        using var project = await TemporaryDotnetProject.CreateWithProjectReferenceAsync();

        var result = Analyze(project, "return Helper.Value();");
        var applicationScope = Assert.Single(result.Scopes.Where(scope => scope.Kind == "application"));

        Assert.True(result.Success, result.Error);
        Assert.Contains(applicationScope.Methods, method => method.AssemblyName == "ReferencedLibrary");
        Assert.Contains(applicationScope.Methods, method => method.AssemblyName == "RootApplication");
    }

    [Fact]
    public async Task Graph_ExpandRootAssembly_IncludesRuntimeOrFrameworkReference()
    {
        using var project = await TemporaryDotnetProject.CreateWithProjectReferenceAsync();
        var request = CreateGraphRequest(project, nodeId: null);
        var artifacts = ArtifactResolver.Resolve(CreateArtifactRequest(project));
        var graph = new ApplicationGraphService(new CecilModuleLoader());

        var root = graph.GetRoot(artifacts, request);
        var rootNode = Assert.Single(root.Nodes);
        var expanded = graph.Expand(artifacts, request with { NodeId = rootNode.Id, PageSize = 1000 });

        Assert.True(expanded.Success, expanded.Error);
        Assert.Contains(expanded.Nodes, node => node.AssemblyKind == AssemblyKinds.Runtime || node.AssemblyKind == AssemblyKinds.Framework);
    }

    [Fact]
    public async Task Decompile_ProjectType_ReturnsCSharp()
    {
        using var project = await TemporaryDotnetProject.CreateWithProjectReferenceAsync();
        var artifacts = ArtifactResolver.Resolve(CreateArtifactRequest(project));
        var decompiler = new DecompilationService(new CecilModuleLoader());

        var result = decompiler.Decompile(artifacts, new DecompileRequest(
            project.ProjectPath,
            "Debug",
            "net8.0",
            null,
            "RootApplication",
            "RootApplication.Entry",
            null,
            null,
            "csharp"));

        Assert.True(result.Success, result.Error);
        Assert.Equal("csharp", result.Language);
        Assert.Contains("class Entry", result.Content, StringComparison.Ordinal);
    }

    private static GraphRequest CreateGraphRequest(TemporaryDotnetProject project, string? nodeId)
    {
        return new GraphRequest(project.ProjectPath, "Debug", "net8.0", nodeId, 250, null);
    }

    private static AnalysisRequest CreateArtifactRequest(TemporaryDotnetProject project)
    {
        return new AnalysisRequest(project.ProjectPath, project.SourcePath, 1, 1, "Debug", "net8.0");
    }
}
