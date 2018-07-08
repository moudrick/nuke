// Copyright Matthias Koch, Sebastian Karasek 2018.
// Distributed under the MIT License.
// https://github.com/nuke-build/nuke/blob/master/LICENSE

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using JetBrains.Annotations;
using Nuke.Common;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.Git;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Utilities;
using Nuke.Common.Utilities.Collections;

namespace Nuke.GlobalTool
{
    public class Program
    {
        private static string ScriptHost => EnvironmentInfo.IsWin ? "powershell" : "bash";
        private static string ScriptExtension => EnvironmentInfo.IsWin ? "ps1" : "sh";
        
        private const char c_commandPrefix = ':';

        private const string c_developBranch = "develop";
        private const string c_masterBranch = "master";
        private const string c_releasePrefix = "release";
        private const string c_hotfixPrefix = "hotfix";
        private const string c_featurePrefix = "feature";

        private static void Main(string[] args)
        {
            try
            {
                Handle(args);
            }
            catch (Exception exception)
            {
                Logger.Error(exception.Message);
                Environment.Exit(exitCode: 1);
            }
        }

        private static void Handle(string[] args)
        {
            var currentDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
            var rootDirectory = FileSystemTasks.FindParentDirectory(currentDirectory, x => x.GetFiles(NukeBuild.ConfigurationFile).Any());
            
            var hasCommand = args.FirstOrDefault()?.StartsWithOrdinalIgnoreCase(c_commandPrefix.ToString()) ?? false;
            if (hasCommand)
            {
                var command = args.First().Trim(trimChar: c_commandPrefix);
                if (string.IsNullOrWhiteSpace(command))
                    ControlFlow.Fail($"No command specified. Usage is: nuke {c_commandPrefix}<command> [args]");

                var commandHandler = typeof(Program).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .SingleOrDefault(x => x.Name.EqualsOrdinalIgnoreCase(command));
                ControlFlow.Assert(commandHandler != null, $"Command '{command}' is not supported.");

                try
                {
                    commandHandler.Invoke(obj: null, parameters: new object[] { rootDirectory, args.Skip(count: 1).ToArray() });
                    return;
                }
                catch (TargetInvocationException ex)
                {
                    ControlFlow.Fail(ex.InnerException.Message);
                }
            }

            var buildScript = rootDirectory?
                .EnumerateFiles($"build.{ScriptExtension}", maxDepth: 2)
                .FirstOrDefault()?.FullName;

            if (buildScript == null)
            {
                if (UserConfirms($"Could not find {NukeBuild.ConfigurationFile} file. Do you want to setup a build?"))
                    Setup(rootDirectory, new string[0]);
                return;
            }

            // TODO: docker
            RunScript(buildScript, args.Select(x => x.DoubleQuoteIfNeeded()).JoinSpace());
        }
        
        [UsedImplicitly]
        private static void Setup([CanBeNull] DirectoryInfo rootDirectory, string[] args)
        {
            // TODO: embed all bootstrapping files and reimplement setup for offline usage?
            // TODO: alternatively, use a similar approach as in SetupIntegrationTest
            var setupScript = Path.Combine(Directory.GetCurrentDirectory(), $"setup.{ScriptExtension}");
            if (!File.Exists(setupScript))
            {
                using (var webClient = new WebClient())
                {
                    var setupScriptUrl = $"https://nuke.build/{ScriptHost}";
                    Logger.Log($"Downloading setup script from {setupScriptUrl}");
                    webClient.DownloadFile(setupScriptUrl, setupScript);
                }
            }

            RunScript(setupScript, args.JoinSpace());
        }

