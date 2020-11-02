﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Tingle.EventBus.Transports.RabbitMQ
{
    public class RabbitMqEventBus : EventBusBase, IDisposable
    {
        private readonly RabbitMqOptions rabbitMqOptions;
        private readonly ILogger logger;

        private readonly RetryPolicy retryPolicy;
        private readonly SemaphoreSlim connectionLock = new SemaphoreSlim(1, 1);

        private readonly Dictionary<string, IModel> subscriptionChannelsCache = new Dictionary<string, IModel>();
        private readonly SemaphoreSlim subscriptionChannelsCacheLock = new SemaphoreSlim(1, 1); // only one at a time.

        private IConnection connection;
        private bool disposed;

        public RabbitMqEventBus(IHostEnvironment environment,
                                IServiceScopeFactory serviceScopeFactory,
                                IOptions<EventBusOptions> optionsAccessor,
                                IOptions<RabbitMqOptions> rabbitMqOptionsAccessor,
                                ILoggerFactory loggerFactory)
            : base(environment, serviceScopeFactory, optionsAccessor, loggerFactory)
        {
            rabbitMqOptions = rabbitMqOptionsAccessor?.Value ?? throw new ArgumentNullException(nameof(rabbitMqOptionsAccessor));
            logger = loggerFactory?.CreateLogger<RabbitMqEventBus>() ?? throw new ArgumentNullException(nameof(loggerFactory));

            retryPolicy = Policy.Handle<BrokerUnreachableException>()
                                .Or<SocketException>()
                                .WaitAndRetry(retryCount: rabbitMqOptions.RetryCount,
                                              sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                                              onRetry: (ex, time) =>
                                              {
                                                  logger.LogError(ex, "RabbitMQ Client could not connect after {Timeout:n1}s ", time.TotalSeconds);
                                              });
        }

        /// <inheritdoc/>
        public override async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                await TryConnectAsync(cancellationToken);
            }

            using var channel = connection.CreateModel();
            return channel.IsOpen;
        }

        /// <inheritdoc/>
        public override async Task StartAsync(CancellationToken cancellationToken) => await ConnectConsumersAsync(cancellationToken);

        /// <inheritdoc/>
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            var channels = subscriptionChannelsCache.Select(kvp => (key: kvp.Key, sc: kvp.Value)).ToList();
            foreach (var (key, channel) in channels)
            {
                logger.LogDebug("Closing channel: {Subscription}", key);

                try
                {
                    if (!channel.IsClosed)
                    {
                        channel.Close();
                        subscriptionChannelsCache.Remove(key);
                    }

                    logger.LogDebug("Closed channel for {Subscription}", key);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Close channel faulted for {Subscription}", key);
                }
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override async Task<string> PublishAsync<TEvent>(EventContext<TEvent> @event,
                                                                DateTimeOffset? scheduled = null,
                                                                CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                await TryConnectAsync(cancellationToken);
            }

            // create channel, declare a fanout exchange
            using var channel = connection.CreateModel();
            var name = Options.GetRegistration<TEvent>().EventName;
            channel.ExchangeDeclare(exchange: name, type: "fanout");

            // serialize the event
            using var ms = new MemoryStream();
            var contentType = await SerializeAsync(ms, @event, cancellationToken);

            // publish message
            string scheduledId = null;
            retryPolicy.Execute(() =>
            {
                // setup properties
                var properties = channel.CreateBasicProperties();
                properties.MessageId = @event.EventId;
                properties.CorrelationId = @event.CorrelationId;
                properties.ContentEncoding = contentType.CharSet;
                properties.ContentType = contentType.MediaType;

                // if scheduled for later, set the delay in the message
                if (scheduled != null)
                {
                    var delay = Math.Max(0, (scheduled.Value - DateTimeOffset.UtcNow).TotalMilliseconds);
                    if (delay > 0)
                    {
                        properties.Headers["x-delay"] = (long)delay;
                        scheduledId = @event.EventId;
                    }
                }

                // if expiry is set in the future, set the ttl in the message
                if (@event.Expires != null && @event.Expires > DateTimeOffset.UtcNow)
                {
                    var ttl = @event.Expires.Value - DateTimeOffset.UtcNow;
                    properties.Expiration = ((long)ttl.TotalMilliseconds).ToString();
                }

                // do actual publish
                channel.BasicPublish(exchange: name,
                                     routingKey: "",
                                     basicProperties: properties,
                                     body: ms.ToArray());
            });

            return scheduledId;
        }

        /// <inheritdoc/>
        public override async Task<IList<string>> PublishAsync<TEvent>(IList<EventContext<TEvent>> events, DateTimeOffset? scheduled = null, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                await TryConnectAsync(cancellationToken);
            }

            // create channel, declare a fanout exchange
            using var channel = connection.CreateModel();
            var name = Options.GetRegistration<TEvent>().EventName;
            channel.ExchangeDeclare(exchange: name, type: "fanout");

            var serializedEvents = new List<(EventContext<TEvent>, ContentType, ReadOnlyMemory<byte>)>();
            foreach (var @event in events)
            {
                using var ms = new MemoryStream();
                var contentType = await SerializeAsync(ms, @event, cancellationToken);
                serializedEvents.Add((@event, contentType, ms.ToArray()));
            }

            retryPolicy.Execute(() =>
            {
                var batch = channel.CreateBasicPublishBatch();
                foreach (var (@event, contentType, body) in serializedEvents)
                {
                    // setup properties
                    var properties = channel.CreateBasicProperties();
                    properties.MessageId = @event.EventId;
                    properties.CorrelationId = @event.CorrelationId;
                    properties.ContentEncoding = contentType.CharSet;
                    properties.ContentType = contentType.MediaType;

                    // if scheduled for later, set the delay in the message
                    if (scheduled != null)
                    {
                        var delay = Math.Max(0, (scheduled.Value - DateTimeOffset.UtcNow).TotalMilliseconds);
                        if (delay > 0)
                        {
                            properties.Headers["x-delay"] = (long)delay;
                        }
                    }

                    // if expiry is set in the future, set the ttl in the message
                    if (@event.Expires != null && @event.Expires > DateTimeOffset.UtcNow)
                    {
                        var ttl = @event.Expires.Value - DateTimeOffset.UtcNow;
                        properties.Expiration = ((long)ttl.TotalMilliseconds).ToString();
                    }

                    // add to batch
                    batch.Add(exchange: name, routingKey: "", mandatory: false, properties: properties, body: body);
                }

                // do actual publish
                batch.Publish();
            });

            var messageIds = events.Select(m => m.EventId);
            return scheduled != null ? messageIds.ToList() : (IList<string>)Array.Empty<string>();
        }

        private async Task ConnectConsumersAsync(CancellationToken cancellationToken)
        {
            if (!IsConnected)
            {
                await TryConnectAsync(cancellationToken);
            }

            var registrations = Options.GetRegistrations();
            foreach (var reg in registrations)
            {
                var exchangeName = reg.EventName;
                var queueName = reg.ConsumerName;

                var channel = await GetSubscriptionChannelAsync(exchangeName: exchangeName, queueName: queueName, cancellationToken);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.Received += delegate (object sender, BasicDeliverEventArgs @event)
                {
                    var method = GetType().GetMethod(nameof(OnMessageReceivedAsync)).MakeGenericMethod(reg.EventType, reg.ConsumerType);
                    return (Task)method.Invoke(this, new object[] { channel, @event, CancellationToken.None, }); // do not chain CancellationToken
                };
                channel.BasicConsume(queue: queueName, autoAck: false, consumer);
            }
        }

        private async Task OnMessageReceivedAsync<TEvent, TConsumer>(IModel channel, BasicDeliverEventArgs args, CancellationToken cancellationToken)
            where TEvent : class
            where TConsumer : IEventBusConsumer<TEvent>
        {
            using var log_scope = logger.BeginScope(new Dictionary<string, string>
            {
                ["MessageId"] = args.BasicProperties?.MessageId,
                ["RoutingKey"] = args.RoutingKey,
                ["CorrelationId"] = args.BasicProperties?.CorrelationId,
                ["DeliveryTag"] = args.DeliveryTag.ToString(),
            });

            try
            {
                using var ms = new MemoryStream(args.Body.ToArray());
                var contentType = GetContentType(args.BasicProperties);
                var context = await DeserializeAsync<TEvent>(ms, contentType, cancellationToken);
                await PushToConsumerAsync<TEvent, TConsumer>(context, cancellationToken);

                // acknowlege the message
                channel.BasicAck(deliveryTag: args.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Event processing failed. Moving to deadletter.");
                channel.BasicNack(deliveryTag: args.DeliveryTag, multiple: false, requeue: false);
            }
        }

        private async Task<IModel> GetSubscriptionChannelAsync(string exchangeName, string queueName, CancellationToken cancellationToken)
        {
            await subscriptionChannelsCacheLock.WaitAsync(cancellationToken);

            try
            {
                var key = $"{exchangeName}/{queueName}";
                if (!subscriptionChannelsCache.TryGetValue(key, out var channel)
                    || channel.IsClosed)
                {
                    // dispose existing channel
                    if (channel != null) channel.Dispose();

                    channel = connection.CreateModel();
                    channel.ExchangeDeclare(exchange: exchangeName, type: "fanout");
                    channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
                    channel.QueueBind(queue: queueName, exchange: exchangeName, routingKey: "");
                    channel.CallbackException += delegate (object sender, CallbackExceptionEventArgs e)
                    {
                        logger.LogError(e.Exception, "Callback exeception for {Subscription}", key);
                        var _ = ConnectConsumersAsync(CancellationToken.None); // do not await or chain token
                    };

                    subscriptionChannelsCache[key] = channel;
                }

                return channel;
            }
            finally
            {
                subscriptionChannelsCacheLock.Release();
            }
        }

        private async Task<bool> TryConnectAsync(CancellationToken cancellationToken)
        {
            logger.LogDebug("RabbitMQ Client is trying to connect.");
            await connectionLock.WaitAsync(cancellationToken);

            try
            {
                // if already connected, do not proceed
                if (IsConnected)
                {
                    logger.LogDebug("RabbitMQ Client is already connected.");
                    return true;
                }

                retryPolicy.Execute(() =>
                {
                    connection = rabbitMqOptions.ConnectionFactory.CreateConnection();
                });

                if (IsConnected)
                {
                    connection.ConnectionShutdown += OnConnectionShutdown;
                    connection.CallbackException += OnCallbackException;
                    connection.ConnectionBlocked += OnConnectionBlocked;

                    logger.LogDebug("RabbitMQ Client acquired a persistent connection to '{HostName}'.",
                                    connection.Endpoint.HostName);

                    return true;
                }
                else
                {
                    logger.LogCritical("RabbitMQ Client connections could not be created and opened.");
                    return false;
                }
            }
            finally
            {
                connectionLock.Release();
            }
        }

        private bool TryConnect() => TryConnectAsync(CancellationToken.None).GetAwaiter().GetResult();

        private bool IsConnected => connection != null && connection.IsOpen && !disposed;

        private void OnConnectionBlocked(object sender, ConnectionBlockedEventArgs e)
        {
            if (disposed) return;

            logger.LogWarning("RabbitMQ connection was blocked for {Reason}. Trying to re-connect...", e.Reason);

            TryConnect();
        }

        private void OnCallbackException(object sender, CallbackExceptionEventArgs e)
        {
            if (disposed) return;

            logger.LogWarning(e.Exception, "A RabbitMQ connection throw exception. Trying to re-connect...");

            TryConnect();
        }

        private void OnConnectionShutdown(object sender, ShutdownEventArgs reason)
        {
            if (disposed) return;

            logger.LogWarning("RabbitMQ connection shutdown. Trying to re-connect...");

            TryConnect();
        }

        private static ContentType GetContentType(IBasicProperties properties)
        {
            var contentType = properties?.ContentType;
            var contentEncoding = properties?.ContentEncoding ?? "utf-8"; // assume a default

            if (string.IsNullOrWhiteSpace(contentType)) return null;
            return new ContentType(contentType) { CharSet = contentEncoding };
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    try
                    {
                        connection.Dispose();
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Trouble disposing RabbitMQ connection");
                    }
                }

                disposed = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}