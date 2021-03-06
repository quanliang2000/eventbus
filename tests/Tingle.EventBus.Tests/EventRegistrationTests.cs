using Microsoft.Extensions.DependencyInjection;
using System;
using Tingle.EventBus.Registrations;
using Tingle.EventBus.Serialization;
using Xunit;

namespace Tingle.EventBus.Tests
{
    public class EventRegistrationTests
    {
        [Fact]
        public void SetSerializer_UsesDefault()
        {
            // when not set, use default
            var registration = new EventRegistration(typeof(TestEvent1));
            Assert.Null(registration.EventSerializerType);
            registration.SetSerializer();
            Assert.Equal(typeof(IEventSerializer), registration.EventSerializerType);
        }

        [Fact]
        public void SetSerializer_RepsectsAttribute()
        {
            // attribute is respected
            var registration = new EventRegistration(typeof(TestEvent2));
            registration.SetSerializer();
            Assert.Equal(typeof(FakeEventSerializer1), registration.EventSerializerType);
        }

        [Fact]
        public void SetSerializer_Throws_InvalidOperationException()
        {
            // attribute is respected
            var registration = new EventRegistration(typeof(TestEvent3));
            var ex = Assert.Throws<InvalidOperationException>(() => registration.SetSerializer());
            Assert.Equal("The type 'Tingle.EventBus.Tests.FakeEventSerializer2' is used"
                       + " as a serializer but does not implement 'Tingle.EventBus.Serialization.IEventSerializer'",
                ex.Message);
        }

        [Theory]
        [InlineData(typeof(TestEvent1), false, "dev", NamingConvention.KebabCase, "dev-test-event1")]
        [InlineData(typeof(TestEvent1), false, "dev", NamingConvention.SnakeCase, "dev_test_event1")]
        [InlineData(typeof(TestEvent1), false, "dev", NamingConvention.DotCase, "dev.test.event1")]
        [InlineData(typeof(TestEvent1), true, "dev", NamingConvention.KebabCase, "dev-tingle-event-bus-tests-test-event1")]
        [InlineData(typeof(TestEvent1), true, "dev", NamingConvention.SnakeCase, "dev_tingle_event_bus_tests_test_event1")]
        [InlineData(typeof(TestEvent1), true, "dev", NamingConvention.DotCase, "dev.tingle.event.bus.tests.test.event1")]
        // Overriden by attribute
        [InlineData(typeof(TestEvent2), true, "dev", NamingConvention.KebabCase, "sample-event")]
        [InlineData(typeof(TestEvent2), true, "dev", NamingConvention.SnakeCase, "sample-event")]
        [InlineData(typeof(TestEvent2), true, "dev", NamingConvention.DotCase, "sample-event")]
        public void SetEventName_Works(Type eventType, bool useFullTypeNames, string scope, NamingConvention namingConvention, string expected)
        {
            var options = new EventBusOptions { };
            options.Naming.Scope = scope;
            options.Naming.Convention = namingConvention;
            options.Naming.UseFullTypeNames = useFullTypeNames;
            var registration = new EventRegistration(eventType);
            registration.SetEventName(options.Naming);
            Assert.Equal(expected, registration.EventName);
        }

