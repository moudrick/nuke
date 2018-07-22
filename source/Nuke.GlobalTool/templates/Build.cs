using System;
using System.Linq;
using Nuke.Common;
using Nuke.Common.Git;                                                                          // GIT
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;                                                                 // DOTNET
using Nuke.Common.Tools.GitVersion;                                                             // GIT_VERSION
using Nuke.Common.Tools.MSBuild;                                                                // MSBUILD
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;                                              // DOTNET
using static Nuke.Common.Tools.MSBuild.MSBuildTasks;                                            // MSBUILD

class Build : NukeBuild
{
    public static int Main () => Execute<Build>(x => x.Compile);

    [Parameter] readonly string Source = "https://api.nuget.org/v3/index.json";                 // NUGET
    [Parameter] readonly string SymbolSource = "https://nuget.smbsrc.net/";                     // NUGET
    [Parameter] readonly string ApiKey;                                                         // NUGET

    [Solution] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;                                       // GIT
    [GitVersion] readonly GitVersion GitVersion;                                                // GIT_VERSION

    AbsolutePath SourceDirectory => RootDirectory / "source";                                   // SOURCE_DIR
    AbsolutePath SourceDirectory => RootDirectory / "src";                                      // SRC_DIR
    AbsolutePath TestsDirectory => RootDirectory / "tests";                                     // TESTS_DIR
    AbsolutePath OutputDirectory => RootDirectory / "output";                                   // OUTPUT_DIR
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";                             // ARTIFACTS_DIR

    Target Clean => _ => _
        .Executes(() =>
        {
            DeleteDirectories(GlobDirectories(SourceDirectory, "**/bin", "**/obj"));            // SOURCE_DIR
            DeleteDirectories(GlobDirectories(SourceDirectory, "**/bin", "**/obj"));            // SRC_DIR
            DeleteDirectories(GlobDirectories(TestsDirectory, "**/bin", "**/obj"));             // TESTS_DIR
            EnsureCleanDirectory(OutputDirectory);                                              // OUTPUT_DIR
            EnsureCleanDirectory(ArtifactsDirectory);                                           // ARTIFACTS_DIR
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            MSBuild(s => DefaultMSBuildRestore);                                                // MSBUILD
            DotNetRestore(s => DefaultDotNetRestore);                                           // DOTNET
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            MSBuild(s => DefaultMSBuildCompile);                                                // MSBUILD
            DotNetBuild(s => DefaultDotNetBuild);                                               // DOTNET
        });

    Target Pack => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetPack(s => DefaultDotNetPack                                                   // DOTNET && OUTPUT_DIR
                .SetOutputDirectory(OutputDirectory));                                          // DOTNET && OUTPUT_DIR
            DotNetPack(s => DefaultDotNetPack                                                   // DOTNET && ARTIFACTS_DIR
                .SetOutputDirectory(ArtifactsDirectory));                                       // DOTNET && ARTIFACTS_DIR
            MSBuild(s => DefaultMSBuildPack                                                     // MSBUILD && OUTPUT_DIR
                .SetPackageOutputPath(OutputDirectory));                                        // MSBUILD && OUTPUT_DIR
            MSBuild(s => DefaultMSBuildPack                                                     // MSBUILD && ARTIFACTS_DIR
                .SetPackageOutputPath(ArtifactsDirectory));                                     // MSBUILD && ARTIFACTS_DIR
        });
}
