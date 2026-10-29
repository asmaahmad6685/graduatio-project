using Microsoft.AspNetCore.Mvc;

namespace graduatio_project.Services
{
    //  حفظ الصور المرفوعة داخل wwwroot/uploads
    //  العقد: الحقل اسمه file، الحد الأقصى ٢ ميجابايت،
    //  والأنواع المقبولة jpg/jpeg/png/webp/gif، والاستجابة { url }
    public static class ImageStorage
    {
        private static readonly string[] Extensions =
            { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

        public const long MaxBytes = 2 * 1024 * 1024;

        //  يرجع خطأ 400 إذا كان الملف غير صالح، و null إذا كان صالحًا
        public static ObjectResult? Validate(IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                return ApiError.BadRequest("invalid_upload", "No file was uploaded.",
                    new { file = new[] { "A file is required." } });
            }

            if (file.Length > MaxBytes)
            {
                return ApiError.BadRequest("file_too_large",
                    "The file exceeds the 2 MB upload limit.");
            }

            var extension = Path.GetExtension(file.FileName ?? string.Empty)
                .ToLowerInvariant();

            if (!Extensions.Contains(extension))
            {
                return ApiError.BadRequest("unsupported_media_type",
                    "Only jpg, jpeg, png, webp and gif images are accepted.");
            }

            return null;
        }

        //  نحفظ الملف باسم عشوائي حتى لا يُخمَّن أحد مسار ملف آخر
        //  ونرجع رابطًا نسبيًا يضيفه الـ Frontend على أصل الـ API
        public static async Task<string> SaveAsync(IFormFile file, IWebHostEnvironment environment)
        {
            var webRoot = string.IsNullOrWhiteSpace(environment.WebRootPath)
                ? Path.Combine(environment.ContentRootPath, "wwwroot")
                : environment.WebRootPath;

            var uploads = Path.Combine(webRoot, "uploads");
            Directory.CreateDirectory(uploads);

            var extension = Path.GetExtension(file.FileName ?? string.Empty)
                .ToLowerInvariant();
            var fileName = $"{Guid.NewGuid():N}{extension}";

            await using var stream = File.Create(Path.Combine(uploads, fileName));
            await file.CopyToAsync(stream);

            return $"/uploads/{fileName}";
        }
    }
}