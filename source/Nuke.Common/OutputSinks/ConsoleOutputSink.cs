// Copyright Matthias Koch, Sebastian Karasek 2018.
// Distributed under the MIT License.
// https://github.com/nuke-build/nuke/blob/master/LICENSE

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Nuke.Common.Execution;
using Nuke.Common.Utilities;

namespace Nuke.Common.OutputSinks
{
    [UsedImplicitly]
    internal class ConsoleOutputSink : IOutputSink
    {
        public virtual void Write(string text)
        {
            WriteWithColors(text, ConsoleColor.White);
        }

        public virtual IDisposable WriteBlock(string text)
        {
            Info(FigletTransform.GetText(text));

            return DelegateDisposable.CreateBracket(
                () => Console.Title = $"Executing: {text}",
                () => Console.Title = $"Finished: {text}");
        }

        public virtual void Trace(string text)
        {
            WriteWithColors(text, ConsoleColor.DarkGray);
        }

        public virtual void Info(string text)
        {
            WriteWithColors(text, ConsoleColor.White);
        }

        public virtual void Warn(string text, string details = null)
        {
            WriteWithColors(text, ConsoleColor.Yellow);
            if (details != null)
                WriteWithColors(details, ConsoleColor.Yellow);
        }

        public virtual void Error(string text, string details = null)
        {
            WriteWithColors(text, ConsoleColor.Red);
            if (details != null)
                WriteWithColors(details, ConsoleColor.Red);
        }

        public virtual void Success(string text)
        {
            WriteWithColors(text, ConsoleColor.Green);
        }

        public virtual void WriteSummary(IReadOnlyCollection<ExecutableTarget> executableTargets)
        {
            var firstColumn = Math.Max(executableTargets.Max(x => x.Name.Length) + 4, val2: 20);
            var secondColumn = 10;
            var thirdColumn = 10;
            var allColumns = firstColumn + secondColumn + thirdColumn;
            var totalDuration = executableTargets.Aggregate(TimeSpan.Zero, (t, x) => t.Add(x.Duration));

            string CreateLine(string target, string executionStatus, string duration)
                => target.PadRight(firstColumn, paddingChar: ' ')
                   + executionStatus.PadRight(secondColumn, paddingChar: ' ')
                   + duration.PadLeft(thirdColumn, paddingChar: ' ');

            string ToMinutesAndSeconds(TimeSpan duration)
                => $"{(int) duration.TotalMinutes}:{duration:ss}";

            Logger.Log(new string(c: '=', count: allColumns));
            Logger.Log(CreateLine("Target", "Status", "Duration"));
            Logger.Log(new string(c: '-', count: allColumns));
            foreach (var target in executableTargets)
            {
                var line = CreateLine(target.Name, target.Status.ToString(), ToMinutesAndSeconds(target.Duration));
                switch (target.Status)
                {
                    case ExecutionStatus.Absent:
                    case ExecutionStatus.Skipped:
                        Logger.Log(line);
                        break;
                    case ExecutionStatus.Executed:
                        Logger.Success(line);
                        break;                  
                    case ExecutionStatus.NotRun:
                    case ExecutionStatus.Failed:
                        Logger.Error(line);
                        break;
                }
            }

            Logger.Log(new string(c: '-', count: allColumns));
            Logger.Log(CreateLine("Total", "", ToMinutesAndSeconds(totalDuration)));
            Logger.Log(new string(c: '=', count: allColumns));
            Logger.Log();
            if (executableTargets.All(x => x.Status != ExecutionStatus.Failed && x.Status != ExecutionStatus.NotRun))
                Logger.Success($"Build succeeded on {DateTime.Now.ToString(CultureInfo.CurrentCulture)}.");
            else
                Logger.Error($"Build failed on {DateTime.Now.ToString(CultureInfo.CurrentCulture)}.");
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        private void WriteWithColors(string text, ConsoleColor foregroundColor)
        {
            var previousForegroundColor = Console.ForegroundColor;

            using (DelegateDisposable.CreateBracket(
                () => Console.ForegroundColor = foregroundColor,
                () => Console.ForegroundColor = previousForegroundColor))
            {
                Console.WriteLine(text);
            }
        }
    }
}
