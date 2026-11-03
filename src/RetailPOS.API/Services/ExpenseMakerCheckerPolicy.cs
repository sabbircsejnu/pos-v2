using RetailPOS.API.Authorization;
using RetailPOS.Application.Expenses.Abstractions;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Core.Entities;

namespace RetailPOS.API.Services;

/// <summary>
/// D7: POS Drawer and Petty Cash expenses are approved by someone other than the creator and the submitter. The
/// only exception is a Business Owner acting under an explicitly enabled Business setting AND holding the exact
/// <c>expenses.self_approve</c> claim — the "*" wildcard, an alias or <c>accounting.expenses.approve</c> never qualifies.
/// </summary>
public sealed class ExpenseMakerCheckerPolicy(
    IHttpContextAccessor httpContextAccessor,
    IRoleSwitchContext roleContext,
    IBusinessPosSettingsStore settingsStore) : IExpenseMakerCheckerPolicy
{
    public const string SelfApprovePermission = "expenses.self_approve";

    public async Task<bool> EvaluateAsync(Expense expense, long approverUserId, CancellationToken cancellationToken)
    {
        if (!ExpensePaymentSources.RequiresMakerChecker(expense.PaymentSource))
            return false;

        var isMaker = expense.CreatedBy == approverUserId || expense.SubmittedBy == approverUserId;
        if (!isMaker)
            return false;

        var settings = await settingsStore.GetAsync(expense.BusinessId, cancellationToken);
        var principal = httpContextAccessor.HttpContext?.User;
        if (settings.AllowOwnerExpenseSelfApproval
            && roleContext.IsBusinessOwner
            && principal.HasExplicitPermission(SelfApprovePermission))
            return true;

        throw new CashSessionException(
            CashSessionErrorCodes.ExpenseMakerChecker,
            "A POS Drawer or Petty Cash expense must be approved by someone other than the user who created or submitted it.",
            403);
    }
}
