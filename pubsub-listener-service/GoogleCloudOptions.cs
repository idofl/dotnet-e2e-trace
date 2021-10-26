namespace GoogleCloudSamples.EndToEndTracing.PubSubListener
{
    public class GoogleCloudOptions
    {    
        public const string Section = "GoogleCloud"; 
        public string ProjectId { get; set; }
        public string SubscriptionId {get; set; }
        public class DiagnosticsOptions {
            public string ServiceName { get; set; }

            public string Version { get; set; }
        }
        public DiagnosticsOptions Diagnostics { get; set; }
    }
}