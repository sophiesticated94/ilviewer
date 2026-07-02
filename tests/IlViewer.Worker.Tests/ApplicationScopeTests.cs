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
}
