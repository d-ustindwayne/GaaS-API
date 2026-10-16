using GaaS_API.Key;
using GaaS_API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Core.Invocation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using System.ComponentModel.DataAnnotations;

namespace GaaS_API.Station;

public class CRUD
{
    private readonly ILogger<CRUD> _logger;
    private readonly DatabaseContext _dbContext;
    private readonly ValidateKey _validateKey;
    private const string _rootpath = "station";

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

    [Function("StationCreate")]
    async public Task<IActionResult> Create([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = _rootpath + "/create")] HttpRequest req)
    {
        // Establish variables to hold the query parameters
        string address;
        decimal latitude;
        decimal longitude;
        int brand;
        string branch;
        string apiKey;

        // Determine if the request is a GET or POST and extract the parameters accordingly
        if (req.Method == "GET")
        {
            address = req.Query["address"].ToString() ?? string.Empty;
            latitude = decimal.TryParse(req.Query["latitude"], out decimal latValue) ? latValue : 0m;
            longitude = decimal.TryParse(req.Query["longitude"], out decimal longValue) ? longValue : 0m;
            brand = int.TryParse(req.Query["brand"], out int brandValue) ? brandValue : 0;
            branch = req.Query["branch"].ToString() ?? string.Empty;
            apiKey = req.Query["apiKey"].ToString() ?? string.Empty;
        }
        else if (req.Method == "POST")
        {
            var data = await req.ReadFromJsonAsync<StationCreateRequest>();
            if (data == null) return new BadRequestObjectResult("Invalid request body");
            address = data.Address ?? string.Empty;
            latitude = data.Latitude;
            longitude = data.Longitude;
            brand = data.Brand;
            branch = data.Branch ?? string.Empty;
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

        if (string.IsNullOrWhiteSpace(address) || latitude == 0m || longitude == 0m || brand == 0 || string.IsNullOrWhiteSpace(branch))
        {
            return new BadRequestObjectResult("All fields are required.");
        }

        if (!await _dbContext.Brands.AnyAsync(b => b.BrandID == brand))
            return new BadRequestObjectResult("Brand does not exist.");

        // Save the GeoLocation first so its real (DB-generated) ID is available for the Station FK
        var geoLocation = new GeoLocation
        {
            AddressString = address,
            Latitude = latitude,
            Longitude = longitude
        };

        // Save the GeoLocation to the database
        _dbContext.GeoLocations.Add(geoLocation);
        await _dbContext.SaveChangesAsync();

        // And then add the Station with the GeoLocationID FK
        _dbContext.Stations.Add(new Models.Station
        {
            GeoLocation = geoLocation.GeoLocationID,
            Brand = brand,
            Branch = branch
        });
        await _dbContext.SaveChangesAsync();

        return new OkObjectResult(new { message = $"New station {brand} {branch} has been created" });
    }

    [Function("StationUpdate")]
    async public Task<IActionResult> Update([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = _rootpath + "/update")] HttpRequest req)
    {
        // Establish variables to hold the query parameters
        int stationID;
        string? address;
        decimal? latitude;
        decimal? longitude;
        int? brand;
        string? branch;
        string apiKey;

        // Determine if the request is a GET or POST and extract the parameters accordingly
        if (req.Method == "GET")
        {
            stationID = int.TryParse(req.Query["stationID"], out int idValue) ? idValue : 0;
            address = string.IsNullOrEmpty(req.Query["address"]) ? null : req.Query["address"].ToString();
            latitude = decimal.TryParse(req.Query["latitude"], out decimal latValue) ? latValue : (decimal?)null;
            longitude = decimal.TryParse(req.Query["longitude"], out decimal longValue) ? longValue : (decimal?)null;
            brand = int.TryParse(req.Query["brand"], out int brandValue) ? brandValue : (int?)null;
            branch = string.IsNullOrEmpty(req.Query["branch"]) ? null : req.Query["branch"].ToString();
            apiKey = req.Query["apiKey"].ToString() ?? string.Empty;
        }
        else if (req.Method == "POST")
        {
            var data = await req.ReadFromJsonAsync<StationUpdateRequest>();
            if (data == null) return new BadRequestObjectResult("Invalid request body");
            stationID = data.StationID;
            address = data.Address;
            latitude = data.Latitude;
            longitude = data.Longitude;
            brand = data.Brand;
            branch = data.Branch;
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

        if (stationID <= 0) return new BadRequestObjectResult("A valid StationID is required");

        // Retrieve the station from the database
        var station = await _dbContext.Stations.FirstOrDefaultAsync(s => s.StationID == stationID);
        if (station == null) return new NotFoundObjectResult("Station not found");

        // Update the station properties if they are provided
        if (brand.HasValue)
        {
            if (!await _dbContext.Brands.AnyAsync(b => b.BrandID == brand.Value))
                return new BadRequestObjectResult("Brand does not exist.");
            station.Brand = brand.Value;
        }
        if (!string.IsNullOrWhiteSpace(branch)) station.Branch = branch;

        if (address != null || latitude.HasValue || longitude.HasValue)
        {
            var geoLocation = await _dbContext.GeoLocations.FirstOrDefaultAsync(g => g.GeoLocationID == station.GeoLocation);
            if (geoLocation != null)
            {
                if (!string.IsNullOrWhiteSpace(address)) geoLocation.AddressString = address;
                if (latitude.HasValue) geoLocation.Latitude = latitude.Value;
                if (longitude.HasValue) geoLocation.Longitude = longitude.Value;
            }
        }

        await _dbContext.SaveChangesAsync();
        return new OkObjectResult(new { message = $"Station ID of {stationID} ({brand} {branch}) has been updated" });
    }

    [Function("StationDelete")]
    async public Task<IActionResult> Delete([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = _rootpath + "/delete")] HttpRequest req)
    {
        // Establish variables to hold the query parameters
        int stationID;
        string apiKey;

        // Variables to hold values later
        string brand;
        string branch;

        // Determine if the request is a GET or POST and extract the parameters accordingly
        if (req.Method == "GET")
        {
            stationID = int.TryParse(req.Query["stationID"], out int idValue) ? idValue : 0;
            apiKey = req.Query["apiKey"].ToString() ?? string.Empty;
        }
        else if (req.Method == "POST")
        {
            var data = await req.ReadFromJsonAsync<StationDeleteRequest>();
            if (data == null) return new BadRequestObjectResult("Invalid request body");
            stationID = data.StationID;
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

        if (stationID <= 0) return new BadRequestObjectResult("A valid StationID is required");

        // Retrieve the station from the database
        var station = await _dbContext.Stations
            .Include(s => s.BrandNavigation)
            .FirstOrDefaultAsync(s => s.StationID == stationID);

        if (station == null) return new NotFoundObjectResult("Station not found");

        // Get brand and branch after the query
        brand = station.BrandNavigation?.BrandName ?? string.Empty;
        branch = station.Branch;

        // Attempt to delete the station, handling potential foreign key constraint violations
        _dbContext.Stations.Remove(station);

        // Added this to prevent deletion if Prices or other related records still reference this station
        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return new ConflictObjectResult("Cannot delete this station because related records (mainly: prices) still reference it.");
        }

        return new OkObjectResult(new { message = $"Station ID of {stationID} ({brand} {branch}) has been deleted" });
    }
}

public class StationCreateRequest
{
    public string? Address { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public int Brand { get; set; }
    public string? Branch { get; set; }
    public string? Key { get; set; }
}

public class StationUpdateRequest
{
    public int StationID { get; set; }
    public string? Address { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public int? Brand { get; set; }
    public string? Branch { get; set; }
    public string? Key { get; set; }
}

public class StationDeleteRequest
{
    public int StationID { get; set; }
    public string? Key { get; set; }
}