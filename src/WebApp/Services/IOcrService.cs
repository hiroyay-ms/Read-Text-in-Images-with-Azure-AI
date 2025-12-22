using Microsoft.AspNetCore.Http;
using WebApp.Models;

namespace WebApp.Services;

public interface IOcrService
{
    Task<OcrResult> ExtractTextAsync(IFormFile imageFile);
    Task<bool> ValidateImageAsync(IFormFile imageFile);
}
