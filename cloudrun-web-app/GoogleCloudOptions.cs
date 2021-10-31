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

namespace GoogleCloudSamples.EndToEndTracing.WebApp
{
    /// <summary>
    /// Google Cloud-related configuration
    /// </summary>
    public class GoogleCloudOptions
    {
        public const string Section = "GoogleCloud"; 
        public class DiagnosticsOptions {
            public string ServiceName { get; set; }
            public string Version { get; set; }
        }

        public string ProjectId { get; set; }
        public DiagnosticsOptions Diagnostics { get; set; }
        public string TopicId { get; set; }
        public string EchoFunctionUrl { get; set; }
    }
}