namespace WebApp.Models;

public class FileUploadOptions
{
    public int MaxFileSizeMB { get; set; } = 10;
    public List<string> AllowedExtensions { get; set; } = new()
    {
        ".jpg", ".jpeg", ".png", ".pdf", ".tiff", ".tif", ".bmp"
    };
}
