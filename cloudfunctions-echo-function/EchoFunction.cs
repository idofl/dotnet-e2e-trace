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

using Google.Cloud.Functions.Framework;
using Google.Cloud.Functions.Hosting;
// [START dotnet_distributed_diagnostics_function_using]
using Google.Cloud.Diagnostics.Common;
using Google.Cloud.Diagnostics.AspNetCore;
using Microsoft.AspNetCore.Builder;
// [END dotnet_distributed_diagnostics_function_using]
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
    /// <summary>
    /// Extend the function's host and service configurations
    /// </summary>
    public class Startup : FunctionsStartup
    {
        /// <summary>
        /// Add Google Diagnostics (Logging, Tracing, and Error reporting) 
        /// services and middleware.
        /// </summary>
        /// <param name="context">The context for the web host being built.</param>
        /// <param name="services">The service collection to configure.</param>
        public override void ConfigureServices(
            WebHostBuilderContext context, 
            IServiceCollection services) 
        {
            var googleCloudOptions = new GoogleCloudOptions();

            context.Configuration
                .GetSection(GoogleCloudOptions.Section)
                .Bind(googleCloudOptions);

            // [START dotnet_distributed_diagnostics_function_add_service]
            services.AddGoogleDiagnosticsForAspNetCore(
                googleCloudOptions.Diagnostics.ProjectId,
                googleCloudOptions.Diagnostics.ServiceName,
                googleCloudOptions.Diagnostics.Version,
                TraceOptions.Create(
                    bufferOptions: BufferOptions.NoBuffer())
            );
            // [END dotnet_distributed_diagnostics_function_add_service]
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
            _logger.LogInformation($"{nameof(HandleAsync)} - Request headers:");
            foreach (var item in context.Request.Headers)
            {
                _logger.LogInformation($"{item.Key}: {item.Value}");
            }

            using (_tracer.StartSpan($"{nameof(EchoFunction)}.{nameof(HandleAsync)}.Processing"))
            {
                _logger.LogInformation($"{nameof(HandleAsync)} - Processing...");
                await Task.Delay(100);
            }
            await context.Response.WriteAsync(text);
        }
    }
}
