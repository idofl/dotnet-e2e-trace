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
using Microsoft.Extensions.DependencyInjection;

namespace dotnet_listener_service
{
    public class Worker : BackgroundService
    {
        private string ProjectId = "idoflatow-devenv";
        private string SubscriptionId = "e2e-test-topic-sub";

        private readonly ILogger<Worker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly Func<ITraceContext, IManagedTracer> _tracerFactory;
        public Worker(ILogger<Worker> logger, IServiceProvider serviceProvider, Func<ITraceContext, IManagedTracer> tracerFactory)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _tracerFactory = tracerFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var subscriptionName = new SubscriptionName(ProjectId, SubscriptionId);

            var subscription = SubscriberClient.Create(subscriptionName);
                
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                                
                Task startTask = subscription.StartAsync((PubsubMessage message, CancellationToken cancel) =>
                {
                    SubscriberClient.Reply ack;
                    using (IServiceScope scope = _serviceProvider.CreateScope())
                    {   
                        // Set up the Google trace context and .NET Activity for the current message
                        InitializeTracingFromMessage(message);
                        var activity = InitializeActivityFromMessage(message, "PubSubOperation").Start();

                        using (ContextTracerManager.GetCurrentTracer().StartSpan("PubSub subscriber"))
                        {
                            _logger.LogInformation($"Message Activity Trace ID {Activity.Current.TraceId}");

                            IScopedProcessingService scopedProcessingService =
                                scope.ServiceProvider.GetRequiredService<IScopedProcessingService>();

                            _logger.LogInformation($"Sending message {message.MessageId} for processing...");
                            ack = scopedProcessingService.ProcessMessage(message, stoppingToken).Result;
                            
                            if (ack == SubscriberClient.Reply.Nack)
                            {
                                // Todo:...
                            }
                            _logger.LogInformation($"Message {message.MessageId} finished processing ({ack})...");
                        }
                        activity.Stop();
                        return Task.FromResult(ack);
                    }
                });
                
                // Lets make sure that the start task finished successfully after the call to stop.
                startTask.Wait(stoppingToken);
            }
            await subscription.StopAsync(stoppingToken);
        }

        private void InitializeTracingFromMessage(PubsubMessage message)
        {
            // Extract trace information from the message attributes
            string traceContextAttribute = message.Attributes["custom-trace-context"];
            
            // Parse the trace context header
            ITraceContext context = TraceHeaderContext.FromHeader(traceContextAttribute);
          
            // Create the IManagedTracer for the current trace context
            var tracer = _tracerFactory(context);
            
            // Set current tracer for the DI when asked for IManagedTracer in the scoped service
            ContextTracerManager.SetCurrentTracer(tracer);
        }

        private Activity InitializeActivityFromMessage(PubsubMessage message, string operationName)
        {
            string parentActivity = message.Attributes["custom-activity-id"];
            return new Activity(operationName).SetParentId(parentActivity);
        }
    }
}
