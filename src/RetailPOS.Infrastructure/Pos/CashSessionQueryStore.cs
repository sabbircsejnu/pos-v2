using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.Pos;

public sealed class CashSessionQueryStore(RetailPOSDbContext dbContext) : ICashSessionQueryStore
{
    public async Task<CashSessionListPage> ListAsync(CashSessionListFilter filter, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var query = dbContext.CashSessions.AsNoTracking().Where(session => session.OutletId == filter.OutletId);
        if (filter.TerminalId.HasValue)
            query = query.Where(session => session.TerminalId == filter.TerminalId.Value);
        if (filter.BusinessDateFrom.HasValue)
            query = query.Where(session => session.BusinessDate >= filter.BusinessDateFrom.Value);
        if (filter.BusinessDateTo.HasValue)
            query = query.Where(session => session.BusinessDate <= filter.BusinessDateTo.Value);
        if (filter.OpenedByUserId.HasValue)
            query = query.Where(session => session.OpenedByUserId == filter.OpenedByUserId.Value);
        query = filter.State switch
        {
            "closed" => query.Where(session => session.Status == CashSessionStatuses.Closed),
            "open" => query.Where(session => session.Status == CashSessionStatuses.Open
                && session.OpenedAt.AddHours(session.DurationSnapshotHours) > now),
            "overdue" => query.Where(session => session.Status == CashSessionStatuses.Open
                && session.OpenedAt.AddHours(session.DurationSnapshotHours) <= now),
            _ => query
        };

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(session => session.OpenedAt)
            .ThenByDescending(session => session.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(session => new
            {
                Session = session,
                OutletName = session.Outlet.Name,
                TerminalName = session.Terminal.Name,
                OpenedByName = session.OpenedByUser.Name,
                ClosedByName = session.ClosedByUser != null ? session.ClosedByUser.Name : null,
                VarianceStatus = dbContext.CashDifferenceApprovals
                    .Where(approval => approval.CashSessionId == session.Id)
                    .Select(approval => approval.Status)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(row => new CashSessionListItem(
            row.Session.Id,
            row.Session.OutletId,
            row.OutletName,
            row.Session.TerminalId,
            row.TerminalName,
            row.Session.BusinessDate.Date,
            UtcTime.Normalize(row.Session.OpenedAt),
            UtcTime.Normalize(row.Session.ClosedAt),
            row.Session.OpeningCashAmount,
            row.Session.ExpectedClosingCashAmount,
            row.Session.ActualClosingCashAmount,
            row.Session.DifferenceAmount,
            row.Session.Status,
            CashSessionMapper.StateOf(row.Session, now),
            row.Session.DurationSnapshotHours,
            row.Session.OpenedByUserId,
            row.OpenedByName,
            row.Session.ClosedByUserId,
            row.ClosedByName,
            row.VarianceStatus)).ToList();
        return new CashSessionListPage(items, filter.Page, filter.PageSize, total, now);
    }

    public async Task<CashSessionDetailReadModel?> GetDetailAsync(
        long sessionId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.CashSessions
            .AsNoTracking()
            .Include(candidate => candidate.Outlet)
            .Include(candidate => candidate.Terminal)
            .Include(candidate => candidate.OpenedByUser)
            .Include(candidate => candidate.ClosedByUser)
            .FirstOrDefaultAsync(candidate => candidate.Id == sessionId, cancellationToken);
        if (session is null)
            return null;

        var sales = await dbContext.Sales
            .AsNoTracking()
            .Where(sale => sale.CashSessionId == sessionId)
            .OrderBy(sale => sale.CreatedAt)
            .Select(sale => new
            {
                sale.Id,
                sale.SaleNumber,
                sale.Status,
                sale.PaymentMethod,
                sale.TotalAmount,
                sale.CreatedAt,
                Cashier = sale.Cashier.Name,
                Payments = sale.Payments
                    .Select(payment => new CashSessionPaymentFact(
                        payment.Method, payment.Amount, payment.Tendered, payment.ChangeGiven, payment.NetCashRetained))
                    .ToList()
            })
            .ToListAsync(cancellationToken);
        var movements = await dbContext.CashSessionMovements
            .AsNoTracking()
            .Where(movement => movement.CashSessionId == sessionId)
            .OrderBy(movement => movement.Id)
            .ToListAsync(cancellationToken);

        var facts = new CashSessionFacts(
            CashSessionMapper.ToSnapshot(session),
            sales.Select(sale => new CashSessionSaleFact(sale.Id, sale.Status, sale.PaymentMethod, sale.TotalAmount, sale.Payments)).ToList(),
            movements.Select(movement => new CashSessionMovementFact(
                movement.Id, movement.MovementType, movement.CashEffect, movement.SourceType, movement.SourceId,
                movement.OriginalSaleId, movement.OriginalCashSessionId, UtcTime.Normalize(movement.RecordedAtUtc))).ToList());
        var summary = CashSessionExpectedCashCalculator.Calculate(
            facts,
            session.ClosedAt.HasValue ? UtcTime.Normalize(session.ClosedAt.Value) : UtcTime.Normalize(nowUtc));

        var snapshot = await dbContext.CashSessionCloseSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.CashSessionId == sessionId, cancellationToken);
        var handovers = await dbContext.CashSessionHandovers.AsNoTracking()
            .Where(handover => handover.CashSessionId == sessionId)
            .OrderBy(handover => handover.Id)
            .ToListAsync(cancellationToken);
        var attachments = await dbContext.CashSessionAttachments.AsNoTracking()
            .Where(attachment => attachment.CashSessionId == sessionId)
            .OrderBy(attachment => attachment.Id)
            .ToListAsync(cancellationToken);
        var approval = await dbContext.CashDifferenceApprovals.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.CashSessionId == sessionId, cancellationToken);

        var userIds = handovers.Select(handover => handover.GivenByUserId).Distinct().ToList();
        var userNames = await dbContext.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.Name, cancellationToken);

        var saleModels = sales.Select(sale =>
        {
            var cash = sale.Payments
                .Where(payment => string.Equals(payment.Method, "cash", StringComparison.OrdinalIgnoreCase))
                .Sum(payment => payment.NetCashRetained ?? payment.Amount);
            return new CashSessionSaleReadModel(
                sale.Id, sale.SaleNumber, sale.Status, sale.TotalAmount, cash, sale.TotalAmount - cash,
                UtcTime.Normalize(sale.CreatedAt), sale.Cashier);
        }).ToList();

        var handoverModels = handovers.Select(handover => new CashSessionHandoverReadModel(
            handover.Id, handover.Method, handover.Amount, handover.Status, handover.GivenByUserId,
            userNames.GetValueOrDefault(handover.GivenByUserId), handover.RecipientUserId, handover.RecipientName,
            handover.BankName, handover.BankAccountReference, handover.DepositReference, handover.DepositDate,
            handover.Notes, UtcTime.Normalize(handover.CreatedAtUtc), UtcTime.Normalize(handover.ConfirmedAtUtc),
            handover.ConfirmedByUserId)).ToList();
        var movementModels = movements.Select(movement => new CashSessionMovementReadModel(
            movement.Id, movement.MovementType, movement.CashEffect, movement.SourceType, movement.SourceId,
            movement.OriginalSaleId, movement.OriginalCashSessionId, movement.Notes,
            UtcTime.Normalize(movement.RecordedAtUtc), movement.RecordedByUserId)).ToList();
        var attachmentModels = attachments.Select(attachment => new CashSessionAttachmentReadModel(
            attachment.Id, attachment.HandoverId, attachment.FileName, attachment.ContentType, attachment.SizeBytes,
            attachment.Sha256, attachment.UploadedByUserId, UtcTime.Normalize(attachment.UploadedAtUtc))).ToList();
        var approvalModel = approval is null ? null : ToApprovalModel(approval);

        var timeline = new List<CashSessionTimelineEntry>
        {
            new(UtcTime.Normalize(session.OpenedAt), "opened", "Session opened",
                $"Opening cash {session.OpeningCashAmount:F2}", session.OpenedByUserId)
        };
        timeline.AddRange(movementModels.Select(movement => new CashSessionTimelineEntry(
            movement.RecordedAtUtc, "movement", $"Cash movement: {movement.MovementType}",
            $"{movement.CashEffect:F2}", movement.RecordedByUserId)));
        if (session.ClosedAt.HasValue)
            timeline.Add(new CashSessionTimelineEntry(
                UtcTime.Normalize(session.ClosedAt.Value), "closed", "Session closed",
                $"Expected {session.ExpectedClosingCashAmount:F2}, counted {session.ActualClosingCashAmount:F2}",
                session.ClosedByUserId));
        timeline.AddRange(handoverModels.Where(handover => handover.ConfirmedAtUtc.HasValue).Select(handover =>
            new CashSessionTimelineEntry(handover.ConfirmedAtUtc!.Value, "handover-confirmed",
                "Handover confirmed", $"{handover.Method} {handover.Amount:F2}", handover.ConfirmedByUserId)));
        timeline.AddRange(attachmentModels.Select(attachment => new CashSessionTimelineEntry(
            attachment.UploadedAtUtc, "attachment", "Attachment added", attachment.FileName, attachment.UploadedByUserId)));
        if (approvalModel is not null)
        {
            timeline.Add(new CashSessionTimelineEntry(approvalModel.CreatedAtUtc, "variance", "Variance recorded",
                $"{approvalModel.VarianceAmount:F2}", approvalModel.CreatedByUserId));
            if (approvalModel.ReviewedAtUtc.HasValue)
                timeline.Add(new CashSessionTimelineEntry(approvalModel.ReviewedAtUtc.Value, "variance-decision",
                    $"Variance {approvalModel.Status}", approvalModel.ReviewNotes, approvalModel.ReviewedByUserId));
            if (approvalModel.PostedAtUtc.HasValue)
                timeline.Add(new CashSessionTimelineEntry(approvalModel.PostedAtUtc.Value, "variance-posted",
                    "Variance journal posted", $"Journal {approvalModel.JournalEntryId}", null));
        }

        return new CashSessionDetailReadModel(
            CashSessionMapper.ToReadModel(session, nowUtc),
            session.Outlet.Name,
            session.Terminal.Name,
            session.OpenedByUser.Name,
            session.ClosedByUser?.Name,
            summary,
            snapshot is null ? null : ToSnapshotModel(snapshot),
            handoverModels,
            movementModels,
            attachmentModels,
            saleModels,
            approvalModel,
            timeline.OrderBy(entry => entry.AtUtc).ToList());
    }

