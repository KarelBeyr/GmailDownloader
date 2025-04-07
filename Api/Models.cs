namespace Api;

public record SearchRequest(string Text);

public record SearchResult
{
    public string ThreadId { get; set; }
    public float Similarity { get; set; }
}
