// Copyright Matthias Koch, Sebastian Karasek 2018.
// Distributed under the MIT License.
// https://github.com/nuke-build/nuke/blob/master/LICENSE

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Nuke.Common.Utilities;

[assembly: InternalsVisibleTo("Nuke.Common.Tests")]

namespace Nuke.Common.Execution
{
    internal class ExecutableTarget
    {
        public ExecutableTarget(
            PropertyInfo property,
            Target factory,
            TargetDefinition definition,
            bool isDefault,
            IReadOnlyList<ExecutableTarget> dependencies)
        {
            Property = property;
            Factory = factory;
            Definition = definition;
            Dependencies = dependencies;
            IsDefault = isDefault;
        }

        public PropertyInfo Property { get; }
        public string Name => Property.Name;
        public Target Factory { get; }
        public TargetDefinition Definition { get; }
        public string Description => Definition.Description;
        public List<Func<bool>> Conditions => Definition.Conditions;
        public IReadOnlyList<LambdaExpression> Requirements => Definition.Requirements;
        public IReadOnlyList<Action> Actions => Definition.Actions;
        public IReadOnlyList<ExecutableTarget> Dependencies { get; }
        public bool IsDefault { get; }
        
        public bool Skip { get; set; }
        public ExecutionStatus Status { get; set; }
        public TimeSpan Duration { get; set; }
}

    internal class BuildFactory
    {
        private readonly Action<NukeBuild> _singletonSetter;

        public BuildFactory(Action<NukeBuild> singletonSetter)
        {
            _singletonSetter = singletonSetter;
        }

        public T Create<T>(Expression<Func<T, Target>> defaultTargetExpression)
            where T : NukeBuild
        {
            var constructors = typeof(T).GetConstructors();
            ControlFlow.Assert(constructors.Length == 1 && constructors.Single().GetParameters().Length == 0,
                $"Type '{typeof(T).Name}' must declare a single parameterless constructor.");

            var build = Activator.CreateInstance<T>();
            var defaultTarget = defaultTargetExpression.Compile().Invoke(build);
            build.ExecutableTargets = GetTargetDefinitions(build, defaultTarget);
            _singletonSetter(build);

            return build;
        }

        private IReadOnlyCollection<ExecutableTarget> GetTargetDefinitions(NukeBuild build, Target defaultTarget)
        {
            var properties = build.GetType()
                .GetProperties(ReflectionService.Instance)
                .Where(x => x.PropertyType == typeof(Target)).ToList();

            var executables = new List<ExecutableTarget>();
            var dependencyDictionary = new Dictionary<ExecutableTarget, List<ExecutableTarget>>();

            foreach (var property in properties)
            {
                var factory = (Target) property.GetValue(build);
                var definition = new TargetDefinition();
                factory.Invoke(definition);
                var isDefault = factory == defaultTarget;
                var dependencies = new List<ExecutableTarget>();
                
                var executable = new ExecutableTarget(property, factory, definition, isDefault, dependencies);

                executables.Add(executable);
                dependencyDictionary.Add(executable, dependencies);
            }

            foreach (var executable in executables)
            {
                var dependencies = dependencyDictionary[executable];

                foreach (var factoryDependency in executable.Definition.FactoryDependencies)
                {
                    var dependency = executables.Single(x => x.Factory == factoryDependency);
                    dependencies.Add(dependency);
                }

                foreach (var nameDependency in executable.Definition.NameDependencies)
                {
                    var dependency = executables.SingleOrDefault(x => x.Name.EqualsOrdinalIgnoreCase(nameDependency));
                    if (dependency != null)
                        dependencies.Add(dependency);
                }
            }

            return executables;
        }
    }
}
