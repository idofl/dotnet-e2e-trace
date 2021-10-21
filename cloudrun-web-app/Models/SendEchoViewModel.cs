using System;
using System.Collections.Generic;

namespace GoogleCloudSamples.EndToEndTracing.WebApp.Models
{
    public class SendEchoViewModel
    {
        public IEnumerable<KeyValuePair<string,string>> TraceInformation { get; set; }
        public IEnumerable<KeyValuePair<string,IEnumerable<string>>> IncomingRequestHeaders { get; set; }
        public IEnumerable<KeyValuePair<string,IEnumerable<string>>> EchoRequestHeaders { get; set; }
        public IEnumerable<KeyValuePair<string,IEnumerable<string>>> EchoResponseHeaders { get; set; }

        public IEnumerable<KeyValuePair<string,string>> PubSubInformation { get; set; }
    }
}
