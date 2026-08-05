using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VietTien.API.Data;
using VietTien.API.DTOs.Marketing;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class AiGeneratorService : IAiGeneratorService
    {
        private readonly ApplicationDbContext _context;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AiGeneratorService> _logger;

        public AiGeneratorService(
            ApplicationDbContext context,
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<AiGeneratorService> logger)
        {
            _context = context;
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<GenerateAiContentResponseDto> GenerateMarketingOptionsAsync(GenerateAiContentRequestDto request)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId);
            if (product == null)
            {
                throw new Exception("Sản phẩm không tồn tại.");
            }

            if (product.IsDiscontinued)
            {
                throw new Exception("Sản phẩm đã ngừng kinh doanh, không thể tạo bài quảng cáo.");
            }

            var priceFormatted = product.StandardListedPrice.ToString("N0") + " VNĐ/" + product.Unit;
            var geminiApiKey = _configuration["GeminiSettings:ApiKey"];

            var options = new List<MarketingOptionDto>();

            // Thử gọi Gemini AI nếu có API Key
            if (!string.IsNullOrWhiteSpace(geminiApiKey))
            {
                try
                {
                    options = await GenerateOptionsWithGeminiAsync(product, request, priceFormatted, geminiApiKey);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gọi Gemini AI thất bại, chuyển sang bộ sinh nội dung AI dự phòng.");
                }
            }

            // Nếu Gemini không có Key hoặc lỗi, dùng bộ sinh AI tự động theo template + Pollinations AI Image
            if (options.Count == 0)
            {
                options = GenerateFallbackOptions(product, request, priceFormatted);
            }

            return new GenerateAiContentResponseDto
            {
                ProductId = product.Id,
                ProductName = product.Name,
                ProductSku = product.Sku,
                ProductPrice = product.StandardListedPrice,
                ProductUnit = product.Unit,
                Options = options
            };
        }

        private async Task<List<MarketingOptionDto>> GenerateOptionsWithGeminiAsync(
            Models.Product product,
            GenerateAiContentRequestDto request,
            string priceFormatted,
            string apiKey)
        {
            var options = new List<MarketingOptionDto>();
            var geminiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={apiKey}";

            var systemPrompt = $@"Bạn là chuyên gia Copywriter Marketing cho Công ty Bao Bì Việt Tiến. 
Hãy viết 3 phương án bài đăng Facebook quảng cáo cho sản phẩm sau đây:
- Tên sản phẩm: {product.Name}
- Mã SKU: {product.Sku}
- Giá niêm yết: {priceFormatted} (BẮT BUỘC giữ nguyên giá này, KHÔNG ĐƯỢC thay đổi)
- Đơn vị: {product.Unit}
- Quy cách: {product.Specifications ?? "Đạt chuẩn ISO chất lượng cao"}
- Mô tả: {product.Description ?? "Vật tư đóng gói cao cấp"}
- Yêu cầu từ người dùng: {request.Prompt}
- Tone giọng: {request.Tone ?? "Hào hứng"}
- Template: {request.TemplateName ?? "Khuyến mãi"}

Yêu cầu trả về đúng định dạng JSON Array chứa 3 object, mỗi object có 4 trường:
1. caption: Nội dung bài viết sinh động, hấp dẫn, trình bày đẹp mắt có emoji.
2. hashtags: Các hashtag phù hợp bắt đầu bằng dấu #.
3. ctaText: Nút kêu gọi hành động (VD: 📩 Nhắn tin ngay để báo giá sỉ!).
4. imagePrompt: Từ khóa tiếng Anh ngắn để tạo ảnh AI (VD: professional packaging photography of bubble wrap, warehouse background).

Trả về DUY NHẤT chuỗi JSON Array (không kèm markdown ```json).";

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = systemPrompt }
                        }
                    }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(geminiUrl, content);

            if (response.IsSuccessStatusCode)
            {
                var jsonStr = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonStr);
                var text = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString() ?? "";

                text = text.Replace("```json", "").Replace("```", "").Trim();
                using var parsedDoc = JsonDocument.Parse(text);

                int id = 1;
                foreach (var element in parsedDoc.RootElement.EnumerateArray())
                {
                    var caption = element.GetProperty("caption").GetString() ?? "";
                    var hashtags = element.GetProperty("hashtags").GetString() ?? "";
                    var ctaText = element.GetProperty("ctaText").GetString() ?? "";
                    var imgPrompt = element.GetProperty("imagePrompt").GetString() ?? $"{product.Name} packaging warehouse";

                    // Sinh ảnh bằng Pollinations.AI API với mô hình FLUX
                    var encodedPrompt = UrlEncoder.Default.Encode($"{imgPrompt}, commercial photo, 4k");
                    var aiImageUrl = $"https://image.pollinations.ai/prompt/{encodedPrompt}?model=flux&width=800&height=800&nologo=true&seed={id * 1234}";

                    options.Add(new MarketingOptionDto
                    {
                        Id = id++,
                        ImageUrl = !string.IsNullOrEmpty(product.ImageUrl) ? product.ImageUrl : aiImageUrl,
                        Caption = caption,
                        Hashtags = hashtags,
                        CtaText = ctaText
                    });
                }
            }

            return options;
        }

        private List<MarketingOptionDto> GenerateFallbackOptions(
            Models.Product product,
            GenerateAiContentRequestDto request,
            string priceFormatted)
        {
            var options = new List<MarketingOptionDto>();

            // Xây dựng Prompt sinh ảnh AI bằng mô hình FLUX dựa trên tên, thông số và ảnh gốc sản phẩm
            var productImgRef = !string.IsNullOrEmpty(product.ImageUrl) ? $" referencing original product photo {product.ImageUrl}" : "";
            var prompt1 = UrlEncoder.Default.Encode($"high quality commercial advertising photo of {product.Name}, {product.Specifications}, packaging supply studio lighting, 4k{productImgRef}");
            var prompt2 = UrlEncoder.Default.Encode($"industrial B2B showcase banner for {product.Name}, factory warehouse background, 8k{productImgRef}");
            var prompt3 = UrlEncoder.Default.Encode($"promotional sale banner for packaging material {product.Name}, professional product photography{productImgRef}");

            var aiImg1 = !string.IsNullOrEmpty(product.ImageUrl)
                ? product.ImageUrl
                : $"https://image.pollinations.ai/prompt/{prompt1}?model=flux&width=800&height=800&nologo=true&seed=101";

            var aiImg2 = $"https://image.pollinations.ai/prompt/{prompt2}?model=flux&width=800&height=800&nologo=true&seed=202";
            var aiImg3 = $"https://image.pollinations.ai/prompt/{prompt3}?model=flux&width=800&height=800&nologo=true&seed=303";

            // Option 1: Khuyến mại Hấp Dẫn (Dùng ảnh gốc sản phẩm nếu có)
            options.Add(new MarketingOptionDto
            {
                Id = 1,
                ImageUrl = aiImg1,
                Caption = $"✨ [BÙNG NỔ ƯU ĐÃI] {product.Name.ToUpper()} - GIÁ CHỈ {priceFormatted}! ✨\n\n" +
                          $"🔥 Bạn đang tìm kiếm giải pháp vật tư đóng gói chất lượng cao cho doanh nghiệp? {product.Name} chính là lựa chọn hoàn hảo!\n\n" +
                          $"📌 Điểm nổi bật:\n" +
                          $"✔️ Mã SKU: {product.Sku}\n" +
                          $"✔️ Chất lượng tiêu chuẩn nhà máy Việt Tiến, độ bền và tính ứng dụng vượt trội.\n" +
                          $"✔️ {product.Description ?? "Đảm bảo đóng gói an toàn, nâng tầm giá trị thương hiệu của bạn."}\n\n" +
                          $"💬 Nhanh tay nhắn tin hoặc gọi Hotline để nhận ưu đãi chiết khấu sỉ tốt nhất hôm nay!",
                Hashtags = $"#{product.Sku} #VietTien #VatTuDongGoi #{product.Name.Replace(" ", "")} #KhuyenMaiHot",
                CtaText = "📩 Nhắn tin ngay để báo giá chiết khấu sỉ!"
            });

            // Option 2: B2B Chuyên Nghiệp (Ảnh AI Studio dựa trên sản phẩm)
            options.Add(new MarketingOptionDto
            {
                Id = 2,
                ImageUrl = aiImg2,
                Caption = $"🏆 DÒNG SẢN PHẨM CAO CẤP: {product.Name.ToUpper()} (MÃ SKU: {product.Sku})\n\n" +
                          $"Công ty Bao Bì Việt Tiến tự hào cung cấp giải pháp đóng gói chuyên nghiệp cho các doanh nghiệp và kho vận toàn quốc.\n\n" +
                          $"Thông số kỹ thuật:\n" +
                          $"- Giá niêm yết: {priceFormatted}\n" +
                          $"- Đơn vị tính: {product.Unit}\n" +
                          $"- Quy cách: {product.Specifications ?? "Đạt chuẩn ISO chất lượng cao"}\n\n" +
                          $"Cam kết cung ứng nguồn hàng ổn định, hỗ trợ xuất hóa đơn VAT và giao hàng tận nơi nhanh chóng.",
                Hashtags = $"#VietTienPackaging #BaoBiViettien #{product.Sku} #B2BSolutions #VatTuKho",
                CtaText = "📞 Hotline tư vấn doanh nghiệp: 1900 6789 - Liên hệ ngay!"
            });

            // Option 3: Kích Cầu Nhanh (Ảnh AI Kho Vận dựa trên sản phẩm)
            options.Add(new MarketingOptionDto
            {
                Id = 3,
                ImageUrl = aiImg3,
                Caption = $"⚡ BẠN ĐANG CẦN {product.Name.ToUpper()} SỐ LƯỢNG LỚN TẠI KHO?\n\n" +
                          $"📦 Nguồn hàng có sẵn số lượng lớn tại các kho Việt Tiến, sẵn sàng giao ngay trong ngày!\n" +
                          $"💰 Giá niêm yết cạnh tranh nhất thị trường: chỉ {priceFormatted}.\n\n" +
                          $"Đừng để thiếu hụt vật tư làm gián đoạn chuỗi cung ứng của bạn. Đặt hàng ngay hôm nay để nhận trợ giá vận chuyển!",
                Hashtags = $"#{product.Sku} #GiaoHangNhanh #KhoDongGoi #BaoBiGiaTot #VietTien",
                CtaText = "🛒 Đặt hàng trực tiếp qua Fanpage hoặc Website!"
            });

            return options;
        }
    }
}
