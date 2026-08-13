namespace VietTien.API.Services.Implementations
{
    /// <summary>
    /// Kiểm tra magic byte (file signature) thật của nội dung file, không dựa vào phần mở rộng tên
    /// file — thứ do chính người gửi tự đặt nên không đáng tin (L3-SEC-14: file PE/EXE đổi đuôi .png
    /// vẫn lọt qua nếu chỉ kiểm Path.GetExtension). Dùng chung cho avatar (UserProfileService) và
    /// media bài marketing (MarketingPostController).
    /// </summary>
    public static class FileSignatureValidator
    {
        private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF };
        private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static readonly byte[] RiffPrefix = { 0x52, 0x49, 0x46, 0x46 }; // "RIFF"
        private static readonly byte[] WebpMarker = { 0x57, 0x45, 0x42, 0x50 }; // "WEBP" ở offset 8

        /// <summary>
        /// Đọc tối đa 16 byte đầu của stream và xác nhận đúng là JPEG/PNG/WEBP. Stream được reset về
        /// vị trí ban đầu (Position = 0) sau khi đọc để lời gọi tiếp theo (vd upload lên Cloudinary)
        /// vẫn đọc được đầy đủ nội dung file.
        /// </summary>
        public static async Task<bool> IsAllowedImageAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            if (!stream.CanSeek)
                throw new InvalidOperationException("Stream phải seek được để kiểm tra magic byte.");

            var header = new byte[16];
            stream.Position = 0;
            var bytesRead = await stream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
            stream.Position = 0;

            if (bytesRead < 3) return false;

            if (StartsWith(header, Jpeg)) return true;
            if (bytesRead >= Png.Length && StartsWith(header, Png)) return true;
            if (bytesRead >= 12 && StartsWith(header, RiffPrefix) && Matches(header, WebpMarker, 8)) return true;

            return false;
        }

        private static bool StartsWith(byte[] data, byte[] signature) => Matches(data, signature, 0);

        private static bool Matches(byte[] data, byte[] signature, int offset)
        {
            if (data.Length < offset + signature.Length) return false;
            for (var i = 0; i < signature.Length; i++)
            {
                if (data[offset + i] != signature[i]) return false;
            }
            return true;
        }
    }
}
