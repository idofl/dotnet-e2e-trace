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
using Microsoft.Extensions.Logging;

namespace GoogleCloudSamples.EndToEndTracing.PubSubListener
{
    /// <Summary>
    /// Define the methods available by the scoped worker.
    /// Based on https://docs.microsoft.com/en-us/dotnet/core/extensions/scoped-service
    /// </Summary>
    public interface IScopedProcessingService
    {
        Task<SubscriberClient.Reply> ProcessMessage(PubsubMessage message, CancellationToken stoppingToken);
    }

    /// <Summary>
    /// Implementation of the <see cref="IScopedProcessingService"/> interface.
    /// </Summary>
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

        /// <summary>
        /// Straightforward processing that does nothing and only logs 
        /// information about the message.
        /// </summary>
        /// <param name="message">The PubSub message to process</param>
        /// <param name="stoppingToken">Cancellation token to indicate we need 
        /// to stop processing</param>
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
