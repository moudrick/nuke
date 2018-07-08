// Copyright Matthias Koch, Sebastian Karasek 2018.
// Distributed under the MIT License.
// https://github.com/nuke-build/nuke/blob/master/LICENSE

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Nuke.Common.Execution
{
    internal class TargetDefinition : ITargetDefinition
    {
        internal string Description { get; set; }
        internal bool IsDefault { get; set; }
        internal List<Func<bool>> Conditions { get; } = new List<Func<bool>>();
        internal List<LambdaExpression> Requirements { get; } = new List<LambdaExpression>();
        internal List<Target> TargetDependencies { get; } = new List<Target>();
        internal List<string> ShadowTargetDependencies { get; } = new List<string>();
        internal List<Action> Actions { get; } = new List<Action>();
        internal DependencyBehavior DependencyBehavior { get; private set; }
        
        ITargetDefinition ITargetDefinition.Description(string description)
        {
            Description = description;
            return this;
        }

        public ITargetDefinition Executes(params Action[] actions)
        {
            Actions.AddRange(actions);
            return this;
        }

        public ITargetDefinition Executes<T>(Func<T> action)
        {
            return Executes(new Action(() => action()));
        }

        public ITargetDefinition Executes(Func<Task> action)
        {
            return Executes(() => action().GetAwaiter().GetResult());
        }

        public ITargetDefinition DependsOn(params Target[] targets)
        {
            TargetDependencies.AddRange(targets);
            return this;
        }

        public ITargetDefinition DependsOn(params string[] shadowTargets)
        {
            ShadowTargetDependencies.AddRange(shadowTargets);
            return this;
        }

        public ITargetDefinition OnlyWhen(params Func<bool>[] conditions)
        {
            Conditions.AddRange(conditions);
            return this;
        }

        public ITargetDefinition Requires<T>(params Expression<Func<T>>[] parameterRequirement)
            where T : class
        {
            Requirements.AddRange(parameterRequirement);
            return this;
        }

        public ITargetDefinition Requires<T>(params Expression<Func<T?>>[] parameterRequirement)
            where T : struct
        {
            Requirements.AddRange(parameterRequirement);
            return this;
        }

        public ITargetDefinition Requires(params Expression<Func<bool>>[] requirement)
        {
            Requirements.AddRange(requirement);
            return this;
        }

        public ITargetDefinition WhenSkipped(DependencyBehavior dependencyBehavior)
        {
            DependencyBehavior = dependencyBehavior;
            return this;
        }
    }
}
