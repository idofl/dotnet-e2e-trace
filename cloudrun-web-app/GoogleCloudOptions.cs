using Google.Cloud.Diagnostics.AspNetCore;

namespace GoogleCloudSamples.EndToEndTracing.WebApp
{
    public class GoogleCloudOptions
    {    
        public const string Section = "GoogleCloud"; 
        public class DiagnosticsOptions {
            public const string Section = "Diagnostics";
            public string ProjectId { get; set; }

            public string ServiceName { get; set; }

            public string Version { get; set; }

        }

        public DiagnosticsOptions Diagnostics { get; set; }
        public string TopicId { get; set; }

        public string TopicProjectId { get; set; }

        public string EchoFunctionUrl { get; set; }
    }
}