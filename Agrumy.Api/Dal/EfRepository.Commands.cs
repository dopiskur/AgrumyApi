using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>ICommandRepository members: raw deviceCommand CRUD.</summary>
    internal partial class EfRepository
    {
        public async Task<bool> HasActiveCommandAsync(int deviceId, CommandActionType actionType, DateTime utcNow)
        {
            int[] activeStatuses = [(int)CommandStatus.Pending, (int)CommandStatus.Acknowledged];
            return await db.DeviceCommands.AsNoTracking().AnyAsync(c =>
                c.DeviceID == deviceId &&
                c.ActionType == (int)actionType &&
                activeStatuses.Contains(c.Status) &&
                c.ExpiresAt > utcNow);
        }

        public async Task<int> AddCommandAsync(int deviceId, CommandActionType actionType, DateTime issuedAt, DateTime expiresAt)
        {
            var row = new DeviceCommandRow
            {
                DeviceID = deviceId,
                ActionType = (int)actionType,
                Status = (int)CommandStatus.Pending,
                IssuedAt = issuedAt,
                ExpiresAt = expiresAt,
            };
            db.DeviceCommands.Add(row);

            // CommandVersion is deliberately separate from ConfigVersion - bumped here and nowhere else.
            var device = await db.Devices.FirstOrDefaultAsync(d => d.IDDevice == deviceId);
            if (device != null)
            {
                device.CommandVersion++;
            }

            await db.SaveChangesAsync();
            return row.IDDeviceCommand;
        }

        public async Task<IList<DeviceCommand>> GetPendingCommandsAsync(int deviceId)
        {
            var rows = await db.DeviceCommands.AsNoTracking()
                .Where(c => c.DeviceID == deviceId && c.Status == (int)CommandStatus.Pending)
                .OrderBy(c => c.IssuedAt)
                .ToListAsync();
            return rows.Select(ToDto).ToList();
        }

        public async Task<DeviceCommand?> GetCommandByIdAsync(int commandId)
        {
            var row = await db.DeviceCommands.AsNoTracking().FirstOrDefaultAsync(c => c.IDDeviceCommand == commandId);
            return row == null ? null : ToDto(row);
        }

        public async Task SetCommandStatusAsync(int commandId, CommandStatus status, DateTime? executedAt = null)
        {
            var row = await db.DeviceCommands.FirstOrDefaultAsync(c => c.IDDeviceCommand == commandId);
            if (row == null)
            {
                return;
            }
            row.Status = (int)status;
            if (executedAt != null)
            {
                row.ExecutedAt = executedAt;
            }
            await db.SaveChangesAsync();
        }

        private static DeviceCommand ToDto(DeviceCommandRow c) => new()
        {
            IDDeviceCommand = c.IDDeviceCommand,
            DeviceID = c.DeviceID,
            ActionType = (CommandActionType)c.ActionType,
            Status = (CommandStatus)c.Status,
            IssuedAt = c.IssuedAt,
            ExpiresAt = c.ExpiresAt,
            ExecutedAt = c.ExecutedAt,
        };
    }
}
