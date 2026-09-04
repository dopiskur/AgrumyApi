namespace api.Dal.Interface
{
    /// <summary>
    /// Full data-layer contract, composed from the per-domain facets. Controllers keep injecting
    /// this - their flows routinely cross domains - while infrastructure with a narrow need injects
    /// just its facet (DbExceptionFilter takes ISystemRepository, DeviceApiKeyHandler takes
    /// IDeviceRepository). Program.cs forwards every facet registration to the same scoped
    /// EfRepository instance, so which interface a consumer picks is purely a visibility choice.
    /// </summary>
    public interface IRepository :
        ISystemRepository,
        IServerConfigRepository,
        IUserRepository,
        ITenantRepository,
        IRefreshTokenRepository,
        IDeviceRepository,
        IDeviceUnitRepository,
        ICommandRepository,
        IFirmwareRepository,
        ISensorDataRepository,
        IAuditLogRepository
    {
    }
}
