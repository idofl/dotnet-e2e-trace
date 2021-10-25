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

        public async Task<IActionResult> SendEcho([FromServices] IManagedTracer tracer)
        {
            _logger.LogInformation($"{nameof(SendEcho)} - Method called");

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
                    model.EchoResponseHeaders);
                WriteCollectionToLog(
                    LogLevel.Information, 
                    $"{nameof(SendEcho)} - Echo response headers", 
                    model.EchoRequestHeaders);
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

        public async Task<string> SendAsync(PublisherClient publisher, PubsubMessage message) 
        {
            // Get the Trace Context value used for HTTP headers
            ITraceContext context = ContextTracerManager.GetCurrentTraceContext();
            ITraceContext traceHeaderContext = TraceHeaderContext.Create(
                context.TraceId, context.SpanId ?? 0, context.ShouldTrace);
            var traceContextValue = traceHeaderContext.ToString();

            // Add .NET Activity and the Google Cloud Trace trace IDs
            message.Attributes.Add("custom-activity-id", Activity.Current.Id);
            message.Attributes.Add("custom-trace-context", traceContextValue);

            var messageId = await publisher.PublishAsync(message);

            return messageId;
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
