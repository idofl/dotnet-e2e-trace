// Copyright 2021 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Diagnostics.Common;
using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace GoogleCloudSamples.EndToEndTracing.PubSubListener
{
        /// <summary>
        /// Background worker that listens for incoming PubSub messages,
        /// and sends each message for processing in a separate scope.
        /// </summary>
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly Func<ITraceContext, IManagedTracer> _tracerFactory;
        private readonly GoogleCloudOptions _options;
        public Worker(
            ILogger<Worker> logger, 
            IServiceProvider serviceProvider, 
            Func<ITraceContext, IManagedTracer> tracerFactory, 
            IOptions<GoogleCloudOptions> options)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _tracerFactory = tracerFactory;
            _options = options.Value;
        }

        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var subscriptionName = new SubscriptionName(_options.ProjectId, _options.SubscriptionId);

            var subscription = SubscriberClient.Create(subscriptionName);

            _logger.LogInformation($"{nameof(ExecuteAsync)} - Subscriber started at: {DateTimeOffset.UtcNow}");
            while (!stoppingToken.IsCancellationRequested)
            {
                Task startTask = subscription.StartAsync((PubsubMessage message, CancellationToken cancel) =>
                {
                    // Create a scope for each incoming message to support 
                    // scoped services. Without a scope all messages will
                    // be treated as running in a singleton worker.
                    // https://docs.microsoft.com/en-us/dotnet/core/extensions/scoped-service
                    using (IServiceScope scope = _serviceProvider.CreateScope())
                    {
                        IScopedProcessingService scopedProcessingService =
                            scope.ServiceProvider.GetRequiredService<IScopedProcessingService>();

                        string operationName = nameof(scopedProcessingService.ProcessMessage);

                        // Set up the Google trace context and 
                        // .NET Activity for the current message
                        InitializeTracingFromMessage(message);
                        using(InitializeActivityFromMessage(message, operationName).Start())
                        {
                            using (ContextTracerManager.GetCurrentTracer().StartSpan($"{nameof(ExecuteAsync)}.HandleMessage"))
                            {
                                _logger.LogInformation($"{nameof(ExecuteAsync)} - Sending message {message.MessageId} for processing");
                                var ack = scopedProcessingService.ProcessMessage(message, stoppingToken).Result;
                                return Task.FromResult(ack);
                            }
                        }
                    }
                });
                startTask.Wait(stoppingToken);
            }
            await subscription.StopAsync(stoppingToken);
            _logger.LogInformation($"{nameof(ExecuteAsync)} - Subscriber stopped at: {DateTimeOffset.UtcNow}");
        }

        private void InitializeTracingFromMessage(PubsubMessage message)
        {
            // Extract trace information from the message attributes
            string traceContextAttribute = message.Attributes["custom-trace-context"];
            // Parse the trace context header
            ITraceContext context = TraceHeaderContext.FromHeader(traceContextAttribute);
            // Create the IManagedTracer for the current trace context
            var tracer = _tracerFactory(context);
            // Set current tracer for the DI when asked for IManagedTracer 
            // in the scoped service
            ContextTracerManager.SetCurrentTracer(tracer);
        }

        private Activity InitializeActivityFromMessage(PubsubMessage message, string operationName)
        {
            string parentActivity = message.Attributes["custom-activity-id"];
            return new Activity(operationName).SetParentId(parentActivity);
        }
    }
}
