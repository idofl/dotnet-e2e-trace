using Google.Cloud.Functions.Framework;
using Google.Cloud.Functions.Hosting;
using Google.Cloud.Diagnostics.Common;
using Google.Cloud.Diagnostics.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Google.Cloud.Trace.V1;
using System;
using System.Reflection;

namespace dotnet_echo_function
{
    public class Startup : FunctionsStartup
    {
        // Provide implementations for IOperationSingleton, and IOperationScoped.
        // The implementation is the same for both interfaces (the Operation class)
        public override void ConfigureServices(WebHostBuilderContext context, IServiceCollection services) 
        {
            Google.Cloud.Diagnostics.AspNetCore.CloudTraceExtension.AddGoogleTrace(services, options =>
                {
                    options.ProjectId = Environment.GetEnvironmentVariable("GCP_PROJECT");
                    options.Options = TraceOptions.Create(
                        bufferOptions: BufferOptions.NoBuffer());
                });
        }

        public override void Configure(WebHostBuilderContext context, IApplicationBuilder app) =>
            app.UseGoogleTrace();
    }

    [FunctionsStartup(typeof(Startup))]
    public class Function : IHttpFunction
    {        
        private readonly ILogger<Function> _logger;
        private readonly IManagedTracer _tracer;

        public Function(ILogger<Function> logger, IManagedTracer tracer) 
        {
            _logger = logger;
            _tracer = tracer;
        }

        /// <summary>
        /// Logic for your function goes here.
        /// </summary>
        /// <param name="context">The HTTP context, containing the request and the response.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleAsync(HttpContext context)
        {
             _logger.LogInformation("Echo function running");
            foreach (var header in context.Request.Headers)
            {
                _logger.LogInformation($"{header.Key}:{header.Value}");
            }
            var text = "test";
            var tracer = _tracer;
            _logger.LogInformation("Original tracer type: " + tracer.GetType().ToString());

            using (ISpan span = tracer.StartSpan("Echo."+nameof(HandleAsync)))
            {
                _logger.LogInformation("In Echo function, processing...");
                _logger.LogInformation("Google TraceID: " + tracer.GetCurrentTraceId());
                _logger.LogInformation("Google SpanID: " + $"{tracer.GetCurrentSpanId():x16}");
                await Task.Delay(100);
            }
            await context.Response.WriteAsync(text);
        }
    }
}
