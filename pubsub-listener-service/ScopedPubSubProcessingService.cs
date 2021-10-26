using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Diagnostics.Common;
using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.Logging;

namespace GoogleCloudSamples.EndToEndTracing.PubSubListener
{
    // Based on https://docs.microsoft.com/en-us/dotnet/core/extensions/scoped-service
    public interface IScopedProcessingService
    {
        Task<SubscriberClient.Reply> ProcessMessage(PubsubMessage message, CancellationToken stoppingToken);
    }

    public class PubSubProcessingService : IScopedProcessingService
    {
        private readonly ILogger<PubSubProcessingService> _logger;
        private readonly IManagedTracer _tracer;
        public PubSubProcessingService(ILogger<PubSubProcessingService> logger,
                                       IManagedTracer tracer)
        {
            _logger = logger;
            _tracer = tracer;
        }

        public async Task<SubscriberClient.Reply> ProcessMessage(PubsubMessage message, CancellationToken stoppingToken)
        {
            var activity = Activity.Current;
            // Add tags with information from current pub/sub message and make sure it is included in the trace
            activity.AddTag("DeliveryAttempt", message.GetDeliveryAttempt()?.ToString() ?? "N/A");
            activity.AddTag("PublishTime", message.PublishTime.ToString());
            activity.AddTag("MessageId", message.MessageId);

            using (_tracer.StartSpanWithActivityTags($"{nameof(ProcessMessage)} ({message.MessageId})"))
            {
                _logger.LogInformation($"{nameof(ProcessMessage)} - Processing message {message.MessageId}");
                string text = System.Text.Encoding.UTF8.GetString(message.Data.ToArray());
                _logger.LogInformation($"{nameof(ProcessMessage)} - Message is '{text}'");
                await Task.Delay(100);
                return SubscriberClient.Reply.Ack;
            }
        }
    }
}
