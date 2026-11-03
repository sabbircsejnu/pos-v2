using System.Security.Claims;
using RetailPOS.API.DTOs.Pos;
using RetailPOS.Application.Pos.Commands;
using RetailPOS.Application.Pos.ReadModels;

namespace RetailPOS.API.Controllers;

internal static class CashSessionApiMapper
{
    public static long? ResolveUserId(ClaimsPrincipal user)
    {
        var claim = user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
            ?? user.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        return long.TryParse(claim, out var id) && id > 0 ? id : null;
    }

    public static CashSessionDto ToDto(CashSessionReadModel session) => new()
    {
        Id = session.Id,
        BusinessId = session.BusinessId,
        OutletId = session.OutletId,
        TerminalId = session.TerminalId,
        BusinessDate = session.BusinessDate,
        OpenedAt = session.OpenedAt,
        ClosedAt = session.ClosedAt,
        OpeningCashAmount = session.OpeningCashAmount,
        ExpectedClosingCashAmount = session.ExpectedClosingCashAmount,
        ActualClosingCashAmount = session.ActualClosingCashAmount,
        DifferenceAmount = session.DifferenceAmount,
        Status = session.Status,
        OpenedByUserId = session.OpenedByUserId,
        ClosedByUserId = session.ClosedByUserId,
        OpeningNotes = session.OpeningNotes,
        ClosingNotes = session.ClosingNotes,
        IsOverdue = session.IsOverdue,
        MaxDurationHours = session.MaxDurationHours,
        ElapsedSeconds = session.ElapsedSeconds,
        TimeZoneId = session.TimeZoneId,
        CutoffAtUtc = session.CutoffAtUtc,
        WarningLevel = session.WarningLevel,
        State = session.State,
        ServerNowUtc = session.ServerNowUtc,
        VarianceReason = session.VarianceReason
    };

    public static CashSessionCloseSummaryDto ToDto(CashSessionCloseSummaryReadModel summary) => new()
    {
        SessionId = summary.SessionId,
        OutletId = summary.OutletId,
        TerminalId = summary.TerminalId,
        BusinessDate = summary.BusinessDate,
        OpenedAt = summary.OpenedAt,
        ClosingAt = summary.ClosingAt,
        OpenDurationSeconds = summary.OpenDurationSeconds,
        OpeningCash = summary.OpeningCash,
        CashSales = summary.CashSales,
        CashTendered = summary.CashTendered,
        ChangeGiven = summary.ChangeGiven,
        CardSales = summary.CardSales,
        MobileSales = summary.MobileSales,
        OtherNonCashSales = summary.OtherNonCashSales,
        DrawerExpenses = summary.DrawerExpenses,
        CashRefunds = summary.CashRefunds,
        Corrections = summary.Corrections,
        ExpectedClosingCash = summary.ExpectedClosingCash,
        SaleCount = summary.SaleCount,
        IsOverdue = summary.IsOverdue
    };

    public static OpenCashSessionCommand ToCommand(OpenCashSessionDto dto, long outletId, long userId) =>
        new(outletId, dto.TerminalId ?? 0, dto.OpeningCashAmount, dto.OpeningNotes, userId, dto.IdempotencyKey);

    public static CloseCashSessionCommand ToCommand(long sessionId, CloseCashSessionDto dto, long userId) =>
        new(
            sessionId,
            dto.ActualClosingCashAmount,
            dto.ClosingNotes,
            userId,
            dto.VarianceReason,
            dto.Denominations?.Select(item => new CashDenominationInput(item.Value, item.Count)).ToList(),
            dto.Handovers?.Select(item => new CashHandoverInput(
                item.Method,
                item.Amount,
                item.RecipientUserId,
                item.RecipientName,
                item.BankName,
                item.BankAccountReference,
                item.DepositReference,
                item.DepositDate,
                item.Notes)).ToList());
}
