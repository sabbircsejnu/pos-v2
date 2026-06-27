# User Invitation and Password - Current Implementation (POS v2)

Updated: 2026-06-07

## Current implementation summary

- Invitation-based password setup is implemented for onboarding users.
- Invitation tokens are stored as SHA-256 hashes and have expiration timestamps.
- User must complete invitation to clear MustResetPassword and use normal login.
- Login blocks users with MustResetPassword true.
- Owner-specific access reset invitation is implemented for BusinessOwner by Super Admin endpoint.
- Refresh token persistence, rotation, and revocation are implemented.

## Implementation status

- Implemented
	- Onboarding invitation and first password setup.
	- Owner reset invitation flow.
	- Refresh token hashing, storage, rotation, expiry validation, and logout revocation.
- Partially Implemented
	- Password self-service and admin-password-management boundary is not cleanly separated.
- Planned
	- Generic forgot-password and reset-password flow for all users.
	- Stronger secure invitation delivery and exposure controls.

## Existing files/classes/services involved

- src/RetailPOS.API/Controllers/AuthController.cs
- src/RetailPOS.API/Services/AuthService.cs
- src/RetailPOS.API/Services/TokenService.cs
- src/RetailPOS.API/Controllers/BusinessesController.cs
- src/RetailPOS.API/Services/BusinessOnboardingService.cs
- src/RetailPOS.Core/Entities/UserInvitation.cs
- src/RetailPOS.Core/Entities/UserRefreshToken.cs
- src/RetailPOS.Core/Entities/User.cs
- src/RetailPOS.API/Services/UserService.cs

## Current flow explanation

### Onboarding invitation setup

1. Business onboarding creates user with random temporary password hash.
2. User is created with MustResetPassword true.
3. Raw invitation token is generated and only token hash is stored.
4. Invitation purpose onboarding and expires-at timestamp are saved.
5. Token is returned in onboarding response payload.

### Invitation completion

1. Client sends token and new password to auth complete-invitation endpoint.
2. Service hashes provided token and matches stored token hash.
3. Service validates invitation unused and not expired.
4. User password hash is replaced with new password hash.
5. MustResetPassword is set false and invitation is marked consumed.
6. Access token and refresh token are returned.

### Login and password behavior

1. Login verifies active user and password hash.
2. If MustResetPassword is true, login is rejected.
3. Authenticated users can change password through users change-password endpoint by providing current password.

### Refresh token lifecycle

1. Refresh token is generated as cryptographically random value.
2. Only token hash is stored server-side.
3. On refresh request:
	- access token claims are validated for user extraction
	- refresh token hash must exist and be active
	- expired token is revoked
	- token rotation revokes current token and persists replacement hash
4. On logout, active refresh tokens for the user are revoked.

### Owner reset access

1. Super Admin calls businesses owner reset-access endpoint.
2. Existing active invitations for that owner are consumed.
3. New owner-reset invitation token is generated and stored as hash.
4. Owner MustResetPassword is set true.

## Gaps or risks found

- No generic forgot-password endpoint for non-owner users.
- Invitation token is returned in API response; secure delivery is outside service boundary.
- Change-password endpoint is under users.edit permission and does not separate self-service from admin-driven operation.
- Access token deny-list is not implemented; revocation applies to refresh tokens.

## Recommended improvements

- Implement generic forgot-password and reset-password workflow for all users.
- Add invitation delivery mechanism with secure transport and optional one-time link UX.
- Add self-service password change endpoint scoped to current authenticated user.
- Keep current refresh token lifecycle and add operational monitoring for reuse/revocation events.
- Add policy and audit requirements for all password and invitation actions.

## Issue register

### Issue 1: Generic forgot-password flow is not implemented

1. Current implementation:
	- Only invitation completion and owner-reset invitation flows exist.
2. Why it is a problem:
	- Non-owner users lack a standardized password recovery path.
3. Recommended solution:
	- Add forgot-password and reset-password endpoints with token TTL, one-time use, and audit trail.
4. Priority:
	- High

### Issue 2: Access-token revocation model is limited to token expiry

1. Current implementation:
	- Refresh token lifecycle is implemented with hashed persistence and rotation.
2. Why it is a problem:
	- N/A as a missing implementation issue; remaining risk is lack of access-token deny-list.
3. Recommended solution:
	- Continue current implementation and evaluate short access-token TTL or deny-list strategy for high-risk scenarios.
4. Priority:
	- Low

### Issue 3: Logout revokes refresh tokens, but not active access tokens

1. Current implementation:
	- Logout revokes active refresh-token records.
2. Why it is a problem:
	- Access tokens remain valid until expiry because access-token deny-list is not implemented.
3. Recommended solution:
	- Keep refresh-token revocation and optionally add access-token deny-list for high-risk sessions.
4. Priority:
	- Medium

### Issue 4: Invitation token handling relies on external delivery controls

1. Current implementation:
	- Raw invitation token is returned from API for external distribution.
2. Why it is a problem:
	- Token exposure risk depends on operational discipline outside code boundaries.
3. Recommended solution:
	- Integrate secure delivery and minimize token exposure in logs and API payload handling.
4. Priority:
	- Medium

## Acceptance criteria

- Invitation tokens must be hashed at rest and validated for expiry and one-time use.
- Users with MustResetPassword true cannot authenticate through normal login.
- Completing invitation must clear MustResetPassword and consume invitation token.
- Owner reset access must invalidate previous active owner invitations and issue a new one.
- Generic forgot-password flow is implemented or explicitly marked unsupported in product behavior documentation.
- Session token lifecycle includes refresh-token persistence, rotation, and revocation controls.
