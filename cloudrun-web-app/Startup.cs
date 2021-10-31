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
using Google.Cloud.Diagnostics.AspNetCore;
using Google.Cloud.Diagnostics.Common;
using Microsoft.AspNetCore.Http;

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
        public void ConfigureDevelopmentServices(IServiceCollection services)
        {
            ConfigureServices(services);

            // In production, the load balancer adds the traceparent
            // and x-cloud-trace-context headers with the same trace ID, 
            // causing Google Trace and .NET Activity to use the same 
            // trace ID.
            // In development the request from the browser doesn't include
            // any tracing headers, causing each trace mechanism to 
            // have its own trace ID.
            // This code runs uses either a given traceparent header
            // or the .NET Activity trace ID to set the Google Cloud Trace
            // Trace ID
            services.AddScoped(CustomTraceContextProvider);
            static ITraceContext CustomTraceContextProvider(IServiceProvider sp)
                {
                    var accessor = sp.GetRequiredService<IHttpContextAccessor>();

                    // Attempt to use a given traceparent header if it was provided
                    string traceId = accessor.HttpContext?.Request?.Headers["traceparent"] ??
                        accessor.HttpContext?.Request?.Headers[TraceHeaderContext.TraceHeader];

                    // If the header doesn't exist, use the current Activity
                    // trace ID (if the header exists, the header and
                    // the Activity trace ID are the same)
                    if (traceId == null)
                    {
                        traceId = System.Diagnostics.Activity.Current.TraceId.ToHexString();
                    }
                    return new SimpleTraceContext(traceId, null, null);
                }
        }

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
            services.AddGoogleDiagnosticsForAspNetCore(
                googleCloudOptions.ProjectId,
                googleCloudOptions.Diagnostics.ServiceName,
                googleCloudOptions.Diagnostics.Version,
                TraceOptions.Create(
                    bufferOptions: BufferOptions.NoBuffer())
            );

            // https://docs.microsoft.com/en-us/aspnet/core/fundamentals/http-requests?view=aspnetcore-5.0#consumption-patterns
            services.AddHttpClient("EchoFunction", c => 
            {
                c.BaseAddress = new Uri(googleCloudOptions.EchoFunctionUrl);
            });
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
