using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using dotnet_e2e_trace.Models;
using System.Net.Http;
using System.Text.Json;
using System.IO;
using Google.Cloud.Diagnostics.Common;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;

namespace dotnet_e2e_trace.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        //private HttpClient _httpClient;
        private readonly IHttpClientFactory _clientFactory;
        private string ProjectId = "idoflatow-devenv";
        private string TopicId = "e2e-test-topic";

        public HomeController(ILogger<HomeController> logger, IHttpClientFactory clientFactory)
        {
            _logger = logger;
            _clientFactory = clientFactory;
        }

        public IActionResult Index()
        {
            foreach(var header in Request.Headers)
            {
                Console.WriteLine(header);
            }
            return View();
        }

        public async Task<IActionResult> CallEcho([FromServices] IManagedTracer tracer)
        {
            string result;
            _logger.LogInformation("CallEcho called");

            using (tracer.StartSpan(nameof(CallEcho) + " - Calling API"))
            {
                _logger.LogInformation("Calling Echo service");
                _logger.LogInformation("Google TraceID: " + tracer.GetCurrentTraceId());
                _logger.LogInformation("ASPNET TraceID: " + Activity.Current.TraceId.ToString());
                
                var httpClient = _clientFactory.CreateClient("echoService");

                var response = await httpClient.GetAsync("?text=test");
                foreach(var header in response.Headers)
                {
                    Console.WriteLine(header);
                }
                result = await response.Content.ReadAsStringAsync();

                //result = "hello from cloud run";
                var messageId =  await PublishToTopic(result);
                _logger.LogInformation($"Message '{messageId}' sent to pubsub");
                result += " " + messageId;
            }
            return View("Echo", result);
        }

        private async Task<string> PublishToTopic(string messageText)
		{            
			var topicName = new TopicName(ProjectId, TopicId);

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
