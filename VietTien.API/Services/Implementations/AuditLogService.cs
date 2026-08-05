using System.Text;
using Microsoft.EntityFrameworkCore;
using VietTien.API.Data;
using VietTien.API.DTOs.Admin;
using VietTien.API.DTOs.Order;
using VietTien.API.Infrastructure.Security;
using VietTien.API.Models;
using VietTien.API.Services.Interfaces;

namespace VietTien.API.Services.Implementations
{
    public class AuditLogService : IAuditLogService
    {
        private readonly ApplicationDbContext _context;

        public AuditLogService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task LogAsync(
            string entityName,
            string entityId,
            string action,
            Guid? actorUserId,
            string? actorEmail,
            string? actorRole,
            object? before,
            object? after,
            string? reason = null,
            string? ipAddress = null)
        {
            var log = new AuditLog
            {
                EntityName = entityName,
                EntityId = entityId,
                Action = action,
                ActorUserId = actorUserId,
                ActorEmail = actorEmail,
                ActorRole = actorRole,
                BeforeJson = SensitiveDataRedactor.RedactToJson(before),
                AfterJson = SensitiveDataRedactor.RedactToJson(after),
                Reason = reason,
                IpAddress = ipAddress,
                CreatedAt = DateTime.UtcNow
            };

            _context.AuditLogs.Add(log);
            await _context.SaveChangesAsync();
        }

        public async Task<PagedResultDto<AuditLogDto>> SearchAsync(AuditLogQueryDto query)
        {
            var page = query.Page < 1 ? 1 : query.Page;
            var pageSize = query.PageSize is < 1 or > 200 ? 20 : query.PageSize;

            var filtered = ApplyFilters(_context.AuditLogs.AsNoTracking(), query);

            var totalCount = await filtered.CountAsync();

            var entities = await filtered
                .OrderByDescending(a => a.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var items = entities.Select(ToDto).ToList();

            return new PagedResultDto<AuditLogDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            };
        }

        public async Task<byte[]> ExportCsvAsync(AuditLogQueryDto query)
        {
            const int maxRows = 10000;

            var filtered = ApplyFilters(_context.AuditLogs.AsNoTracking(), query);

            var rows = await filtered
                .OrderByDescending(a => a.CreatedAt)
                .Take(maxRows)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("CreatedAt,EntityName,EntityId,Action,ActorEmail,ActorRole,Reason,IpAddress,BeforeJson,AfterJson");

            foreach (var row in rows)
            {
                sb.AppendLine(string.Join(",",
                    CsvEscape(row.CreatedAt.ToString("O")),
                    CsvEscape(row.EntityName),
                    CsvEscape(row.EntityId),
                    CsvEscape(row.Action),
                    CsvEscape(row.ActorEmail),
                    CsvEscape(row.ActorRole),
                    CsvEscape(row.Reason),
                    CsvEscape(row.IpAddress),
                    CsvEscape(row.BeforeJson),
                    CsvEscape(row.AfterJson)));
            }

            return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        }

        private static IQueryable<AuditLog> ApplyFilters(IQueryable<AuditLog> source, AuditLogQueryDto query)
        {
            if (!string.IsNullOrWhiteSpace(query.EntityName))
                source = source.Where(a => a.EntityName == query.EntityName);

            if (!string.IsNullOrWhiteSpace(query.Action))
                source = source.Where(a => a.Action == query.Action);

            if (query.ActorUserId.HasValue)
                source = source.Where(a => a.ActorUserId == query.ActorUserId.Value);

            if (query.FromDate.HasValue)
                source = source.Where(a => a.CreatedAt >= query.FromDate.Value);

            if (query.ToDate.HasValue)
                source = source.Where(a => a.CreatedAt <= query.ToDate.Value);

            if (!string.IsNullOrWhiteSpace(query.SearchQuery))
            {
                var q = query.SearchQuery.Trim();
                source = source.Where(a =>
                    a.EntityId.Contains(q) ||
                    (a.ActorEmail != null && a.ActorEmail.Contains(q)) ||
                    (a.Reason != null && a.Reason.Contains(q)));
            }

            return source;
        }

        private static AuditLogDto ToDto(AuditLog a) => new()
        {
            Id = a.Id,
            EntityName = a.EntityName,
            EntityId = a.EntityId,
            Action = a.Action,
            ActorUserId = a.ActorUserId,
            ActorEmail = a.ActorEmail,
            ActorRole = a.ActorRole,
            BeforeJson = a.BeforeJson,
            AfterJson = a.AfterJson,
            Reason = a.Reason,
            IpAddress = a.IpAddress,
            CreatedAt = a.CreatedAt
        };

        private static string CsvEscape(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.IndexOfAny(new[] { '"', ',', '\n', '\r' }) >= 0)
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }
    }
}
