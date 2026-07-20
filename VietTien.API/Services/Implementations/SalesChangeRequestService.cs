using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.SalesChange;
using VietTien.API.Hubs;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    // LUỒNG 7: Khách hàng yêu cầu đổi nhân viên Sale phụ trách (WF-07; CUS-11, MGR-06)
    public class SalesChangeRequestService : ISalesChangeRequestService
    {
        private const int MaxEvidenceFiles = 5;
        private const long MaxAttachmentBytes = 10 * 1024 * 1024; // 10MB/file

        // Định dạng file giải trình được phép: ảnh + PDF + Word
        private static readonly string[] AllowedExplanationExtensions =
        {
            ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf", ".doc", ".docx"
        };

        // Đơn "đang chạy" = chưa kết thúc: Manager phải quyết định giữ/chuyển từng đơn khi phê duyệt
        private static readonly OrderStatus[] ClosedOrderStatuses =
        {
            OrderStatus.Draft, OrderStatus.Completed, OrderStatus.Cancelled, OrderStatus.CancelledReallocated
        };

        private readonly ApplicationDbContext _context;
        private readonly ILogger<SalesChangeRequestService> _logger;
        private readonly IHubContext<SalesHub> _salesHub;
        private readonly ICloudinaryService _cloudinary;

        public SalesChangeRequestService(
            ApplicationDbContext context,
            ILogger<SalesChangeRequestService> logger,
            IHubContext<SalesHub> salesHub,
            ICloudinaryService cloudinary)
        {
            _context = context;
            _logger = logger;
            _salesHub = salesHub;
            _cloudinary = cloudinary;
        }

        // ─── CUSTOMER ─────────────────────────────────────────────────────────────

        public async Task<Guid> CreateAsync(Guid userId, CreateSalesChangeRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Reason))
                throw new InvalidOperationException("Vui lòng nhập lý do yêu cầu đổi Sale.");
            if (string.IsNullOrWhiteSpace(dto.ProblemDescription))
                throw new InvalidOperationException("Vui lòng mô tả vấn đề bạn gặp phải.");

            var profile = await _context.CustomerProfiles
                .Include(cp => cp.User)
                .FirstOrDefaultAsync(cp => cp.UserId == userId)
                ?? throw new KeyNotFoundException("Không tìm thấy hồ sơ khách hàng.");

            if (profile.AssignedSalesStaffId is null)
                throw new InvalidOperationException("Bạn chưa được gán Sale phụ trách nên không thể tạo yêu cầu đổi Sale.");

            // Chỉ cho phép 1 yêu cầu đang mở cho mỗi khách hàng (Bước 1)
            var hasOpen = await _context.SalesChangeRequests.AnyAsync(r =>
                r.CustomerProfileId == profile.Id &&
                (r.Status == SalesChangeRequestStatus.Pending || r.Status == SalesChangeRequestStatus.MoreInfoRequested));
            if (hasOpen)
                throw new InvalidOperationException("Bạn đang có một yêu cầu đổi Sale chưa được xử lý. Vui lòng chờ kết quả hoặc hủy yêu cầu cũ.");

            if (dto.DesiredSalesStaffId.HasValue)
            {
                if (dto.DesiredSalesStaffId.Value == profile.AssignedSalesStaffId.Value)
                    throw new InvalidOperationException("Sale mong muốn phải khác Sale hiện tại.");

                var desiredValid = await _context.Users.AnyAsync(u =>
                    u.Id == dto.DesiredSalesStaffId.Value && u.Role == SystemRole.SalesStaff);
                if (!desiredValid)
                    throw new InvalidOperationException("Sale mong muốn không hợp lệ.");
            }

            // Upload bằng chứng (nếu có) → lưu JSON array URL
            string? evidenceJson = null;
            if (dto.Files is { Count: > 0 })
            {
                if (dto.Files.Count > MaxEvidenceFiles)
                    throw new InvalidOperationException($"Chỉ được đính kèm tối đa {MaxEvidenceFiles} ảnh bằng chứng.");

                var urls = new List<string>();
                foreach (var file in dto.Files)
                    urls.Add(await _cloudinary.UploadEvidenceAsync(file, "sales-change-evidence"));
                evidenceJson = JsonSerializer.Serialize(urls);
            }

            var request = new SalesChangeRequest
            {
                CustomerProfileId = profile.Id,
                CurrentSalesStaffId = profile.AssignedSalesStaffId.Value,
                DesiredSalesStaffId = dto.DesiredSalesStaffId,
                Reason = dto.Reason.Trim(),
                ProblemDescription = dto.ProblemDescription.Trim(),
                EvidenceUrls = evidenceJson,
                Status = SalesChangeRequestStatus.Pending // Bước 2: PENDING, CreatedAt = mốc SLA
            };

            _context.SalesChangeRequests.Add(request);
            await _context.SaveChangesAsync();

            // Bước 2: chỉ thông báo cho Sales Manager. Sale hiện tại KHÔNG được biết
            // cho tới khi Manager bấm "Yêu cầu giải trình" (gate bảo vệ khách hàng).
            var customerName = DisplayName(profile);
            await NotifyManagersAsync("SalesChangeRequestCreated", new
            {
                requestId = request.Id,
                customerName,
                message = $"Khách hàng {customerName} yêu cầu đổi Sale phụ trách."
            });

            return request.Id;
        }

        public async Task<List<SalesChangeRequestDetailDto>> GetMineAsync(Guid userId)
        {
            var profileId = await GetProfileIdAsync(userId);
            var requests = await DetailQuery()
                .Where(r => r.CustomerProfileId == profileId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
            return requests.Select(ToDetailDto).ToList();
        }

        public async Task<MyAssignedSaleDto> GetMyAssignedSaleAsync(Guid userId)
        {
            var profile = await _context.CustomerProfiles
                .AsNoTracking()
                .Include(cp => cp.AssignedSalesStaff)
                .FirstOrDefaultAsync(cp => cp.UserId == userId)
                ?? throw new KeyNotFoundException("Không tìm thấy hồ sơ khách hàng.");

            return new MyAssignedSaleDto
            {
                SalesStaffId = profile.AssignedSalesStaffId,
                FullName = profile.AssignedSalesStaff?.FullName,
                Email = profile.AssignedSalesStaff?.Email,
                PhoneNumber = profile.AssignedSalesStaff?.PhoneNumber,
                AvatarUrl = profile.AssignedSalesStaff?.AvatarUrl,
                AssignedAt = profile.AssignedAt
            };
        }

        public async Task<List<SalesOptionDto>> GetSalesOptionsAsync()
        {
            return await _context.Users
                .AsNoTracking()
                .Where(u => u.Role == SystemRole.SalesStaff)
                .OrderBy(u => u.FullName)
                .Select(u => new SalesOptionDto { Id = u.Id, FullName = u.FullName })
                .ToListAsync();
        }

        public async Task CancelAsync(Guid userId, Guid requestId)
        {
            var profileId = await GetProfileIdAsync(userId);
            var request = await _context.SalesChangeRequests.FirstOrDefaultAsync(r => r.Id == requestId)
                ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu.");

            if (request.CustomerProfileId != profileId)
                throw new UnauthorizedAccessException("Bạn không có quyền hủy yêu cầu này.");
            EnsureOpen(request);

            request.Status = SalesChangeRequestStatus.Cancelled;
            request.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        public async Task SubmitAdditionalInfoAsync(Guid userId, Guid requestId, string info)
        {
            if (string.IsNullOrWhiteSpace(info))
                throw new InvalidOperationException("Vui lòng nhập nội dung bổ sung.");

            var profileId = await GetProfileIdAsync(userId);
            var request = await _context.SalesChangeRequests
                .Include(r => r.CustomerProfile).ThenInclude(cp => cp.User)
                .FirstOrDefaultAsync(r => r.Id == requestId)
                ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu.");

            if (request.CustomerProfileId != profileId)
                throw new UnauthorizedAccessException("Bạn không có quyền cập nhật yêu cầu này.");
            if (request.Status != SalesChangeRequestStatus.MoreInfoRequested)
                throw new InvalidOperationException("Yêu cầu không ở trạng thái chờ bổ sung thông tin.");

            request.CustomerAdditionalInfo = info.Trim();
            request.Status = SalesChangeRequestStatus.Pending; // quay lại hàng chờ Manager
            request.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await NotifyManagersAsync("SalesChangeRequestInfoProvided", new
            {
                requestId = request.Id,
                customerName = DisplayName(request.CustomerProfile),
                message = $"Khách hàng {DisplayName(request.CustomerProfile)} đã bổ sung thông tin cho yêu cầu đổi Sale."
            });
        }

        // ─── SALES STAFF (Sale hiện tại) ──────────────────────────────────────────
        // Bước 3: Sale chỉ được XEM và GIẢI TRÌNH — không có API sửa/xóa/từ chối yêu cầu của khách.
        // Gate bảo vệ khách hàng: Sale chỉ thấy yêu cầu SAU KHI Manager bấm "Yêu cầu giải trình".

        public async Task<List<SalesChangeRequestDetailDto>> GetAboutMeAsync(Guid salesStaffId)
        {
            var requests = await DetailQuery()
                .Where(r => r.CurrentSalesStaffId == salesStaffId && r.ExplanationRequestedAt != null)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
            return requests.Select(ToDetailDto).ToList();
        }

        public async Task SubmitExplanationAsync(Guid salesStaffId, Guid requestId, SaleExplanationDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Explanation))
                throw new InvalidOperationException("Vui lòng nhập nội dung giải trình.");

            var request = await _context.SalesChangeRequests
                .Include(r => r.CustomerProfile).ThenInclude(cp => cp.User)
                .FirstOrDefaultAsync(r => r.Id == requestId)
                ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu.");

            if (request.CurrentSalesStaffId != salesStaffId)
                throw new UnauthorizedAccessException("Bạn không phải Sale phụ trách của yêu cầu này.");
            if (request.ExplanationRequestedAt is null)
                throw new UnauthorizedAccessException("Yêu cầu này chưa được quản lý mở giải trình.");
            EnsureOpen(request);

            // Upload file đính kèm giải trình (ảnh/PDF/Word, nếu có) → lưu JSON array URL
            if (dto.Files is { Count: > 0 })
            {
                if (dto.Files.Count > MaxEvidenceFiles)
                    throw new InvalidOperationException($"Chỉ được đính kèm tối đa {MaxEvidenceFiles} file.");

                foreach (var file in dto.Files)
                {
                    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                    if (!AllowedExplanationExtensions.Contains(extension))
                        throw new InvalidOperationException(
                            $"File \"{file.FileName}\" không được hỗ trợ. Chỉ chấp nhận ảnh, PDF, Word (doc/docx).");
                    if (file.Length > MaxAttachmentBytes)
                        throw new InvalidOperationException(
                            $"File \"{file.FileName}\" vượt quá 10MB.");
                }

                var urls = new List<string>();
                foreach (var file in dto.Files)
                    urls.Add(await _cloudinary.UploadAttachmentAsync(file, "sales-change-explanation"));
                request.SaleExplanationFileUrls = JsonSerializer.Serialize(urls);
            }

            request.SaleExplanation = dto.Explanation.Trim();
            request.SaleExplainedAt = DateTime.UtcNow;
            request.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await NotifyManagersAsync("SalesChangeRequestExplained", new
            {
                requestId = request.Id,
                customerName = DisplayName(request.CustomerProfile),
                message = $"Sale đã gửi giải trình cho yêu cầu đổi Sale của khách hàng {DisplayName(request.CustomerProfile)}."
            });
        }

        // ─── SALES MANAGER ────────────────────────────────────────────────────────

        public async Task<SalesChangeRequestPagedResultDto> GetPagedAsync(SalesChangeRequestQueryDto query)
        {
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);

            var baseQuery = DetailQuery();
            if (!string.IsNullOrWhiteSpace(query.Status) &&
                Enum.TryParse<SalesChangeRequestStatus>(query.Status, ignoreCase: true, out var status))
            {
                baseQuery = baseQuery.Where(r => r.Status == status);
            }

            var total = await baseQuery.CountAsync();
            var items = await baseQuery
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new SalesChangeRequestPagedResultDto
            {
                Total = total,
                Page = page,
                PageSize = pageSize,
                Items = items.Select(ToListItemDto).ToList()
            };
        }

        public async Task<SalesChangeRequestDetailDto> GetDetailAsync(Guid requestId)
        {
            var request = await DetailQuery().FirstOrDefaultAsync(r => r.Id == requestId)
                ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu.");
            return ToDetailDto(request);
        }

        // Bước 4: dữ liệu hỗ trợ Manager rà soát — đơn đang chạy của khách + workload các Sale
        public async Task<ReviewContextDto> GetReviewContextAsync(Guid requestId)
        {
            var request = await _context.SalesChangeRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == requestId)
                ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu.");

            var runningOrders = await _context.Orders
                .AsNoTracking()
                .Where(o => o.CustomerProfileId == request.CustomerProfileId &&
                            !ClosedOrderStatuses.Contains(o.OrderStatus))
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => new RunningOrderDto
                {
                    OrderId = o.Id,
                    OrderCode = o.OrderCode,
                    OrderStatus = o.OrderStatus.ToString(),
                    DeliveryStatus = o.DeliveryStatus.ToString(),
                    FinalPayment = o.FinalPayment,
                    CreatedAt = o.CreatedAt,
                    IsInDelivery = o.DeliveryStatus == DeliveryStatus.InDelivery
                })
                .ToListAsync();

            var candidates = await _context.Users
                .AsNoTracking()
                .Where(u => u.Role == SystemRole.SalesStaff)
                .OrderBy(u => u.FullName)
                .Select(u => new SalesWorkloadDto
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    Email = u.Email,
                    CustomerCount = _context.CustomerProfiles.Count(cp => cp.AssignedSalesStaffId == u.Id),
                    OpenOrderCount = _context.Orders.Count(o =>
                        o.SalesStaffId == u.Id && !ClosedOrderStatuses.Contains(o.OrderStatus)),
                    IsActive = !_context.RoundRobinParticipants.Any(rp => rp.SalesStaffId == u.Id) ||
                               _context.RoundRobinParticipants.Any(rp => rp.SalesStaffId == u.Id && rp.IsActive),
                    IsDesired = request.DesiredSalesStaffId == u.Id,
                    IsCurrent = request.CurrentSalesStaffId == u.Id
                })
                .ToListAsync();

            return new ReviewContextDto { RunningOrders = runningOrders, SalesCandidates = candidates };
        }

        // Gate bảo vệ khách hàng: Manager chủ động mở giải trình thì Sale hiện tại mới thấy được khiếu nại
        public async Task RequestExplanationAsync(Guid managerId, Guid requestId)
        {
            var request = await _context.SalesChangeRequests
                .Include(r => r.CustomerProfile).ThenInclude(cp => cp.User)
                .FirstOrDefaultAsync(r => r.Id == requestId)
                ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu.");
            EnsureOpen(request);

            if (request.ExplanationRequestedAt != null)
                throw new InvalidOperationException("Đã yêu cầu Sale giải trình cho yêu cầu này rồi.");

            request.ExplanationRequestedAt = DateTime.UtcNow;
            request.ExplanationRequestedByUserId = managerId;
            request.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var customerName = DisplayName(request.CustomerProfile);
            await NotifyUserAsync(request.CurrentSalesStaffId, "SalesChangeRequestExplanationRequested", new
            {
                requestId = request.Id,
                customerName,
                message = $"Khách hàng {customerName} đã gửi yêu cầu đổi Sale phụ trách. Vui lòng gửi giải trình."
            });
        }

        public async Task RequestMoreInfoAsync(Guid managerId, Guid requestId, string note)
        {
            if (string.IsNullOrWhiteSpace(note))
                throw new InvalidOperationException("Vui lòng nhập nội dung cần khách bổ sung.");

            var request = await _context.SalesChangeRequests
                .Include(r => r.CustomerProfile).ThenInclude(cp => cp.User)
                .FirstOrDefaultAsync(r => r.Id == requestId)
                ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu.");

            if (request.Status != SalesChangeRequestStatus.Pending)
                throw new InvalidOperationException("Chỉ yêu cầu đang chờ xử lý mới có thể yêu cầu bổ sung thông tin.");

            request.Status = SalesChangeRequestStatus.MoreInfoRequested;
            request.ManagerNote = note.Trim();
            request.ReviewedByUserId = managerId;
            request.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await NotifyUserAsync(request.CustomerProfile.UserId, "SalesChangeRequestMoreInfo", new
            {
                requestId = request.Id,
                message = "Quản lý yêu cầu bạn bổ sung thông tin cho yêu cầu đổi Sale."
            });
        }

        public async Task RejectAsync(Guid managerId, Guid requestId, string note)
        {
            if (string.IsNullOrWhiteSpace(note))
                throw new InvalidOperationException("Từ chối yêu cầu bắt buộc phải có lý do.");

            var request = await _context.SalesChangeRequests
                .Include(r => r.CustomerProfile).ThenInclude(cp => cp.User)
                .FirstOrDefaultAsync(r => r.Id == requestId)
                ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu.");
            EnsureOpen(request);

            request.Status = SalesChangeRequestStatus.Rejected;
            request.ManagerNote = note.Trim();
            request.ReviewedByUserId = managerId;
            request.ReviewedAt = DateTime.UtcNow;
            request.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await NotifyUserAsync(request.CustomerProfile.UserId, "SalesChangeRequestRejected", new
            {
                requestId = request.Id,
                message = "Yêu cầu đổi Sale của bạn đã bị từ chối. Xem lý do trong chi tiết yêu cầu."
            });
            await NotifyUserAsync(request.CurrentSalesStaffId, "SalesChangeRequestRejected", new
            {
                requestId = request.Id,
                customerName = DisplayName(request.CustomerProfile),
                message = $"Yêu cầu đổi Sale của khách hàng {DisplayName(request.CustomerProfile)} đã bị từ chối."
            });
        }

        // Bước 5 + 6: phê duyệt, chỉ định Sale mới, quyết định giữ/chuyển từng đơn, audit + thông báo
        public async Task ApproveAsync(Guid managerId, Guid requestId, ApproveSalesChangeRequestDto dto)
        {
            Guid customerUserId;
            Guid oldSaleId;
            string customerName;

            await using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                var request = await _context.SalesChangeRequests
                    .Include(r => r.CustomerProfile).ThenInclude(cp => cp.User)
                    .FirstOrDefaultAsync(r => r.Id == requestId)
                    ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu.");
                EnsureOpen(request);

                var profile = request.CustomerProfile;
                oldSaleId = request.CurrentSalesStaffId;
                customerUserId = profile.UserId;
                customerName = DisplayName(profile);

                // Sale mới phải là SalesStaff và khác Sale hiện tại
                var newSale = await _context.Users.FirstOrDefaultAsync(u =>
                    u.Id == dto.NewSalesStaffId && u.Role == SystemRole.SalesStaff)
                    ?? throw new InvalidOperationException("Sale mới không hợp lệ.");
                if (newSale.Id == oldSaleId)
                    throw new InvalidOperationException("Sale mới phải khác Sale hiện tại.");

                // Ngoại lệ: chọn Sale khác Sale khách mong muốn (inactive/quá tải) → bắt buộc ghi lý do
                if (request.DesiredSalesStaffId.HasValue &&
                    request.DesiredSalesStaffId.Value != dto.NewSalesStaffId &&
                    string.IsNullOrWhiteSpace(dto.OverrideReason))
                {
                    throw new InvalidOperationException("Chọn Sale khác với Sale khách mong muốn bắt buộc phải ghi lý do.");
                }

                // Quyết định phải phủ đúng toàn bộ đơn đang chạy của khách
                var runningOrders = await _context.Orders
                    .Where(o => o.CustomerProfileId == profile.Id &&
                                !ClosedOrderStatuses.Contains(o.OrderStatus))
                    .ToListAsync();

                var decisionByOrderId = dto.OrderDecisions.ToDictionary(d => d.OrderId);
                if (decisionByOrderId.Count != dto.OrderDecisions.Count)
                    throw new InvalidOperationException("Danh sách quyết định có đơn hàng bị trùng.");

                var runningIds = runningOrders.Select(o => o.Id).ToHashSet();
                if (!runningIds.SetEquals(decisionByOrderId.Keys))
                    throw new InvalidOperationException("Quyết định giữ/chuyển phải bao phủ đúng toàn bộ đơn đang chạy của khách.");

                foreach (var order in runningOrders)
                {
                    var decision = decisionByOrderId[order.Id];

                    // Đơn đang giao mặc định do Sale cũ hoàn tất; Manager override phải ghi lý do
                    if (decision.TransferToNewSale &&
                        order.DeliveryStatus == DeliveryStatus.InDelivery &&
                        string.IsNullOrWhiteSpace(decision.Note))
                    {
                        throw new InvalidOperationException(
                            $"Đơn {order.OrderCode} đang giao — chuyển cho Sale mới bắt buộc phải ghi lý do.");
                    }

                    if (decision.TransferToNewSale)
                        order.SalesStaffId = dto.NewSalesStaffId; // chuyển quyền xử lý; đơn KEEP giữ nguyên snapshot

                    _context.SalesChangeRequestOrderDecisions.Add(new SalesChangeRequestOrderDecision
                    {
                        SalesChangeRequestId = request.Id,
                        OrderId = order.Id,
                        TransferToNewSale = decision.TransferToNewSale,
                        Note = decision.Note?.Trim()
                    });
                }

                // Bước 6: đổi chủ sở hữu khách, hiệu lực từ thời điểm phê duyệt
                profile.AssignedSalesStaffId = dto.NewSalesStaffId;
                profile.AssignmentSource = AssignmentSource.ManualReassignment;
                profile.AssignedAt = DateTime.UtcNow;

                // Audit before/after
                var auditNote = $"Đổi Sale theo yêu cầu của khách (LUỒNG 7). Lý do: {request.Reason}";
                if (!string.IsNullOrWhiteSpace(dto.OverrideReason))
                    auditNote += $" | Lý do chọn Sale khác mong muốn: {dto.OverrideReason.Trim()}";

                _context.CustomerAssignmentHistories.Add(new CustomerAssignmentHistory
                {
                    CustomerProfileId = profile.Id,
                    SalesStaffId = dto.NewSalesStaffId,
                    PreviousSalesStaffId = oldSaleId,
                    AssignedById = managerId,
                    Source = AssignmentSource.ManualReassignment,
                    Note = auditNote
                });

                request.Status = SalesChangeRequestStatus.Approved;
                request.NewSalesStaffId = dto.NewSalesStaffId;
                request.OverrideReason = dto.OverrideReason?.Trim();
                request.ReviewedByUserId = managerId;
                request.ReviewedAt = DateTime.UtcNow;
                request.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            // Thông báo sau khi commit (Bước 6): khách + Sale cũ + Sale mới
            await NotifyUserAsync(customerUserId, "SalesChangeRequestApproved", new
            {
                requestId,
                message = "Yêu cầu đổi Sale của bạn đã được phê duyệt. Sale mới sẽ liên hệ với bạn."
            });
            await NotifyUserAsync(oldSaleId, "SalesChangeRequestApproved", new
            {
                requestId,
                customerName,
                message = $"Khách hàng {customerName} đã được chuyển cho Sale khác. Vui lòng hoàn tất các đơn được giữ lại."
            });
            // Sale mới dùng lại event CustomerAssigned sẵn có → toast trong SalesPortal hoạt động ngay
            await NotifyUserAsync(dto.NewSalesStaffId, "CustomerAssigned", new
            {
                customerName,
                source = AssignmentSource.ManualReassignment,
                assignedAt = DateTime.UtcNow
            });
        }

        // ─── HELPERS ──────────────────────────────────────────────────────────────

        private IQueryable<SalesChangeRequest> DetailQuery() =>
            _context.SalesChangeRequests
                .AsNoTracking()
                .Include(r => r.CustomerProfile).ThenInclude(cp => cp.User)
                .Include(r => r.CurrentSalesStaff)
                .Include(r => r.DesiredSalesStaff)
                .Include(r => r.NewSalesStaff)
                .Include(r => r.ReviewedBy)
                .Include(r => r.OrderDecisions).ThenInclude(d => d.Order);

        private async Task<Guid> GetProfileIdAsync(Guid userId)
        {
            var profileId = await _context.CustomerProfiles
                .Where(cp => cp.UserId == userId)
                .Select(cp => (Guid?)cp.Id)
                .FirstOrDefaultAsync();
            return profileId ?? throw new KeyNotFoundException("Không tìm thấy hồ sơ khách hàng.");
        }

        private static void EnsureOpen(SalesChangeRequest request)
        {
            if (request.Status != SalesChangeRequestStatus.Pending &&
                request.Status != SalesChangeRequestStatus.MoreInfoRequested)
            {
                throw new InvalidOperationException("Yêu cầu đã được xử lý, không thể thao tác thêm.");
            }
        }

        private static string DisplayName(CustomerProfile profile) =>
            string.IsNullOrWhiteSpace(profile.CompanyName) ? profile.User.FullName : profile.CompanyName!;

        private static SalesChangeRequestListItemDto ToListItemDto(SalesChangeRequest r) => new()
        {
            Id = r.Id,
            CustomerName = r.CustomerProfile.User.FullName,
            CompanyName = r.CustomerProfile.CompanyName,
            CurrentSalesStaffName = r.CurrentSalesStaff.FullName,
            DesiredSalesStaffName = r.DesiredSalesStaff?.FullName,
            Reason = r.Reason,
            Status = r.Status.ToString(),
            HasExplanation = r.SaleExplainedAt != null,
            CreatedAt = r.CreatedAt,
            ReviewedAt = r.ReviewedAt
        };

        // Parse JSON array URL, dữ liệu hỏng thì trả rỗng — không làm fail cả trang
        private static List<string> ParseUrlList(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new();
            try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
            catch { return new(); }
        }

        private static SalesChangeRequestDetailDto ToDetailDto(SalesChangeRequest r)
        {
            return new SalesChangeRequestDetailDto
            {
                Id = r.Id,
                CustomerProfileId = r.CustomerProfileId,
                CustomerName = r.CustomerProfile.User.FullName,
                CompanyName = r.CustomerProfile.CompanyName,
                CurrentSalesStaffId = r.CurrentSalesStaffId,
                CurrentSalesStaffName = r.CurrentSalesStaff.FullName,
                DesiredSalesStaffId = r.DesiredSalesStaffId,
                DesiredSalesStaffName = r.DesiredSalesStaff?.FullName,
                Reason = r.Reason,
                ProblemDescription = r.ProblemDescription,
                EvidenceUrls = ParseUrlList(r.EvidenceUrls),
                Status = r.Status.ToString(),
                HasExplanation = r.SaleExplainedAt != null,
                ExplanationRequestedAt = r.ExplanationRequestedAt,
                SaleExplanation = r.SaleExplanation,
                SaleExplanationFileUrls = ParseUrlList(r.SaleExplanationFileUrls),
                SaleExplainedAt = r.SaleExplainedAt,
                ManagerNote = r.ManagerNote,
                ReviewedByName = r.ReviewedBy?.FullName,
                NewSalesStaffId = r.NewSalesStaffId,
                NewSalesStaffName = r.NewSalesStaff?.FullName,
                OverrideReason = r.OverrideReason,
                CustomerAdditionalInfo = r.CustomerAdditionalInfo,
                CreatedAt = r.CreatedAt,
                ReviewedAt = r.ReviewedAt,
                OrderDecisions = r.OrderDecisions.Select(d => new OrderDecisionResultDto
                {
                    OrderId = d.OrderId,
                    OrderCode = d.Order.OrderCode,
                    TransferToNewSale = d.TransferToNewSale,
                    Note = d.Note
                }).ToList()
            };
        }

        // Gửi realtime tới 1 user qua SalesHub (Clients.User = NameIdentifier trong JWT), lỗi không làm fail nghiệp vụ
        private async Task NotifyUserAsync(Guid userId, string eventName, object payload)
        {
            try
            {
                await _salesHub.Clients.User(userId.ToString()).SendAsync(eventName, payload);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không gửi được thông báo {Event} cho user {UserId}", eventName, userId);
            }
        }

        private async Task NotifyManagersAsync(string eventName, object payload)
        {
            var managerIds = await _context.Users
                .Where(u => u.Role == SystemRole.SalesManager)
                .Select(u => u.Id)
                .ToListAsync();

            foreach (var id in managerIds)
                await NotifyUserAsync(id, eventName, payload);
        }
    }
}
