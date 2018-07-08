// Copyright Matthias Koch, Sebastian Karasek 2018.
// Distributed under the MIT License.
// https://github.com/nuke-build/nuke/blob/master/LICENSE

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Nuke.Common.OutputSinks;
using Nuke.Common.Utilities;

namespace Nuke.Common.Execution
{
    internal static class BuildExecutor
    {
        public const string DefaultTarget = "default";

        private static readonly BuildFactory s_buildFactory = new BuildFactory(x => NukeBuild.Instance = x);
        private static readonly InjectionService s_injectionService = new InjectionService();

        public static int Execute<T>(Expression<Func<T, Target>> defaultTargetExpression)
            where T : NukeBuild
        {
            Logger.Log(FigletTransform.GetText("NUKE"));
            Logger.Log($"Version: {typeof(BuildExecutor).GetTypeInfo().Assembly.GetVersionText()}");
            Logger.Log($"Host: {EnvironmentInfo.HostType}");
            Logger.Log();

            var executingTargets = default(IReadOnlyCollection<ExecutableTarget>);
            try
            {
                var build = s_buildFactory.Create(defaultTargetExpression);
                s_injectionService.InjectValues(build);
                HandleEarlyExits(build);

                executingTargets = TargetDefinitionLoader.GetExecutingTargets(build, build.InvokedTargets);
                RequirementService.ValidateRequirements(executingTargets, build);
                Execute(executingTargets);
                
                return 0;
            }
            catch (AggregateException exception)
            {
                foreach (var innerException in exception.Flatten().InnerExceptions)
                    OutputSink.Error(innerException.Message, innerException.StackTrace);
                return -exception.Message.GetHashCode();
            }
            catch (TargetInvocationException exception)
            {
                var innerException = exception.InnerException.NotNull();
                OutputSink.Error(innerException.Message, innerException.StackTrace);
                return -exception.Message.GetHashCode();
            }
            catch (Exception exception)
            {
                OutputSink.Error(exception.Message, exception.StackTrace);
                return -exception.Message.GetHashCode();
            }
            finally
            {
                if (executingTargets != null)
                    OutputSink.WriteSummary(executingTargets);
            }
        }

        private static void Execute(IEnumerable<ExecutableTarget> executableTargets)
        {
            foreach (var target in executableTargets)
            {
                if (target.Factory == null)
                {
                    target.Status = ExecutionStatus.Absent;
                    continue;
                }

                if (target.Skip || target.Conditions.Any(x => !x()))
                {
                    target.Status = ExecutionStatus.Skipped;
                    continue;
                }

                using (Logger.Block(target.Name))
                {
                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        target.Actions.ToList().ForEach(x => x());
                        target.Duration = stopwatch.Elapsed;

                        target.Status = ExecutionStatus.Executed;
                    }
                    catch
                    {
                        target.Status = ExecutionStatus.Failed;
                        throw;
                    }
                    finally
                    {
                        target.Duration = stopwatch.Elapsed;
                    }
                }
            }
        }

        private static void HandleEarlyExits<T>(T build)
            where T : NukeBuild
        {
            if (build.Help)
            {
                Logger.Log(HelpTextService.GetTargetsText(build));
                Logger.Log(HelpTextService.GetParametersText(build));
            }

            if (build.Graph)
                GraphService.ShowGraph(build);

            if (build.Help || build.Graph)
                Environment.Exit(exitCode: 0);
        }
    }
}
