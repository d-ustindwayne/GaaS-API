using GaaS_API.Key;
using GaaS_API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenTelemetry;

namespace GaaS_API.Price;

public class CRUD
{
    private readonly ILogger<CRUD> _logger;
    private readonly DatabaseContext _dbContext;
    private readonly ValidateKey _validateKey;
    private const string _rootpath = "price";

    public CRUD(ILogger<CRUD> logger, DatabaseContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
        _validateKey = new ValidateKey(dbContext);
    }

    // Created this for validating the role within the API key. Not in Key since It needs to return a Task<IActionResult> for the function to return the proper response
    private async Task<IActionResult?> ValidateAdminAccessAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return new BadRequestObjectResult("API Key is required");
        if (!_validateKey.IsValidKey(key)) return new UnauthorizedObjectResult("Invalid API Key");

        await _dbContext.ApiKeys.Where(k => k.KeyValue == key).ExecuteUpdateAsync(setters => setters.SetProperty(k => k.KeyCallCount, k => k.KeyCallCount + 1));

        if (!_validateKey.GetPermissionLevel(key)) return new ObjectResult("Admin permission required") { StatusCode = StatusCodes.Status403Forbidden };
        return null;
    }

    [Function("PriceCreate")]
    async public Task<IActionResult> Create([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = _rootpath + "/create")] HttpRequest req)
    {
        // Establish variables to hold the query parameters
        int station;
        int fuelType;
        decimal priceValue;
        string apiKey;

        // Variables to hold values later
        string stationName;
        string fuelTypeName;

        // Determine if the request is a GET or POST and extract the parameters accordingly
        if (req.Method == "GET")
        {
            station = int.TryParse(req.Query["station"], out int stationValue) ? stationValue : 0;
            fuelType = int.TryParse(req.Query["fuelType"], out int fuelValue) ? fuelValue : 0;
            priceValue = decimal.TryParse(req.Query["priceValue"], out decimal priceVal) ? priceVal : 0m;
            apiKey = req.Query["apiKey"].ToString() ?? string.Empty;
        }
        else if (req.Method == "POST")
        {
            var data = await req.ReadFromJsonAsync<PriceCreateRequest>();
            if (data == null) return new BadRequestObjectResult("Invalid request body");
            station = data.Station;
            fuelType = data.FuelType;
            priceValue = data.PriceValue;
            apiKey = data.Key ?? string.Empty;
        }
        // If the request method is neither GET nor POST, return a bad request response
        else
        {
            return new BadRequestObjectResult("Invalid request method");
        }

        // Validate the API key and admin access
        var accessError = await ValidateAdminAccessAsync(apiKey);
        if (accessError != null) return accessError;

        if (station == 0 || fuelType == 0 || priceValue <= 0m)
            return new BadRequestObjectResult("All fields are required.");

        if (!await _dbContext.Stations.AnyAsync(s => s.StationID == station))
            return new BadRequestObjectResult("Station does not exist.");
        if (!await _dbContext.FuelTypes.AnyAsync(f => f.FuelID == fuelType))
            return new BadRequestObjectResult("FuelType does not exist.");

        // Create a new Price entity and add it to the database
        _dbContext.Prices.Add(new Models.Price
        {
            Station = station,
            FuelType = fuelType,
            PriceValue = priceValue
        });
        await _dbContext.SaveChangesAsync();

        var price = _dbContext.Prices
            .Include(p => p.StationNavigation)
            .Include(p => p.FuelTypeNavigation)
            .FirstOrDefault(p => p.PriceID == _dbContext.Prices.First().PriceID);
        stationName = price.StationNavigation?.BrandNavigation?.BrandName ?? station.ToString();
        fuelTypeName = price.FuelTypeNavigation?.FuelTypeName ?? fuelType.ToString();

        return new OkObjectResult(new
        {
            message = $"New price data of {price.PriceValue} for station {price.StationNavigation?.BrandNavigation?.BrandName} {price.StationNavigation?.Branch} has been created"
        });
    }

    [Function("PriceUpdate")]
    async public Task<IActionResult> Update([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = _rootpath + "/update")] HttpRequest req)
    {
        // Establish variables to hold the query parameters
        int priceID;
        int? station;
        int? fuelType;
        decimal? priceValue;
        string apiKey;

        // Determine if the request is a GET or POST and extract the parameters accordingly
        if (req.Method == "GET")
        {
            priceID = int.TryParse(req.Query["priceID"], out int idValue) ? idValue : 0;
            station = int.TryParse(req.Query["station"], out int stationValue) ? stationValue : (int?)null;
            fuelType = int.TryParse(req.Query["fuelType"], out int fuelValue) ? fuelValue : (int?)null;
            priceValue = decimal.TryParse(req.Query["priceValue"], out decimal priceVal) ? priceVal : (decimal?)null;
            apiKey = req.Query["apiKey"].ToString() ?? string.Empty;
        }
        else if (req.Method == "POST")
        {
            var data = await req.ReadFromJsonAsync<PriceUpdateRequest>();
            if (data == null) return new BadRequestObjectResult("Invalid request body");
            priceID = data.PriceID;
            station = data.Station;
            fuelType = data.FuelType;
            priceValue = data.PriceValue;
            apiKey = data.Key ?? string.Empty;
        }
        // If the request method is neither GET nor POST, return a bad request response
        else
        {
            return new BadRequestObjectResult("Invalid request method");
        }

        // Validate the API key and admin access
        var accessError = await ValidateAdminAccessAsync(apiKey);
        if (accessError != null) return accessError;

        if (priceID <= 0) return new BadRequestObjectResult("A valid PriceID is required");

        // Get the price entity by provided priceID
        var price = await _dbContext.Prices.FirstOrDefaultAsync(p => p.PriceID == priceID);
        if (price == null) return new NotFoundObjectResult("Price not found");

        // Update the price entity with provided values if they are not null
        if (station.HasValue)
        {
            if (!await _dbContext.Stations.AnyAsync(s => s.StationID == station.Value))
                return new BadRequestObjectResult("Station does not exist.");
            price.Station = station.Value;
        }
        if (fuelType.HasValue)
        {
            if (!await _dbContext.FuelTypes.AnyAsync(f => f.FuelID == fuelType.Value))
                return new BadRequestObjectResult("FuelType does not exist.");
            price.FuelType = fuelType.Value;
        }
        if (priceValue.HasValue) price.PriceValue = priceValue.Value;

        await _dbContext.SaveChangesAsync();
        return new OkObjectResult("OK");
    }

    [Function("PriceDelete")]
    async public Task<IActionResult> Delete([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = _rootpath + "/delete")] HttpRequest req)
    {
        // Establish variables to hold the query parameters
        int priceID;
        string apiKey;

        // Determine if the request is a GET or POST and extract the parameters accordingly
        if (req.Method == "GET")
        {
            priceID = int.TryParse(req.Query["priceID"], out int idValue) ? idValue : 0;
            apiKey = req.Query["apiKey"].ToString() ?? string.Empty;
        }
        else if (req.Method == "POST")
        {
            var data = await req.ReadFromJsonAsync<PriceDeleteRequest>();
            if (data == null) return new BadRequestObjectResult("Invalid request body");
            priceID = data.PriceID;
            apiKey = data.Key ?? string.Empty;
        }
        // If the request method is neither GET nor POST, return a bad request 
        else
        {
            return new BadRequestObjectResult("Invalid request method");
        }

        // Validate the API key and admin access
        var accessError = await ValidateAdminAccessAsync(apiKey);
        if (accessError != null) return accessError;

        if (priceID <= 0) return new BadRequestObjectResult("A valid PriceID is required");

        // Get the price entity by provided priceID
        var price = await _dbContext.Prices.FirstOrDefaultAsync(p => p.PriceID == priceID);
        if (price == null) return new NotFoundObjectResult("Price not found");

        // Remove the price entity from the database
        _dbContext.Prices.Remove(price);
        await _dbContext.SaveChangesAsync();

        return new OkObjectResult("OK");
    }
}

public class PriceCreateRequest
{
    public int Station { get; set; }
    public int FuelType { get; set; }
    public decimal PriceValue { get; set; }
    public string? Key { get; set; }
}

public class PriceUpdateRequest
{
    public int PriceID { get; set; }
    public int? Station { get; set; }
    public int? FuelType { get; set; }
    public decimal? PriceValue { get; set; }
    public string? Key { get; set; }
}

public class PriceDeleteRequest
{
    public int PriceID { get; set; }
    public string? Key { get; set; }
}