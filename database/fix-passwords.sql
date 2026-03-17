-- Update all user passwords with valid BCrypt hash
-- Password: Admin@123
UPDATE users SET password_hash = '$2a$11$A1K7RvjnXpXriu/iW6S87eDSoHZSG8vZIGYlF4RSsPVgiNUpMm3Su';

-- Verify
SELECT id, email, password_hash FROM users;
