namespace RetailPOS.Infrastructure.Data.Migrations
{
    // Hand-written parts of PosV2P32FinancialPostingOperations. They live in their own file so regenerating the
    // scaffolded migration never loses them.
    public partial class PosV2P32FinancialPostingOperations
    {
        // P3.2-D03: the dedicated permissions go only to roles that already held the equivalent accounting rights
        // (the Phase 3.1 endpoints used accounting.journals.view / accounting.manual_journals.post). Cashier and
        // outlet roles without those accounting permissions receive nothing.
        private const string GrantFinancialPostingPermissionsSql = @"
UPDATE roles r
SET permissions = (
    SELECT COALESCE(jsonb_agg(DISTINCT p), '[]'::jsonb)
    FROM jsonb_array_elements_text(r.permissions || '[""sales.financial_postings.view""]'::jsonb) AS p)
WHERE jsonb_typeof(r.permissions) = 'array'
  AND (r.permissions ? 'accounting.journals.view' OR r.permissions ? 'accounting.manual_journals.post');

UPDATE roles r
SET permissions = (
    SELECT COALESCE(jsonb_agg(DISTINCT p), '[]'::jsonb)
    FROM jsonb_array_elements_text(r.permissions || '[""sales.financial_postings.retry""]'::jsonb) AS p)
WHERE jsonb_typeof(r.permissions) = 'array'
  AND r.permissions ? 'accounting.manual_journals.post';";

        private const string RevokeFinancialPostingPermissionsSql = @"
UPDATE roles r
SET permissions = (
    SELECT COALESCE(jsonb_agg(p), '[]'::jsonb)
    FROM jsonb_array_elements_text(r.permissions) AS p
    WHERE p NOT IN ('sales.financial_postings.view','sales.financial_postings.retry'))
WHERE jsonb_typeof(r.permissions) = 'array';";
    }
}
