using GaaS_API.Key;
using GaaS_API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GaaS_API.Price;

public class PriceService
{
    private readonly ILogger<PriceService> _logger;
    private readonly DatabaseContext _dbContext;
    private readonly ValidateKey _validateKey;

    public PriceService(ILogger<PriceService> logger, DatabaseContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
        _validateKey = new ValidateKey(dbContext);
    }

    [Function("SearchPrice")]
    async public Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "price/search")] HttpRequest req)
    {
        // Establish variables to hold the query parameters
        string? stationQuery;
        string? fuelTypeQuery;
        decimal rangeMin;
        decimal rangeMax;
        int quantity;
        string apiKey;

        // Variables to hold values later
        bool isAdmin;
        object result;

        // Determine if the request is a GET or POST and extract the parameters accordingly
        if (req.Method == "GET")
        {
            stationQuery = req.Query["station"].ToString() ?? string.Empty;
            fuelTypeQuery = req.Query["fuelType"].ToString() ?? string.Empty;
            rangeMin = decimal.TryParse(req.Query["min"], out decimal rangeMinValue) ? rangeMinValue : 1m;
            rangeMax = decimal.TryParse(req.Query["max"], out decimal rangeMaxValue) ? rangeMaxValue : 10.00m;
            quantity = int.TryParse(req.Query["quantity"].ToString(), out int quantityValue) ? quantityValue : 0;
            apiKey = req.Query["key"].ToString() ?? string.Empty;
        }
        else if (req.Method == "POST")
        {
            var data = await req.ReadFromJsonAsync<PriceRequestParams>();
            if (data == null) return new BadRequestObjectResult("Invalid request body");
            stationQuery = data.Station ?? string.Empty;
            fuelTypeQuery = data.FuelType ?? string.Empty;
            rangeMin = data.RangeMin ?? 0m;
            rangeMax = data.RangeMax ?? 10.00m;
            quantity = data.Quantity;
            apiKey = data.Key ?? string.Empty;
        }
        // If the request method is neither GET nor POST, return a bad request response
        else
        {
            return new BadRequestObjectResult("Invalid request method");
        }

        // Validate the API key
        if (string.IsNullOrWhiteSpace(apiKey)) return new BadRequestObjectResult("API apiKey is required");
        if (!_validateKey.IsValidKey(apiKey)) return new UnauthorizedObjectResult("Invalid API apiKey");

        // Add 1 to KeyCallCount for the API key used in this request
        await _dbContext.ApiKeys.Where(k => k.KeyValue == apiKey).ExecuteUpdateAsync(setters => setters.SetProperty(k => k.KeyCallCount, k => k.KeyCallCount + 1));

        isAdmin = _validateKey.GetPermissionLevel(apiKey);
        IQueryable<PriceResponse> query;

        // Initialize lists to hold the IDs of stations and fuel types
        List<int> stationIDList = new List<int>();
        List<int> fuelTypeIDList = new List<int>();

        // If the station query is not empty, attempt to parse it as an integer. If parsing fails, perform a database search for matching station names
        if (!string.IsNullOrWhiteSpace(stationQuery))
        {
            if (int.TryParse(stationQuery, out int parsedStation))
            {
                stationIDList.Add(parsedStation);
            }
            else
            {
                stationIDList = await
                    (from s in _dbContext.Stations
                     join b in _dbContext.Brands on s.Brand equals b.BrandID
                     select new { s.StationID, Station = $"{b.BrandName} {s.Branch}" })
                     .AsEnumerable()
                     .Where(x => x.Station.Contains(stationQuery, StringComparison.OrdinalIgnoreCase))
                     .Select(x => x.StationID)
                     .ToAsyncEnumerable()
                     .ToListAsync();
            }
        }

        // If the fuel type query is not empty, attempt to parse it as an integer. If parsing fails, perform a database search for matching fuel type names
        if (!string.IsNullOrWhiteSpace(fuelTypeQuery))
        {
            string[] numberFuels = { "91", "95", "98" };
            if (int.TryParse(fuelTypeQuery, out int fuelValue) && !numberFuels.All(a => fuelTypeQuery.Contains(a)))
            {
                fuelTypeIDList.Add(fuelValue);
            }
            else
            {
                fuelTypeIDList = await
                    (from f in _dbContext.FuelTypes
                     where f.FuelTypeName.ToLower().Contains(fuelTypeQuery.ToLower())
                     select f.FuelID)
                .ToListAsync();
            }
        }

        // Validate the price range parameters
        if (rangeMin > rangeMax) return new BadRequestObjectResult("Minimum price cannot be greater than maximum price");

        if (quantity < 0) return new BadRequestObjectResult("quantity must be greater than zero");


        if (!isAdmin && stationIDList.Count == 0 && fuelTypeIDList.Count == 0) return new BadRequestObjectResult("Provide at least a Station or a Fuel Type by ID or Name");

        // Run the query to get the prices based on the search parameters
        query =
            (from p in _dbContext.Prices
             join s in _dbContext.Stations on p.Station equals s.StationID
             join b in _dbContext.Brands on s.Brand equals b.BrandID
             join f in _dbContext.FuelTypes on p.FuelType equals f.FuelID
             where (stationIDList.Count == 0 || (p.Station.HasValue && stationIDList.Contains(p.Station.Value)))
                && (fuelTypeIDList.Count == 0 || (p.FuelType.HasValue && fuelTypeIDList.Contains(p.FuelType.Value)))
                && p.PriceValue >= rangeMin && p.PriceValue <= rangeMax
             select new PriceResponse
             {
                 PriceID = p.PriceID,
                 StationName = $"{b.BrandName} {s.Branch}",
                 FuelType = f.FuelTypeName,
                 PriceValue = p.PriceValue
             });

        // Return all record if admin and no parameters are provided

        // Check quantity if
        // 0 = return all results
        // 0 > = return the specified number of results
        // if quantity is more than what is available, it will still return all available results from the query without error
        List<PriceResponse> alldata;

        alldata = await (quantity > 0 ? query.Take(quantity) : query).ToListAsync();
        if (alldata == null || !alldata.Any()) return new NotFoundObjectResult("No result found. Please try a different search");


        result = alldata;
        return new OkObjectResult(result);
    }
}

public class PriceRequestParams
{
    public string? Station { get; set; }
    public string? FuelType { get; set; }
    public decimal? RangeMin { get; set; }
    public decimal? RangeMax { get; set; }
    public int Quantity { get; set; }
    public string? Key { get; set; }
}

public class PriceResponse
{
    public int PriceID { get; set; }
    public string? StationName { get; set; }
    public string? FuelType { get; set; }
    public decimal PriceValue { get; set; }
}