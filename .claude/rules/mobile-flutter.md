---
paths:
  - "retailpos-mobile/**"
---

# Flutter business mobile app (`retailpos-mobile/`)

- Specs: `Docs/Mobile/IRANIMART_BUSINESS_MOBILE_SRS_v1.4.md` and `..._LLD_v1.4.md` (highest version wins). CI: `.github/workflows/flutter-mobile.yml`.
- The mobile app consumes the existing `Mobile*` API controllers; the server enforces permissions and business/outlet scope.
- Follow `analysis_options.yaml` and the existing `lib/` structure; verify with `flutter analyze` and focused `flutter test`. No new packages without approval.
- No offline loyalty redemption; offline stock-take submissions must remain idempotent.
