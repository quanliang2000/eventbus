﻿using System;
using Tingle.EventBus;
using Tingle.EventBus.Transports.Azure.EventHubs;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods on <see cref="EventBusBuilder"/> for Azure EventHubs
    /// </summary>
    public static class EventBusBuilderExtensions
    {
        /// <summary>
        /// Add Azure EventHubs as the underlying transport for the Event Bus.
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static EventBusBuilder AddAzureEventHubs(this EventBusBuilder builder, Action<AzureEventHubsOptions> configure)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            if (configure is null) throw new ArgumentNullException(nameof(configure));

            var services = builder.Services;

            // configure the options for Azure Service Bus
            services.Configure(configure);
            services.PostConfigure<AzureEventHubsOptions>(options =>
            {
                // ensure the connection string
                if (string.IsNullOrWhiteSpace(options.ConnectionString))
                {
                    throw new InvalidOperationException($"The '{nameof(options.ConnectionString)}' must be provided");
                }

                // ensure the connection string for blob storage is valid
                if (string.IsNullOrWhiteSpace(options.BlobStorageConnectionString))
                {
                    throw new InvalidOperationException($"The '{nameof(options.BlobStorageConnectionString)}' must be provided");
                }

                // ensure the blob container prefix is provided
                if (string.IsNullOrWhiteSpace(options.BlobContainerPrefix))
                {
                    throw new InvalidOperationException($"The '{nameof(options.BlobContainerPrefix)}' must be provided");
                }

                // ensure the prefix is always lower case.
                options.BlobContainerPrefix = options.BlobContainerPrefix.ToLower();
            });

            // register the event bus
            services.AddSingleton<IEventBus, AzureEventHubsEventBus>();

            return builder;
        }
    }
}