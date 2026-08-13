using VietTien.API.DTOs.Quotation;
using VietTien.API.Models;
using VietTien.API.Repositories.Interfaces;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class QuotationService : IQuotationService
    {
        private readonly IQuotationRepository _quotationRepo;
        private readonly ICartRepository _cartRepo;
        private readonly IUserRepository _userRepo;
        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationService _notificationService;

        public QuotationService(
            IQuotationRepository quotationRepo,
            ICartRepository cartRepo,
            IUserRepository userRepo,
            IUnitOfWork unitOfWork,
            INotificationService notificationService)
        {
            _quotationRepo = quotationRepo;
            _cartRepo = cartRepo;
            _userRepo = userRepo;
            _unitOfWork = unitOfWork;
            _notificationService = notificationService;
        }

        public async Task<QuotationDto> CreateQuotationFromCartAsync(Guid userId, CreateQuotationRequest request)
        {
            var profile = await _userRepo.GetCustomerProfileByUserIdAsync(userId);
            if (profile == null) throw new KeyNotFoundException("Customer profile not found");

            var cart = await _cartRepo.GetCartByCustomerIdAsync(profile.Id);
            if (cart == null || !cart.Items.Any()) throw new Exception("Cart is empty");

            var originalTotal = cart.Items.Sum(i => i.Quantity * i.Product.StandardListedPrice);
            if (originalTotal < 100000000)
                throw new Exception("Đơn hàng phải từ 100 triệu trở lên để đàm phán giá.");

            var quotation = new Quotation
            {
                CustomerProfileId = profile.Id,
                CartId = cart.Id,
                GeneralNote = request.GeneralNote,
                Status = QuotationStatus.Draft,
                RequestDate = DateTime.UtcNow,
                OriginalTotal = originalTotal
            };

            foreach (var item in cart.Items)
            {
                quotation.Items.Add(new QuotationItem
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    OriginalUnitPrice = item.Product.StandardListedPrice
                });
            }

            await _quotationRepo.CreateAsync(quotation);
            await _unitOfWork.SaveChangesAsync();

            if (profile.AssignedSalesStaffId != null)
            {
                // Báo giá đã được lưu thành công ở trên -> lỗi gửi notification không được
                // làm fail request tạo báo giá, chỉ log để theo dõi.
                try
                {
                    await _notificationService.CreateNotificationAsync(
                        NotificationType.SYS_16_NewQuotationRequest,
                        profile.AssignedSalesStaffId.Value,
                        "Yêu cầu báo giá mới",
                        $"Khách hàng {profile.User.FullName} vừa tạo yêu cầu báo giá trị giá {originalTotal:N0}đ.",
                        quotation.Id,
                        "Quotation"
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[QuotationService] Error sending new quotation request notification: {ex.Message}");
                }
            }

            return await GetQuotationByIdAsync(quotation.Id, userId, "Customer");
        }

        public async Task<QuotationDto> GetQuotationByIdAsync(Guid quotationId, Guid userId, string userRole)
        {
            var q = await _quotationRepo.GetByIdAsync(quotationId);
            if (q == null) throw new KeyNotFoundException("Quotation not found");

            if (userRole == "Customer" && q.CustomerProfile.UserId != userId)
                throw new UnauthorizedAccessException("Unauthorized to view this quotation");
            if (userRole == "SalesStaff" && q.SalesStaffId != null && q.SalesStaffId != userId)
                throw new InvalidOperationException("Assigned to another sales staff");

            return MapToDto(q);
        }

        public async Task<IEnumerable<QuotationDto>> GetCustomerQuotationsAsync(Guid userId)
        {
            var profile = await _userRepo.GetCustomerProfileByUserIdAsync(userId);
            if (profile == null) return new List<QuotationDto>();

            var list = await _quotationRepo.GetByCustomerProfileIdAsync(profile.Id);
            return list.Select(MapToDto);
        }

        public async Task<IEnumerable<QuotationDto>> GetSalesQuotationsAsync(Guid userId)
        {
            var list = await _quotationRepo.GetBySalesStaffIdAsync(userId);
            return list.Select(MapToDto);
        }

        public async Task<IEnumerable<QuotationDto>> GetAllPendingQuotationsAsync()
        {
            var list = await _quotationRepo.GetAllPendingAsync();
            return list.Select(MapToDto);
        }

        public async Task<IEnumerable<QuotationDto>> GetAllQuotationsAsync()
        {
            var list = await _quotationRepo.GetAllAsync();
            return list.Select(MapToDto);
        }

        public async Task<IEnumerable<QuotationDto>> GetManagerPendingApprovalQuotationsAsync()
        {
            var list = await _quotationRepo.GetManagerPendingApprovalAsync();
            return list.Select(MapToDto);
        }

        public async Task<IEnumerable<QuotationDto>> GetCeoPendingApprovalQuotationsAsync()
        {
            var list = await _quotationRepo.GetCeoPendingApprovalAsync();
            return list.Select(MapToDto);
        }

        public async Task<QuotationDto> PickUpQuotationAsync(Guid quotationId, Guid salesStaffId)
        {
            var q = await _quotationRepo.GetByIdAsync(quotationId);
            if (q == null) throw new KeyNotFoundException("Quotation not found");

            if (q.SalesStaffId != null) throw new InvalidOperationException("Already picked up by another sales staff");

            q.SalesStaffId = salesStaffId;
            q.Status = QuotationStatus.Negotiating;

            await _quotationRepo.UpdateAsync(q);
            await _unitOfWork.SaveChangesAsync();

            return MapToDto(q);
        }

        public async Task<QuotationVersionDto> CreateVersionAsync(Guid quotationId, Guid salesStaffId, CreateQuotationVersionRequest request)
        {
            var q = await _quotationRepo.GetByIdAsync(quotationId);
            if (q == null) throw new KeyNotFoundException("Quotation not found");
            if (q.SalesStaffId != salesStaffId) throw new UnauthorizedAccessException("Unauthorized");
            if (q.Status == QuotationStatus.CustomerAccepted || q.Status == QuotationStatus.Expired || q.Status == QuotationStatus.Cancelled)
                throw new InvalidOperationException("Cannot create version for this quotation state");

            int newVersionNum = q.Versions.Any() ? q.Versions.Max(v => v.VersionNumber) + 1 : 1;

            var version = new QuotationVersion
            {
                QuotationId = quotationId,
                VersionNumber = newVersionNum,
                ProposedTotal = request.ProposedTotal,
                SalesNote = request.SalesNote,
                Status = QuotationVersionStatus.PendingManager,
                CreatedByUserId = salesStaffId,
                CreatedAt = DateTime.UtcNow
            };

            foreach (var reqItem in request.Items)
            {
                var qItem = q.Items.FirstOrDefault(i => i.ProductId == reqItem.ProductId);
                if (qItem == null) throw new Exception($"Product {reqItem.ProductId} not in original quotation");

                version.Items.Add(new QuotationVersionItem
                {
                    ProductId = reqItem.ProductId,
                    Quantity = qItem.Quantity,
                    OriginalUnitPrice = qItem.OriginalUnitPrice,
                    ProposedUnitPrice = reqItem.ProposedUnitPrice
                });
            }

            // Manager/CEO duyệt dựa trên ProposedTotal, nhưng tiền thực tính khi đặt hàng lại dựa theo
            // ProposedUnitPrice từng dòng (OrderService.CalculateDiscountAsync) — nếu 2 số này lệch nhau,
            // người duyệt tưởng đang chốt 1 mức giá nhưng hệ thống lại áp dụng đơn giá khác. Bắt khớp
            // tuyệt đối (VND không có đơn vị nhỏ hơn 1 đồng) trước khi cho tạo version.
            var computedTotal = version.Items.Sum(i => i.ProposedUnitPrice * i.Quantity);
            if (request.ProposedTotal != computedTotal)
                throw new Exception(
                    $"ProposedTotal ({request.ProposedTotal:N0}đ) không khớp tổng đơn giá từng dòng x số lượng ({computedTotal:N0}đ).");

            await _quotationRepo.CreateVersionAsync(version);

            q.Status = QuotationStatus.PendingManager;
            await _quotationRepo.UpdateAsync(q);

            await _unitOfWork.SaveChangesAsync();

            // Phiên bản báo giá đã được lưu thành công ở trên -> lỗi gửi notification không được
            // làm fail request gửi báo giá, chỉ log để theo dõi.
            try
            {
                await _notificationService.CreateRoleNotificationAsync(
                    NotificationType.SYS_17_QuotationPendingApproval,
                    SystemRole.SalesManager,
                    "Báo giá cần duyệt",
                    $"Sale vừa gửi báo giá chờ duyệt (tổng đề xuất: {request.ProposedTotal:N0}đ).",
                    quotationId,
                    "Quotation"
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[QuotationService] Error sending quotation pending approval notification: {ex.Message}");
            }

            return MapToVersionDto(version);
        }

        public async Task<QuotationVersionDto> ManagerReviewVersionAsync(Guid quotationId, Guid managerId, ManagerReviewRequest request)
        {
            var q = await _quotationRepo.GetByIdAsync(quotationId);
            if (q == null) throw new KeyNotFoundException("Quotation not found");

            var version = q.Versions.FirstOrDefault(v => v.Status == QuotationVersionStatus.PendingManager);
            if (version == null) throw new InvalidOperationException("No version pending manager review");

            if (version.Status != QuotationVersionStatus.PendingManager)
                throw new InvalidOperationException("Version is not pending manager review");

            version.ManagerNote = request.ManagerNote;
            version.ManagerApprovedByUserId = managerId;
            version.ManagerReviewedAt = DateTime.UtcNow;

            if (request.IsApproved)
            {
                version.Status = QuotationVersionStatus.PendingCeo;
                q.Status = QuotationStatus.PendingCeo;
            }
            else
            {
                version.Status = QuotationVersionStatus.ManagerRejected;
                q.Status = QuotationStatus.Negotiating;
            }

            await _quotationRepo.UpdateVersionAsync(version);
            await _quotationRepo.UpdateAsync(q);
            await _unitOfWork.SaveChangesAsync();

            return MapToVersionDto(version);
        }

        public async Task<QuotationVersionDto> CeoReviewVersionAsync(Guid quotationId, Guid ceoId, CeoReviewRequest request)
        {
            var q = await _quotationRepo.GetByIdAsync(quotationId);
            if (q == null) throw new KeyNotFoundException("Quotation not found");

            var version = q.Versions.FirstOrDefault(v => v.Status == QuotationVersionStatus.PendingCeo);
            if (version == null) throw new InvalidOperationException("No version pending CEO review");

            if (version.Status != QuotationVersionStatus.PendingCeo)
                throw new InvalidOperationException("Version is not pending CEO review");

            version.CeoNote = request.CeoNote;
            version.CeoApprovedByUserId = ceoId;
            version.CeoReviewedAt = DateTime.UtcNow;

            if (request.IsApproved)
            {
                version.Status = QuotationVersionStatus.CeoApproved;
                version.ValidUntil = DateTime.UtcNow.AddDays(7); // Báo giá có hiệu lực 7 ngày sau khi duyệt
                q.Status = QuotationStatus.Approved;
                q.ValidUntil = version.ValidUntil;
            }
            else
            {
                version.Status = QuotationVersionStatus.CeoRejected;
                q.Status = QuotationStatus.Negotiating;
            }

            await _quotationRepo.UpdateVersionAsync(version);
            await _quotationRepo.UpdateAsync(q);
            await _unitOfWork.SaveChangesAsync();

            return MapToVersionDto(version);
        }

        private const int MaxNegotiationRounds = 5;

        public async Task<QuotationVersionDto> CustomerDecisionAsync(Guid quotationId, Guid customerId, CustomerDecisionRequest request)
        {
            var q = await _quotationRepo.GetByIdAsync(quotationId);
            if (q == null || q.CustomerProfile.UserId != customerId) throw new UnauthorizedAccessException("Unauthorized or Quotation not found");

            var version = q.Versions.FirstOrDefault(v => v.Status == QuotationVersionStatus.CeoApproved);
            if (version == null) throw new InvalidOperationException("No approved version for customer decision");

            if (version.Status != QuotationVersionStatus.CeoApproved)
                throw new InvalidOperationException("Version is not approved by CEO");

            if (version.ValidUntil.HasValue && version.ValidUntil.Value < DateTime.UtcNow)
            {
                version.Status = QuotationVersionStatus.Expired;
                q.Status = QuotationStatus.Expired;
                await _quotationRepo.UpdateVersionAsync(version);
                await _quotationRepo.UpdateAsync(q);
                await _unitOfWork.SaveChangesAsync();
                throw new InvalidOperationException("Quotation version has expired");
            }

            if (request.IsAccepted)
            {
                version.Status = QuotationVersionStatus.CustomerAccepted;
                q.Status = QuotationStatus.CustomerAccepted;
                q.AcceptedVersionId = version.Id;
            }
            else
            {
                version.Status = QuotationVersionStatus.CustomerRejected;

                // Giới hạn tối đa 5 vòng đàm phán (BV-03): từ chối ở vòng thứ 5 trở đi -> đóng báo giá,
                // không cho tạo thêm vòng mới (CreateVersionAsync đã chặn version mới khi Status=Cancelled).
                if (version.VersionNumber >= MaxNegotiationRounds)
                {
                    q.Status = QuotationStatus.Cancelled;
                }
                else
                {
                    q.Status = QuotationStatus.Negotiating;
                }
            }

            await _quotationRepo.UpdateVersionAsync(version);
            await _quotationRepo.UpdateAsync(q);
            await _unitOfWork.SaveChangesAsync();

            if (q.Status == QuotationStatus.Cancelled)
            {
                // Báo giá đã bị đóng và lưu thành công ở trên -> lỗi gửi notification không được
                // làm fail request, chỉ log để theo dõi.
                try
                {
                    await _notificationService.CreateRoleNotificationAsync(
                        NotificationType.SYS_27_QuotationNegotiationLimitReached,
                        SystemRole.SalesManager,
                        "Báo giá đạt giới hạn đàm phán",
                        $"Báo giá đã bị khách từ chối ở vòng {version.VersionNumber}, vượt giới hạn {MaxNegotiationRounds} vòng đàm phán và đã tự động đóng.",
                        quotationId,
                        "Quotation"
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[QuotationService] Error sending negotiation limit reached notification: {ex.Message}");
                }
            }

            return MapToVersionDto(version);
        }

        public async Task<QuotationDto> CancelQuotationAsync(Guid quotationId, Guid customerId)
        {
            var q = await _quotationRepo.GetByIdAsync(quotationId);
            if (q == null || q.CustomerProfile.UserId != customerId) throw new UnauthorizedAccessException("Unauthorized or Quotation not found");

            if (q.Status == QuotationStatus.CustomerAccepted)
                throw new InvalidOperationException("Cannot cancel an accepted quotation");

            q.Status = QuotationStatus.Cancelled;
            await _quotationRepo.UpdateAsync(q);
            await _unitOfWork.SaveChangesAsync();

            return MapToDto(q);
        }

        public async Task<ChatMessageDto> SendMessageAsync(Guid quotationId, Guid senderId, SendChatMessageRequest request)
        {
            var q = await _quotationRepo.GetByIdAsync(quotationId);
            if (q == null) throw new KeyNotFoundException("Quotation not found");

            if (q.CustomerProfile.UserId != senderId && q.SalesStaffId != senderId)
                throw new UnauthorizedAccessException("Bạn không phải là người tham gia báo giá này, không thể gửi tin nhắn.");

            var msg = new ChatMessage
            {
                QuotationId = quotationId,
                SenderId = senderId,
                MessageText = request.MessageText,
                FileUrl = request.FileUrl,
                SentAt = DateTime.UtcNow,
                IsRead = false
            };

            await _quotationRepo.AddMessageAsync(msg);
            
            var savedMsg = await _quotationRepo.GetMessagesByQuotationIdAsync(quotationId);
            var theMsg = savedMsg.Last();

            return new ChatMessageDto
            {
                Id = theMsg.Id,
                QuotationId = theMsg.QuotationId,
                SenderId = theMsg.SenderId,
                SenderName = theMsg.Sender.FullName,
                SenderRole = theMsg.Sender.Role.ToString(),
                MessageText = theMsg.MessageText,
                FileUrl = theMsg.FileUrl,
                SentAt = theMsg.SentAt,
                IsRead = false
            };
        }

        public async Task<IEnumerable<ChatMessageDto>> GetMessagesAsync(Guid quotationId, Guid userId, string userRole)
        {
            var q = await _quotationRepo.GetByIdAsync(quotationId);
            if (q == null) throw new KeyNotFoundException("Quotation not found");

            var isParticipant = q.CustomerProfile.UserId == userId || q.SalesStaffId == userId;
            var isPrivileged = userRole is "SalesManager" or "CEO" or "Admin";
            if (!isParticipant && !isPrivileged)
                throw new UnauthorizedAccessException("Bạn không có quyền xem hội thoại của báo giá này.");

            var msgs = await _quotationRepo.GetMessagesByQuotationIdAsync(quotationId);
            return msgs.Select(m => new ChatMessageDto
            {
                Id = m.Id,
                QuotationId = m.QuotationId,
                SenderId = m.SenderId,
                SenderName = m.Sender.FullName,
                SenderRole = m.Sender.Role.ToString(),
                MessageText = m.MessageText,
                FileUrl = m.FileUrl,
                SentAt = m.SentAt,
                IsRead = m.IsRead
            });
        }

        private QuotationDto MapToDto(Quotation q)
        {
            return new QuotationDto
            {
                Id = q.Id,
                CustomerProfileId = q.CustomerProfileId,
                SalesStaffId = q.SalesStaffId,
                CartId = q.CartId,
                AcceptedVersionId = q.AcceptedVersionId,
                CustomerName = q.CustomerProfile?.User?.FullName ?? "Unknown",
                SalesStaffName = q.SalesStaff?.FullName,
                Status = q.Status.ToString(),
                OriginalTotal = q.OriginalTotal,
                RequestDate = q.RequestDate,
                ValidUntil = q.ValidUntil,
                GeneralNote = q.GeneralNote,
                Items = q.Items.Select(i => new QuotationItemDto
                {
                    Id = i.Id,
                    ProductId = i.ProductId,
                    ProductName = i.Product?.Name ?? "Unknown",
                    ProductImageUrl = i.Product?.ImageUrl,
                    Quantity = i.Quantity,
                    OriginalUnitPrice = i.OriginalUnitPrice
                }).ToList(),
                Versions = q.Versions.Select(MapToVersionDto).OrderByDescending(v => v.VersionNumber).ToList()
            };
        }

        private QuotationVersionDto MapToVersionDto(QuotationVersion v)
        {
            return new QuotationVersionDto
            {
                Id = v.Id,
                VersionNumber = v.VersionNumber,
                ProposedTotal = v.ProposedTotal,
                SalesNote = v.SalesNote,
                ManagerNote = v.ManagerNote,
                CeoNote = v.CeoNote,
                Status = v.Status.ToString(),
                CreatedAt = v.CreatedAt,
                ManagerReviewedAt = v.ManagerReviewedAt,
                CeoReviewedAt = v.CeoReviewedAt,
                ValidUntil = v.ValidUntil,
                Items = v.Items.Select(i => new QuotationVersionItemDto
                {
                    Id = i.Id,
                    ProductId = i.ProductId,
                    ProductName = i.Product?.Name ?? "Unknown",
                    ProductImageUrl = i.Product?.ImageUrl,
                    Quantity = i.Quantity,
                    OriginalUnitPrice = i.OriginalUnitPrice,
                    ProposedUnitPrice = i.ProposedUnitPrice
                }).ToList()
            };
        }
    }
}