    public async Task<CashDifferenceListPage> ListVariancesAsync(
        long businessId,
        IReadOnlyCollection<long>? outletIds,
        CashDifferenceListFilter filter,
        CancellationToken cancellationToken)
    {
        var query = dbContext.CashDifferenceApprovals.AsNoTracking().Where(approval => approval.BusinessId == businessId);
        if (outletIds is not null)
            query = query.Where(approval => outletIds.Contains(approval.OutletId));
        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(approval => approval.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.PostingStatus))
            query = query.Where(approval => approval.PostingStatus == filter.PostingStatus);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(approval => approval.CreatedAtUtc)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(approval => new
            {
                Approval = approval,
                OutletName = approval.CashSession.Outlet.Name,
                approval.CashSession.BusinessDate,
                approval.CashSession.TerminalId
            })
            .ToListAsync(cancellationToken);
        return new CashDifferenceListPage(
            rows.Select(row => new CashDifferenceListItem(
                ToApprovalModel(row.Approval), row.Approval.OutletId, row.OutletName, row.BusinessDate.Date, row.TerminalId)).ToList(),
            filter.Page,
            filter.PageSize,
            total);
    }

    internal static CashDifferenceApprovalReadModel ToApprovalModel(CashDifferenceApproval approval) =>
        CashSessionMapper.ToApprovalModel(approval);

    private static CashSessionCloseSnapshotReadModel ToSnapshotModel(CashSessionCloseSnapshot snapshot) =>
        new(
            snapshot.OpeningCash, snapshot.CashSalesRetained, snapshot.CashTendered, snapshot.ChangeGiven,
            snapshot.CardTotal, snapshot.MobileTotal, snapshot.DrawerExpenses, snapshot.CashRefunds,
            snapshot.Corrections, snapshot.ExpectedCash, snapshot.CountedCash, snapshot.Variance,
            snapshot.VarianceReason, snapshot.HandedOverTotal, snapshot.RetainedAtOutlet, snapshot.DenominationsJson,
            snapshot.Notes, snapshot.ClosedByUserId, UtcTime.Normalize(snapshot.ClosedAtUtc),
            snapshot.WasOverdueAtClose, snapshot.SnapshotHash);

    public async Task<BusinessDaySalesTotals> GetBusinessDaySalesAsync(long outletId, DateTime businessDate, CancellationToken cancellationToken)
    {
        var date = businessDate.Date;
        var groups = await dbContext.Sales
            .AsNoTracking()
            .Where(sale => sale.OutletId == outletId && sale.BusinessDate == date)
            .GroupBy(sale => sale.Status)
            .Select(group => new
            {
                Status = group.Key,
                Count = group.Count(),
                Total = group.Sum(sale => sale.TotalAmount),
                Tax = group.Sum(sale => sale.Tax),
                Discount = group.Sum(sale => sale.Discount)
            })
            .ToListAsync(cancellationToken);

        var completed = groups.FirstOrDefault(group => group.Status == "completed");
        var refunded = groups.FirstOrDefault(group => group.Status == "refunded");
        return new BusinessDaySalesTotals(
            completed?.Count ?? 0,
            completed?.Total ?? 0m,
            completed?.Tax ?? 0m,
            completed?.Discount ?? 0m,
            groups.Where(group => group.Status == "voided").Sum(group => group.Count),
            refunded?.Count ?? 0,
            refunded?.Total ?? 0m);
    }
}
