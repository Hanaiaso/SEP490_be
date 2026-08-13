using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Marketing;
using VietTien.API.Exceptions;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [Route("api/marketing-posts")]
    [ApiController]
    public class MarketingPostController : ControllerBase
    {
        private readonly IMarketingPostService _marketingPostService;
        private readonly IAiGeneratorService _aiGeneratorService;
        private readonly IConfiguration _configuration;

        public MarketingPostController(
            IMarketingPostService marketingPostService,
            IAiGeneratorService aiGeneratorService,
            IConfiguration configuration)
        {
            _marketingPostService = marketingPostService;
            _aiGeneratorService = aiGeneratorService;
            _configuration = configuration;
        }

        [HttpPost("generate-options")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> GenerateOptions([FromBody] GenerateAiContentRequestDto request)
        {
            try
            {
                var result = await _aiGeneratorService.GenerateMarketingOptionsAsync(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("generate-image")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public IActionResult GenerateImage([FromBody] GenerateImageRequestDto request)
        {
            try
            {
                var rawPrompt = request?.Prompt ?? "";
                var englishDesc = VietTien.API.Services.Implementations.AiGeneratorService.TranslateToEnglishPackaging(rawPrompt);

                var enhancedPrompt = $"{englishDesc}, hyperrealistic commercial product photography, 8k resolution, soft studio lighting, sharp focus, clean white studio background, packaging supply product, no humans, no people, no face, no anime";
                var encodedPrompt = System.Text.Encodings.Web.UrlEncoder.Default.Encode(enhancedPrompt);
                var seed = Random.Shared.Next(10000, 999999);
                var imageUrl = $"https://image.pollinations.ai/prompt/{encodedPrompt}?width=1024&height=1024&nologo=true&seed={seed}";

                return Ok(new { imageUrl });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetPosts([FromQuery] string? status, [FromQuery] Guid? productId)
        {
            try
            {
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
                var userId = string.IsNullOrEmpty(userIdStr) ? Guid.Empty : Guid.Parse(userIdStr);

                var result = await _marketingPostService.GetPostsAsync(status, productId, role, userId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{id}")]
        [Authorize]
        public async Task<IActionResult> GetPostById(Guid id)
        {
            try
            {
                var result = await _marketingPostService.GetPostByIdAsync(id);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        [HttpPost]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> CreatePost([FromBody] CreateMarketingPostDto dto)
        {
            try
            {
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userIdStr)) return Unauthorized();

                var result = await _marketingPostService.CreatePostAsync(dto, Guid.Parse(userIdStr));
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> UpdatePost(Guid id, [FromBody] UpdateMarketingPostDto dto)
        {
            try
            {
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userIdStr)) return Unauthorized();
                var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

                var result = await _marketingPostService.UpdatePostAsync(id, dto, Guid.Parse(userIdStr), role);
                return Ok(result);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/submit")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> SubmitPost(Guid id)
        {
            try
            {
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userIdStr)) return Unauthorized();
                var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

                var result = await _marketingPostService.SubmitPostAsync(id, Guid.Parse(userIdStr), role);
                return Ok(result);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/decision")]
        [Authorize(Roles = "SalesManager,Admin,CEO")]
        public async Task<IActionResult> MakeDecision(Guid id, [FromBody] MarketingPostDecisionDto dto)
        {
            try
            {
                var managerIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(managerIdStr)) return Unauthorized();

                var result = await _marketingPostService.MakeDecisionAsync(id, dto, Guid.Parse(managerIdStr));
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/media")]
        [Authorize(Roles = "SalesStaff,SalesManager,Admin")]
        public async Task<IActionResult> UploadMedia(Guid id, IFormFile? file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Vui lòng chọn file media." });

            try
            {
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userIdStr)) return Unauthorized();
                var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

                var result = await _marketingPostService.UploadMediaAsync(id, file, Guid.Parse(userIdStr), role);
                return Ok(result);
            }
            catch (MediaTypeUnsupportedException ex)
            {
                return StatusCode(StatusCodes.Status415UnsupportedMediaType, new { code = "MEDIA_TYPE_UNSUPPORTED", message = ex.Message });
            }
            catch (MediaTooLargeException ex)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge, new { code = "MEDIA_TOO_LARGE", message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{id}/metrics")]
        [Authorize]
        public async Task<IActionResult> GetMetrics(Guid id)
        {
            try
            {
                var result = await _marketingPostService.GetMetricsAsync(id);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/webhook-callback")]
        [AllowAnonymous]
        public async Task<IActionResult> MakeWebhookCallback(Guid id, [FromBody] MakeWebhookCallbackDto dto)
        {
            if (!IsValidMakeSecret(out var authError)) return authError!;

            try
            {
                var result = await _marketingPostService.HandleMakeWebhookCallbackAsync(id, dto);
                return Ok(new { success = true, post = result });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // L3-MKT-11 (hướng Make.com — Business Portfolio của người vận hành bị Facebook cấm chia sẻ
        // App nên không tự gọi Graph API trực tiếp được): Make.com giữ kết nối Facebook riêng của nó
        // (đã dùng để publish), nên để Make.com tự tra insights rồi gửi kết quả về đây thay vì backend
        // tự gọi Facebook. 2 endpoint dưới cùng cơ chế xác thực x-make-secret với webhook-callback.

        [HttpGet("for-metrics-sync")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPostsForMetricsSync()
        {
            if (!IsValidMakeSecret(out var authError)) return authError!;

            var result = await _marketingPostService.GetPostsForMetricsSyncAsync();
            return Ok(result);
        }

        [HttpPost("{id}/metrics-callback")]
        [AllowAnonymous]
        public async Task<IActionResult> MetricsCallback(Guid id, [FromBody] MarketingMetricsCallbackDto dto)
        {
            if (!IsValidMakeSecret(out var authError)) return authError!;

            try
            {
                var result = await _marketingPostService.UpdateMetricsFromCallbackAsync(id, dto);
                return Ok(new { success = true, post = result });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // Chỉ Make.com (server-to-server) mới được gọi các callback trên — không có secret thì bất kỳ
        // ai cũng giả mạo được kết quả đăng bài / số liệu cho post bất kỳ.
        private bool IsValidMakeSecret(out IActionResult? errorResult)
        {
            var expectedSecret = _configuration["MakeCom:CallbackSecret"];
            var isDevelopment = string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(expectedSecret) || !isDevelopment)
            {
                var providedSecret = Request.Headers["x-make-secret"].FirstOrDefault();
                if (string.IsNullOrEmpty(providedSecret) || providedSecret != expectedSecret)
                {
                    errorResult = Unauthorized(new { success = false, message = "Missing or invalid callback secret." });
                    return false;
                }
            }

            errorResult = null;
            return true;
        }
    }
}
