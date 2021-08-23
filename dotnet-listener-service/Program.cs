using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Diagnostics.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Google.Cloud.Logging.Console;
using Google.Api.Gax;

namespace dotnet_listener_service
{
    public class Program
    {
        private static string ProjectId = "idoflatow-devenv";
        
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Worker>();
                    services.AddScoped<IScopedProcessingService, PubSubProcessingService>();

                    services.AddGoogleTrace(options =>
                    {
                        options.ProjectId = ProjectId;
                        options.Options = TraceOptions.Create(
                            bufferOptions: BufferOptions.NoBuffer());
                    });
                })
                // https://github.com/googleapis/google-cloud-dotnet/issues/6367
                .ConfigureLogging((hostBuilder, logging) =>
                {
                    //logging.AddProvider(GoogleLoggerProvider.Create(serviceProvider: null, ProjectId));
                    
                    logging
                        .AddConsoleFormatter<GoogleCloudConsoleFormatter, GoogleCloudConsoleFormatterOptions>(
                            options => {
                                options.IncludeScopes = true;                                
                                options.GetSpanID = () => $"{(ContextTracerManager.GetCurrentTracer().GetCurrentSpanId()):x16}";
                                options.GetTraceID = () => {
                                    var traceId = ContextTracerManager.GetCurrentTracer()?.GetCurrentTraceId();
                                    return (traceId != null) ? 
                                        TraceTarget.ForProject(ProjectId).GetFullTraceName(traceId) : null;
                                };
                            })
                        .AddConsole(options => options.FormatterName = nameof(GoogleCloudConsoleFormatter));
                    
                    // For GCP hosted platforms (CR, GCF, GKE) we can rely on 
                    // custom console logging and the logging agent of the host
                    // For non-hosted platform, we'll use the logging library
                    if (Platform.Instance().Type == PlatformType.Unknown) 
                        logging.AddProvider(GoogleLoggerProvider.Create(serviceProvider: null, ProjectId));
                    Console.WriteLine(Platform.Instance().ToString());
                });
    }
}
