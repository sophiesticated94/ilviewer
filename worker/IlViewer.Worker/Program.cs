using IlViewer.Worker.Analysis;
using IlViewer.Worker.Artifacts;
using IlViewer.Worker.Cli;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection()
    .AddSingleton<IProjectArtifactResolver, ProjectArtifactResolver>()
    .AddSingleton<IInstructionCatalog, InstructionCatalog>()
    .AddSingleton<ISourceRegionAnalyzer, SourceRegionAnalyzer>()
    .AddSingleton<CecilModuleLoader>()
    .AddSingleton<InstructionNavigationTargetFactory>()
    .AddSingleton<IlInstructionFactory>()
    .AddSingleton<IlScopeBuilder>()
    .AddSingleton<InstructionHighlightBuilder>()
    .AddSingleton<IApplicationGraphService, ApplicationGraphService>()
    .AddSingleton<IDecompilationService, DecompilationService>()
    .AddSingleton<IAssemblyIlAnalyzer, AssemblyIlAnalyzer>()
    .AddSingleton<WorkerCli>()
    .BuildServiceProvider();

return await services.GetRequiredService<WorkerCli>().RunAsync(args, Console.Out, Console.Error);
