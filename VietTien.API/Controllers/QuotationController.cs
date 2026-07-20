using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using VietTien.API.DTOs.Quotation;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class QuotationController : ControllerBase
    {
        private readonly IQuotationService _quotationService;

        public QuotationController(IQuotationService quotationService)
        {
            _quotationService = quotationService;
        }

        private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        private string GetUserRole() => User.FindFirstValue(ClaimTypes.Role)!;

        [HttpPost("from-cart")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> CreateQuotationFromCart([FromBody] CreateQuotationRequest request)
        {
            try
            {
                var result = await _quotationService.CreateQuotationFromCartAsync(GetUserId(), request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetQuotations()
        {
            try
            {
                var role = GetUserRole();
                var userId = GetUserId();

                if (role == "Customer")
                {
                    return Ok(await _quotationService.GetCustomerQuotationsAsync(userId));
                }
                else if (role == "SalesStaff")
                {
                    var myQuotations = await _quotationService.GetSalesQuotationsAsync(userId);
                    var pendingQuotations = await _quotationService.GetAllPendingQuotationsAsync();
                    
                    return Ok(new
                    {
                        MyQuotations = myQuotations,
                        PendingQuotations = pendingQuotations
                    });
                }
                else if (role == "SalesManager")
                {
                    return Ok(await _quotationService.GetAllQuotationsAsync());
                }
                else if (role == "CEO" || role == "Admin")
                {
                    return Ok(await _quotationService.GetAllQuotationsAsync());
                }

                return Forbid();
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetQuotationById(Guid id)
        {
            try
            {
                return Ok(await _quotationService.GetQuotationByIdAsync(id, GetUserId(), GetUserRole()));
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/pickup")]
        [Authorize(Roles = "SalesStaff")]
        public async Task<IActionResult> PickUpQuotation(Guid id)
        {
            try
            {
                return Ok(await _quotationService.PickUpQuotationAsync(id, GetUserId()));
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/versions")]
        [Authorize(Roles = "SalesStaff")]
        public async Task<IActionResult> CreateVersion(Guid id, [FromBody] CreateQuotationVersionRequest request)
        {
            try
            {
                return Ok(await _quotationService.CreateVersionAsync(id, GetUserId(), request));
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/manager-decision")]
        [Authorize(Roles = "SalesManager")]
        public async Task<IActionResult> ManagerDecision(Guid id, [FromBody] ManagerReviewRequest request)
        {
            try
            {
                return Ok(await _quotationService.ManagerReviewVersionAsync(id, GetUserId(), request));
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/ceo-decision")]
        [Authorize(Roles = "CEO")]
        public async Task<IActionResult> CeoDecision(Guid id, [FromBody] CeoReviewRequest request)
        {
            try
            {
                return Ok(await _quotationService.CeoReviewVersionAsync(id, GetUserId(), request));
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/customer-decision")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> CustomerDecision(Guid id, [FromBody] CustomerDecisionRequest request)
        {
            try
            {
                return Ok(await _quotationService.CustomerDecisionAsync(id, GetUserId(), request));
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/cancel")]
        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> CancelQuotation(Guid id)
        {
            try
            {
                return Ok(await _quotationService.CancelQuotationAsync(id, GetUserId()));
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // --- CHAT API ---
        [HttpGet("{id}/messages")]
        public async Task<IActionResult> GetMessages(Guid id)
        {
            try
            {
                return Ok(await _quotationService.GetMessagesAsync(id, GetUserId(), GetUserRole()));
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id}/messages")]
        public async Task<IActionResult> SendMessage(Guid id, [FromBody] SendChatMessageRequest request)
        {
            try
            {
                return Ok(await _quotationService.SendMessageAsync(id, GetUserId(), request));
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
