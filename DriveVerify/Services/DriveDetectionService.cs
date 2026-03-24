using System.IO;
using DriveVerify.Models;

namespace DriveVerify.Services;

public static class DriveDetectionService
{
    public static IEnumerable<DriveItem> GetDrives(bool includeFixed = false)
    {
        var drives = new List<DriveItem>();

        foreach (var driveInfo in DriveInfo.GetDrives())
        {
            try
            {
                bool isRemovable = driveInfo.DriveType == DriveType.Removable;

                if (!isRemovable && !includeFixed)
                    continue;

                if (driveInfo.DriveType != DriveType.Removable && driveInfo.DriveType != DriveType.Fixed)
                    continue;

                var item = new DriveItem
                {
                    DriveLetter = driveInfo.Name.TrimEnd('\\'),
                    IsRemovable = isRemovable,
                    IsReady = driveInfo.IsReady
                };

                if (driveInfo.IsReady)
                {
                    item.VolumeLabel = driveInfo.VolumeLabel;
                    item.FileSystem = driveInfo.DriveFormat;
                    item.TotalSize = driveInfo.TotalSize;
                    item.FreeSpace = driveInfo.AvailableFreeSpace;
                }

                drives.Add(item);
            }
            catch (IOException)
            {
                // Drive not ready or inaccessible
            }
            catch (UnauthorizedAccessException)
            {
                // No permission to access drive info
            }
        }

        return drives;
    }
}
