using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietTien.API.DTOs.Customer;
using VietTien.API.Models;
using VietTien.API.Repositories.Interfaces;
using VietTien.API.Data;
using Microsoft.EntityFrameworkCore;

namespace VietTien.API.Controllers
{
    [ApiController]
    [Route("api/customer-profile")]
    [Authorize]
    [Produces("application/json")]
    public class CustomerProfileController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ApplicationDbContext _context;

        public CustomerProfileController(IUnitOfWork unitOfWork, ApplicationDbContext context)
        {
            _unitOfWork = unitOfWork;
            _context = context;
        }

        /// <summary>Lấy thông tin MST của khách hàng hiện tại</summary>
        [HttpGet]
        [ProducesResponseType(typeof(CustomerProfileDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetProfile()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
            {
                return Unauthorized(new { message = "Không xác định được người dùng." });
            }

            var profile = await _unitOfWork.Users.GetCustomerProfileByUserIdAsync(userId);
            if (profile == null)
            {
                // Trả về DTO trống đại diện cho profile chưa có thông tin
                return Ok(new CustomerProfileDto());
            }

            return Ok(new CustomerProfileDto
            {
                TaxCode = profile.TaxCode,
                CompanyName = profile.CompanyName,
                CompanyAddress = profile.CompanyAddress,
                InvoiceEmail = profile.InvoiceEmail,
                Representative = profile.Representative,
                CompanyPhone = profile.CompanyPhone,
                AvailableCredit = profile.AvailableCredit
            });
        }

        /// <summary>Kiểm tra hồ sơ đã đầy đủ chưa (có ít nhất 1 địa chỉ giao hàng) để mở khoá mua hàng</summary>
        [HttpGet("status")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetProfileStatus()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
            {
                return Unauthorized(new { message = "Không xác định được người dùng." });
            }

            var profile = await _unitOfWork.Users.GetCustomerProfileByUserIdAsync(userId);
            var hasAddress = profile != null
                && await _unitOfWork.Addresses.CountByCustomerProfileIdAsync(profile.Id) > 0;

            return Ok(new { isProfileCompleted = hasAddress, hasAddress });
        }

        /// <summary>Cập nhật hoặc tạo mới thông tin MST của khách hàng hiện tại</summary>
        [HttpPut]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> UpdateProfile([FromBody] CustomerProfileDto dto)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
            {
                return Unauthorized(new { message = "Không xác định được người dùng." });
            }

            var profile = await _unitOfWork.Users.GetCustomerProfileByUserIdAsync(userId);
            bool isNew = false;

            if (profile == null)
            {
                profile = new CustomerProfile
                {
                    UserId = userId
                };
                isNew = true;
            }

            profile.TaxCode = dto.TaxCode;
            profile.CompanyName = dto.CompanyName;
            profile.CompanyAddress = dto.CompanyAddress;
            profile.InvoiceEmail = dto.InvoiceEmail;
            profile.Representative = dto.Representative;
            profile.CompanyPhone = dto.CompanyPhone;

            if (isNew)
            {
                await _unitOfWork.Users.AddCustomerProfileAsync(profile);
            }

            await _unitOfWork.SaveChangesAsync();

            return Ok(new { message = "Cập nhật thông tin MST thành công." });
        }

        /// <summary>Lấy hồ sơ khách hàng theo số điện thoại (dành cho Sales POS)</summary>
        [HttpGet("by-phone/{phone}")]
        [Authorize(Roles = "SalesStaff,Admin")]
        public async Task<IActionResult> GetProfileByPhone(string phone)
        {
            var profile = await _unitOfWork.Users.GetCustomerProfileByPhoneAsync(phone);
            if (profile == null)
            {
                return NotFound(new { message = "Không tìm thấy hồ sơ khách hàng." });
            }

            return Ok(new CustomerProfileDto
            {
                TaxCode = profile.TaxCode,
                CompanyName = profile.CompanyName,
                CompanyAddress = profile.CompanyAddress,
                InvoiceEmail = profile.InvoiceEmail,
                Representative = profile.Representative,
                CompanyPhone = profile.CompanyPhone
            });
        }

        [HttpGet("credit-history")]
        [ProducesResponseType(typeof(List<CreditTransactionDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCreditHistory()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("sub");
            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
            {
                return Unauthorized(new { message = "Không xác định được người dùng." });
            }

            var profile = await _unitOfWork.Users.GetCustomerProfileByUserIdAsync(userId);
            if (profile == null)
            {
                return Ok(new List<CreditTransactionDto>());
            }

            var history = await _context.CreditTransactions
                .Where(ct => ct.CustomerProfileId == profile.Id)
                .OrderByDescending(ct => ct.CreatedAt)
                .Select(ct => new CreditTransactionDto
                {
                    Id = ct.Id,
                    Amount = ct.Amount,
                    Description = ct.Description,
                    CreatedAt = ct.CreatedAt,
                    OrderId = ct.OrderId
                })
                .ToListAsync();

            return Ok(history);
        }
    }
}
