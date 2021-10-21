using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using GoogleCloudSamples.EndToEndTracing.WebApp.Models;
using System.Net.Http;
using Google.Cloud.Diagnostics.Common;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System.Linq;

namespace GoogleCloudSamples.EndToEndTracing.WebApp.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IHttpClientFactory _clientFactory;
        private GoogleCloudOptions _options;

        public HomeController(ILogger<HomeController> logger, IHttpClientFactory clientFactory, IOptions<GoogleCloudOptions> options)
        {
            _logger = logger;
            _clientFactory = clientFactory;
            _options = options.Value;
        }

        public IActionResult Index()
        {
            foreach(var header in Request.Headers)
            {
                Console.WriteLine(header);
            }
            return View();
        }

        private void WriteCollectionToLog(LogLevel logLevel, IEnumerable<KeyValuePair<string, string>> collection) {
            foreach (var item in collection) {
                _logger.Log(
                    logLevel,
                    $"{item.Key}: {item.Value}"
                );
            }
        }

        private void WriteCollectionToLog(LogLevel logLevel, IEnumerable<KeyValuePair<string, IEnumerable<string>>> collection) {
            foreach (var item in collection) {
                _logger.Log(
                    logLevel,
                    $"{item.Key}: {string.Join(",",item.Value)}"
                );
            }
        }

        public async Task<IActionResult> SendEcho([FromServices] IManagedTracer tracer)
        {
            string result;
            var model = new SendEchoViewModel();

            _logger.LogInformation("SendEcho called");
            model.IncomingRequestHeaders = this.Request.Headers//.Cast<KeyValuePair<string, IEnumerable<string>>>();
             .Select(
                 item => new KeyValuePair<string, IEnumerable<string>>(
                     item.Key, 
                     item.Value));

            WriteCollectionToLog(LogLevel.Information, model.IncomingRequestHeaders);

            using (tracer.StartSpan(nameof(SendEcho) + " - Calling API"))
            {
                model.TraceInformation = new Dictionary<string,string>{
                        {"Google TraceID:",tracer.GetCurrentTraceId()},
                        {"ASPNET TraceID:",Activity.Current.TraceId.ToString()}
                    };

                _logger.LogInformation("Calling Echo service");
                WriteCollectionToLog(LogLevel.Information, model.TraceInformation);

                var httpClient = _clientFactory.CreateClient("echoService");

                var response = await httpClient.GetAsync("?text=test");

                _logger.LogInformation("Request headers:");
                model.EchoResponseHeaders = response.RequestMessage.Headers;
                WriteCollectionToLog(LogLevel.Information, model.EchoResponseHeaders);
                // foreach(var header in response.RequestMessage.Headers)
                // {
                //     _logger.LogInformation($"{header.Key}:{string.Join(",",header.Value)}");
                // }
                _logger.LogInformation("Response headers:");
                model.EchoRequestHeaders = response.Headers;
                WriteCollectionToLog(LogLevel.Information, model.EchoRequestHeaders);
                // foreach(var header in response.Headers)
                // {
                //     _logger.LogInformation($"{header.Key}:{string.Join(",",header.Value)}");
                // }
                result = await response.Content.ReadAsStringAsync();

                //result = "hello from cloud run";
                var messageId =  await PublishToTopic(result);
                _logger.LogInformation($"Message '{messageId}' sent to pubsub");
                result += " " + messageId;
            }
            return View("SendEcho", result);
        }

        private async Task<string> PublishToTopic(string messageText)
		{
			var topicName = new TopicName(_options.TopicProjectId, _options.TopicId);

			PublisherClient publisher = PublisherClient.Create(topicName);

			// Create a message
			var message = new PubsubMessage()
			{
				Data = ByteString.CopyFromUtf8(messageText)
			};
			message.Attributes.Add("custom-message-type", "Type1");

            _logger.LogInformation("Sending message to pubsub");
            var messageId = await SendAsync(publisher, message);
            return messageId;
		}

        public async Task<string> SendAsync(PublisherClient publisher, PubsubMessage message) 
        {
            ITraceContext context = ContextTracerManager.GetCurrentTraceContext();
            ITraceContext traceHeaderContext = TraceHeaderContext.Create(context.TraceId, context.SpanId ?? 0, context.ShouldTrace);
            var traceContextValue = traceHeaderContext.ToString();

            // Add ID/Context for both the .NET Activity and the Google Cloud Trace context
            message.Attributes.Add("custom-activity-id", Activity.Current.Id);
            message.Attributes.Add("custom-trace-context", traceContextValue);

            // Add PubSub info to the span's labels
            ContextTracerManager.GetCurrentTracer().AnnotateSpan(
                new Dictionary<string, string>(){
                    {"pubsub/topic", publisher.TopicName.ToString()},
                    {"pubsub/publish_time", DateTime.UtcNow.ToString()}
                }
            );

            var messageId = await publisher.PublishAsync(message);

            // Add the message ID to the span's labels
            ContextTracerManager.GetCurrentTracer().AnnotateSpan(
                new Dictionary<string, string>(){
                    {"pubsub/meassage_id", messageId}
                }
            );

            return messageId;
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
