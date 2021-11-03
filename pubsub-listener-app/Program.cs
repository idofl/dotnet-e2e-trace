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

        /// <summary>
        /// Configure the host for Google Cloud-related diagnostics 
        /// (Logging, Tracing, and Error Reporting)
        /// </summary>
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Worker>();
                    services.AddScoped<IScopedProcessingService, PubSubProcessingService>();

                    // Get Google Cloud-related configuration
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
                    // In GCP-hosted platforms (CR, GCF, GKE) there is a host 
                    // agent that sends stdout logs to Cloud Logging. In these
                    // platforms, we replace the Cloud Logging provider
                    // with a console logger and a formatter that implements 
                    // the structured logging JSON format.
                    // https://cloud.google.com/logging/docs/structured-logging
                    // In all other plaforms (other clouds, on-prem) we use the 
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
                                    options.GetSpanID = () => 
                                        $"{(ContextTracerManager.GetCurrentTracer().GetCurrentSpanId()):x16}";
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
