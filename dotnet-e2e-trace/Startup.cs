using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Google.Cloud.Diagnostics.AspNetCore;
using Google.Cloud.Diagnostics.Common;
using Google.Cloud.Trace.V1;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace dotnet_e2e_trace
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {            
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllersWithViews();
            
            Google.Cloud.Diagnostics.AspNetCore.CloudTraceExtension.AddGoogleTrace(services, options =>
            //services.AddGoogleTrace(options =>
            {
                options.ProjectId = Configuration["Tracing:ProjectId"];
                options.Options = TraceOptions.Create(
                    bufferOptions: BufferOptions.NoBuffer());
            });
            
            services.AddGoogleExceptionLogging(options =>
            {
                options.ProjectId = Configuration["Tracing:ProjectId"];
                options.ServiceName = Configuration["Tracing:ServiceName"];
                options.Version = Configuration["Tracing:Version"];
            });

            
            // https://docs.microsoft.com/en-us/aspnet/core/fundamentals/http-requests?view=aspnetcore-5.0#consumption-patterns
            // No need to use AddOutgoingGoogleTraceHandler, as we rely on Activity to add the traceparent header
            services.AddHttpClient("echoService", c => 
            {
                c.BaseAddress = new Uri("http://localhost:8080/echo");
                //c.BaseAddress = new Uri("https://localhost:5004/echo");
                //c.BaseAddress = new Uri("https://us-central1-idoflatow-devenv.cloudfunctions.net/echo");
            }).AddOutgoingGoogleTraceHandler();

        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddGoogle(app.ApplicationServices, Configuration["Tracing:ProjectId"]);
            app.UseGoogleExceptionLogging();
            // Use at the start of the request pipeline to ensure the entire request is traced.
            app.UseGoogleTrace();

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
