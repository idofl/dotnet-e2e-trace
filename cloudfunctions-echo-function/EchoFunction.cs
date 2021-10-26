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
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Reflection;

namespace GoogleCloudSamples.EndToEndTracing.Function
{
    public class Startup : FunctionsStartup
    {
        public override void ConfigureAppConfiguration(WebHostBuilderContext context, IConfigurationBuilder configuration)
        {
            configuration.AddEnvironmentVariables(prefix: GoogleCloudOptions.Section);
        }

        public override void ConfigureServices(WebHostBuilderContext context, IServiceCollection services) 
        {
            var googleCloudOptions = new GoogleCloudOptions();

            context.Configuration
                .GetSection(GoogleCloudOptions.Section)
                .Bind(googleCloudOptions);

            services.AddGoogleDiagnosticsForAspNetCore(
                googleCloudOptions.Diagnostics.ProjectId,
                googleCloudOptions.Diagnostics.ServiceName,
                googleCloudOptions.Diagnostics.Version,
                TraceOptions.Create(
                    bufferOptions: BufferOptions.NoBuffer())
            );
        }
    }

    [FunctionsStartup(typeof(Startup))]
    public class EchoFunction : IHttpFunction
    {
        private readonly ILogger<EchoFunction> _logger;
        private readonly IManagedTracer _tracer;

        public EchoFunction(ILogger<EchoFunction> logger, IManagedTracer tracer) 
        {
            _logger = logger;
            _tracer = tracer;
        }

        /// <summary>
        /// Echo function that returns the message it got
        /// </summary>
        /// <param name="context">The HTTP context, containing the request and the response.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleAsync(HttpContext context)
        {
            string text;
            using (StreamReader stream = new StreamReader(context.Request.Body))
            {
                text = await stream.ReadToEndAsync();
            }

            _logger.LogInformation($"{nameof(HandleAsync)} - Echo function running");
            _logger.LogInformation($"{nameof(HandleAsync)} - Google TraceID: {_tracer.GetCurrentTraceId()}");
            _logger.LogInformation($"{nameof(HandleAsync)} - Got message: {text}");

            using (_tracer.StartSpan($"{nameof(EchoFunction)}.{nameof(HandleAsync)}.Processing"))
            {
                _logger.LogInformation($"{nameof(HandleAsync)} - Processing...");
                await Task.Delay(100);
            }
            await context.Response.WriteAsync(text);
        }
    }
}
