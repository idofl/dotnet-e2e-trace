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

        public void ConfigureDevelopmentServices(IServiceCollection services)
        {
            ConfigureServices(services);

            // In production, the load balancer will inject the traceparent
            // header with a trace ID, causing Google Trace and ASP.NET 
            // Activity to use the same trace ID.
            // In development the request from the browser doesn't include
            // any tracing headers and this will each trace mechanism to 
            // have its own trace ID.
            // This code applies the ASP.NET Activity trace ID to Google Trace
            services.AddScoped(CustomTraceContextProvider);
            static ITraceContext CustomTraceContextProvider(IServiceProvider sp)
                {
                    var accessor = sp.GetRequiredService<IHttpContextAccessor>();

                    string traceId = accessor.HttpContext?.Request?.Headers["traceparent"] ??
                        accessor.HttpContext?.Request?.Headers[TraceHeaderContext.TraceHeader];

                    if (traceId == null)
                    {
                        traceId = System.Diagnostics.Activity.Current.TraceId.ToHexString();
                    }
                    return new SimpleTraceContext(traceId, null, null);
                }
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllersWithViews();

            var googleCloudConfiguration = Configuration.GetSection(GoogleCloudOptions.Section);
            services.Configure<GoogleCloudOptions>(googleCloudConfiguration);

            var googleCloudOptions = new GoogleCloudOptions();
            googleCloudConfiguration.Bind(googleCloudOptions);

            // Add Tracing, Logging, and Error Reporting configuration and middleware
            services.AddGoogleDiagnosticsForAspNetCore(
                googleCloudOptions.Diagnostics.ProjectId,
                googleCloudOptions.Diagnostics.ServiceName,
                googleCloudOptions.Diagnostics.Version,
                TraceOptions.Create(
                    bufferOptions: BufferOptions.NoBuffer())
            );

            // No need to use AddOutgoingGoogleTraceHandler, as we rely on Activity to add the `traceparent` header
            // https://docs.microsoft.com/en-us/aspnet/core/fundamentals/http-requests?view=aspnetcore-5.0#consumption-patterns
            services.AddHttpClient("echoService", c => 
            {
                c.BaseAddress = new Uri(googleCloudOptions.EchoFunctionUrl);
            });//.AddOutgoingGoogleTraceHandler();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)//, ILoggingBuilder loggingBuilder) //ILoggerFactory loggerFactory)
        {

            // loggingBuilder.AddGoogle(
            //     new LoggingServiceOptions {
            //         ProjectId = Configuration["GoogleCloud:ProjectId"]
            //     }
            // );
            //loggerFactory.AddGoogle(app.ApplicationServices, Configuration["GoogleCloud:ProjectId"]);

            //app.UseGoogleExceptionLogging();
            // Use at the start of the request pipeline to ensure the entire request is traced.
            //app.UseGoogleTrace();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();

            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
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
