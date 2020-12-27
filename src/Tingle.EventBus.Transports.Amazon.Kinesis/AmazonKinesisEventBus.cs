﻿using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Tingle.EventBus.Transports.Amazon.Kinesis
{
    /// <summary>
    /// Implementation of <see cref="IEventBus"/> via <see cref="EventBusBase{TTransportOptions}"/> using
    /// Amazon Kinesis as the transport.
    /// </summary>
    public class AmazonKinesisEventBus : EventBusBase<AmazonKinesisOptions>
    {
        private readonly AmazonKinesisClient kinesisClient;
        private readonly ILogger logger;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="environment"></param>
        /// <param name="serviceScopeFactory"></param>
        /// <param name="busOptionsAccessor"></param>
        /// <param name="transportOptionsAccessor"></param>
        /// <param name="loggerFactory"></param>
        public AmazonKinesisEventBus(IHostEnvironment environment,
                                     IServiceScopeFactory serviceScopeFactory,
                                     IOptions<EventBusOptions> busOptionsAccessor,
                                     IOptions<AmazonKinesisOptions> transportOptionsAccessor,
                                     ILoggerFactory loggerFactory)
            : base(environment, serviceScopeFactory, busOptionsAccessor, transportOptionsAccessor, loggerFactory)
        {
            kinesisClient = new AmazonKinesisClient(credentials: TransportOptions.Credentials,
                                                    clientConfig: TransportOptions.KinesisConfig);

            logger = loggerFactory?.CreateLogger<AmazonKinesisEventBus>() ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        /// <inheritdoc/>
        public override async Task<bool> CheckHealthAsync(EventBusHealthCheckExtras extras,
                                                          CancellationToken cancellationToken = default)
        {
            _ = await kinesisClient.ListStreamsAsync(cancellationToken);
            return true;
        }

        /// <inheritdoc/>
        protected override Task StartBusAsync(CancellationToken cancellationToken)
        {
            // Consuming is not yet supported on this bus due to it's complexity
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        protected override Task StopBusAsync(CancellationToken cancellationToken)
        {
            // Consuming is not yet supported on this bus due to it's complexity
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        protected override async Task<string> PublishOnBusAsync<TEvent>(EventContext<TEvent> @event,
                                                                        DateTimeOffset? scheduled = null,
                                                                        CancellationToken cancellationToken = default)
        {
            // log warning when trying to publish scheduled message
            if (scheduled != null)
            {
                logger.LogWarning("Amazon Kinesis does not support delay or scheduled publish");
            }

            using var scope = CreateScope();
            var reg = BusOptions.GetOrCreateEventRegistration<TEvent>();
            using var ms = new MemoryStream();
            var contentType = await SerializeAsync(body: ms,
                                                   @event: @event,
                                                   registration: reg,
                                                   scope: scope,
                                                   cancellationToken: cancellationToken);

            // prepare the record
            var request = new PutRecordRequest
            {
                Data = ms,
                PartitionKey = @event.EventId, // TODO: consider a better partition key
                StreamName = reg.EventName,
            };

            // send the event
            var response = await kinesisClient.PutRecordAsync(request, cancellationToken);
            // TODO: response.EnsureSuccess();

            // return the sequence number
            return scheduled != null ? response.SequenceNumber : null;
        }

        /// <inheritdoc/>
        protected override async Task<IList<string>> PublishOnBusAsync<TEvent>(IList<EventContext<TEvent>> events,
                                                                               DateTimeOffset? scheduled = null,
                                                                               CancellationToken cancellationToken = default)
        {
            // log warning when trying to publish scheduled message
            if (scheduled != null)
            {
                logger.LogWarning("Amazon Kinesis does not support delay or scheduled publish");
            }

            using var scope = CreateScope();
            var reg = BusOptions.GetOrCreateEventRegistration<TEvent>();
            var records = new List<PutRecordsRequestEntry>();

            // work on each event
            foreach (var @event in events)
            {
                using var ms = new MemoryStream();
                var contentType = await SerializeAsync(body: ms,
                                                       @event: @event,
                                                       registration: reg,
                                                       scope: scope,
                                                       cancellationToken: cancellationToken);

                var record = new PutRecordsRequestEntry
                {
                    Data = ms,
                    PartitionKey = @event.EventId, // TODO: consider a better partition key
                };
                records.Add(record);
            }

            // prepare the request
            var request = new PutRecordsRequest
            {
                StreamName = reg.EventName,
                Records = records,
            };

            var response = await kinesisClient.PutRecordsAsync(request, cancellationToken);
            // TODO: response.EnsureSuccess();

            // Should we check for failed records and throw exception?

            // return the sequence numbers
            return response.Records.Select(m => m.SequenceNumber.ToString()).ToList();
        }

        /// <inheritdoc/>
        public override Task CancelAsync<TEvent>(string id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Amazon Kinesis does not support canceling published events.");
        }

        /// <inheritdoc/>
        public override Task CancelAsync<TEvent>(IList<string> ids, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Amazon Kinesis does not support canceling published events.");
        }
    }
}