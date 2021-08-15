using System.Collections.Generic;
using System.Diagnostics;
using Google.Cloud.Diagnostics.Common;

namespace dotnet_listener_service
{
    public static class IManagedTracerExtensions {
          public static ISpan StartSpanEx(this IManagedTracer tracer, string name, StartSpanOptions options = null)
          {
            var span = tracer.StartSpan(name, options);
            span.AnnotateSpan(new Dictionary<string,string>(Activity.Current.Baggage));
            return span;
          }
    }
}