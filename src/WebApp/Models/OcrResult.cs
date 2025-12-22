namespace WebApp.Models;

public class OcrResult
{
    public bool Success { get; set; }
    public string? ExtractedText { get; set; }
    public List<TextLine> Lines { get; set; } = new();
    public int PageCount { get; set; }
    public string? Language { get; set; }
    public double ConfidenceScore { get; set; }
}

public class TextLine
{
    public string Text { get; set; } = string.Empty;
    public double Confidence { get; set; }
}
