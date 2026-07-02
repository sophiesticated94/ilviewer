namespace IlViewer.Worker.Tests;

internal static class ProjectFiles
{
    public static string CSharpProject()
    {
        return """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <DebugType>portable</DebugType>
            <Optimize>false</Optimize>
          </PropertyGroup>
        </Project>
        """;
    }

    public static string FSharpProject()
    {
        return """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <DebugType>portable</DebugType>
            <Optimize>false</Optimize>
          </PropertyGroup>
          <ItemGroup>
            <Compile Include="Library.fs" />
          </ItemGroup>
        </Project>
        """;
    }

    public static string VisualBasicProject()
    {
        return """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <RootNamespace>Sample</RootNamespace>
            <DebugType>portable</DebugType>
            <Optimize>false</Optimize>
          </PropertyGroup>
        </Project>
        """;
    }
}
