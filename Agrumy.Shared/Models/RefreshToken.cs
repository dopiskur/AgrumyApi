using System.ComponentModel.DataAnnotations;

namespace api.Models
{
    /// <summary>DAL-facing view of a stored refresh token — never the plaintext, only its hash lives in the DB.</summary>
    public class RefreshTokenInfo
    {
        public int UserID { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
    }

    public class RefreshTokenRequest
    {
        [Required(ErrorMessage = "Refresh token is required")]
        public string? RefreshToken { get; set; }
    }
}
