﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sentry.Core;

namespace Sentry
{
    public interface ISentry
    {
        Task StartAsync();
        Task StopAsync();
    }

    public class Sentry : ISentry
    {
        private readonly SentryConfiguration _configuration;
        private long _iterationOrdinal = 1;
        private bool _started = false;

        public Sentry(SentryConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration), "Sentry configuration has not been provided.");

            _configuration = configuration;
        }

        public async Task StartAsync()
        {
            _started = true;
            _configuration.Hooks.OnStart.Execute();
            await _configuration.Hooks.OnStartAsync.ExecuteAsync();

            try
            {
                while (CanExecuteIteration(_iterationOrdinal))
                {
                    _configuration.Hooks.OnIterationStart.Execute(_iterationOrdinal);
                    await _configuration.Hooks.OnIterationStartAsync.ExecuteAsync(_iterationOrdinal);
                    var iteration = await ExecuteIterationAsync(_iterationOrdinal);
                    _configuration.Hooks.OnIterationCompleted.Execute(iteration);
                    await _configuration.Hooks.OnIterationCompletedAsync.ExecuteAsync(iteration);
                    await Task.Delay(_configuration.IterationDelay);
                    _iterationOrdinal++;
                }
            }
            catch (Exception exception)
            {
                _configuration.Hooks.OnError.Execute(exception);
                await _configuration.Hooks.OnErrorAsync.ExecuteAsync(exception);
            }
        }

        private bool CanExecuteIteration(long ordinal)
        {
            if (!_started)
                return false;
            if (!_configuration.IterationsCount.HasValue)
                return true;
            if (ordinal <= _configuration.IterationsCount)
                return true;

            return false;
        }

        public async Task StopAsync()
        {
            _started = false;
            _configuration.Hooks.OnStop.Execute();
            await _configuration.Hooks.OnStopAsync.ExecuteAsync();
        }

        private async Task<ISentryIteration> ExecuteIterationAsync(long ordinal)
        {
            var iterationStartedAtUtc = DateTime.UtcNow;
            var results = new List<ISentryCheckResult>();
            var tasks = _configuration.Watchers.Select(async watcherConfiguration =>
            {
                var startedAtUtc = DateTime.UtcNow;
                var watcher = watcherConfiguration.Watcher;
                ISentryCheckResult sentryCheckResult = null;
                try
                {
                    await InvokeOnStartHooksAsync(watcherConfiguration, WatcherCheck.Create(watcher));
                    var watcherCheckResult = await watcher.ExecuteAsync();
                    var completedAtUtc = DateTime.UtcNow;
                    sentryCheckResult = SentryCheckResult.Create(watcherCheckResult, startedAtUtc, completedAtUtc);
                    results.Add(sentryCheckResult);
                    if (watcherCheckResult.IsValid)
                    {
                        await InvokeOnSuccessHooksAsync(watcherConfiguration, sentryCheckResult);
                    }
                    else
                    {
                        await InvokeOnFailureHooksAsync(watcherConfiguration, sentryCheckResult);
                    }
                }
                catch (Exception exception)
                {
                    var sentryException = new SentryException("There was an error while executing Sentry " +
                                                              $"caused by watcher: '{watcher.Name}'.", exception);

                    await InvokeOnErrorHooksAsync(watcherConfiguration, sentryException);
                }
                finally
                {
                    await InvokeOnCompletedHooksAsync(watcherConfiguration, sentryCheckResult);
                }
            });

            await Task.WhenAll(tasks);
            var iterationCompleteddAtUtc = DateTime.UtcNow;
            var iteration = SentryIteration.Create(ordinal, results, iterationStartedAtUtc, iterationCompleteddAtUtc);

            return iteration;
        }

        private async Task InvokeOnStartHooksAsync(WatcherConfiguration watcherConfiguration, IWatcherCheck check)
        {
            watcherConfiguration.Hooks.OnStart.Execute(check);
            await watcherConfiguration.Hooks.OnStartAsync.ExecuteAsync(check);
            _configuration.GlobalWatcherHooks.OnStart.Execute(check);
            await _configuration.GlobalWatcherHooks.OnStartAsync.ExecuteAsync(check);
        }

        private async Task InvokeOnSuccessHooksAsync(WatcherConfiguration watcherConfiguration, ISentryCheckResult checkResult)
        {
            watcherConfiguration.Hooks.OnSuccess.Execute(checkResult);
            await watcherConfiguration.Hooks.OnSuccessAsync.ExecuteAsync(checkResult);
            _configuration.GlobalWatcherHooks.OnSuccess.Execute(checkResult);
            await _configuration.GlobalWatcherHooks.OnSuccessAsync.ExecuteAsync(checkResult);
        }

        private async Task InvokeOnErrorHooksAsync(WatcherConfiguration watcherConfiguration, Exception exception)
        {
            watcherConfiguration.Hooks.OnError.Execute(exception);
            await watcherConfiguration.Hooks.OnErrorAsync.ExecuteAsync(exception);
            _configuration.GlobalWatcherHooks.OnError.Execute(exception);
            await _configuration.GlobalWatcherHooks.OnErrorAsync.ExecuteAsync(exception);
        }

        private async Task InvokeOnFailureHooksAsync(WatcherConfiguration watcherConfiguration, ISentryCheckResult checkResult)
        {
            watcherConfiguration.Hooks.OnFailure.Execute(checkResult);
            await watcherConfiguration.Hooks.OnFailureAsync.ExecuteAsync(checkResult);
            _configuration.GlobalWatcherHooks.OnFailure.Execute(checkResult);
            await _configuration.GlobalWatcherHooks.OnFailureAsync.ExecuteAsync(checkResult);
        }

        private async Task InvokeOnCompletedHooksAsync(WatcherConfiguration watcherConfiguration, ISentryCheckResult checkResult)
        {
            watcherConfiguration.Hooks.OnCompleted.Execute(checkResult);
            await watcherConfiguration.Hooks.OnCompletedAsync.ExecuteAsync(checkResult);
            _configuration.GlobalWatcherHooks.OnCompleted.Execute(checkResult);
            await _configuration.GlobalWatcherHooks.OnCompletedAsync.ExecuteAsync(checkResult);
        }
    }
}