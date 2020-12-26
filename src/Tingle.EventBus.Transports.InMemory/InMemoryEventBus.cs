﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Tingle.EventBus.Transports.InMemory
{
    /// <summary>
    /// Implementation of <see cref="IEventBus"/> via <see cref="EventBusBase{TTransportOptions}"/> using an in-memory transport.
    /// This implementation should only be used for unit testing or similar scenarios as it does not offer persistence.
    /// </summary>
    public class InMemoryEventBus : EventBusBase<InMemoryOptions>
    {
        private readonly ConcurrentBag<object> published = new ConcurrentBag<object>();
        private readonly ConcurrentBag<object> consumed = new ConcurrentBag<object>();
        private readonly ConcurrentBag<object> failed = new ConcurrentBag<object>();
        private readonly ILogger logger;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="environment"></param>
        /// <param name="serviceScopeFactory"></param>
        /// <param name="busOptionsAccessor"></param>
        /// <param name="transportOptionsAccessor"></param>
        /// <param name="loggerFactory"></param>
        public InMemoryEventBus(IHostEnvironment environment,
                                IServiceScopeFactory serviceScopeFactory,
                                IOptions<EventBusOptions> busOptionsAccessor,
                                IOptions<InMemoryOptions> transportOptionsAccessor,
                                ILoggerFactory loggerFactory)
            : base(environment, serviceScopeFactory, busOptionsAccessor, transportOptionsAccessor, loggerFactory)
        {
            logger = loggerFactory?.CreateLogger<InMemoryEventBus>() ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        /// <summary>
        /// The published events.
        /// </summary>
        public IEnumerable<object> Published => published;

        /// <summary>
        /// The consumed events.
        /// </summary>
        public IEnumerable<object> Consumed => consumed;

        /// <summary>
        /// The failed events.
        /// </summary>
        public IEnumerable<object> Failed => failed;

        /// <inheritdoc/>
        public override Task<bool> CheckHealthAsync(EventBusHealthCheckExtras extras,
                                                    CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        /// <inheritdoc/>
        protected override Task StartBusAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <inheritdoc/>
        protected override Task StopBusAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <inheritdoc/>
        protected override Task<string> PublishOnBusAsync<TEvent>(EventContext<TEvent> @event,
                                                                  DateTimeOffset? scheduled = null,
                                                                  CancellationToken cancellationToken = default)
        {
            // log warning when trying to publish scheduled message
            if (scheduled != null)
            {
                logger.LogWarning("InMemory EventBus uses a short-lived timer that is not persisted for scheduled publish");
            }

            var scheduledId = scheduled?.ToUnixTimeMilliseconds().ToString();
            published.Add(@event);
            var _ = SendToConsumersAsync(@event, scheduled);
            return Task.FromResult(scheduledId);
        }

        /// <inheritdoc/>
        protected override Task<IList<string>> PublishOnBusAsync<TEvent>(IList<EventContext<TEvent>> events,
                                                                         DateTimeOffset? scheduled = null,
                                                                         CancellationToken cancellationToken = default)
        {
            // log warning when trying to publish scheduled message
            if (scheduled != null)
            {
                logger.LogWarning("InMemory EventBus uses a short-lived timer that is not persisted for scheduled publish");
            }

            foreach (var @event in events)
            {
                var _ = SendToConsumersAsync(@event, scheduled);
            }

            var random = new Random();
            var scheduledIds = Enumerable.Range(0, events.Count).Select(_ =>
            {
                var bys = new byte[8];
                random.NextBytes(bys);
                return Convert.ToString(BitConverter.ToInt64(bys));
            });
            return Task.FromResult(scheduled != null ? scheduledIds.ToList() : (IList<string>)Array.Empty<string>());
        }

        /// <inheritdoc/>
        public override Task CancelAsync<TEvent>(string id, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("InMemory EventBus does not support canceling published messages.");
        }

        /// <inheritdoc/>
        public override Task CancelAsync<TEvent>(IList<string> ids, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("InMemory EventBus does not support canceling published messages.");
        }

        private async Task SendToConsumersAsync<TEvent>(EventContext<TEvent> @event, DateTimeOffset? scheduled)
        {
            var cts = new CancellationTokenSource();
            var cancellationToken = cts.Token;

            // if the message is scheduled, apply a delay for the duration
            if (scheduled != null)
            {
                var remainder = DateTimeOffset.UtcNow - scheduled.Value;
                if (remainder > TimeSpan.Zero)
                {
                    await Task.Delay(remainder, cancellationToken);
                }
            }

            using var scope = CreateScope(); // shared

            // find consumers registered for the event
            var eventType = typeof(TEvent);
            var registered = BusOptions.GetConsumerRegistrations().Where(r => r.EventType == eventType).ToList();

            // send the message to each consumer in parallel
            var tasks = registered.Select(reg =>
            {
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var mt = GetType().GetMethod(nameof(DispatchToConsumerAsync), flags);
                var method = mt.MakeGenericMethod(typeof(TEvent), reg.ConsumerType);
                return (Task)method.Invoke(this, new object[] { @event, scope, cancellationToken, });
            }).ToList();
            await Task.WhenAll(tasks);
        }

        private async Task DispatchToConsumerAsync<TEvent, TConsumer>(EventContext<TEvent> @event,
                                                                      IServiceScope scope,
                                                                      CancellationToken cancellationToken)
            where TEvent : class
            where TConsumer : IEventBusConsumer<TEvent>
        {
            var context = new EventContext<TEvent>
            {
                EventId = Guid.NewGuid().ToString(),
                CorrelationId = @event.CorrelationId,
                Event = @event.Event,
            };

            try
            {
                await ConsumeAsync<TEvent, TConsumer>(@event: context,
                                                      scope: scope,
                                                      cancellationToken: cancellationToken);
                consumed.Add(context);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Event processing failed. Moving to deadletter.");
                failed.Add(context);
            }
        }
    }
}
