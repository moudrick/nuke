// Copyright Matthias Koch, Sebastian Karasek 2018.
// Distributed under the MIT License.
// https://github.com/nuke-build/nuke/blob/master/LICENSE

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using Nuke.Common.Utilities;
using Nuke.Common.Utilities.Collections;

namespace Nuke.Common.Execution
{
    internal static class TargetDefinitionLoader
    {
        public static IReadOnlyCollection<ExecutableTarget> GetExecutingTargets(NukeBuild build, string[] invokedTargetNames = null)
        {
            ControlFlow.Assert(build.ExecutableTargets.All(x => !x.Name.EqualsOrdinalIgnoreCase(BuildExecutor.DefaultTarget)),
                $"The name '{BuildExecutor.DefaultTarget}' cannot be used as target name.");

            var invokedTargets = invokedTargetNames?.Select(x => GetExecutableTarget(x, build)).ToList() ?? new List<ExecutableTarget>();
            var executingTargets = GetUnfilteredExecutingTargets(build, invokedTargets);

            var skippedTargets = executingTargets
                .Where(x => !invokedTargets.Contains(x) &&
                            build.SkippedTargets != null &&
                            (build.SkippedTargets.Length == 0 ||
                             build.SkippedTargets.Contains(x.Name, StringComparer.OrdinalIgnoreCase))).ToList();
            skippedTargets.ForEach(x => x.Skip = true);
            
            executingTargets
                .Where(x => x.Definition.DependencyBehavior == DependencyBehavior.Skip)
                .Where(x => x.Conditions.Any(y => !y()))
                .ForEach(x => SkipTargetAndDependencies(x, invokedTargets, executingTargets));

            string[] GetNames(IEnumerable<ExecutableTarget> targets)
                => targets.Select(x => x.Name).ToArray();

            ReflectionService.SetValue(build, nameof(NukeBuild.InvokedTargets), GetNames(invokedTargets));
            ReflectionService.SetValue(build, nameof(NukeBuild.SkippedTargets), GetNames(skippedTargets));
            ReflectionService.SetValue(build, nameof(NukeBuild.ExecutingTargets), GetNames(executingTargets.Except(skippedTargets)));

            return executingTargets;
        }

        private static void SkipTargetAndDependencies(
            ExecutableTarget target,
            IReadOnlyCollection<ExecutableTarget> invokedTargets,
            IReadOnlyCollection<ExecutableTarget> executingTargets,
            IDictionary<ExecutableTarget, List<ExecutableTarget>> skipRequestDictionary = null)
        {
            skipRequestDictionary = skipRequestDictionary ?? new Dictionary<ExecutableTarget, List<ExecutableTarget>>();
            
            target.Skip = true;
            foreach (var dependency in target.Dependencies)
            {
                if (invokedTargets.Contains(dependency))
                    continue;
                
                var skipRequests = skipRequestDictionary.GetValueOrDefault(dependency);
                if (skipRequests == null)
                    skipRequests = skipRequestDictionary[dependency] = new List<ExecutableTarget>();
                
                var executingDependentTargets = executingTargets
                    .Where(x => x != target)
                    .Where(x => x.Dependencies.Contains(dependency) && !skipRequests.Contains(x));
                
                if (executingDependentTargets.Any())
                    skipRequests.Add(target);
                else
                    SkipTargetAndDependencies(dependency, invokedTargets, executingTargets, skipRequestDictionary);
            }
        }

        private static ExecutableTarget GetExecutableTarget(
            string targetName,
            NukeBuild build)
        {
            if (targetName.EqualsOrdinalIgnoreCase(BuildExecutor.DefaultTarget))
                return build.ExecutableTargets.Single(x => x.IsDefault);

            var targetDefinition = build.ExecutableTargets.SingleOrDefault(x => x.Name.EqualsOrdinalIgnoreCase(targetName));
            if (targetDefinition == null)
            {
                var stringBuilder = new StringBuilder()
                    .AppendLine($"Target with name '{targetName}' is not available.")
                    .AppendLine()
                    .AppendLine(HelpTextService.GetTargetsText(build));

                ControlFlow.Fail(stringBuilder.ToString());
            }

            return targetDefinition;
        }

        private static List<ExecutableTarget> GetUnfilteredExecutingTargets(NukeBuild build, IReadOnlyCollection<ExecutableTarget> invokedTargets)
        {
            var vertexDictionary = build.ExecutableTargets.ToDictionary(x => x, x => new Vertex<ExecutableTarget>(x));
            foreach (var pair in vertexDictionary)
                pair.Value.Dependencies = pair.Key.Dependencies.Select(x => vertexDictionary[x]).ToList();

            var graphAsList = vertexDictionary.Values.ToList();
            var executingTargets = new List<ExecutableTarget>();

            while (graphAsList.Any())
            {
                var independents = graphAsList.Where(x => !graphAsList.Any(y => y.Dependencies.Contains(x))).ToList();
                if (EnvironmentInfo.ArgumentSwitch("strict") && independents.Count > 1)
                {
                    ControlFlow.Fail(
                        new[] { "Incomplete target definition order." }
                            .Concat(independents.Select(x => $"  - {x.Value.Name}"))
                            .JoinNewLine());
                }

                var independent = independents.FirstOrDefault();
                if (independent == null)
                {
                    var scc = new StronglyConnectedComponentFinder<ExecutableTarget>();
                    var cycles = scc.DetectCycle(graphAsList)
                        .Cycles()
                        .Select(x => string.Join(" -> ", x.Select(y => y.Value.Name)));

                    ControlFlow.Fail(
                        new[] { "Circular dependencies between target definitions." }
                            .Concat(independents.Select(x => $"  - {cycles}"))
                            .JoinNewLine());
                }

                graphAsList.Remove(independent);

                var targetDefinition = independent.Value;
                var dependencies = executingTargets.SelectMany(x => x.Dependencies);
                if (!invokedTargets.Contains(targetDefinition) &&
                    !dependencies.Contains(targetDefinition))
                    continue;

                executingTargets.Add(targetDefinition);
            }

            executingTargets.Reverse();

            return executingTargets;
        }
    }
}
