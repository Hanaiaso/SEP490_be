using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Warehouse;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class GoodsIssueService : IGoodsIssueService
    {
        private readonly ApplicationDbContext _context;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly IWarehouseAccessGuard _warehouseAccessGuard;
        private readonly IAuditLogService _auditLogService;

        public GoodsIssueService(
            ApplicationDbContext context,
            ICloudinaryService cloudinaryService,
            IWarehouseAccessGuard warehouseAccessGuard,
            IAuditLogService auditLogService)
        {
            _context = context;
            _cloudinaryService = cloudinaryService;
            _warehouseAccessGuard = warehouseAccessGuard;
            _auditLogService = auditLogService;
        }

        public async Task<IEnumerable<GoodsIssueDto>> GetGoodsIssuesAsync(string? type, Guid staffId)
        {
            var query = _context.GoodsIssues
                .Include(gi => gi.Warehouse)
                .Include(gi => gi.IssuedByUser)
                .Include(gi => gi.Items).ThenInclude(i => i.Product)
                .Include(gi => gi.Items).ThenInclude(i => i.Material)
                .AsQueryable();

            // Trước đây danh sách không lọc kho: mọi nhân viên kho đều thấy toàn bộ phiếu xuất của
            // cả 3 kho. Lọc theo kho được phân công như InventoryCountSessionService.GetListAsync.
            var scopedWarehouseId = await _warehouseAccessGuard.GetScopedWarehouseIdAsync(staffId);
            if (scopedWarehouseId.HasValue)
            {
                query = query.Where(gi => gi.WarehouseId == scopedWarehouseId.Value);
            }

            if (!string.IsNullOrEmpty(type) && Enum.TryParse<GoodsIssueType>(type, true, out var typeEnum))
            {
                query = query.Where(gi => gi.Type == typeEnum);
            }

            var issues = await query.OrderByDescending(gi => gi.CreatedAt).ToListAsync();
            return issues.Select(MapToDto);
        }

        public async Task<GoodsIssueDto> GetGoodsIssueByIdAsync(Guid id, Guid staffId)
        {
            var issue = await LoadIssueAsync(id);

            await _warehouseAccessGuard.EnsureWarehouseAccessAsync(
                staffId, issue.WarehouseId, "xem phiếu xuất kho", "GoodsIssue", issue.Id.ToString());

            return MapToDto(issue);
        }

        // Lần đầu dùng ClosedXML để GHI (trước giờ chỉ dùng để đọc file Excel import ở PurchaseOrderService).
        public async Task<byte[]> ExportExcelAsync(Guid id, Guid staffId)
        {
            var issue = await LoadIssueAsync(id);

            await _warehouseAccessGuard.EnsureWarehouseAccessAsync(
                staffId, issue.WarehouseId, "xuất Excel phiếu xuất kho", "GoodsIssue", issue.Id.ToString());

            using var workbook = new XLWorkbook();
            var sheet = workbook.AddWorksheet("Phiếu xuất kho");

            sheet.Cell(1, 1).Value = "Mã phiếu";
            sheet.Cell(1, 2).Value = issue.Code;
            sheet.Cell(2, 1).Value = "Loại phiếu";
            sheet.Cell(2, 2).Value = issue.Type.ToString();
            sheet.Cell(3, 1).Value = "Kho";
            sheet.Cell(3, 2).Value = issue.Warehouse?.Name ?? "";
            sheet.Cell(4, 1).Value = "Người xuất";
            sheet.Cell(4, 2).Value = issue.IssuedByUser?.FullName ?? "";
            sheet.Cell(5, 1).Value = "Ngày xuất";
            sheet.Cell(5, 2).Value = (issue.IssueDate ?? issue.CreatedAt).ToString("dd/MM/yyyy HH:mm");
            sheet.Range(1, 1, 5, 1).Style.Font.Bold = true;

            var headerRow = 7;
            sheet.Cell(headerRow, 1).Value = "SKU";
            sheet.Cell(headerRow, 2).Value = "Tên sản phẩm / nguyên liệu";
            sheet.Cell(headerRow, 3).Value = "Số lượng";
            sheet.Range(headerRow, 1, headerRow, 3).Style.Font.Bold = true;

            var row = headerRow + 1;
            foreach (var item in issue.Items)
            {
                sheet.Cell(row, 1).Value = item.Product?.Sku ?? "";
                sheet.Cell(row, 2).Value = item.Product?.Name ?? item.Material?.Name ?? "";
                sheet.Cell(row, 3).Value = item.Quantity;
                row++;
            }

            sheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private async Task<GoodsIssue> LoadIssueAsync(Guid id)
        {
            var issue = await _context.GoodsIssues
                .Include(gi => gi.Warehouse)
                .Include(gi => gi.IssuedByUser)
                .Include(gi => gi.Items).ThenInclude(i => i.Product)
                .Include(gi => gi.Items).ThenInclude(i => i.Material)
                .FirstOrDefaultAsync(gi => gi.Id == id);

            if (issue == null) throw new KeyNotFoundException("Không tìm thấy phiếu xuất kho.");

            return issue;
        }

        // Đọc lại DTO sau khi thao tác đã được authorize -> không guard lại lần nữa.
        private async Task<GoodsIssueDto> LoadDtoAsync(Guid id) => MapToDto(await LoadIssueAsync(id));

        public async Task<GoodsIssueDto> CreateGoodsIssueAsync(CreateGoodsIssueRequestDto request, Guid staffId)
        {
            // WarehouseId đến thẳng từ request nên phải chặn ngay tại cửa, trước mọi validate khác:
            // nếu không, nhân viên kho WH-PE tạo được phiếu xuất cho WH-PROD rồi Post để trừ tồn thật.
            await _warehouseAccessGuard.EnsureWarehouseAccessAsync(
                staffId, request.WarehouseId, "tạo phiếu xuất kho", "GoodsIssue");

            // Ngoại lệ A2: Kiểm tra số biên bản trùng nếu có nhập trước
            if (!string.IsNullOrWhiteSpace(request.PaperDocumentNumber))
            {
                var existsPaperDoc = await _context.GoodsIssues
                    .AnyAsync(gi => gi.PaperDocumentNumber == request.PaperDocumentNumber.Trim());
                if (existsPaperDoc)
                {
                    throw new Exception($"[A2: Trùng chứng từ] Số biên bản giấy '{request.PaperDocumentNumber}' đã tồn tại trong hệ thống.");
                }
            }

            var issue = new GoodsIssue
            {
                Code = $"GI-{DateTime.UtcNow:yyyyMM}-{Guid.NewGuid().ToString().Substring(0, 4).ToUpper()}",
                Type = Enum.TryParse<GoodsIssueType>(request.Type, true, out var t) ? t : GoodsIssueType.Other,
                ReferenceId = request.ReferenceId,
                WarehouseId = request.WarehouseId,
                IssuedByUserId = staffId,
                Status = GoodsIssueStatus.Draft,
                ExternalRecipientName = request.ExternalRecipientName?.Trim(),
                Department = request.Department?.Trim(),
                ReceivedAt = request.ReceivedAt,
                PaperDocumentNumber = request.PaperDocumentNumber?.Trim(),
                UsagePurpose = request.UsagePurpose?.Trim(),
                Note = request.Note?.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            if (issue.Type == GoodsIssueType.ProductionMaterial)
            {
                issue.Status = GoodsIssueStatus.ProofPending;
            }

            foreach (var item in request.Items)
            {
                if (item.ProductId == null && item.MaterialId == null)
                    throw new Exception("Mỗi dòng xuất kho phải chọn ProductId hoặc MaterialId.");
                if (item.ProductId != null && item.MaterialId != null)
                    throw new Exception("Mỗi dòng xuất kho chỉ được chọn 1 trong 2: ProductId hoặc MaterialId.");
                if (item.Quantity <= 0)
                    throw new Exception("Số lượng xuất phải lớn hơn 0.");

                issue.Items.Add(new GoodsIssueItem
                {
                    ProductId = item.ProductId,
                    MaterialId = item.MaterialId,
                    Quantity = item.Quantity,
                    Note = item.Note?.Trim()
                });
            }

            _context.GoodsIssues.Add(issue);
            await _context.SaveChangesAsync();

            return await LoadDtoAsync(issue.Id);
        }

        public async Task<GoodsIssueDto> UploadProofAsync(Guid issueId, Guid staffId, IFormFile file)
        {
            var issue = await _context.GoodsIssues.FirstOrDefaultAsync(gi => gi.Id == issueId);
            if (issue == null) throw new KeyNotFoundException("Không tìm thấy phiếu xuất kho.");

            await _warehouseAccessGuard.EnsureWarehouseAccessAsync(
                staffId, issue.WarehouseId, "đính kèm bằng chứng phiếu xuất kho", "GoodsIssue", issue.Id.ToString());

            if (issue.Status == GoodsIssueStatus.Posted || issue.Status == GoodsIssueStatus.Cancelled || issue.Status == GoodsIssueStatus.Reversed)
            {
                throw new InvalidOperationException("Phiếu xuất kho đã hoàn tất hoặc bị hủy, không thể đính kèm bằng chứng mới.");
            }

            var uploadUrl = await _cloudinaryService.UploadEvidenceAsync(file, "GoodsIssues");
            issue.ImageProofUrl = uploadUrl;

            if (issue.Status == GoodsIssueStatus.ProofPending)
            {
                issue.Status = GoodsIssueStatus.ProofUploaded;
            }

            await _context.SaveChangesAsync();

            return await LoadDtoAsync(issue.Id);
        }

        public async Task<GoodsIssueDto> UpdateHandoverInfoAsync(Guid issueId, Guid staffId, UpdateGoodsIssueHandoverDto dto)
        {
            var issue = await _context.GoodsIssues.FirstOrDefaultAsync(gi => gi.Id == issueId);
            if (issue == null) throw new KeyNotFoundException("Không tìm thấy phiếu xuất kho.");

            await _warehouseAccessGuard.EnsureWarehouseAccessAsync(
                staffId, issue.WarehouseId, "sửa thông tin bàn giao phiếu xuất kho", "GoodsIssue", issue.Id.ToString());

            if (issue.Status == GoodsIssueStatus.Posted || issue.Status == GoodsIssueStatus.Cancelled || issue.Status == GoodsIssueStatus.Reversed)
            {
                throw new InvalidOperationException("Chứng từ đã Post/Hủy/Reversed không thể chỉnh sửa thông tin bàn giao.");
            }

            // Ngoại lệ A2: Kiểm tra số biên bản giấy trùng nhau
            if (!string.IsNullOrWhiteSpace(dto.PaperDocumentNumber))
            {
                var trimmedPaperDoc = dto.PaperDocumentNumber.Trim();
                var existsPaperDoc = await _context.GoodsIssues
                    .AnyAsync(gi => gi.Id != issueId && gi.PaperDocumentNumber == trimmedPaperDoc);

                if (existsPaperDoc)
                {
                    throw new Exception($"[A2: Trùng chứng từ] Số biên bản giấy '{trimmedPaperDoc}' đã tồn tại trong hệ thống.");
                }
                issue.PaperDocumentNumber = trimmedPaperDoc;
            }

            issue.ExternalRecipientName = dto.ExternalRecipientName?.Trim();
            issue.Department = dto.Department?.Trim();
            issue.ReceivedAt = dto.ReceivedAt;
            if (!string.IsNullOrWhiteSpace(dto.UsagePurpose)) issue.UsagePurpose = dto.UsagePurpose.Trim();

            await _context.SaveChangesAsync();
            return await LoadDtoAsync(issue.Id);
        }

        public async Task<GoodsIssueDto> PostGoodsIssueAsync(Guid issueId, Guid staffId)
        {
            // Post là thao tác trừ tồn kho vật lý -> guard NGOÀI transaction, trước khi mở.
            var issueScope = await _context.GoodsIssues
                .AsNoTracking()
                .Where(gi => gi.Id == issueId)
                .Select(gi => new { gi.WarehouseId })
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException("Không tìm thấy phiếu xuất kho.");

            await _warehouseAccessGuard.EnsureWarehouseAccessAsync(
                staffId, issueScope.WarehouseId, "Post phiếu xuất kho", "GoodsIssue", issueId.ToString());

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var issue = await _context.GoodsIssues
                    .Include(gi => gi.Items)
                    .FirstOrDefaultAsync(gi => gi.Id == issueId);

                if (issue == null) throw new KeyNotFoundException("Không tìm thấy phiếu xuất kho.");

                if (issue.Status == GoodsIssueStatus.Posted || issue.Status == GoodsIssueStatus.Cancelled || issue.Status == GoodsIssueStatus.Reversed)
                {
                    throw new InvalidOperationException("Chứng từ đã được Post hoặc bị Hủy trước đó, không thể thao tác lại.");
                }

                // Ngoại lệ A1 (v6.0): Đăng ký xuất nguyên liệu sản xuất bắt buộc phải có đủ 5 trường bằng chứng & thông tin người nhận
                if (issue.Type == GoodsIssueType.ProductionMaterial)
                {
                    if (string.IsNullOrWhiteSpace(issue.ImageProofUrl))
                    {
                        throw new Exception("[A1: Thiếu bằng chứng] Không thể Post phiếu. Bắt buộc chụp/upload ảnh biên bản giấy đã có chữ ký.");
                    }
                    if (string.IsNullOrWhiteSpace(issue.ExternalRecipientName))
                    {
                        throw new Exception("[A1: Thiếu thông tin] Bắt buộc nhập Tên người nhận đại diện sản xuất ngoài hệ thống.");
                    }
                    if (string.IsNullOrWhiteSpace(issue.Department))
                    {
                        throw new Exception("[A1: Thiếu thông tin] Bắt buộc nhập Bộ phận sản xuất nhận nguyên liệu.");
                    }
                    if (!issue.ReceivedAt.HasValue)
                    {
                        throw new Exception("[A1: Thiếu thông tin] Bắt buộc nhập Thời điểm thực tế nhận nguyên liệu.");
                    }
                    if (string.IsNullOrWhiteSpace(issue.PaperDocumentNumber))
                    {
                        throw new Exception("[A1: Thiếu thông tin] Bắt buộc nhập Số biên bản/chứng từ giấy.");
                    }

                    // Ngoại lệ A2: Kiểm tra lại duy nhất số biên bản giấy một lần nữa trước khi Post
                    var existsPaperDoc = await _context.GoodsIssues
                        .AnyAsync(gi => gi.Id != issueId && gi.PaperDocumentNumber == issue.PaperDocumentNumber.Trim());
                    if (existsPaperDoc)
                    {
                        throw new Exception($"[A2: Trùng chứng từ] Số biên bản giấy '{issue.PaperDocumentNumber}' đã tồn tại trong hệ thống.");
                    }
                }

                var defaultLocation = await _context.WarehouseLocations
                    .FirstOrDefaultAsync(l => l.WarehouseId == issue.WarehouseId && l.Type == "Normal");

                if (defaultLocation == null)
                {
                    defaultLocation = await _context.WarehouseLocations
                        .FirstOrDefaultAsync(l => l.WarehouseId == issue.WarehouseId);
                }

                if (defaultLocation == null) throw new Exception("Không tìm thấy vị trí lưu trữ hợp lệ trong kho xuất.");

                foreach (var item in issue.Items)
                {
                    Inventory? inventory = null;
                    if (item.ProductId != null)
                    {
                        inventory = await _context.Inventories.FirstOrDefaultAsync(inv => 
                            inv.ProductId == item.ProductId && 
                            inv.WarehouseLocationId == defaultLocation.Id);
                    }
                    else if (item.MaterialId != null)
                    {
                        inventory = await _context.Inventories.FirstOrDefaultAsync(inv => 
                            inv.MaterialId == item.MaterialId && 
                            inv.WarehouseLocationId == defaultLocation.Id);
                    }

                    int availableQty = inventory != null ? (inventory.OnHandQuantity - inventory.ReservedQuantity) : 0;

                    if (inventory == null || availableQty < item.Quantity)
                    {
                        var itemName = item.ProductId != null ? $"sản phẩm ID {item.ProductId}" : $"nguyên liệu ID {item.MaterialId}";
                        throw new Exception($"[Tồn kho không đủ] Không đủ tồn khả dụng cho {itemName}. Tồn khả dụng hiện có: {availableQty}, Yêu cầu xuất: {item.Quantity}.");
                    }

                    // Giảm tồn kho vật lý
                    inventory.OnHandQuantity -= item.Quantity;
                    inventory.LastUpdatedAt = DateTime.UtcNow;
                    inventory.LastUpdatedByUserId = staffId;

                    // Log biến động tồn kho StockTransaction
                    var tx = new StockTransaction
                    {
                        InventoryId = inventory.Id,
                        ProductId = item.ProductId,
                        MaterialId = item.MaterialId,
                        WarehouseLocationId = defaultLocation.Id,
                        QuantityChange = -item.Quantity, // Âm (Giảm tồn)
                        TransactionType = TransactionType.GoodsIssue,
                        ReferenceId = issue.Id,
                        CreatedByUserId = staffId,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.StockTransactions.Add(tx);
                }

                var statusBeforePost = issue.Status;
                issue.Status = GoodsIssueStatus.Posted;
                issue.IssueDate = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // BR-022: biến động tồn kho là "critical change" bắt buộc có audit trail — trước đây
                // toàn bộ nghiệp vụ kho (xuất/nhập/điều chỉnh/chuyển kho) không ghi AuditLog nào cả.
                var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == staffId);
                await _auditLogService.LogAsync(
                    entityName: "GoodsIssue",
                    entityId: issue.Id.ToString(),
                    action: "POST",
                    actorUserId: staffId,
                    actorEmail: actor?.Email,
                    actorRole: "WarehouseStaff",
                    before: new { Status = statusBeforePost.ToString(), issue.Code },
                    after: new { Status = GoodsIssueStatus.Posted.ToString(), issue.Code, issue.WarehouseId },
                    reason: $"Đăng sổ xuất kho loại {issue.Type}");

                // LoadDtoAsync thay cho GetGoodsIssueByIdAsync: thao tác đã được guard phân quyền kho
                // ở đầu hàm rồi, đọc lại DTO không cần (và không nên) guard thêm lần nữa.
                return await LoadDtoAsync(issue.Id);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<GoodsIssueDto> CreateReversalAsync(Guid issueId, CreateReversalRequestDto dto, Guid staffId)
        {
            if (string.IsNullOrWhiteSpace(dto.ReversalReason))
            {
                throw new Exception("Vui lòng nhập lý do tạo phiếu Reversal đảo chứng từ.");
            }

            // Reversal cộng ngược tồn kho -> guard NGOÀI transaction, trước khi mở.
            var originalScope = await _context.GoodsIssues
                .AsNoTracking()
                .Where(gi => gi.Id == issueId)
                .Select(gi => new { gi.WarehouseId })
                .FirstOrDefaultAsync()
                ?? throw new KeyNotFoundException("Không tìm thấy phiếu xuất gốc.");

            await _warehouseAccessGuard.EnsureWarehouseAccessAsync(
                staffId, originalScope.WarehouseId, "đảo phiếu xuất kho", "GoodsIssue", issueId.ToString());

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var originalIssue = await _context.GoodsIssues
                    .Include(gi => gi.Items)
                    .FirstOrDefaultAsync(gi => gi.Id == issueId);

                if (originalIssue == null) throw new KeyNotFoundException("Không tìm thấy phiếu xuất gốc.");

                if (originalIssue.Status != GoodsIssueStatus.Posted)
                {
                    throw new InvalidOperationException("Chỉ có thể tạo phiếu Reversal cho chứng từ đã Post thành công.");
                }

                if (originalIssue.IsReversal || originalIssue.Status == GoodsIssueStatus.Reversed)
                {
                    throw new InvalidOperationException("Phiếu này đã được đảo chứng từ (Reversal) trước đó.");
                }

                // 1. Tạo phiếu Reversal đối ứng
                var reversalIssue = new GoodsIssue
                {
                    Code = $"GI-REV-{originalIssue.Code}",
                    Type = originalIssue.Type,
                    ReferenceId = originalIssue.ReferenceId,
                    WarehouseId = originalIssue.WarehouseId,
                    IssuedByUserId = staffId,
                    Status = GoodsIssueStatus.Posted, // Phiếu đảo tự động Post ngay
                    IsReversal = true,
                    ReversalForIssueId = originalIssue.Id,
                    ReversalReason = dto.ReversalReason.Trim(),
                    ExternalRecipientName = originalIssue.ExternalRecipientName,
                    Department = originalIssue.Department,
                    ReceivedAt = DateTime.UtcNow,
                    PaperDocumentNumber = $"REV-{originalIssue.PaperDocumentNumber}",
                    UsagePurpose = $"Phiếu đảo cho chứng từ {originalIssue.Code}: {dto.ReversalReason.Trim()}",
                    ImageProofUrl = originalIssue.ImageProofUrl,
                    CreatedAt = DateTime.UtcNow,
                    IssueDate = DateTime.UtcNow
                };

                var defaultLocation = await _context.WarehouseLocations
                    .FirstOrDefaultAsync(l => l.WarehouseId == originalIssue.WarehouseId && l.Type == "Normal");
                if (defaultLocation == null)
                {
                    defaultLocation = await _context.WarehouseLocations.FirstOrDefaultAsync(l => l.WarehouseId == originalIssue.WarehouseId);
                }

                if (defaultLocation == null) throw new Exception("Không tìm thấy vị trí kho hợp lệ.");

                // 2. Đảo ngược tồn kho (+Quantity)
                foreach (var item in originalIssue.Items)
                {
                    reversalIssue.Items.Add(new GoodsIssueItem
                    {
                        ProductId = item.ProductId,
                        MaterialId = item.MaterialId,
                        Quantity = item.Quantity,
                        Note = item.Note
                    });

                    Inventory? inventory = null;
                    if (item.ProductId != null)
                    {
                        inventory = await _context.Inventories.FirstOrDefaultAsync(inv => 
                            inv.ProductId == item.ProductId && inv.WarehouseLocationId == defaultLocation.Id);
                    }
                    else if (item.MaterialId != null)
                    {
                        inventory = await _context.Inventories.FirstOrDefaultAsync(inv => 
                            inv.MaterialId == item.MaterialId && inv.WarehouseLocationId == defaultLocation.Id);
                    }

                    if (inventory != null)
                    {
                        inventory.OnHandQuantity += item.Quantity; // Hoàn lại tồn kho
                        inventory.LastUpdatedAt = DateTime.UtcNow;
                        inventory.LastUpdatedByUserId = staffId;
                    }

                    // Log StockTransaction dương (+Quantity)
                    var tx = new StockTransaction
                    {
                        InventoryId = inventory?.Id ?? Guid.Empty,
                        ProductId = item.ProductId,
                        MaterialId = item.MaterialId,
                        WarehouseLocationId = defaultLocation.Id,
                        QuantityChange = item.Quantity, // Dương (Hoàn tồn)
                        TransactionType = TransactionType.GoodsIssue,
                        ReferenceId = reversalIssue.Id,
                        CreatedByUserId = staffId,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.StockTransactions.Add(tx);
                }

                // 3. Đánh dấu phiếu gốc là Reversed
                originalIssue.Status = GoodsIssueStatus.Reversed;
                originalIssue.ReversalReason = dto.ReversalReason.Trim();

                _context.GoodsIssues.Add(reversalIssue);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                var reversalActor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == staffId);
                await _auditLogService.LogAsync(
                    entityName: "GoodsIssue",
                    entityId: originalIssue.Id.ToString(),
                    action: "REVERSAL",
                    actorUserId: staffId,
                    actorEmail: reversalActor?.Email,
                    actorRole: "WarehouseStaff",
                    before: new { Status = "Posted", originalIssue.Code },
                    after: new { Status = "Reversed", originalIssue.Code, ReversalIssueCode = reversalIssue.Code },
                    reason: dto.ReversalReason.Trim());

                return await LoadDtoAsync(reversalIssue.Id);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private GoodsIssueDto MapToDto(GoodsIssue gi)
        {
            return new GoodsIssueDto
            {
                Id = gi.Id,
                Code = gi.Code,
                Type = gi.Type.ToString(),
                ReferenceId = gi.ReferenceId,
                WarehouseId = gi.WarehouseId,
                WarehouseName = gi.Warehouse != null ? gi.Warehouse.Name : string.Empty,
                IssuedByUserId = gi.IssuedByUserId,
                IssuedByName = gi.IssuedByUser != null ? gi.IssuedByUser.FullName : string.Empty,
                Status = gi.Status.ToString(),
                CreatedAt = gi.CreatedAt,
                IssueDate = gi.IssueDate,
                ImageProofUrl = gi.ImageProofUrl,
                ExternalRecipientName = gi.ExternalRecipientName,
                Department = gi.Department,
                ReceivedAt = gi.ReceivedAt,
                PaperDocumentNumber = gi.PaperDocumentNumber,
                UsagePurpose = gi.UsagePurpose,
                ReversalForIssueId = gi.ReversalForIssueId,
                ReversalReason = gi.ReversalReason,
                IsReversal = gi.IsReversal,
                Note = gi.Note,
                Items = gi.Items.Select(i => new GoodsIssueItemDto
                {
                    Id = i.Id,
                    ProductId = i.ProductId,
                    MaterialId = i.MaterialId,
                    ItemName = i.Product != null ? i.Product.Name : i.Material != null ? i.Material.Name : "N/A",
                    ItemSku = i.Product != null ? i.Product.Sku : string.Empty,
                    ItemType = i.MaterialId != null ? "Material" : "Product",
                    Unit = i.Product != null ? i.Product.Unit : i.Material != null ? i.Material.Unit : string.Empty,
                    Quantity = i.Quantity,
                    Note = i.Note
                }).ToList()
            };
        }
    }
}
