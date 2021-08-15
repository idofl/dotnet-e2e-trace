using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Diagnostics.Common;
using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.Logging;

namespace dotnet_listener_service
{
    // Based on https://docs.microsoft.com/en-us/dotnet/core/extensions/scoped-service
    public interface IScopedProcessingService
    {
        Task<SubscriberClient.Reply> ProcessMessage(PubsubMessage message, CancellationToken stoppingToken);
    }

    public class PubSubProcessingService : IScopedProcessingService
    {
        private int _executionCount;
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
            _logger.LogInformation(
                "{ServiceName} working, execution count: {Count}",
                nameof(PubSubProcessingService),
                _executionCount);

            var activity = Activity.Current;
            // Add baggage with information from current pub/sub message and make sure it is included in the trace
            activity.AddBaggage("DeliveryAttempt", message.GetDeliveryAttempt()?.ToString() ?? "N/A");
            activity.AddBaggage("PublishTime", message.PublishTime.ToString());
            activity.AddBaggage("MessageId", message.MessageId);

            using (_tracer.StartSpanEx($"ProcessMessage ({message.MessageId})"))
            {
              //_tracer.AnnotateSpan(new Dictionary<string,string>(Activity.Current.Baggage));
              _logger.LogInformation($"Processing using activity: {Activity.Current.TraceId}");
              _logger.LogInformation($"Current Google Trace ID: {_tracer.GetCurrentTraceId()}");

              string messageType;
              if (!message.Attributes.TryGetValue("custom-message-type", out messageType))
                throw new ArgumentNullException("Message type not specified");

              var ack = SubscriberClient.Reply.Nack;

              switch (messageType) 
              {
                case "Type1":
                  ack = (await ProcessType1Message(message)) ? SubscriberClient.Reply.Ack : SubscriberClient.Reply.Nack;
                  break;
                default:
                  // Let the message return as Nacked
                  //throw new ArgumentException($"Message type unknown (${messageType})");
                  break;
              }

              return ack;
            }
        }

        private async Task<bool> ProcessType1Message(PubsubMessage message)
        {
            using (_tracer.StartSpan(nameof(ProcessType1Message)))
            {
              string text = System.Text.Encoding.UTF8.GetString(message.Data.ToArray());
              _logger.LogInformation($"Message {message.MessageId}: {text}");
              _logger.LogWarning("Something is wrong, here's what we know...", message.MessageId, text, message.PublishTime.ToString());
              return await Task.FromResult(true);
            }
        }
    }
}