        [UsedImplicitly]
        private static void Release([CanBeNull] DirectoryInfo rootDirectory, string[] args)
        {
            ControlFlow.Assert(rootDirectory != null, "Must be executed in NUKE-controlled repository.");
            ControlFlow.Assert(args.Length == 0, "Arguments must be empty.");
            
            var repository = GitRepository.FromLocalDirectory(rootDirectory.FullName);
            var gitVersion = GitVersionTasks.GitVersion(s => s.SetWorkingDirectory(rootDirectory.FullName));
            var releaseBranch = $"{c_releasePrefix}/{(args.Length == 0 ? gitVersion.MajorMinorPatch : args.SingleOrDefault())}";
            var onReleaseBranch = repository.Branch.StartsWithOrdinalIgnoreCase(c_releasePrefix);

            if (!onReleaseBranch)
            {
                GitTasks.Git($"checkout -b {releaseBranch} {c_developBranch}");
            }
            else
            {
                ControlFlow.Assert(args.Length == 0, $"Arguments must be empty.");
                GitTasks.Git($"checkout {c_developBranch}");
                GitTasks.Git($"merge --no-ff --no-edit {releaseBranch}");
                GitTasks.Git($"checkout {c_masterBranch}");
                GitTasks.Git($"merge --no-ff --no-edit {releaseBranch}");
                GitTasks.Git($"tag {gitVersion.MajorMinorPatch}");
                GitTasks.Git($"branch -D {releaseBranch}");
            }
        }
        
        [UsedImplicitly]
        private static void Hotfix([CanBeNull] DirectoryInfo rootDirectory, string[] args)
        {
            ControlFlow.Assert(rootDirectory != null, "Must be executed in NUKE-controlled repository.");

            var repository = GitRepository.FromLocalDirectory(rootDirectory.FullName);
            var gitVersion = GitVersionTasks.GitVersion(s => s.SetWorkingDirectory(rootDirectory.FullName));
            var hotfixBranch = args.Length == 0 ? repository.Branch : $"{c_hotfixPrefix}/{args.FirstOrDefault()}";
            var onHotfixBranch = repository.Branch.StartsWithOrdinalIgnoreCase(c_hotfixPrefix);

            if (!onHotfixBranch)
            {
                ControlFlow.Assert(args.Length == 1, $"Must provide a single name for the {c_hotfixPrefix} branch.");
                GitTasks.Git($"checkout -b {hotfixBranch} {c_masterBranch}");
            }
            else
            {
                ControlFlow.Assert(args.Length == 0, $"Arguments must be empty.");
                GitTasks.Git($"checkout {c_developBranch}");
                GitTasks.Git($"merge --no-ff --no-edit {hotfixBranch}");
                GitTasks.Git($"checkout {c_masterBranch}");
                GitTasks.Git($"merge --no-ff --no-edit {hotfixBranch}");
                GitTasks.Git($"tag {gitVersion.MajorMinorPatch}");
                GitTasks.Git($"branch -D {hotfixBranch}");
            }
        }
        
        [UsedImplicitly]
        private static void Feature([CanBeNull] DirectoryInfo rootDirectory, string[] args)
        {
            ControlFlow.Assert(rootDirectory != null, "Must be executed in NUKE-controlled repository.");
            
            var repository = GitRepository.FromLocalDirectory(rootDirectory.FullName);
            var featureBranch = args.Length == 0 ? repository.Branch : $"{c_featurePrefix}/{args.FirstOrDefault()}";
            var onFeatureBranch = repository.Branch.StartsWithOrdinalIgnoreCase(c_featurePrefix);

            if (!onFeatureBranch)
            {
                ControlFlow.Assert(args.Length == 1, $"Must provide a single name for the {c_featurePrefix} branch.");
                GitTasks.Git($"checkout -b {featureBranch} {c_developBranch}");
            }
            else
            {
                ControlFlow.Assert(args.Length == 0, $"Arguments must be empty.");
                GitTasks.Git($"checkout {c_developBranch}");
                GitTasks.Git($"merge --no-ff --no-edit {featureBranch}");
                GitTasks.Git($"branch -D {featureBranch}");
            }
        }

        private static bool UserConfirms(string question)
        {
            ConsoleKey response;
            do
            {
                Logger.Log($"{question} [y/n]");
                response = Console.ReadKey(intercept: true).Key;
            } while (response != ConsoleKey.Y && response != ConsoleKey.N);

            return response == ConsoleKey.Y;
        }

        private static void RunScript(string file, string arguments = null)
        {
            file = file.DoubleQuoteIfNeeded();
            
            var process = ProcessTasks.StartProcess(
                ScriptHost,
                EnvironmentInfo.IsWin
                    ? $"-File {file} {arguments}"
                    : $"{file} {arguments}");

            process.AssertWaitForExit();
            if (process.ExitCode != 0)
                Environment.Exit(process.ExitCode);
        }
    }
}
