using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Diagnostics.Common;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Google.Cloud.Logging.Console;
using Google.Api.Gax;

namespace GoogleCloudSamples.EndToEndTracing.PubSubListener
{
    public class Program
    {
        readonly static GoogleCloudOptions _googleCloudOptions = new GoogleCloudOptions();

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

                    var googleCloudConfiguration = hostContext.Configuration.GetSection(GoogleCloudOptions.Section);
                    services.Configure<GoogleCloudOptions>(googleCloudConfiguration);
                    googleCloudConfiguration.Bind(_googleCloudOptions);

                    services.AddGoogleDiagnostics(
                        _googleCloudOptions.ProjectId,
                        _googleCloudOptions.Diagnostics.ServiceName,
                        _googleCloudOptions.Diagnostics.Version,
                        TraceOptions.Create(
                            bufferOptions: BufferOptions.NoBuffer())
                    );
                })
                .ConfigureLogging((hostBuilder, logging) =>
                {
                    // In GCP hosted platforms (CR, GCF, GKE) there is a host 
                    // agent that sends stdout logs to Cloud Logging. In these
                    // platforms, we replace the Cloud Logging provider
                    // with a console logger and a formatter that implements 
                    // the structured logging JSON format.
                    // https://cloud.google.com/logging/docs/structured-logging
                    // In all other plaforms (non-GCP / on-prem) we use the 
                    // already configured Google Cloud Logger provider.
                    if (Platform.Instance().Type != PlatformType.Unknown) 
                    {
                        // Remove the already configured GoogleLoggerProvider
                        var serviceDescriptor = logging.Services.FirstOrDefault(descriptor => 
                            descriptor.ServiceType == typeof(ILoggerProvider) &&
                            descriptor.ImplementationFactory is Func<IServiceProvider, GoogleLoggerProvider>);
                            
                        if (serviceDescriptor != null)
                            logging.Services.Remove(serviceDescriptor);

                        // Add the console logger with a Google Cloud structured Logging JSON schema
                        logging
                            .AddConsoleFormatter<GoogleCloudConsoleFormatter, GoogleCloudConsoleFormatterOptions>(
                                options => {
                                    options.IncludeScopes = true;
                                    options.GetSpanID = () => $"{(ContextTracerManager.GetCurrentTracer().GetCurrentSpanId()):x16}";
                                    options.GetTraceID = () => {
                                        var traceId = ContextTracerManager.GetCurrentTracer()?.GetCurrentTraceId();
                                        return (traceId != null) ? 
                                            TraceTarget.ForProject(_googleCloudOptions.ProjectId).GetFullTraceName(traceId) : null;
                                    };
                                })
                            .AddConsole(options => options.FormatterName = nameof(GoogleCloudConsoleFormatter));
                    }
                });
    }
}
