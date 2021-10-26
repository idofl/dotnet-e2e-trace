namespace GoogleCloudSamples.EndToEndTracing.Function
{
    public class GoogleCloudOptions
    {    
        public const string Section = "GoogleCloud"; 
        public class DiagnosticsOptions {
            public string ServiceName { get; set; }
            public string Version { get; set; }
            public string ProjectId { get; set; }
        }
        public DiagnosticsOptions Diagnostics { get; set; }
    }
}