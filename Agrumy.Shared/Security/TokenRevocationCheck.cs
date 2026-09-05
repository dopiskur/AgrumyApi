namespace api.Security
{
    /// The DB-backed half of token revocation - a JWT is otherwise self-validating and cannot be un-issued before its own expiry, so this compares its issue time against a per-user cutoff bumped on password change or Enabled->false.
    public static class TokenRevocationCheck
    {
        public static bool IsRevoked(DateTime tokenIssuedAtUtc, DateTime? tokensValidAfterUtc) =>
            tokensValidAfterUtc is DateTime cutoff && tokenIssuedAtUtc < cutoff;
    }
}
