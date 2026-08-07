using System.ComponentModel.DataAnnotations;

namespace VietTien.API.DTOs.Review
{
    /// <summary>Đánh giá sản phẩm trả về cho danh sách công khai trên trang sản phẩm.</summary>
    public class ProductReviewDto
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public string? ProductName { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public int Rating { get; set; }
        public string Comment { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        /// <summary>Phản hồi công khai từ Sales phụ trách/Admin (null nếu chưa có).</summary>
        public string? ReplyText { get; set; }
        public string? RepliedByName { get; set; }
        public DateTime? RepliedAt { get; set; }
    }

    /// <summary>Điểm trung bình và số lượt đánh giá của 1 sản phẩm.</summary>
    public class ProductReviewSummaryDto
    {
        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }
    }

    /// <summary>Cho biết khách hàng hiện tại có được viết đánh giá cho sản phẩm này không.</summary>
    public class ReviewEligibilityDto
    {
        public bool CanReview { get; set; }
        public bool AlreadyReviewed { get; set; }
        public Guid? ExistingReviewId { get; set; }
    }

    public class CreateReviewRequest
    {
        [Range(1, 5, ErrorMessage = "Số sao đánh giá phải từ 1 đến 5.")]
        public int Rating { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập nội dung đánh giá.")]
        [MaxLength(2000, ErrorMessage = "Nội dung đánh giá không được vượt quá 2000 ký tự.")]
        public string Comment { get; set; } = string.Empty;
    }

    public class UpdateReviewRequest
    {
        [Range(1, 5, ErrorMessage = "Số sao đánh giá phải từ 1 đến 5.")]
        public int Rating { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập nội dung đánh giá.")]
        [MaxLength(2000, ErrorMessage = "Nội dung đánh giá không được vượt quá 2000 ký tự.")]
        public string Comment { get; set; } = string.Empty;
    }

    /// <summary>Sales phụ trách/Admin phản hồi công khai 1 đánh giá.</summary>
    public class ReplyReviewRequest
    {
        [Required(ErrorMessage = "Vui lòng nhập nội dung phản hồi.")]
        [MaxLength(2000, ErrorMessage = "Nội dung phản hồi không được vượt quá 2000 ký tự.")]
        public string ReplyText { get; set; } = string.Empty;
    }
}
