using GaaS_API.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GaaS_API.Notification;

// reference: https://learn.microsoft.com/en-us/azure/azure-signalr/signalr-concept-azure-functions
public class NotificationService
{
    private readonly ILogger<NotificationService> _logger;
    private readonly DatabaseContext _dbContext;

    public NotificationService(ILogger<NotificationService> logger, DatabaseContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    // SignalR needs this exact shape (HttpRequestData + plain string return) to hand clients a connection,
    // so it doesn't follow the HttpRequest/IActionResult pattern the rest of the services use -- that's on purpose, not a mistake.
    [Function("negotiate")]
    public string Negotiate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req,
        [SignalRConnectionInfoInput(HubName = "price-notifications")] string connectionInfo)
    {
        return connectionInfo;
    }

    // Runs every minute, checks whatever the trigger logged since last time, and blasts a notification out if it's worth one.
    [Function("CheckPriceChanges")]
    [SignalROutput(HubName = "price-notifications", ConnectionStringSetting = "AzureSignalRConnectionString")]
    async public Task<SignalRMessageAction?> CheckPriceChanges([TimerTrigger("0 */1 * * * *")] TimerInfo timer)
    {
        // grab log id alongside station id so we know exactly which rows to clear after, instead of nuking the whole table
        var changes = await _dbContext.PriceChangeLogs.Select(c => new { c.LogID, c.StationID }).ToListAsync();
        if (!changes.Any()) return null;

        int stationCount = changes.Select(c => c.StationID).Distinct().Count();

        // only clear what we just read -- if the trigger drops new rows in while this is running, they just get picked up next minute
        int maxLogID = changes.Max(c => c.LogID);
        await _dbContext.PriceChangeLogs.Where(c => c.LogID <= maxLogID).ExecuteDeleteAsync();

        var result = BuildNotification(stationCount);
        if (result == null) return null;

        _logger.LogInformation($"Broadcasting price change notification for {stationCount} stations.");

        return new SignalRMessageAction("priceChangeNotification")
        {
            Arguments = new object[] { result }
        };
    }

    // right now this is the notification logic but originally i planned on checking the amount of changes and the change value
    // if some went up or down and it will affect the notification content
    // i think it will be better if the notification message is modifiable by the admin
    // but the notification system will be automated nonetheless
    private static PriceChangeResult? BuildNotification(int stationCount)
    {
        if (stationCount < 10) return null;

        if (stationCount <= 30)
            return new PriceChangeResult
            {
                Title = "Prices just moved",
                Message = $"{stationCount} stations updated their prices! Check if your regulars changed."
            };

        if (stationCount <= 50)
            return new PriceChangeResult
            {
                Title = "Prices shifting fast",
                Message = $"A wave of price changes just hit {stationCount} stations. Good time to compare."
            };

        return new PriceChangeResult
        {
            Title = "Major price shake-up",
            Message = $"{stationCount}+ stations changed prices at once — worth a look before you fill up."
        };
    }
}

// never gets saved anywhere, it only exists long enough to get pushed out to whoever's listening
public class PriceChangeResult
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}