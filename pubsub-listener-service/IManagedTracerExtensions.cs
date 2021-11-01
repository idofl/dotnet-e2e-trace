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

using System.Collections.Generic;
using System.Diagnostics;
using Google.Cloud.Diagnostics.Common;

namespace GoogleCloudSamples.EndToEndTracing.PubSubListener
{
  /// <summary>
  /// Extensions methods for <see cref="IManagedTracer"/>.
  /// </summary>
  public static class IManagedTracerExtensions {
    
    /// <summary>
    /// Create a new trace span and annotate the span
    /// with tags from the current .NET Diagnostics Activity
    /// </summary>
    /// <param name="name">The name of the span</param>
    /// <param name="options">The span options to override default values</param>
    /// <returns>
    /// An <see cref="ISpan"/> that will end the current span when disposed.
    /// </returns>
    public static ISpan StartSpanWithActivityTags(this IManagedTracer tracer, string name, StartSpanOptions options = null)
    {
      var span = tracer.StartSpan(name, options);
      if (Activity.Current != null && Activity.Current.Tags != null)
      {
        span.AnnotateSpan(new Dictionary<string,string>(Activity.Current.Tags));
      }
      return span;
    }
  }
}