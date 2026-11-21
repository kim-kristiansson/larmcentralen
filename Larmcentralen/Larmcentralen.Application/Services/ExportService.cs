namespace Larmcentralen.Application.Services;

public class ExportService
{
    public async Task<(byte[] Bytes, string FileName)?> ExportAlarmToDocxAsync(int alarmId)
    {
        return ([], "foo");
    }
}