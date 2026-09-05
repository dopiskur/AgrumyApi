namespace api.Dal.Interface
{
    /// Full data-layer contract composed from the per-domain facets below - controllers inject this since their flows cross domains, narrow infrastructure injects just its facet, both resolve to the same scoped EfRepository instance.
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
        IAuditLogRepository,
        IRelayRepository,
        IDiscoveryRepository
    {
    }
}
