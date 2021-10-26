using System.Collections.Generic;
using System.Diagnostics;
using Google.Cloud.Diagnostics.Common;

namespace GoogleCloudSamples.EndToEndTracing.PubSubListener
{
    public static class IManagedTracerExtensions {
          // Copy the baggage from the current Activity to the new trace span
          public static ISpan StartSpanWithActivityTags(this IManagedTracer tracer, string name, StartSpanOptions options = null)
          {
            var span = tracer.StartSpan(name, options);
            span.AnnotateSpan(new Dictionary<string,string>(Activity.Current.Tags));
            return span;
          }
    }
}