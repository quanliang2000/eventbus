﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Tingle.EventBus
{
    /// <summary>
    /// Generic context for an event.
    /// </summary>
    public abstract class EventContext : IEventBusPublisher
    {
        private IEventBus bus;

        /// <summary>
        /// The unique identifier of the event.
        /// </summary>
        public string EventId { get; set; }

        /// <summary>
        /// The unique identifier of the request accosiated with the event.
        /// </summary>
        public string RequestId { get; set; }

        /// <summary>
        /// A value shared between related events.
        /// </summary>
        public string CorrelationId { get; set; }

        /// <summary>
        /// The unique identifier of the conversation.
        /// </summary>
        public string ConversationId { get; set; }

        /// <summary>
        /// The unique identifier of the initiator of the event.
        /// </summary>
        public string InitiatorId { get; set; }

        /// <summary>
        /// The specific time at which the event expires.
        /// </summary>
        public DateTimeOffset? Expires { get; set; }

        /// <summary>
        /// The specific time the event was sent.
        /// </summary>
        public DateTimeOffset? Sent { get; set; }

        /// <summary>
        /// The headers published alongside the event.
        /// </summary>
        public IDictionary<string, object> Headers { get; set; } = new Dictionary<string, object>();

        /// <inheritdoc/>
        Task<string> IEventBusPublisher.PublishAsync<TEvent>(TEvent @event,
                                                             DateTimeOffset? scheduled,
                                                             CancellationToken cancellationToken)
        {
            var context = new EventContext<TEvent>
            {
                CorrelationId = EventId,
                Event = @event,
            };

            return bus.PublishAsync(@event: context, scheduled: scheduled, cancellationToken: cancellationToken);
        }

        /// <inheritdoc/>
        Task<IList<string>> IEventBusPublisher.PublishAsync<TEvent>(IList<TEvent> events,
                                                                    DateTimeOffset? scheduled,
                                                                    CancellationToken cancellationToken)
        {
            var contexts = events.Select(e => new EventContext<TEvent>
            {
                CorrelationId = EventId,
                Event = e,
            }).ToList();
            return bus.PublishAsync(events: contexts, scheduled: scheduled, cancellationToken: cancellationToken);
        }

        internal void SetBus(IEventBus bus)
        {
            this.bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }
    }

    /// <summary>
    /// The context for a specific event.
    /// </summary>
    /// <typeparam name="T">The type of event carried.</typeparam>
    public class EventContext<T> : EventContext
    {
        /// <summary>
        /// The event published or to be published.
        /// </summary>
        public T Event { get; set; }
    }
}
