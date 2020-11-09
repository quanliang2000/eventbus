﻿using Amazon;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Microsoft.Extensions.Options;
using System;
using Tingle.EventBus;
using Tingle.EventBus.Transports.AmazonSqs;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods on <see cref="EventBusBuilder"/> for Amazon SQS.
    /// </summary>
    public static class EventBusBuilderExtensions
    {
        /// <summary>
        /// Add Amazon SQS as the underlying transport for the Event Bus.
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static EventBusBuilder AddAmazonSqs(this EventBusBuilder builder, Action<AmazonSqsOptions> configure)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            if (configure is null) throw new ArgumentNullException(nameof(configure));

            var services = builder.Services;

            // configure the options for Azure Service Bus
            services.Configure(configure);
            services.PostConfigure<AmazonSqsOptions>(options =>
            {
                // ensure the region is provided
                if (string.IsNullOrWhiteSpace(options.RegionName) && options.Region == null)
                {
                    throw new InvalidOperationException($"Either '{nameof(options.RegionName)}' or '{nameof(options.Region)}' must be provided");
                }

                options.Region ??= RegionEndpoint.GetBySystemName(options.RegionName);

                // ensure the access key is specified
                if (string.IsNullOrWhiteSpace(options.AccessKey))
                {
                    throw new InvalidOperationException($"The '{nameof(options.AccessKey)}' must be provided");
                }

                // ensure the secret is specified
                if (string.IsNullOrWhiteSpace(options.SecretKey))
                {
                    throw new InvalidOperationException($"The '{nameof(options.SecretKey)}' must be provided");
                }

                // ensure we have options for SQS and SNS and their regions are set
                options.SqsConfig ??= new AmazonSQSConfig();
                options.SqsConfig.RegionEndpoint ??= options.Region;
                options.SnsConfig ??= new AmazonSimpleNotificationServiceConfig();
                options.SnsConfig.RegionEndpoint ??= options.Region;
            });

            // register the AmazonSQSClient
            services.AddSingleton(p =>
            {
                var options = p.GetRequiredService<IOptions<AmazonSqsOptions>>().Value;
                return new AmazonSQSClient(credentials: options.Credentials, clientConfig: options.SqsConfig);
            });

            // register the AmazonSimpleNotificationServiceClient
            services.AddSingleton(p =>
            {
                var options = p.GetRequiredService<IOptions<AmazonSqsOptions>>().Value;
                return new AmazonSimpleNotificationServiceClient(credentials: options.Credentials, clientConfig: options.SnsConfig);
            });

            // register the event bus
            services.AddSingleton<IEventBus, AmazonSqsEventBus>();

            return builder;
        }
    }
}