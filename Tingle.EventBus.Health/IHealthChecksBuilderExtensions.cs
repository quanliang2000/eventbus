﻿using Tingle.EventBus;
using Tingle.EventBus.Health;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods on <see cref="IHealthChecksBuilder"/> for <see cref="IEventBus"/>.
    /// </summary>
    public static class IHealthChecksBuilderExtensions
    {
        /// <summary>
        /// Add a health check for the event bus.
        /// <see cref="IEventBus"/> must be resolvable from the services provider.
        /// </summary>
        /// <param name="builder">The <see cref="IHealthChecksBuilder"/>.</param>
        /// <param name="name">The health check name.</param>
        /// <returns>The <see cref="IHealthChecksBuilder"/>.</returns>
        public static IHealthChecksBuilder AddEventBus(this IHealthChecksBuilder builder, string name = "eventbus")
        {
            return builder.AddCheck<EventBusHealthCheck>(name);
        }
    }
}
