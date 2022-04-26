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
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using GoogleCloudSamples.EndToEndTracing.WebApp.ViewModels;
using System.Net.Http;
using Google.Cloud.Diagnostics.Common;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Microsoft.Extensions.Options;
using System.Linq;

namespace GoogleCloudSamples.EndToEndTracing.WebApp.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IHttpClientFactory _clientFactory;
        private readonly GoogleCloudOptions _options;

         // [START dotnet_distributed_diagnostics_ctor_ilogger]
        public HomeController(ILogger<HomeController> logger, IHttpClientFactory clientFactory, IOptions<GoogleCloudOptions> options)
        {
            _logger = logger;
            _clientFactory = clientFactory;
            _options = options.Value;
        }
        // [END dotnet_distributed_diagnostics_ctor_ilogger]

        public IActionResult Index()
        {
            return View();
        }

        private void WriteCollectionToLog(LogLevel logLevel, string logTitle, IEnumerable<KeyValuePair<string, string>> collection) {
            _logger.Log(logLevel, logTitle);
            foreach (var item in collection) {
                _logger.Log(
                    logLevel,
                    $"{item.Key}: {item.Value}"
                );
            }
        }

        private void WriteCollectionToLog(LogLevel logLevel,  string logTitle, IEnumerable<KeyValuePair<string, IEnumerable<string>>> collection) {
            _logger.Log(logLevel, logTitle);
            foreach (var item in collection) {
                _logger.Log(
                    logLevel,
                    $"{item.Key}: {string.Join(",",item.Value)}"
                );
            }
        }

        /// <summary>
        /// Send request to the Echo function and then send the response
        /// From the function to PubSub
        /// </summary>
        /// <param name="tracer">Managed tracer for creating child spans</param>
        /// <returns>HTML page with metadata information about 
        /// requests and responses</returns>
        // [START dotnet_distributed_diagnostics_aspnet_imanagedtracer]
        public async Task<IActionResult> SendEcho([FromServices] IManagedTracer tracer)
        // [END dotnet_distributed_diagnostics_aspnet_imanagedtracer]
        {
            // [START dotnet_distributed_diagnostics_ilogger_log]
            _logger.LogInformation($"{nameof(SendEcho)} - Method called");
            // [END dotnet_distributed_diagnostics_ilogger_log]

            var model = new SendEchoViewModel();
            string result;

            // Store Trace IDs
            model.TraceInformation = new Dictionary<string,string>{
                {"Google TraceID",tracer.GetCurrentTraceId()},
                {"ASPNET TraceID",Activity.Current.TraceId.ToString()}};

            // Store incoming request's headers
            model.IncomingRequestHeaders = this.Request.Headers
             .Select(item => new KeyValuePair<string, IEnumerable<string>>(
                item.Key, 
                item.Value));

            // By default, all logs are outputted to stdout
            WriteCollectionToLog(LogLevel.Information, "Trace IDs", model.TraceInformation);
            WriteCollectionToLog(LogLevel.Information, "Incoming request's headers", model.IncomingRequestHeaders);

            using (tracer.StartSpan(nameof(SendEcho) + " - Calling the Echo Cloud Function"))
            {
                _logger.LogInformation($"{nameof(SendEcho)} - Calling the Echo Cloud Function");

                var httpClient = _clientFactory.CreateClient("EchoFunction");
                var response = await httpClient.PostAsync("", new StringContent("Hello World"));
                result = await response.Content.ReadAsStringAsync();

                model.EchoRequestHeaders = response.RequestMessage.Headers;
                model.EchoResponseHeaders = response.Headers;

                WriteCollectionToLog(
                    LogLevel.Information, 
                    $"{nameof(SendEcho)} - Echo request headers", 
                    model.EchoRequestHeaders);
                WriteCollectionToLog(
                    LogLevel.Information, 
                    $"{nameof(SendEcho)} - Echo response headers", 
                    model.EchoResponseHeaders);
            }

            using (tracer.StartSpan(nameof(SendEcho) + " - Sending a message to PubSub"))
            {
                _logger.LogInformation($"{nameof(SendEcho)} - Sending a message to PubSub");

                // Send the response from the Echo function to PubSub
                var messageId =  await PublishToTopic(result);

                model.PubSubInformation = new Dictionary<string,string>{
                        {"MessageID",messageId},
                    };

                _logger.LogInformation(
                    $"{nameof(SendEcho)} - Message '{result}' sent to pubsub. Message ID is {messageId}");
            }

            return View("SendEcho", model);
        }

        private async Task<string> PublishToTopic(string messageText)
		{
			var topicName = new TopicName(_options.ProjectId, _options.TopicId);
			PublisherClient publisher = PublisherClient.Create(topicName);

			var message = new PubsubMessage()
			{
				Data = ByteString.CopyFromUtf8(messageText)
			};

            _logger.LogInformation($"{nameof(PublishToTopic)} - Sending a message to pubsub");
            var messageId = await SendAsync(publisher, message);

            // Add information about the PubSub message to the current span
            ContextTracerManager.GetCurrentTracer().AnnotateSpan(
                new Dictionary<string, string>(){
                    {"pubsub/topic", publisher.TopicName.ToString()},
                    {"pubsub/publish_time", DateTime.UtcNow.ToString()},
                    {"pubsub/meassage_id", messageId}
                }
            );

            return messageId;
		}

        // [START dotnet_distributed_diagnostics_pubsub_trace_publish]
        private async Task<string> SendAsync(PublisherClient publisher, PubsubMessage message) 
        {
            // Get the current trace context
            ITraceContext context = ContextTracerManager.GetCurrentTraceContext();
            // Create the string representation for the trace context,
            // using the same format used in the HTTP trace header 
            ITraceContext traceHeaderContext = TraceHeaderContext.Create(
                context.TraceId, context.SpanId ?? 0, context.ShouldTrace);
            var traceContextValue = traceHeaderContext.ToString();

            // Add .NET Activity and the Google Cloud Trace trace IDs
            message.Attributes.Add("custom-activity-id", Activity.Current.Id);
            message.Attributes.Add("custom-trace-context", traceContextValue);

            var messageId = await publisher.PublishAsync(message);

            return messageId;
        }
        // [END dotnet_distributed_diagnostics_pubsub_trace_publish]

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
