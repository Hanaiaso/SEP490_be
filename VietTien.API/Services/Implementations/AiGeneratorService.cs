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

            if (string.IsNullOrWhiteSpace(geminiApiKey))
            {
                throw new Exception("Chưa cấu hình Gemini API Key (GeminiSettings:ApiKey). Vui lòng cấu hình API Key để sinh nội dung bằng AI.");
            }

            var options = await GenerateOptionsWithGeminiAsync(product, request, priceFormatted, geminiApiKey);

            if (options.Count == 0)
            {
                throw new Exception("Gemini AI không trả về phương án bài viết nào. Vui lòng kiểm tra lại prompt hoặc kết nối.");
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
            var geminiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";
            var userPrompt = string.IsNullOrWhiteSpace(request.Prompt) ? "Viết bài quảng cáo bán hàng hấp dẫn" : request.Prompt;

            var systemPrompt = $@"Bạn là chuyên gia Copywriter và Visual Director Marketing cho Công ty Sản Xuất Bao Bì Việt Tiến. 
Hãy viết 3 phương án bài đăng Facebook quảng cáo cho sản phẩm sau đây:
- Tên sản phẩm: {product.Name}
- Mã SKU: {product.Sku}
- Giá niêm yết: {priceFormatted} (BẮT BUỘC giữ nguyên giá này, KHÔNG ĐƯỢC thay đổi)
- Đơn vị: {product.Unit}
- Quy cách: {product.Specifications ?? "Đạt chuẩn ISO chất lượng cao"}
- Mô tả: {product.Description ?? "Vật tư đóng gói cao cấp Việt Tiến"}
- Yêu cầu từ người dùng: {userPrompt}
- Tone giọng: {request.Tone ?? "Hào hứng"}
- Template: {request.TemplateName ?? "Khuyến mãi"}

QUY TẮC QUAN TRỌNG VỀ ĐỊNH DẠNG:
- TUYỆT ĐỐI KHÔNG dùng ký hiệu Markdown như ** (dấu sao bôi đậm), ##, __ trong nội dung.
- Bài đăng Facebook phải là Plain Text chuẩn, tự nhiên.
- Dùng emoji (🚀, ✨, 📌, 📦, 💥) hoặc VIẾT IN HOA để nhấn mạnh thay vì dùng dấu **.

Yêu cầu trả về đúng định dạng JSON Array chứa 3 object, mỗi object có 4 trường:
1. caption: Nội dung bài viết sinh động, hấp dẫn, trình bày đẹp mắt có emoji (Plain Text, không có dấu **).
2. hashtags: Các hashtag phù hợp bắt đầu bằng dấu #.
3. ctaText: Nút kêu gọi hành động (VD: 📩 Nhắn tin ngay để báo giá sỉ!).
4. imagePrompt: Mô tả tiếng Anh thật chi tiết và CHÍNH XÁC về sản phẩm này để sinh ảnh AI quảng cáo thương mại.
   - Miêu tả đúng hình dạng và chủng loại vật liệu (vd: cuộn xốp hơi bubble wrap roll, thùng carton corrugated box, cuộn màng PE stretch film, băng dính opp tape, túi bóng niêm phong, v.v.).
   - Phong cách: Professional commercial product photography, studio softbox lighting, clean modern minimalist bright background, sharp focus, 8k resolution, highly detailed, photorealistic packaging product showcase.

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
                },
                generationConfig = new
                {
                    responseMimeType = "application/json"
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(geminiUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Gọi Gemini AI thất bại (Mã lỗi {response.StatusCode}): {errBody}");
            }

            var jsonStr = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonStr);
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? "";

            text = text.Replace("```json", "").Replace("```", "").Trim();
            var firstBracket = text.IndexOf('[');
            var lastBracket = text.LastIndexOf(']');
            if (firstBracket >= 0 && lastBracket > firstBracket)
            {
                text = text.Substring(firstBracket, lastBracket - firstBracket + 1);
            }

            using var parsedDoc = JsonDocument.Parse(text);

            int id = 1;
            foreach (var element in parsedDoc.RootElement.EnumerateArray())
            {
                var rawCaption = element.GetProperty("caption").GetString() ?? "";
                var caption = rawCaption.Replace("**", "").Replace("__", "").Trim();
                var hashtags = element.GetProperty("hashtags").GetString() ?? "";
                var rawCta = element.GetProperty("ctaText").GetString() ?? "";
                var ctaText = rawCta.Replace("**", "").Replace("__", "").Trim();
                var imgPrompt = element.GetProperty("imagePrompt").GetString() ?? "";
                var cleanPrompt = !string.IsNullOrWhiteSpace(imgPrompt)
                    ? imgPrompt
                    : GetEnglishPackagingPrompt(product);

                // Sinh ảnh bằng Pollinations.AI API với mô hình FLUX nâng cao (1024x1024 + enhance=true)
                var enhancedPrompt = $"{cleanPrompt}, hyperrealistic commercial product photography, 8k resolution, crisp sharp focus, soft studio lighting, clean white studio background, packaging supply product, no humans, no people, no face, no anime";
                var encodedPrompt = UrlEncoder.Default.Encode(enhancedPrompt);
                var seed = Random.Shared.Next(10000, 999999);
                var aiImageUrl = $"https://image.pollinations.ai/prompt/{encodedPrompt}?width=1024&height=1024&nologo=true&seed={seed}";

                options.Add(new MarketingOptionDto
                {
                    Id = id++,
                    ImageUrl = !string.IsNullOrEmpty(product.ImageUrl) ? product.ImageUrl : aiImageUrl,
                    Caption = caption,
                    Hashtags = hashtags,
                    CtaText = ctaText
                });
            }

            return options;
        }

        public static string GetEnglishPackagingPrompt(Models.Product product)
        {
            var nameLower = (product.Name ?? "").ToLowerInvariant();
            var skuLower = (product.Sku ?? "").ToLowerInvariant();

            return TranslateToEnglishPackaging($"{nameLower} {skuLower}");
        }

        public static string TranslateToEnglishPackaging(string text)
        {
            var lower = (text ?? "").ToLowerInvariant();

            if (lower.Contains("đục") || lower.Contains("brown") || lower.Contains("tape-br") || lower.Contains("vàng đục"))
            {
                return "a clean stack of 6 rolls of brown tan OPP carton box sealing packing tape, heavy-duty parcel packaging tape rolls on modern white studio surface";
            }
            if (lower.Contains("trong") || lower.Contains("clear") || lower.Contains("transparent") || lower.Contains("tape-tr"))
            {
                return "stacked rolls of clear transparent glossy OPP packing tape, adhesive shipping tape rolls on bright clean studio table";
            }
            if (lower.Contains("in logo") || lower.Contains("logo"))
            {
                return "commercial printed custom logo packaging tape rolls stacked neatly, corporate branded carton sealing tape";
            }
            if (lower.Contains("băng keo") || lower.Contains("băng dính") || lower.Contains("tape"))
            {
                return "stack of industrial adhesive shipping packing tape rolls, carton packaging supplies";
            }
            if (lower.Contains("xốp hơi") || lower.Contains("bóng khí") || lower.Contains("bubble"))
            {
                return "large industrial roll of clear plastic air bubble wrap cushioning packaging material, bubble film roll in modern studio";
            }
            if (lower.Contains("xốp pe") || lower.Contains("pe foam") || lower.Contains("foam"))
            {
                return "large white rolls of EPE foam sheet cushioning packaging material, shockproof protective packaging roll";
            }
            if (lower.Contains("quấn pallet") || lower.Contains("màng pe") || lower.Contains("stretch") || lower.Contains("film"))
            {
                return "rolls of industrial transparent plastic stretch wrap film for pallet wrapping, logistics packing supplies";
            }
            if (lower.Contains("thùng carton") || lower.Contains("hộp carton") || lower.Contains("box"))
            {
                return "neat stack of clean brown corrugated cardboard shipping carton boxes, commercial packaging boxes";
            }
            if (lower.Contains("túi") || lower.Contains("nilon") || lower.Contains("niêm phong") || lower.Contains("mailer") || lower.Contains("bag"))
            {
                return "neat stack of durable plastic poly mailer packaging bags and express parcel courier bags";
            }
            if (lower.Contains("cắt băng keo") || lower.Contains("dụng cụ") || lower.Contains("tool") || lower.Contains("cut"))
            {
                return "handheld metal industrial packaging tape dispenser gun tool with ergonomic handle, tape cutter";
            }

            return "commercial industrial packaging supply materials, parcel shipping products on clean studio white background";
        }
    }
}
