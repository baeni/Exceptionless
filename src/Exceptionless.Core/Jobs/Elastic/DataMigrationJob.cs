using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Elasticsearch.Net;
using Exceptionless.Core.Extensions;
using Exceptionless.Core.Repositories.Configuration;
using Exceptionless.DateTimeExtensions;
using Foundatio.Caching;
using Foundatio.Jobs;
using Foundatio.Parsers.ElasticQueries.Extensions;
using Foundatio.Repositories.Elasticsearch.Configuration;
using Foundatio.Repositories.Elasticsearch.Extensions;
using Foundatio.Utility;
using Microsoft.Extensions.Logging;
using Nest;

namespace Exceptionless.Core.Jobs.Elastic {
    [Job(Description = "Migrate data to new format.", IsContinuous = false)]
    public class DataMigrationJob : JobBase {
        private readonly ExceptionlessElasticConfiguration _configuration;

        public DataMigrationJob(
            ExceptionlessElasticConfiguration configuration,
            ILoggerFactory loggerFactory
        ) : base(loggerFactory) {
            _configuration = configuration;
        }

        protected override async Task<JobResult> RunInternalAsync(JobContext context) {
            var elasticOptions = _configuration.Options;
            if (elasticOptions.ElasticsearchToMigrate == null)
                return JobResult.CancelledWithMessage($"Please configure the connection string EX_{nameof(elasticOptions.ElasticsearchToMigrate)}.");

            var retentionPeriod = _configuration.Events.MaxIndexAge.GetValueOrDefault(TimeSpan.FromDays(180));
            string sourceScope = elasticOptions.ElasticsearchToMigrate.Scope;
            string scope = elasticOptions.ScopePrefix;
            var cutOffDate = elasticOptions.ReindexCutOffDate;

            var client = _configuration.Client;
            await _configuration.ConfigureIndexesAsync().AnyContext();

            var indexQueue = new Queue<(string SourceIndex, string SourceIndexType, string TargetIndex, string DateField, int Attempts, Func<Task> CreateIndex)>();
            indexQueue.Enqueue(($"{sourceScope}organizations-v1", "organization", $"{scope}organizations-v1", "updated_utc", 0, null));
            indexQueue.Enqueue(($"{sourceScope}organizations-v1", "project", $"{scope}projects-v1", "updated_utc", 0, null));
            indexQueue.Enqueue(($"{sourceScope}organizations-v1", "token", $"{scope}tokens-v1", "updated_utc", 0, null));
            indexQueue.Enqueue(($"{sourceScope}organizations-v1", "user", $"{scope}users-v1", "updated_utc", 0, null));
            indexQueue.Enqueue(($"{sourceScope}organizations-v1", "webhook", $"{scope}webhooks-v1", "created_utc", 0, null));
            indexQueue.Enqueue(($"{sourceScope}stacks-v1", "stacks", $"{scope}stacks-v1", "last_occurrence", 0, null));

            // create the new indexes, don't migrate yet
            foreach (var index in _configuration.Indexes.OfType<DailyIndex>()) {
                for (int day = 0; day <= retentionPeriod.Days; day++) {
                    var date = day == 0 ? SystemClock.UtcNow : SystemClock.UtcNow.SubtractDays(day);
                    string indexToCreate = $"{scope}events-v1-{date:yyyy.MM.dd}";
                    indexQueue.Enqueue(($"{sourceScope}events-v1-{date:yyyy.MM.dd}", "events", indexToCreate, "updated_utc", 0, () => index.EnsureIndexAsync(date)));
                }
            }

            // Reset the alias cache
            var aliasCache = new ScopedCacheClient(_configuration.Cache, "alias");
            await aliasCache.RemoveAllAsync().AnyContext();

            var started = SystemClock.UtcNow;
            var lastProgress = SystemClock.UtcNow;
            int retriesCount = 0;
            int totalTasks = indexQueue.Count;
            var workingTasks = new List<(TaskId TaskId, string SourceIndex, string SourceIndexType, string TargetIndex, string DateField, List<Exception> Errors, int Attempts)>();
            var completedTasks = new List<(TaskId TaskId, string SourceIndex, string SourceIndexType, string TargetIndex, TaskInfo Task, int Attempts)>();
            var failedTasks = new List<(TaskId TaskId, string SourceIndex, string SourceIndexType, string TargetIndex, List<Exception> Errors, TaskInfo Task, int Attempts)>();
            while (true) {
                if (workingTasks.Count == 0 && indexQueue.Count == 0)
                    break;

                if (workingTasks.Count < 5 && indexQueue.TryDequeue(out var entry)) {
                    if (entry.CreateIndex != null)
                        await entry.CreateIndex().AnyContext();

                    var response = String.IsNullOrEmpty(entry.DateField)
                        ? await client.ReindexOnServerAsync(r => r.Source(s => s.Remote(ConfigureRemoteElasticSource).Index(entry.SourceIndex).Size(250).Query<object>(q => q.Term("_type", entry.SourceIndexType)).Sort<object>(f => f.Field("id", SortOrder.Ascending))).Destination(d => d.Index(entry.TargetIndex)).Conflicts(Conflicts.Proceed).WaitForCompletion(false)).AnyContext()
                        : await client.ReindexOnServerAsync(r => r.Source(s => s.Remote(ConfigureRemoteElasticSource).Index(entry.SourceIndex).Size(250).Query<object>(q => q.Term("_type", entry.SourceIndexType) && q.DateRange(d => d.Field(entry.DateField).GreaterThanOrEquals(cutOffDate))).Sort<object>(f => f.Field(entry.DateField, SortOrder.Ascending))).Destination(d => d.Index(entry.TargetIndex)).Conflicts(Conflicts.Proceed).WaitForCompletion(false)).AnyContext();

                    _logger.LogInformation("STARTED - {SourceIndex}/{SourceType} -> {TargetIndex} A:{Attempts} ({TaskId})...", entry.SourceIndex, entry.SourceIndexType, entry.TargetIndex, entry.Attempts, response.Task);

                    entry.Attempts += 1;
                    workingTasks.Add((response.Task, entry.SourceIndex, entry.SourceIndexType, entry.TargetIndex, entry.DateField, new List<Exception>(), entry.Attempts));
                    continue;
                }

                double highestProgress = 0;
                foreach (var task in workingTasks.ToArray()) {
                    var taskStatus = await client.Tasks.GetTaskAsync(task.TaskId, t => t.WaitForCompletion(false)).AnyContext();
                    _logger.LogTraceRequest(taskStatus);

                    var status = taskStatus?.Task?.Status;
                    if (status == null) {
                        _logger.LogWarning(taskStatus?.OriginalException, "Error getting task status for {TargetIndex} {TaskId}: {Message}", task.TargetIndex, task.TaskId, taskStatus.GetErrorMessage());
                        if (taskStatus?.ServerError?.Status == 429)
                            await Task.Delay(TimeSpan.FromSeconds(1));

                        continue;
                    }

                    var duration = TimeSpan.FromMilliseconds(taskStatus.Task.RunningTimeInNanoseconds * 0.000001);
                    double progress = status.Total > 0 ? (status.Created + status.Updated + status.Deleted + status.VersionConflicts * 1.0) / status.Total : 0;
                    highestProgress = Math.Max(highestProgress, progress);

                    if (!taskStatus.IsValid) {
                        if (taskStatus.ServerError?.Status == 404) {
                            _logger.LogInformation("Task for {TargetIndex} ({TaskId}) is not found", task.TargetIndex, task.TaskId);
                            continue;
                        }

                        _logger.LogWarning(taskStatus.OriginalException, "Error getting task status for {TargetIndex} ({TaskId}): {Message}", task.TargetIndex, task.TaskId, taskStatus.GetErrorMessage());
                        task.Errors.Add(taskStatus.OriginalException);
                        if (task.Errors.Count > 5 || taskStatus.Completed) {
                            workingTasks.Remove(task);
                            failedTasks.Add((task.TaskId, task.SourceIndex, task.SourceIndexType, task.TargetIndex, task.Errors, taskStatus.Task, task.Attempts));

                            if (taskStatus.Completed && task.Attempts < 3) {
                                _logger.LogWarning("FAILED RETRY - {SourceIndex}/{SourceType} -> {TargetIndex} in {Duration:hh\\:mm} C:{Created} U:{Updated} D:{Deleted} X:{Conflicts} T:{Total} A:{Attempts} ID:{TaskId}", task.SourceIndex, task.SourceIndexType, task.TargetIndex, duration, status.Created, status.Updated, status.Deleted, status.VersionConflicts, status.Total, task.Attempts, task.TaskId);
                                indexQueue.Enqueue((task.SourceIndex, task.SourceIndexType, task.TargetIndex, task.DateField, task.Attempts, null));
                                retriesCount++;
                                await Task.Delay(TimeSpan.FromSeconds(15)).AnyContext();
                            } else {
                                _logger.LogCritical("FAILED - {SourceIndex}/{SourceType} -> {TargetIndex} in {Duration:hh\\:mm} C:{Created} U:{Updated} D:{Deleted} X:{Conflicts} T:{Total} A:{Attempts} ID:{TaskId}", task.SourceIndex, task.SourceIndexType, task.TargetIndex, duration, status.Created, status.Updated, status.Deleted, status.VersionConflicts, status.Total, task.Attempts, task.TaskId);
                            }
                        }

                        continue;
                    }

                    task.Errors.Clear();
                    if (!taskStatus.Completed)
                        continue;

                    workingTasks.Remove(task);
                    completedTasks.Add((task.TaskId, task.SourceIndex, task.SourceIndexType, task.TargetIndex, taskStatus.Task, task.Attempts));
                    var targetCount = await client.CountAsync<object>(d => d.Index(task.TargetIndex)).AnyContext();

                    _logger.LogInformation("COMPLETED -{SourceIndex}/{SourceType} -> {TargetIndex} ({TargetCount}) in {Duration:hh\\:mm} C:{Created} U:{Updated} D:{Deleted} X:{Conflicts} T:{Total} A:{Attempts} ID:{TaskId}", task.SourceIndex, task.SourceIndexType, task.TargetIndex, targetCount.Count, duration, status.Created, status.Updated, status.Deleted, status.VersionConflicts, status.Total, task.Attempts, task.TaskId);
                }
                if (SystemClock.UtcNow.Subtract(lastProgress) > TimeSpan.FromMinutes(5)) {
                    _logger.LogInformation("STATUS - I:{Completed}/{Total} P:{Progress:F0}% T:{Duration:d\\.hh\\:mm} W:{Working} F:{Failed} R:{Retries}", completedTasks.Count, totalTasks, highestProgress * 100, SystemClock.UtcNow.Subtract(started), workingTasks.Count, failedTasks.Count, retriesCount);
                    lastProgress = SystemClock.UtcNow;
                }
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            _logger.LogInformation("----- DONE - I:{Completed}/{Total} T:{Duration:d\\.hh\\:mm} F:{Failed} R:{Retries}", completedTasks.Count, totalTasks, SystemClock.UtcNow.Subtract(started), failedTasks.Count, retriesCount);
            foreach (var task in completedTasks) {
                var status = task.Task.Status;
                var duration = TimeSpan.FromMilliseconds(task.Task.RunningTimeInNanoseconds * 0.000001);
                double progress = status.Total > 0 ? (status.Created + status.Updated + status.Deleted + status.VersionConflicts * 1.0) / status.Total : 0;

                var targetCount = await client.CountAsync<object>(d => d.Index(task.TargetIndex)).AnyContext();
                _logger.LogInformation("SUCCESS - {SourceIndex}/{SourceType} -> {TargetIndex} ({TargetCount}) in {Duration:hh\\:mm} C:{Created} U:{Updated} D:{Deleted} X:{Conflicts} T:{Total} A:{Attempts} ID:{TaskId}", task.SourceIndex, task.SourceIndexType, task.TargetIndex, targetCount.Count, duration, status.Created, status.Updated, status.Deleted, status.VersionConflicts, status.Total, task.Attempts, task.TaskId);
            }

            foreach (var task in failedTasks) {
                var status = task.Task.Status;
                var duration = TimeSpan.FromMilliseconds(task.Task.RunningTimeInNanoseconds * 0.000001);
                double progress = status.Total > 0 ? (status.Created + status.Updated + status.Deleted + status.VersionConflicts * 1.0) / status.Total : 0;

                var targetCount = await client.CountAsync<object>(d => d.Index(task.TargetIndex));
                _logger.LogCritical("FAILED - {SourceIndex}/{SourceType} -> {TargetIndex} ({TargetCount}) in {Duration:hh\\:mm} C:{Created} U:{Updated} D:{Deleted} X:{Conflicts} T:{Total} A:{Attempts} ID:{TaskId}", task.SourceIndex, task.SourceIndexType, task.TargetIndex, targetCount.Count, duration, status.Created, status.Updated, status.Deleted, status.VersionConflicts, status.Total, task.Attempts, task.TaskId);
            }

            _logger.LogInformation("Updating aliases");
            await _configuration.MaintainIndexesAsync();
            _logger.LogInformation("Updated aliases");
            return JobResult.Success;
        }

        private IRemoteSource ConfigureRemoteElasticSource(RemoteSourceDescriptor rsd) {
            var elasticOptions = _configuration.Options.ElasticsearchToMigrate;
            if (!String.IsNullOrEmpty(elasticOptions.UserName) && !String.IsNullOrEmpty(elasticOptions.Password))
                rsd.Username(elasticOptions.UserName).Password(elasticOptions.Password);

            return rsd.Host(new Uri(elasticOptions.ServerUrl));
        }
    }
}
