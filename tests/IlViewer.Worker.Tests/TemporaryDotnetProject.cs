using System.Diagnostics;

namespace IlViewer.Worker.Tests;

public sealed class TemporaryDotnetProject : IDisposable
{
    private TemporaryDotnetProject(string directoryPath, string projectPath, string sourcePath, string sourceText)
    {
        DirectoryPath = directoryPath;
        ProjectPath = projectPath;
        SourcePath = sourcePath;
        SourceText = sourceText;
    }

    public string DirectoryPath { get; }
    public string ProjectPath { get; }
    public string SourcePath { get; }
    private string SourceText { get; }

    public static async Task<TemporaryDotnetProject> CreateAsync(
        string projectName,
        string projectExtension,
        string projectText,
        string sourceFileName,
        string sourceText)
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), "ilviewer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);

        var projectPath = Path.Combine(directoryPath, $"{projectName}.{projectExtension}");
        var sourcePath = Path.Combine(directoryPath, sourceFileName);
        await File.WriteAllTextAsync(projectPath, projectText);
        await File.WriteAllTextAsync(sourcePath, sourceText);

        var project = new TemporaryDotnetProject(directoryPath, projectPath, sourcePath, sourceText);
        await BuildAsync(projectPath, directoryPath);
        return project;
    }

    public static async Task<TemporaryDotnetProject> CreateWithProjectReferenceAsync()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), "ilviewer-tests", Guid.NewGuid().ToString("N"));
        var rootDirectory = Path.Combine(directoryPath, "RootApplication");
        var referenceDirectory = Path.Combine(directoryPath, "ReferencedLibrary");
        Directory.CreateDirectory(rootDirectory);
        Directory.CreateDirectory(referenceDirectory);

        var rootProjectPath = Path.Combine(rootDirectory, "RootApplication.csproj");
        var rootSourcePath = Path.Combine(rootDirectory, "Program.cs");
        var referenceProjectPath = Path.Combine(referenceDirectory, "ReferencedLibrary.csproj");
        var referenceSourcePath = Path.Combine(referenceDirectory, "Helper.cs");

        await File.WriteAllTextAsync(referenceProjectPath, ProjectFiles.CSharpProject());
        await File.WriteAllTextAsync(referenceSourcePath, """
        namespace ReferencedLibrary;

        public static class Helper
        {
            public static int Value()
            {
                return 41;
            }
        }
        """);
        await File.WriteAllTextAsync(rootProjectPath, """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <DebugType>portable</DebugType>
            <Optimize>false</Optimize>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="..\ReferencedLibrary\ReferencedLibrary.csproj" />
          </ItemGroup>
        </Project>
        """);
        var rootSource = """
        using ReferencedLibrary;

        namespace RootApplication;

        public class Entry
        {
            public int Run()
            {
                return Helper.Value();
            }
        }
        """;
        await File.WriteAllTextAsync(rootSourcePath, rootSource);

        var project = new TemporaryDotnetProject(directoryPath, rootProjectPath, rootSourcePath, rootSource);
        await BuildAsync(rootProjectPath, rootDirectory);
        return project;
    }

    public int FindLine(string text)
    {
        var lines = SourceText.Replace("\r\n", "\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].Contains(text, StringComparison.Ordinal))
            {
                return index + 1;
            }
        }

        throw new InvalidOperationException($"Could not find line containing '{text}'.");
    }

    public int FindBlankLineAfter(string text)
    {
        var lines = SourceText.Replace("\r\n", "\n").Split('\n');
        var previousLineIndex = Array.FindIndex(lines, line => line.Contains(text, StringComparison.Ordinal));
        if (previousLineIndex < 0)
        {
            throw new InvalidOperationException($"Could not find line containing '{text}'.");
        }

        for (var index = previousLineIndex + 1; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                return index + 1;
            }
        }

        throw new InvalidOperationException($"Could not find a blank line after '{text}'.");
    }

    public SourceSelection FindRange(string text)
    {
        var normalizedSource = SourceText.Replace("\r\n", "\n");
        var normalizedText = text.Replace("\r\n", "\n");
        var start = normalizedSource.IndexOf(normalizedText, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException($"Could not find text '{text}'.");
        }

        var end = start + normalizedText.Length;
        var beforeStart = normalizedSource[..start];
        var beforeEnd = normalizedSource[..end];
        var startLine = beforeStart.Count(character => character == '\n') + 1;
        var endLine = beforeEnd.Count(character => character == '\n') + 1;
        var startColumn = start - beforeStart.LastIndexOf('\n');
        var endColumn = end - beforeEnd.LastIndexOf('\n');
        return new SourceSelection(startLine, endLine, startColumn, endColumn);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task BuildAsync(string projectPath, string workingDirectory)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        process.StartInfo.ArgumentList.Add("build");
        process.StartInfo.ArgumentList.Add(projectPath);
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add("Debug");
        process.StartInfo.ArgumentList.Add("-v");
        process.StartInfo.ArgumentList.Add("minimal");
        process.StartInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet build failed for {projectPath}:{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
        }
    }
}
