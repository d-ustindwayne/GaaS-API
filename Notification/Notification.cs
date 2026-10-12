//using Microsoft.AspNetCore.Mvc;
//using Microsoft.Azure.Functions.Worker;
//using Microsoft.Azure.Functions.Worker.Extensions.Sql;
//using Microsoft.Extensions.Logging;
//using Newtonsoft.Json;

//namespace GaaS_API.Notification;

//public class Notification
//{
//    private readonly ILogger _logger;

//    public Notification(ILoggerFactory loggerFactory)
//    {
//        _logger = loggerFactory.CreateLogger<Notification>();
//    }

//    [Function("Notification")]
//    async public Task<IActionResult> Run([SqlTrigger("Price", "GaaS_API_DB")] IReadOnlyList<SqlChange<PriceRow>> changes, FunctionContext context)
//    {
//        var stationCount = changes.Select(c => c.Item.StationId).Distinct().Count();
//        var result = PriceChangeNotifier.Build(stationCount);

//        var json = JsonConvert.SerializeObject(result);

//        return new OkObjectResult(json);
//    }
//}

//public class PriceRow
//{
//    public string Id { get; set; }
//    public string StationId { get; set; }
//    public decimal NewPrice { get; set; }
//}

//public class PriceChangeResult
//{
//    public string Title { get; set; }
//    public string Message { get; set; }
//}

//public static class PriceChangeNotifier
//{
//    // right now this is the notification logic but originally i planned on checking the amount of changes and the change value
//    // if some went up or down and it will affect the notification content
//    // i think it will be better if the notification message is modifiable by the admin
//    // but the notification system will be automated nonetheless
//    // i ain't spending my skadoodle here, i'll leave it here for now
//    // time check: 3:42 am 13th of October 2026, i am showerless for 4 days
//    public static PriceChangeResult Build(int stationCount)
//    {
//        if (stationCount < 10) return null;

//        if (stationCount <= 30)
//            return new PriceChangeResult
//            {
//                Title = "Prices just moved",
//                Message = $"{stationCount} stations updated their prices! Check if your regulars changed."
//            };

//        if (stationCount <= 50)
//            return new PriceChangeResult
//            {
//                Title = "Prices shifting fast",
//                Message = $"A wave of price changes just hit {stationCount} stations. Good time to compare."
//            };

//        return new PriceChangeResult
//        {
//            Title = "Major price shake-up",
//            Message = $"{stationCount}+ stations changed prices at once — worth a look before you fill up."
//        };
//    }
//}