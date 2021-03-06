﻿// ==========================================================================
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex UG (haftungsbeschränkt)
//  All rights reserved. Licensed under the MIT license.
// ==========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
using Squidex.Infrastructure.MongoDb;

namespace Squidex.Infrastructure.EventSourcing
{
    public partial class MongoEventStore : MongoRepositoryBase<MongoEventCommit>, IEventStore
    {
        public Task CreateIndexAsync(string property)
        {
            return Collection.Indexes.CreateOneAsync(Index.Ascending(CreateIndexPath(property)));
        }

        public IEventSubscription CreateSubscription(IEventSubscriber subscriber, string streamFilter, string position = null)
        {
            Guard.NotNull(subscriber, nameof(subscriber));
            Guard.NotNullOrEmpty(streamFilter, nameof(streamFilter));

            return new PollingSubscription(this, notifier, subscriber, streamFilter, position);
        }

        public async Task<IReadOnlyList<StoredEvent>> QueryAsync(string streamName, long streamPosition = 0)
        {
            var commits =
                await Collection.Find(
                    Filter.And(
                        Filter.Eq(EventStreamField, streamName),
                        Filter.Gte(EventStreamOffsetField, streamPosition - 1)))
                    .Sort(Sort.Ascending(TimestampField)).ToListAsync();

            var result = new List<StoredEvent>();

            foreach (var commit in commits)
            {
                var eventStreamOffset = (int)commit.EventStreamOffset;

                var commitTimestamp = commit.Timestamp;
                var commitOffset = 0;

                foreach (var e in commit.Events)
                {
                    eventStreamOffset++;

                    if (eventStreamOffset >= streamPosition)
                    {
                        var eventData = e.ToEventData();
                        var eventToken = new StreamPosition(commitTimestamp, commitOffset, commit.Events.Length);

                        result.Add(new StoredEvent(eventToken, eventStreamOffset, eventData));
                    }
                }
            }

            return result;
        }

        public Task QueryAsync(Func<StoredEvent, Task> callback, string property, object value, string position = null, CancellationToken ct = default(CancellationToken))
        {
            Guard.NotNull(callback, nameof(callback));

            StreamPosition lastPosition = position;

            var filter = CreateFilter(property, value, lastPosition);

            return QueryAsync(callback, lastPosition, filter, ct);
        }

        public Task QueryAsync(Func<StoredEvent, Task> callback, string streamFilter = null, string position = null, CancellationToken ct = default(CancellationToken))
        {
            Guard.NotNull(callback, nameof(callback));

            StreamPosition lastPosition = position;

            var filter = CreateFilter(streamFilter, lastPosition);

            return QueryAsync(callback, lastPosition, filter, ct);
        }

        private Task QueryAsync(Func<StoredEvent, Task> callback, StreamPosition lastPosition, FilterDefinition<MongoEventCommit> filter, CancellationToken ct)
        {
            return Collection.Find(filter).Sort(Sort.Ascending(TimestampField)).ForEachPipelineAsync(async commit =>
            {
                var eventStreamOffset = (int)commit.EventStreamOffset;

                var commitTimestamp = commit.Timestamp;
                var commitOffset = 0;

                foreach (var e in commit.Events)
                {
                    eventStreamOffset++;

                    if (commitOffset > lastPosition.CommitOffset || commitTimestamp > lastPosition.Timestamp)
                    {
                        var eventData = e.ToEventData();
                        var eventToken = new StreamPosition(commitTimestamp, commitOffset, commit.Events.Length);

                        await callback(new StoredEvent(eventToken, eventStreamOffset, eventData));

                        commitOffset++;
                    }
                }
            }, ct);
        }

        private static FilterDefinition<MongoEventCommit> CreateFilter(string property, object value, StreamPosition streamPosition)
        {
            var filters = new List<FilterDefinition<MongoEventCommit>>();

            AddPositionFilter(streamPosition, filters);
            AddPropertyFitler(property, value, filters);

            return Filter.And(filters);
        }

        private static FilterDefinition<MongoEventCommit> CreateFilter(string streamFilter, StreamPosition streamPosition)
        {
            var filters = new List<FilterDefinition<MongoEventCommit>>();

            AddPositionFilter(streamPosition, filters);
            AddStreamFilter(streamFilter, filters);

            return Filter.And(filters);
        }

        private static void AddPropertyFitler(string property, object value, List<FilterDefinition<MongoEventCommit>> filters)
        {
            filters.Add(Filter.Eq(CreateIndexPath(property), value));
        }

        private static void AddStreamFilter(string streamFilter, List<FilterDefinition<MongoEventCommit>> filters)
        {
            if (!string.IsNullOrWhiteSpace(streamFilter) && !string.Equals(streamFilter, ".*", StringComparison.OrdinalIgnoreCase))
            {
                if (streamFilter.Contains("^"))
                {
                    filters.Add(Filter.Regex(EventStreamField, streamFilter));
                }
                else
                {
                    filters.Add(Filter.Eq(EventStreamField, streamFilter));
                }
            }
        }

        private static void AddPositionFilter(StreamPosition streamPosition, List<FilterDefinition<MongoEventCommit>> filters)
        {
            if (streamPosition.IsEndOfCommit)
            {
                filters.Add(Filter.Gt(TimestampField, streamPosition.Timestamp));
            }
            else
            {
                filters.Add(Filter.Gte(TimestampField, streamPosition.Timestamp));
            }
        }

        private static string CreateIndexPath(string property)
        {
            return $"Events.Metadata.{property}";
        }
    }
}