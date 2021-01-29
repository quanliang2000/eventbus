﻿using Microsoft.Extensions.Options;
using System;
using Tingle.EventBus;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// A class to finish the configuration of instances of <see cref="AzureServiceBusTransportOptions"/>.
    /// </summary>
    internal class AzureServiceBusPostConfigureOptions : IPostConfigureOptions<AzureServiceBusTransportOptions>
    {
        private readonly EventBusOptions busOptions;

        public AzureServiceBusPostConfigureOptions(IOptions<EventBusOptions> busOptionsAccessor)
        {
            busOptions = busOptionsAccessor?.Value ?? throw new ArgumentNullException(nameof(busOptionsAccessor));
        }

        public void PostConfigure(string name, AzureServiceBusTransportOptions options)
        {
            // ensure the connection string is not null
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException($"The '{nameof(options.ConnectionString)}' must be provided");
            }

            // ensure the entity names are not longer than 50 characters
            var registrations = busOptions.GetRegistrations(TransportNames.AzureServiceBus);
            foreach (var ereg in registrations)
            {
                if (ereg.EventName.Length > 50)
                {
                    throw new InvalidOperationException($"EventName '{ereg.EventName}' generated from '{ereg.EventType.Name}' is too long. "
                                                       + "Azure Service Bus does not allow more than 50 characters.");
                }

                foreach (var creg in ereg.Consumers)
                {
                    if (creg.ConsumerName.Length > 50)
                    {
                        throw new InvalidOperationException($"ConsumerName '{creg.ConsumerName}' generated from '{creg.ConsumerType.Name}' is too long. "
                                                           + "Azure Service Bus does not allow more than 50 characters.");
                    }
                }
            }
        }
    }
}