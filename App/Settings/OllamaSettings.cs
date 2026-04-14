namespace MovieAgentCLI.Settings
{
    public class OllamaSettings
    {
        public string Endpoint { get; set; } = "http://localhost:11434/v1";
        public string ModelId { get; set; } = "qwen2.5:3b";
    }
}
