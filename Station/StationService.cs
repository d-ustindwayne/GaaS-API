using GaaS_API.Key;
using GaaS_API.Models;
using GaaS_API.Price;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GaaS_API.Station;

public class StationService
{
    private readonly ILogger<StationService> _logger;
    private readonly DatabaseContext _dbContext;
    private readonly ValidateKey _validateKey;

    public StationService(ILogger<StationService> logger, DatabaseContext dbcontext)
    {
        _logger = logger;
        _dbContext = dbcontext;
        _validateKey = new ValidateKey(dbcontext);
    }

    [Function("SearchStation")]
    async public Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "station/search")] HttpRequest req)
    {
        // Establish variables to hold the query parameters
        string brand;
        string branch;
        string addressInput;
        int quantity;
        string apiKey;

        // Variables to hold values later
        bool isAdmin;
        object result;

        // Determine if the request is a GET or POST and extract the parameters accordingly
        if (req.Method == "GET")
        {
            brand = req.Query["brand"].ToString() ?? string.Empty;
            branch = req.Query["branch"].ToString() ?? string.Empty;
            addressInput = req.Query["address"].ToString() ?? string.Empty;
            quantity = int.TryParse(req.Query["quantity"].ToString(), out int quantityValue) ? quantityValue : 0;
            apiKey = req.Query["apiKey"].ToString() ?? string.Empty;
        }
        else if (req.Method == "POST")
        {
            var data = await req.ReadFromJsonAsync<StationRequestParams>();
            if (data == null) return new BadRequestObjectResult("Invalid request body");
            brand = data.Brand ?? string.Empty;
            branch = data.Branch ?? string.Empty;
            addressInput = data.Address ?? string.Empty;
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
        IQueryable<StationResponse> query;

        // Validate the price range parameters
        if (!isAdmin && string.IsNullOrEmpty(brand) && string.IsNullOrEmpty(branch) && string.IsNullOrEmpty(addressInput)) return new BadRequestObjectResult("At least one search parameter must be provided");
        if (quantity < 0) return new BadRequestObjectResult("quantity must be greater than zero");

        int BrandID = 0;

        if (!string.IsNullOrEmpty(brand))
        {
            if (int.TryParse(brand, out int brandValue))
            {
                BrandID = brandValue;
            }
            else
            {
                BrandID = await
                    (from b in _dbContext.Brands
                     where b.BrandName.ToLower().Contains(brand.ToLower())
                     select b.BrandID)
                    .FirstOrDefaultAsync();

                if (BrandID == 0)
                {
                    return new NotFoundObjectResult("No stations found. Please try a different search");
                }
            }
        }

        // Run the query to get the stations based on the search parameters
        query =
            (from s in _dbContext.Stations
             join b in _dbContext.Brands on s.Brand equals b.BrandID
             join g in _dbContext.GeoLocations on s.GeoLocation equals g.GeoLocationID
             where
                (BrandID <= 0 || b.BrandID == BrandID) &&
                (string.IsNullOrEmpty(branch) || s.Branch.Contains(branch)) &&
                (string.IsNullOrEmpty(addressInput) || g.AddressString.ToLower().Contains(addressInput.ToLower()))
             select new StationResponse
             {
                 AddressString = g.AddressString,
                 Latitude = g.Latitude,
                 Longitude = g.Longitude,
                 BrandName = b.BrandName,
                 Branch = s.Branch
             });

        // Check quantity if
        // 0 = return all results
        // 0 > = return the specified number of results
        // if quantity is more than what is available, it will still return all available results from the query without error
        var alldata = await (quantity > 0 ? query.Take(quantity) : query).ToListAsync();
        if (alldata == null || !alldata.Any()) return new NotFoundObjectResult("No stations found. Please try a different search");

        result = alldata;
        return new OkObjectResult(result);
    }
}

public class StationRequestParams
{
    public string? Brand { get; set; }
    public string? Branch { get; set; }
    public string? Address { get; set; }
    public int Quantity { get; set; }
    public string Key { get; set; }
}

public class StationResponse
{
    public string AddressString { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public string BrandName { get; set; }
    public string Branch { get; set; }
}