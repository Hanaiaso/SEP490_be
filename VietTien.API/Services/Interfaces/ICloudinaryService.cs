namespace VietTien.API.Services.Interfaces
{
    public interface ICloudinaryService
    {
        /// <summary>Upload ảnh lên Cloudinary và trả về URL</summary>
        Task<string> UploadImageAsync(IFormFile file, string folder);

        /// <summary>Upload ảnh dạng Base64 lên Cloudinary và trả về URL</summary>
        Task<string> UploadBase64ImageAsync(string base64String, string folder, string fileName);
      
        /// <summary>Upload ảnh bằng chứng (giữ nguyên kích thước, không crop) và trả về URL</summary>
        Task<string> UploadEvidenceAsync(IFormFile file, string folder);

        /// <summary>Upload file đính kèm đa định dạng: ảnh/PDF theo resource image, DOC/DOCX... theo resource raw</summary>
        Task<string> UploadAttachmentAsync(IFormFile file, string folder);

        /// <summary>Xóa ảnh trên Cloudinary theo publicId</summary>
        Task<bool> DeleteImageAsync(string publicId);

        /// <summary>Trích xuất publicId từ Cloudinary URL</summary>
        string? ExtractPublicId(string url);
    }
}
