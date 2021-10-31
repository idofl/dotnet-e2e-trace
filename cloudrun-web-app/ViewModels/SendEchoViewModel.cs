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

namespace GoogleCloudSamples.EndToEndTracing.WebApp.ViewModels
{

    /// <summary>
    /// View model for the <see cref="HomeController"/>'s <see cref="SendEcho"/> view
    /// </summary>
    public class SendEchoViewModel
    {
        public IEnumerable<KeyValuePair<string,string>> TraceInformation { get; set; }
        public IEnumerable<KeyValuePair<string,IEnumerable<string>>> IncomingRequestHeaders { get; set; }
        public IEnumerable<KeyValuePair<string,IEnumerable<string>>> EchoRequestHeaders { get; set; }
        public IEnumerable<KeyValuePair<string,IEnumerable<string>>> EchoResponseHeaders { get; set; }

        public IEnumerable<KeyValuePair<string,string>> PubSubInformation { get; set; }
    }
}