        [Theory]
        // Full type names
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), false, "service1", true, ConsumerNameSource.TypeName, NamingConvention.KebabCase, "test-consumer1-test-event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), false, "service1", true, ConsumerNameSource.TypeName, NamingConvention.SnakeCase, "test_consumer1_test_event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), false, "service1", true, ConsumerNameSource.TypeName, NamingConvention.DotCase, "test.consumer1.test.event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), false, null, true, ConsumerNameSource.Prefix, NamingConvention.KebabCase, "app1-test-event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), false, null, true, ConsumerNameSource.Prefix, NamingConvention.SnakeCase, "app1_test_event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), false, null, true, ConsumerNameSource.Prefix, NamingConvention.DotCase, "app1.test.event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), false, null, true, ConsumerNameSource.PrefixAndTypeName, NamingConvention.KebabCase, "app1-test-consumer1-test-event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), false, null, true, ConsumerNameSource.PrefixAndTypeName, NamingConvention.SnakeCase, "app1_test_consumer1_test_event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), false, null, true, ConsumerNameSource.PrefixAndTypeName, NamingConvention.DotCase, "app1.test.consumer1.test.event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), false, "service1", true, ConsumerNameSource.Prefix, NamingConvention.KebabCase, "service1-test-event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), false, "service1", true, ConsumerNameSource.Prefix, NamingConvention.SnakeCase, "service1_test_event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), false, "service1", true, ConsumerNameSource.Prefix, NamingConvention.DotCase, "service1.test.event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), false, "service1", true, ConsumerNameSource.PrefixAndTypeName, NamingConvention.KebabCase, "service1-test-consumer1-test-event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), false, "service1", true, ConsumerNameSource.PrefixAndTypeName, NamingConvention.SnakeCase, "service1_test_consumer1_test_event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), false, "service1", true, ConsumerNameSource.PrefixAndTypeName, NamingConvention.DotCase, "service1.test.consumer1.test.event1")]

        // Short type names
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), true, null, true, ConsumerNameSource.TypeName, NamingConvention.KebabCase,
            "tingle-event-bus-tests-test-consumer1-tingle-event-bus-tests-test-event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), true, null, true, ConsumerNameSource.TypeName, NamingConvention.SnakeCase,
            "tingle_event_bus_tests_test_consumer1_tingle_event_bus_tests_test_event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), true, null, true, ConsumerNameSource.TypeName, NamingConvention.DotCase,
            "tingle.event.bus.tests.test.consumer1.tingle.event.bus.tests.test.event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), true, null, true, ConsumerNameSource.Prefix, NamingConvention.KebabCase, "app1-tingle-event-bus-tests-test-event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), true, null, true, ConsumerNameSource.Prefix, NamingConvention.SnakeCase, "app1_tingle_event_bus_tests_test_event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), true, null, true, ConsumerNameSource.Prefix, NamingConvention.DotCase, "app1.tingle.event.bus.tests.test.event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), true, null, true, ConsumerNameSource.PrefixAndTypeName, NamingConvention.KebabCase,
            "app1-tingle-event-bus-tests-test-consumer1-tingle-event-bus-tests-test-event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), true, null, true, ConsumerNameSource.PrefixAndTypeName, NamingConvention.SnakeCase,
            "app1_tingle_event_bus_tests_test_consumer1_tingle_event_bus_tests_test_event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), true, null, true, ConsumerNameSource.PrefixAndTypeName, NamingConvention.DotCase,
            "app1.tingle.event.bus.tests.test.consumer1.tingle.event.bus.tests.test.event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), true, "service1", true, ConsumerNameSource.Prefix, NamingConvention.KebabCase, "service1-tingle-event-bus-tests-test-event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), true, "service1", true, ConsumerNameSource.Prefix, NamingConvention.SnakeCase, "service1_tingle_event_bus_tests_test_event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), true, "service1", true, ConsumerNameSource.Prefix, NamingConvention.DotCase, "service1.tingle.event.bus.tests.test.event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), true, "service1", true, ConsumerNameSource.PrefixAndTypeName, NamingConvention.KebabCase,
            "service1-tingle-event-bus-tests-test-consumer1-tingle-event-bus-tests-test-event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), true, "service1", true, ConsumerNameSource.PrefixAndTypeName, NamingConvention.SnakeCase,
            "service1_tingle_event_bus_tests_test_consumer1_tingle_event_bus_tests_test_event1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), true, "service1", true, ConsumerNameSource.PrefixAndTypeName, NamingConvention.DotCase,
            "service1.tingle.event.bus.tests.test.consumer1.tingle.event.bus.tests.test.event1")]

        // Overriden by attribute
        [InlineData(typeof(TestEvent2), typeof(TestConsumer2), false, null, true, ConsumerNameSource.TypeName, NamingConvention.SnakeCase, "sample-consumer_sample-event")]
        [InlineData(typeof(TestEvent2), typeof(TestConsumer2), false, null, true, ConsumerNameSource.Prefix, NamingConvention.SnakeCase, "sample-consumer_sample-event")]
        [InlineData(typeof(TestEvent2), typeof(TestConsumer2), false, "service1", true, ConsumerNameSource.PrefixAndTypeName, NamingConvention.SnakeCase, "sample-consumer_sample-event")]
        [InlineData(typeof(TestEvent2), typeof(TestConsumer2), false, "service1", true, ConsumerNameSource.PrefixAndTypeName, NamingConvention.DotCase, "sample-consumer.sample-event")]
        [InlineData(typeof(TestEvent2), typeof(TestConsumer2), true, "service1", true, ConsumerNameSource.TypeName, NamingConvention.KebabCase, "sample-consumer-sample-event")]
        [InlineData(typeof(TestEvent2), typeof(TestConsumer2), true, "service1", true, ConsumerNameSource.Prefix, NamingConvention.KebabCase, "sample-consumer-sample-event")]
        [InlineData(typeof(TestEvent2), typeof(TestConsumer2), true, "service1", true, ConsumerNameSource.Prefix, NamingConvention.DotCase, "sample-consumer.sample-event")]
        [InlineData(typeof(TestEvent2), typeof(TestConsumer2), true, null, true, ConsumerNameSource.PrefixAndTypeName, NamingConvention.KebabCase, "sample-consumer-sample-event")]
        [InlineData(typeof(TestEvent2), typeof(TestConsumer2), true, null, true, ConsumerNameSource.PrefixAndTypeName, NamingConvention.SnakeCase, "sample-consumer_sample-event")]
        [InlineData(typeof(TestEvent2), typeof(TestConsumer2), true, null, true, ConsumerNameSource.PrefixAndTypeName, NamingConvention.DotCase, "sample-consumer.sample-event")]

        // Appending
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), false, "service1", false, ConsumerNameSource.TypeName, NamingConvention.KebabCase, "test-consumer1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), false, null, false, ConsumerNameSource.PrefixAndTypeName, NamingConvention.KebabCase, "app1-test-consumer1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), true, null, false, ConsumerNameSource.TypeName, NamingConvention.KebabCase, "tingle-event-bus-tests-test-consumer1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), true, null, false, ConsumerNameSource.TypeName, NamingConvention.SnakeCase, "tingle_event_bus_tests_test_consumer1")]
        [InlineData(typeof(TestEvent1), typeof(TestConsumer1), true, null, false, ConsumerNameSource.TypeName, NamingConvention.DotCase, "tingle.event.bus.tests.test.consumer1")]
        [InlineData(typeof(TestEvent2), typeof(TestConsumer2), true, null, false, ConsumerNameSource.PrefixAndTypeName, NamingConvention.KebabCase, "sample-consumer")]
        [InlineData(typeof(TestEvent2), typeof(TestConsumer2), true, null, false, ConsumerNameSource.PrefixAndTypeName, NamingConvention.DotCase, "sample-consumer")]
        public void SetConsumerName_Works(Type eventType,
                                          Type consumerType,
                                          bool useFullTypeNames,
                                          string prefix,
                                          bool suffixEventName,
                                          ConsumerNameSource consumerNameSource,
                                          NamingConvention namingConvention,
                                          string expected)
        {
            var environment = new FakeHostEnvironment("app1");
            var options = new EventBusOptions { };
            options.Naming.Convention = namingConvention;
            options.Naming.UseFullTypeNames = useFullTypeNames;
            options.Naming.ConsumerNameSource = consumerNameSource;
            options.Naming.ConsumerNamePrefix = prefix;
            options.Naming.SuffixConsumerName = suffixEventName;

            var registration = new EventRegistration(eventType);
            registration.Consumers.Add(new EventConsumerRegistration(consumerType));

            var creg = Assert.Single(registration.Consumers);
            registration.SetEventName(options.Naming)
                        .SetConsumerNames(options.Naming, environment);
            Assert.Equal(expected, creg.ConsumerName);
        }
    }
}
