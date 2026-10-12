using GaaS_API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace GaaS_API.Station;

public class StationService
{
    private readonly ILogger<StationService> _logger;
    private readonly DatabaseContext _dbContext;

    public StationService(ILogger<StationService> logger, DatabaseContext dbcontext)
    {
        _logger = logger;
        _dbContext = dbcontext;
    }

    [Function("SearchStation")]
    async public Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "station/search")] HttpRequest req)
    {
        string Brand;
        string Branch;
        string AddressInput;
        int? Quantity;

        if (req.Method == "GET")
        {
            Brand = req.Query["brand"].ToString() ?? string.Empty;
            Branch = req.Query["branch"].ToString() ?? string.Empty;
            AddressInput = req.Query["addressInput"].ToString() ?? string.Empty;
            Quantity = int.TryParse(req.Query["quantity"].ToString(), out int quantityValue) ? quantityValue : null;

        }
        else if (req.Method == "POST")
        {
            var data = await req.ReadFromJsonAsync<StationRequest>();
            if (data == null) return new BadRequestObjectResult("Invalid request body");
            Brand = data.Brand ?? string.Empty;
            Branch = data.Branch ?? string.Empty;
            AddressInput = data.AddressString ?? string.Empty;
            Quantity = data.Quantity;
        }
        else
        {
            return new BadRequestObjectResult("Invalid request method");
        }

        if (string.IsNullOrEmpty(Brand) && string.IsNullOrEmpty(Branch) && string.IsNullOrEmpty(AddressInput))
        {
            return new BadRequestObjectResult("At least one search parameter must be provided");
        }
        if (Quantity.HasValue && Quantity <= 0)
        {
            return new BadRequestObjectResult("Quantity must be greater than zero");
        }


        int BrandID = 0;

        if (!string.IsNullOrEmpty(Brand))
        {
            if (int.TryParse(Brand, out int brandValue))
            {
                BrandID = brandValue;
            }
            else
            {
                BrandID = await
                    (from b in _dbContext.Brands
                     where b.BrandName.ToLower().Contains(Brand.ToLower())
                     select b.BrandID)
                    .FirstOrDefaultAsync();

                if (BrandID == 0)
                {
                    return new NotFoundObjectResult("No stations found. Please try a different search");
                }
            }
        }

        var query =
            (from s in _dbContext.Stations
             join b in _dbContext.Brands on s.Brand equals b.BrandID
             join g in _dbContext.GeoLocations on s.GeoLocation equals g.GeoLocationID
             where
                (BrandID <= 0 || b.BrandID == BrandID) &&
                (string.IsNullOrEmpty(Branch) || s.Branch.Contains(Branch)) &&
                (string.IsNullOrEmpty(AddressInput) || g.AddressString.ToLower().Contains(AddressInput.ToLower()))
             select new
             {
                 AddressString = g.AddressString,
                 Latitude = g.Latitude,
                 Longitude = g.Longitude,
                 BrandName = b.BrandName,
                 Branch = s.Branch
             });

        var result = await 
            (Quantity.HasValue && Quantity.Value > 0 ? query.Take(Quantity.Value) : query)
            .ToListAsync();

        if (result == null || !result.Any()) return new NotFoundObjectResult("No stations found. Please try a different search");

        return new OkObjectResult(result);
    }
}

public class StationRequest
{
    public string? Brand { get; set; }
    public string? Branch { get; set; }
    public string? AddressString { get; set; }
    public int? Quantity { get; set; }
}