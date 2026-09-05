using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// ICommandRepository members: raw deviceCommand CRUD.
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

        /// <summary>Null return means the ux_deviceCommand_device_activekey unique index rejected
        /// the insert - another request won the same check-then-insert race; the caller treats this device as a dedup skip, not an error.</summary>
        public async Task<int?> AddCommandAsync(int deviceId, CommandActionType actionType, DateTime issuedAt, DateTime expiresAt)
        {
            var row = new DeviceCommandRow
            {
                DeviceID = deviceId,
                ActionType = (int)actionType,
                Status = (int)CommandStatus.Pending,
                ActiveKey = (int)actionType,
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

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ClassifyException(ex) == DbFailureKind.ConstraintViolation)
            {
                // SaveChangesAsync runs both statements in one transaction, so nothing was actually
                // persisted - detach/revert so the change tracker matches that and a later
                // SaveChangesAsync on this same context doesn't retry either statement.
                db.Entry(row).State = EntityState.Detached;
                if (device != null)
                {
                    device.CommandVersion--;
                }
                return null;
            }
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
            // Frees the (DeviceID, ActiveKey) unique slot the moment this row stops being active -
            // Acknowledged keeps it set (still active), only the two terminal states clear it.
            if (status is CommandStatus.Executed or CommandStatus.Expired)
            {
                row.ActiveKey = null;
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
