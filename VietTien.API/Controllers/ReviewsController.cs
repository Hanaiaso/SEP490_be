using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Review;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [ApiController]
    [Produces("application/json")]
    public class ReviewsController : ControllerBase
    {
        private readonly IReviewService _reviewService;

        public ReviewsController(IReviewService reviewService)
        {
            _reviewService = reviewService;
        }

        private Guid GetUserId()
        {
            var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
                throw new UnauthorizedAccessException("Invalid user token.");
            return userId;
        }

        private string GetUserRole() => User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        /// <summary>Danh sách đánh giá công khai của 1 sản phẩm</summary>
        [HttpGet("/api/products/{productId:guid}/reviews")]
        public async Task<IActionResult> GetProductReviews([FromRoute] Guid productId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            var result = await _reviewService.GetProductReviewsAsync(productId, page, pageSize);
            return Ok(result);
        }

        /// <summary>Điểm trung bình và số lượt đánh giá của 1 sản phẩm</summary>
        [HttpGet("/api/products/{productId:guid}/reviews/summary")]
        public async Task<IActionResult> GetReviewSummary([FromRoute] Guid productId)
        {
            try
            {
                var result = await _reviewService.GetSummaryAsync(productId);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>Khách hàng đang đăng nhập có được đánh giá sản phẩm này không</summary>
        [HttpGet("/api/products/{productId:guid}/reviews/eligibility")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> GetEligibility([FromRoute] Guid productId)
        {
            try
            {
                var result = await _reviewService.GetEligibilityAsync(productId, GetUserId());
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        /// <summary>Khách hàng tạo đánh giá mới cho sản phẩm đã mua và nhận hàng thành công</summary>
        [HttpPost("/api/products/{productId:guid}/reviews")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> CreateReview([FromRoute] Guid productId, [FromBody] CreateReviewRequest request)
        {
            try
            {
                var result = await _reviewService.CreateReviewAsync(productId, GetUserId(), request);
                return CreatedAtAction(nameof(GetProductReviews), new { productId }, result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        /// <summary>Khách hàng sửa đánh giá của chính mình</summary>
        [HttpPut("/api/reviews/{reviewId:guid}")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> UpdateReview([FromRoute] Guid reviewId, [FromBody] UpdateReviewRequest request)
        {
            try
            {
                var result = await _reviewService.UpdateReviewAsync(reviewId, GetUserId(), request);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        /// <summary>Khách hàng xoá đánh giá của chính mình</summary>
        [HttpDelete("/api/reviews/{reviewId:guid}")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> DeleteReview([FromRoute] Guid reviewId)
        {
            try
            {
                await _reviewService.DeleteReviewAsync(reviewId, GetUserId());
                return NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }

        /// <summary>Danh sách đánh giá để Sales/Admin phản hồi. SalesStaff chỉ thấy đánh giá của khách mình phụ trách.</summary>
        [HttpGet("/api/reviews/for-sales")]
        [Authorize(Roles = "SalesStaff,Admin")]
        public async Task<IActionResult> GetReviewsForSales()
        {
            var result = await _reviewService.GetReviewsForSalesAsync(GetUserId(), GetUserRole());
            return Ok(result);
        }

        /// <summary>Sales phụ trách/Admin phản hồi công khai 1 đánh giá</summary>
        [HttpPost("/api/reviews/{reviewId:guid}/reply")]
        [Authorize(Roles = "SalesStaff,Admin")]
        public async Task<IActionResult> ReplyToReview([FromRoute] Guid reviewId, [FromBody] ReplyReviewRequest request)
        {
            try
            {
                var result = await _reviewService.ReplyToReviewAsync(reviewId, GetUserId(), GetUserRole(), request);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
        }
    }
}
