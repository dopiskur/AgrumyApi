namespace api.Utils
{
    /// <summary>Shared bound for WaterPump's device-side hard safety limits (seconds), used by both ServerConfigApiController and DeviceApiController so the two save paths can never accept different ranges for the same fields. Null/0 is a real, intentional "disabled" state; only a negative value or an unreasonable upper bound is rejected.</summary>
    public static class SafetyLimitValidation
    {
        public const int MaxReasonableSeconds = 86400; // 24h

        public static bool IsValid(int? seconds) => seconds is null or (>= 0 and <= MaxReasonableSeconds);
    }
}
