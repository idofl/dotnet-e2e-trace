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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
// [START dotnet_distributed_diagnostics_aspnet_using]
using Google.Cloud.Diagnostics.AspNetCore;
using Google.Cloud.Diagnostics.Common;
// [END dotnet_distributed_diagnostics_aspnet_using]
using Microsoft.AspNetCore.Http;
using System.Diagnostics;

namespace GoogleCloudSamples.EndToEndTracing.WebApp
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        /// <summary>
        /// Configre a custom trace context that will be used in 
        /// development environments.
        /// </summary>
        // [START dotnet_distributed_diagnostics_development_trace_id]
        public void ConfigureDevelopmentServices(IServiceCollection services)
        {
            ConfigureServices(services);

            // Create a customized provider to initialize both Google Trace
            // and .NET Activity to the same trace ID. The trace ID is taken
            // from either the traceparent or x-cloud-trace-context HTTP header
            // with preference to the traceparent header.
            // If both headers are missing, use the .NET Activity trace ID.
            services.AddScoped(CustomTraceContextProvider);
            static ITraceContext CustomTraceContextProvider(IServiceProvider sp)
                {
                    var accessor = sp.GetRequiredService<IHttpContextAccessor>();
                    var googleTraceHeader = accessor.HttpContext?.Request?.Headers[TraceHeaderContext.TraceHeader];
                    var activityTraceHeader = accessor.HttpContext?.Request?.Headers["traceparent"];
                    var activityTraceId = Activity.Current.TraceId.ToHexString();
                    ITraceContext traceContext = null;

                    if (!string.IsNullOrEmpty(googleTraceHeader))
                    {
                        // Google Trace provided, use the header to create the
                        // trace context
                        traceContext = TraceHeaderContext.FromHeader(googleTraceHeader);

                        if (string.IsNullOrEmpty(activityTraceHeader))
                        {
                            // traceparent header not provided. Use Google Trace header
                            // to set the Activity's trace ID
                            Activity.Current.SetParentId(
                                ActivityTraceId.CreateFromString(traceContext.TraceId),
                                ActivitySpanId.CreateFromString(traceContext.SpanId.Value.ToString("x")));
                        }
                        else if (traceContext.TraceId != activityTraceId)
                        {
                            // Both Activity and Google Trace headers provided but have different values
                            // override Google Trace header with that of the activity
                            traceContext = new SimpleTraceContext(activityTraceId, null, null);
                        }
                    }
                    else
                    {
                        // Google Trace not provided. Use the Activity's trace ID
                        // to set a new Google Trace  (if traceparent header was 
                        // provided, Activity is already set to this value)
                        traceContext = new SimpleTraceContext(activityTraceId, null, null); 
                    }

                    return traceContext;
                }
        }
        // [END dotnet_distributed_diagnostics_development_trace_id]

        /// <summary>
        /// Configre the DI system with Google Cloud-related configuration
        /// and Google Cloud Diagnostics (Tracing, Logging, 
        /// and Error Reporting)
        /// </summary>
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllersWithViews();

            var googleCloudConfiguration = Configuration.GetSection(GoogleCloudOptions.Section);
            services.Configure<GoogleCloudOptions>(googleCloudConfiguration);

            var googleCloudOptions = new GoogleCloudOptions();
            googleCloudConfiguration.Bind(googleCloudOptions);

            // Add Tracing, Logging, and Error Reporting configuration and middleware
            // [START dotnet_distributed_diagnostics_aspnet_add_service]
            services.AddGoogleDiagnosticsForAspNetCore(
                googleCloudOptions.ProjectId,
                googleCloudOptions.Diagnostics.ServiceName,
                googleCloudOptions.Diagnostics.Version,
                Google.Cloud.Diagnostics.Common.TraceOptions.Create(
                    bufferOptions: BufferOptions.NoBuffer())
            );
            // [END dotnet_distributed_diagnostics_aspnet_add_service]

            // https://docs.microsoft.com/en-us/aspnet/core/fundamentals/http-requests?view=aspnetcore-5.0#consumption-patterns
            // [START dotnet_distributed_diagnostics_http_client_factory]
            services.AddHttpClient("EchoFunction", c => 
            {
                c.BaseAddress = new Uri(googleCloudOptions.EchoFunctionUrl);
            }).AddOutgoingGoogleTraceHandler();
            // [END dotnet_distributed_diagnostics_http_client_factory]
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change 
                // this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}
