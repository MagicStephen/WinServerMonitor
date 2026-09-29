using WinServerMonitor.Core.Metrics;

namespace WinServerMonitor.Infrastructure.Metrics;

internal static class DriveSampler
{
    private const long MinimumSize = 1L << 30;

    public static IReadOnlyList<DriveUsage> Sample()
    {
        var result = new List<DriveUsage>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                // Skip read-only images and tiny system mounts (relevant on Linux hosts).
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady || drive.TotalSize < MinimumSize ||
                    drive.DriveFormat is "squashfs" or "iso9660")
                {
                    continue;
                }

                result.Add(new DriveUsage(drive.Name, drive.VolumeLabel, drive.DriveFormat, drive.TotalSize, drive.AvailableFreeSpace));
            }
            catch
            {
                // Drive disappeared or is not accessible.
            }
        }

        return result;
    }
}
