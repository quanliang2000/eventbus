﻿using System;
using System.Collections.Generic;
using Tingle.EventBus;

namespace Microsoft.Extensions.Logging
{
    /// <summary>
    /// Extensions on <see cref="ILogger"/> for the EventBus
    /// </summary>
    internal static class ILoggerExtensions
    {
        private static readonly Action<ILogger, int, TimeSpan, Exception> _startingTransport
            = LoggerMessage.Define<int, TimeSpan>(
                eventId: new EventId(1, nameof(StartingTransport)),
                logLevel: LogLevel.Debug,
                formatString: "Starting transport. Consumers: {Count}, EmptyResultsDelay: '{EmptyResultsDelay}'");

        private static readonly Action<ILogger, Exception> _stoppingTransport
            = LoggerMessage.Define(
                eventId: new EventId(2, nameof(StoppingTransport)),
                logLevel: LogLevel.Debug,
                formatString: "Stopping transport.");

        public static void StartingTransport(this ILogger logger, int count, TimeSpan emptyResultsDelay)
        {
            _startingTransport(logger, count, emptyResultsDelay, null);
        }

        public static void StoppingTransport(this ILogger logger) => _stoppingTransport(logger, null);


        /// <summary>
        /// Begins a logical operation scope.
        /// </summary>
        /// <param name="logger">The <see cref="ILogger"/> in which to create teh scope.</param>
        /// <param name="id"></param>
        /// <param name="correlationId"></param>
        /// <param name="sequenceNumber"></param>
        /// <param name="extras">The extras to put in the scope. (Optional)</param>
        /// <returns>A disposable object that ends the logical operation scope on dispose.</returns>
        public static IDisposable BeginScopeForConsume(this ILogger logger,
                                                       string id,
                                                       string correlationId,
                                                       string sequenceNumber = null,
                                                       IDictionary<string, string> extras = null)
        {
            var state = new Dictionary<string, string>();
            state.AddIfNotDefault(AttributeNames.Id, id);
            state.AddIfNotDefault(AttributeNames.CorrelationId, correlationId);
            state.AddIfNotDefault(AttributeNames.SequenceNumber, sequenceNumber);

            // if there are extras, add them
            if (extras != null)
            {
                foreach (var kvp in extras)
                {
                    state.AddIfNotDefault(kvp.Key, kvp.Value);
                }
            }

            // create the scope
            return logger.BeginScope(state);
        }

        /// <summary>
        /// Begins a logical operation scope.
        /// </summary>
        /// <param name="logger">The <see cref="ILogger"/> in which to create teh scope.</param>
        /// <param name="id"></param>
        /// <param name="correlationId"></param>
        /// <param name="sequenceNumber"></param>
        /// <param name="extras">The extras to put in the scope. (Optional)</param>
        /// <returns>A disposable object that ends the logical operation scope on dispose.</returns>
        public static IDisposable BeginScopeForConsume(this ILogger logger,
                                                       string id,
                                                       string correlationId,
                                                       long sequenceNumber,
                                                       IDictionary<string, string> extras = null)
        {
            return logger.BeginScopeForConsume(id: id,
                                            correlationId: correlationId,
                                            sequenceNumber: sequenceNumber.ToString(),
                                            extras: extras);
        }
    }
}
