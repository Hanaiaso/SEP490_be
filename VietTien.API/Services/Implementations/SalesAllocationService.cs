using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Order;
using VietTien.API.DTOs.Sales;
using VietTien.API.Hubs;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class SalesAllocationService : ISalesAllocationService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<SalesAllocationService> _logger;
        private readonly IHubContext<SalesHub> _salesHub;
        private readonly INotificationService _notificationService;

        public SalesAllocationService(
            ApplicationDbContext context,
            ILogger<SalesAllocationService> logger,
            IHubContext<SalesHub> salesHub,
            INotificationService notificationService)
        {
            _context = context;
            _logger = logger;
            _salesHub = salesHub;
            _notificationService = notificationService;
        }

        // ─── KHÁCH HÀNG CỦA TÔI (SAL-02) ──────────────────────────────────────────

        public async Task<PagedResultDto<MyCustomerListItemDto>> GetMyCustomersAsync(Guid staffId, MyCustomerQueryDto query)
        {
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 100);

            var baseQuery = _context.CustomerProfiles
                .AsNoTracking()
                .Include(cp => cp.User)
                .Where(cp => cp.AssignedSalesStaffId == staffId);

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var s = query.Search.Trim();
                baseQuery = baseQuery.Where(cp =>
                    cp.User.FullName.Contains(s) ||
                    cp.User.Email.Contains(s) ||
                    cp.User.PhoneNumber.Contains(s) ||
                    (cp.CompanyName != null && cp.CompanyName.Contains(s)) ||
                    (cp.TaxCode != null && cp.TaxCode.Contains(s)));
            }

            var totalCount = await baseQuery.CountAsync();

            var items = await baseQuery
                .OrderByDescending(cp => cp.AssignedAt ?? DateTime.MinValue)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(cp => new MyCustomerListItemDto
                {
                    Id = cp.Id,
                    UserId = cp.UserId,
                    FullName = cp.User.FullName,
                    Email = cp.User.Email,
                    PhoneNumber = cp.User.PhoneNumber,
                    CompanyName = cp.CompanyName,
                    Representative = cp.Representative,
                    TaxCode = cp.TaxCode,
                    AvailableCredit = cp.AvailableCredit,
                    AssignmentSource = cp.AssignmentSource,
                    AssignedAt = cp.AssignedAt,
                    SalesNote = cp.SalesNote,
                    OrderCount = cp.Orders.Count,
                    LastOrderDate = cp.Orders.Max(o => (DateTime?)o.CreatedAt)
                })
                .ToListAsync();

            return new PagedResultDto<MyCustomerListItemDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            };
        }

        public async Task<MyCustomerDetailDto> GetMyCustomerDetailAsync(Guid staffId, Guid customerProfileId)
        {
            var cp = await _context.CustomerProfiles
                .AsNoTracking()
                .Include(c => c.User)
                .Include(c => c.Addresses)
                .FirstOrDefaultAsync(c => c.Id == customerProfileId && c.AssignedSalesStaffId == staffId)
                ?? throw new Exception("Không tìm thấy khách hàng hoặc khách không thuộc quyền phụ trách của bạn.");

            var recentOrders = await _context.Orders
                .AsNoTracking()
                .Where(o => o.CustomerProfileId == cp.Id)
                .OrderByDescending(o => o.CreatedAt)
                .Take(10)
                .Select(o => new CustomerRecentOrderDto
                {
                    Id = o.Id,
                    OrderCode = o.OrderCode,
                    CreatedAt = o.CreatedAt,
                    FinalPayment = o.FinalPayment,
                    OrderStatus = o.OrderStatus.ToString(),
                    PaymentStatus = o.PaymentStatus.ToString()
                })
                .ToListAsync();

            var history = await _context.CustomerAssignmentHistories
                .AsNoTracking()
                .Where(h => h.CustomerProfileId == cp.Id)
                .OrderByDescending(h => h.AssignedAt)
                .Select(h => new AssignmentHistoryDto
                {
                    Id = h.Id,
                    SalesStaffName = h.SalesStaff.FullName,
                    AssignedByName = h.AssignedBy != null ? h.AssignedBy.FullName : null,
                    Source = h.Source,
                    AssignedAt = h.AssignedAt
                })
                .ToListAsync();

            var orderCount = await _context.Orders.CountAsync(o => o.CustomerProfileId == cp.Id);

            return new MyCustomerDetailDto
            {
                Id = cp.Id,
                UserId = cp.UserId,
                FullName = cp.User.FullName,
                Email = cp.User.Email,
                PhoneNumber = cp.User.PhoneNumber,
                CompanyName = cp.CompanyName,
                Representative = cp.Representative,
                TaxCode = cp.TaxCode,
                AvailableCredit = cp.AvailableCredit,
                AssignmentSource = cp.AssignmentSource,
                AssignedAt = cp.AssignedAt,
                SalesNote = cp.SalesNote,
                CompanyAddress = cp.CompanyAddress,
                InvoiceEmail = cp.InvoiceEmail,
                OrderCount = orderCount,
                LastOrderDate = recentOrders.Count > 0 ? recentOrders[0].CreatedAt : null,
                Addresses = cp.Addresses.Select(a => new CustomerAddressDto
                {
                    Id = a.Id,
                    ReceiverName = a.ReceiverName,
                    ReceiverPhone = a.ReceiverPhone,
                    FullAddress = string.Join(", ", new[] { a.SpecificAddress, a.Ward, a.District, a.City }
                        .Where(x => !string.IsNullOrWhiteSpace(x))),
                    IsDefault = a.IsDefault
                }).ToList(),
                RecentOrders = recentOrders,
                AssignmentHistory = history
            };
        }

        public async Task UpdateCustomerNoteAsync(Guid staffId, Guid customerProfileId, string? note)
        {
            var cp = await _context.CustomerProfiles
                .FirstOrDefaultAsync(c => c.Id == customerProfileId && c.AssignedSalesStaffId == staffId)
                ?? throw new Exception("Không tìm thấy khách hàng hoặc khách không thuộc quyền phụ trách của bạn.");

            cp.SalesNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            await _context.SaveChangesAsync();
        }

        // ─── QUẢN LÝ ROUND-ROBIN (MGR-02) ─────────────────────────────────────────

        public async Task<RoundRobinStateDto> GetRoundRobinStateAsync()
        {
            var state = await GetOrCreateStateAsync();
            await SeedParticipantsAsync();
            return await BuildStateDtoAsync(state);
        }

        public async Task<RoundRobinStateDto> UpdateRoundRobinAsync(Guid managerId, UpdateRoundRobinRequest request)
        {
            var state = await GetOrCreateStateAsync();
            await SeedParticipantsAsync();

            if (request.IsPaused.HasValue)
                state.IsPaused = request.IsPaused.Value;

            if (request.CursorStaffId.HasValue)
            {
                var isParticipant = await _context.RoundRobinParticipants
                    .AnyAsync(p => p.SalesStaffId == request.CursorStaffId.Value);
                if (!isParticipant)
                    throw new Exception("Nhân viên được chọn làm cursor không nằm trong danh sách round-robin.");
                state.LastAssignedSalesStaffId = request.CursorStaffId.Value;
            }

            if (request.Participants is { Count: > 0 })
            {
                var staffIds = request.Participants.Select(p => p.StaffId).ToList();
                var participants = await _context.RoundRobinParticipants
                    .Where(p => staffIds.Contains(p.SalesStaffId))
                    .ToListAsync();
                foreach (var toggle in request.Participants)
                {
                    var participant = participants.FirstOrDefault(p => p.SalesStaffId == toggle.StaffId)
                        ?? throw new Exception($"Không tìm thấy participant với StaffId {toggle.StaffId}.");
                    participant.IsActive = toggle.IsActive;
                }
            }

            state.UpdatedAt = DateTime.UtcNow;
            state.UpdatedById = managerId;
            await _context.SaveChangesAsync();

            return await BuildStateDtoAsync(state);
        }

        public async Task<RoundRobinAssignResultDto> AssignUnassignedCustomersAsync(Guid managerId)
        {
            // Spec v6.0: khóa transaction khi cập nhật RoundRobinCursor
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var state = await GetOrCreateStateAsync();
            await SeedParticipantsAsync();

            var pool = await GetActivePoolAsync();
            if (pool.Count == 0)
                throw new Exception("Không có nhân viên Sale nào đang active trong round-robin.");

            var unassigned = await _context.CustomerProfiles
                .Include(cp => cp.User)
                .Where(cp => cp.AssignedSalesStaffId == null && cp.User.Role == SystemRole.Customer)
                .OrderBy(cp => cp.UserId)
                .ToListAsync();

            var result = new RoundRobinAssignResultDto();
            var cursorIndex = FindCursorIndex(pool, state.LastAssignedSalesStaffId);

            foreach (var customer in unassigned)
            {
                cursorIndex = (cursorIndex + 1) % pool.Count;
                var staff = pool[cursorIndex];

                customer.AssignedSalesStaffId = staff.SalesStaffId;
                customer.AssignmentSource = AssignmentSource.RoundRobin;
                customer.AssignedAt = DateTime.UtcNow;

                _context.CustomerAssignmentHistories.Add(new CustomerAssignmentHistory
                {
                    CustomerProfileId = customer.Id,
                    SalesStaffId = staff.SalesStaffId,
                    AssignedById = managerId,
                    Source = AssignmentSource.RoundRobin
                });

                result.Assignments.Add(new RoundRobinAssignmentItemDto
                {
                    CustomerProfileId = customer.Id,
                    CustomerName = string.IsNullOrWhiteSpace(customer.CompanyName)
                        ? customer.User.FullName
                        : customer.CompanyName,
                    StaffId = staff.SalesStaffId,
                    StaffName = staff.SalesStaff.FullName
                });
            }

            if (result.Assignments.Count > 0)
            {
                state.LastAssignedSalesStaffId = pool[cursorIndex].SalesStaffId;
                state.UpdatedAt = DateTime.UtcNow;
                state.UpdatedById = managerId;
            }

            result.AssignedCount = result.Assignments.Count;
            result.NewCursorStaffId = state.LastAssignedSalesStaffId;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // WF-01 bước 6: báo cho từng Sale được gán khách (sau khi commit)
            foreach (var a in result.Assignments)
                await NotifyStaffAssignedAsync(a.StaffId, a.CustomerName, AssignmentSource.RoundRobin);

            return result;
        }

        // ─── HOOK GÁN TỰ ĐỘNG KHI ĐĂNG KÝ (WF-01) ─────────────────────────────────
        // BR-002: khách cũ giữ Sale cũ → referral về Sale giới thiệu → còn lại Round-robin

        public async Task AutoAssignCustomerAsync(Guid userId)
        {
            try
            {
                Guid? notifyStaffId = null;
                string? notifySource = null;
                string? customerName = null;

                await using (var transaction = await _context.Database.BeginTransactionAsync())
                {
                    var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                    if (user is null || user.Role != SystemRole.Customer) return;

                    var profile = await _context.CustomerProfiles.FirstOrDefaultAsync(cp => cp.UserId == userId);
                    if (profile is null)
                    {
                        profile = new CustomerProfile { UserId = userId };
                        _context.CustomerProfiles.Add(profile);
                    }
                    else if (profile.AssignedSalesStaffId != null)
                    {
                        return; // Đã có Sale phụ trách (BR-001)
                    }

                    customerName = string.IsNullOrWhiteSpace(profile.CompanyName) ? user.FullName : profile.CompanyName;

                    // 1) KHÁCH CŨ: cùng MST với một hồ sơ đã có Sale phụ trách → giữ Sale cũ
                    var returningStaffId = await FindReturningCustomerStaffAsync(profile);
                    if (returningStaffId.HasValue)
                    {
                        ApplyAssignment(profile, returningStaffId.Value, AssignmentSource.ReturningCustomer);
                        notifyStaffId = returningStaffId;
                        notifySource = AssignmentSource.ReturningCustomer;
                    }
                    // 2) REFERRAL: khách nhập mã giới thiệu hợp lệ khi đăng ký
                    else if (user.ReferredBySalesStaffId.HasValue &&
                             await _context.Users.AnyAsync(u =>
                                 u.Id == user.ReferredBySalesStaffId.Value && u.Role == SystemRole.SalesStaff))
                    {
                        ApplyAssignment(profile, user.ReferredBySalesStaffId.Value, AssignmentSource.Referral);
                        notifyStaffId = user.ReferredBySalesStaffId;
                        notifySource = AssignmentSource.Referral;
                    }
                    // 3) ROUND-ROBIN: phần còn lại (tôn trọng trạng thái pause)
                    else
                    {
                        var state = await GetOrCreateStateAsync();
                        if (state.IsPaused) { await transaction.CommitAsync(); return; }

                        await SeedParticipantsAsync();
                        var pool = await GetActivePoolAsync();
                        if (pool.Count == 0) { await transaction.CommitAsync(); return; }

                        var cursorIndex = FindCursorIndex(pool, state.LastAssignedSalesStaffId);
                        var staff = pool[(cursorIndex + 1) % pool.Count];

                        ApplyAssignment(profile, staff.SalesStaffId, AssignmentSource.RoundRobin);
                        state.LastAssignedSalesStaffId = staff.SalesStaffId;
                        state.UpdatedAt = DateTime.UtcNow;

                        notifyStaffId = staff.SalesStaffId;
                        notifySource = AssignmentSource.RoundRobin;
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }

                // WF-01 bước 6: gửi thông báo cho Sale (ngoài transaction)
                if (notifyStaffId.HasValue)
                    await NotifyStaffAssignedAsync(notifyStaffId.Value, customerName ?? "Khách hàng mới", notifySource!);
            }
            catch (Exception ex)
            {
                // Lỗi gán không được làm fail luồng đăng ký/xác minh
                _logger.LogError(ex, "Auto-assign thất bại cho user {UserId}", userId);
            }
        }

        public async Task<(Guid? StaffId, string? Error)> ResolveReferralStaffAsync(string code)
        {
            var normalized = code.Trim();
            var matches = await _context.Users
                .Where(u => u.Role == SystemRole.SalesStaff &&
                            (u.ReferralCode == normalized || u.Email == normalized.ToLower()))
                .Select(u => u.Id)
                .ToListAsync();

            if (matches.Count == 0)
                return (null, "Mã giới thiệu không hợp lệ. Vui lòng kiểm tra lại hoặc bỏ trống để hệ thống tự phân bổ nhân viên hỗ trợ.");
            if (matches.Count > 1)
                return (null, "Mã giới thiệu trùng với nhiều nhân viên. Vui lòng dùng email của nhân viên giới thiệu.");
            return (matches[0], null);
        }

        public async Task EnsureCustomerProfileAsync(Guid userId, string? taxCode)
        {
            var profile = await _context.CustomerProfiles.FirstOrDefaultAsync(cp => cp.UserId == userId);
            if (profile is null)
            {
                profile = new CustomerProfile { UserId = userId };
                _context.CustomerProfiles.Add(profile);
            }
            if (!string.IsNullOrWhiteSpace(taxCode) && string.IsNullOrWhiteSpace(profile.TaxCode))
                profile.TaxCode = taxCode.Trim();
            await _context.SaveChangesAsync();
        }

        // Khách cũ = tồn tại CustomerProfile khác cùng MST đang có Sale phụ trách (Sale còn là SalesStaff)
        private async Task<Guid?> FindReturningCustomerStaffAsync(CustomerProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.TaxCode)) return null;

            return await _context.CustomerProfiles
                .Where(cp => cp.Id != profile.Id &&
                             cp.TaxCode == profile.TaxCode &&
                             cp.AssignedSalesStaffId != null &&
                             cp.AssignedSalesStaff!.Role == SystemRole.SalesStaff)
                .OrderByDescending(cp => cp.AssignedAt)
                .Select(cp => cp.AssignedSalesStaffId)
                .FirstOrDefaultAsync();
        }

        private void ApplyAssignment(CustomerProfile profile, Guid staffId, string source)
        {
            var previousStaffId = profile.AssignedSalesStaffId; // audit before/after

            profile.AssignedSalesStaffId = staffId;
            profile.AssignmentSource = source;
            profile.AssignedAt = DateTime.UtcNow;

            _context.CustomerAssignmentHistories.Add(new CustomerAssignmentHistory
            {
                CustomerProfile = profile,
                SalesStaffId = staffId,
                PreviousSalesStaffId = previousStaffId,
                AssignedById = null,
                Source = source
            });
        }

        // Gửi realtime qua SalesHub tới đúng Sale được gán (Clients.User = NameIdentifier trong JWT)
        private async Task NotifyStaffAssignedAsync(Guid staffId, string customerName, string source)
        {
            try
            {
                var body = $"Bạn vừa được phân công phụ trách khách hàng mới: {customerName} (Nguồn: {source}).";
                await _notificationService.CreateNotificationAsync(
                    NotificationType.SYS_01_CustomerAssigned,
                    staffId,
                    "Khách hàng mới được phân công",
                    body
                );

                // Gửi realtime qua SalesHub cũ (nếu frontend vẫn dùng)
                await _salesHub.Clients.User(staffId.ToString()).SendAsync("CustomerAssigned", new
                {
                    customerName,
                    source,
                    assignedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không gửi được thông báo gán khách cho Sale {StaffId}", staffId);
            }
        }

        // ─── HELPERS ──────────────────────────────────────────────────────────────

        private async Task<RoundRobinState> GetOrCreateStateAsync()
        {
            var state = await _context.RoundRobinStates.FirstOrDefaultAsync();
            if (state is null)
            {
                state = new RoundRobinState();
                _context.RoundRobinStates.Add(state);
                await _context.SaveChangesAsync();
            }
            return state;
        }

        // Tự thêm mọi SalesStaff chưa có trong pool (giữ nguyên trạng thái người đã có)
        private async Task SeedParticipantsAsync()
        {
            var existingIds = await _context.RoundRobinParticipants
                .Select(p => p.SalesStaffId)
                .ToListAsync();

            var missingStaff = await _context.Users
                .Where(u => u.Role == SystemRole.SalesStaff && !existingIds.Contains(u.Id))
                .OrderBy(u => u.CreatedAt)
                .ToListAsync();

            if (missingStaff.Count == 0) return;

            var maxOrder = await _context.RoundRobinParticipants
                .Select(p => (int?)p.SortOrder)
                .MaxAsync() ?? 0;

            foreach (var staff in missingStaff)
            {
                _context.RoundRobinParticipants.Add(new RoundRobinParticipant
                {
                    SalesStaffId = staff.Id,
                    IsActive = true,
                    SortOrder = ++maxOrder
                });
            }
            await _context.SaveChangesAsync();
        }

        private async Task<List<RoundRobinParticipant>> GetActivePoolAsync()
        {
            return await _context.RoundRobinParticipants
                .Include(p => p.SalesStaff)
                .Where(p => p.IsActive)
                .OrderBy(p => p.SortOrder)
                .ToListAsync();
        }

        // Trả về index của cursor trong pool; -1 nếu cursor null/không còn trong pool
        // (người kế tiếp luôn là index + 1, nên -1 nghĩa là bắt đầu từ người đầu tiên)
        private static int FindCursorIndex(List<RoundRobinParticipant> pool, Guid? cursorStaffId)
        {
            if (!cursorStaffId.HasValue) return -1;
            return pool.FindIndex(p => p.SalesStaffId == cursorStaffId.Value);
        }

        private async Task<RoundRobinStateDto> BuildStateDtoAsync(RoundRobinState state)
        {
            var participants = await _context.RoundRobinParticipants
                .AsNoTracking()
                .Include(p => p.SalesStaff)
                .OrderBy(p => p.SortOrder)
                .Select(p => new RoundRobinParticipantDto
                {
                    ParticipantId = p.Id,
                    StaffId = p.SalesStaffId,
                    Name = p.SalesStaff.FullName,
                    Email = p.SalesStaff.Email,
                    IsActive = p.IsActive,
                    SortOrder = p.SortOrder,
                    AssignedCustomerCount = _context.CustomerProfiles
                        .Count(cp => cp.AssignedSalesStaffId == p.SalesStaffId)
                })
                .ToListAsync();

            var activePool = participants.Where(p => p.IsActive).ToList();
            RoundRobinStaffDto? nextStaff = null;
            if (activePool.Count > 0)
            {
                var idx = state.LastAssignedSalesStaffId.HasValue
                    ? activePool.FindIndex(p => p.StaffId == state.LastAssignedSalesStaffId.Value)
                    : -1;
                var next = activePool[(idx + 1) % activePool.Count];
                nextStaff = new RoundRobinStaffDto { Id = next.StaffId, Name = next.Name };
            }

            var cursorName = state.LastAssignedSalesStaffId.HasValue
                ? await _context.Users
                    .Where(u => u.Id == state.LastAssignedSalesStaffId.Value)
                    .Select(u => u.FullName)
                    .FirstOrDefaultAsync()
                : null;

            var unassignedCount = await _context.CustomerProfiles
                .CountAsync(cp => cp.AssignedSalesStaffId == null && cp.User.Role == SystemRole.Customer);

            var recentAssignments = await _context.CustomerAssignmentHistories
                .AsNoTracking()
                .OrderByDescending(h => h.AssignedAt)
                .Take(20)
                .Select(h => new RoundRobinAssignmentLogDto
                {
                    Id = h.Id,
                    CustomerName = h.CustomerProfile.CompanyName ?? h.CustomerProfile.User.FullName,
                    StaffName = h.SalesStaff.FullName,
                    AssignedByName = h.AssignedBy != null ? h.AssignedBy.FullName : null,
                    Source = h.Source,
                    AssignedAt = h.AssignedAt
                })
                .ToListAsync();

            return new RoundRobinStateDto
            {
                IsPaused = state.IsPaused,
                CursorStaffId = state.LastAssignedSalesStaffId,
                CursorStaffName = cursorName,
                NextStaff = nextStaff,
                Participants = participants,
                UnassignedCustomerCount = unassignedCount,
                UpdatedAt = state.UpdatedAt,
                RecentAssignments = recentAssignments
            };
        }
    }
}
