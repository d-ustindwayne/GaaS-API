using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using GaaS_API.Models;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace GaaS_API.Price;

public class PriceService
{
    private readonly ILogger<PriceService> _logger;
    private readonly DatabaseContext _dbContext;

    public PriceService(ILogger<PriceService> logger, DatabaseContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    [Function("SearchPrice")]
    async public Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "price/data")] HttpRequest req)
    {
        string? stationQuery;
        string? fuelTypeQuery;
        decimal rangeMin;
        decimal rangeMax;
        bool showAll;
        object result;

        if (req.Method == "GET")
        {
            stationQuery = req.Query["station"].ToString()?? string.Empty;
            fuelTypeQuery = req.Query["fuelType"].ToString()?? string.Empty;
            rangeMin = decimal.TryParse(req.Query["min"], out decimal rangeMinValue) ? rangeMinValue : 1m;
            rangeMax = decimal.TryParse(req.Query["max"], out decimal rangeMaxValue) ? rangeMaxValue : 10.00m;
            showAll = req.Query.ContainsKey("showAll") && bool.Parse(req.Query["showAll"].ToString());
        }
        else if (req.Method == "POST")
        {
            var data = await req.ReadFromJsonAsync<PriceRequest>();
            if (data == null) return new BadRequestObjectResult("Invalid request body");
            stationQuery = data.Station?? string.Empty;
            fuelTypeQuery = data.FuelType?? string.Empty;
            rangeMin = data.RangeMin?? 0m;
            rangeMax = data.RangeMax?? 10.00m;
            showAll = data.ShowAll;
        }
        else
        {
            return new BadRequestObjectResult("Invalid request method");
        }

        int stationID = 0;
        int fuelTypeID = 0;

        if (!string.IsNullOrWhiteSpace(fuelTypeQuery))
        {
            if (int.TryParse(fuelTypeQuery, out int fuelValue))
            {
                fuelTypeID = fuelValue;
            }
            else
            {
                fuelTypeID = await
                    (from f in _dbContext.FuelTypes
                     where f.FuelTypeName.ToLower().Contains(fuelTypeQuery.ToLower())
                     select f.FuelID)
                    .FirstOrDefaultAsync();
            }
        }

        if (!string.IsNullOrWhiteSpace(stationQuery))
        {
            if (int.TryParse(stationQuery, out int parsedStation))
            {
                stationID = parsedStation;
            }
            else
            {
                stationID = await
                    (from s in _dbContext.Stations
                    join b in _dbContext.Brands on s.Brand equals b.BrandID
                    where ($"{b.BrandName} {s.Branch}".ToLower()).Contains(stationQuery.ToLower())
                    select s.StationID)
                .FirstOrDefaultAsync();
            }
        }

        if (rangeMin > rangeMax) return new BadRequestObjectResult("Minimum price cannot be greater than maximum price.");

        if (showAll)
        {
            if (stationID <= 0 && fuelTypeID <= 0) return new BadRequestObjectResult("Please provide at least a Station ID or a Fuel Type ID.");
            var alldata = await
                (from p in _dbContext.Prices
                where (stationID == 0 || p.Station == stationID)
                    && (fuelTypeID == 0 || p.FuelType == fuelTypeID)
                    && p.PriceValue >= rangeMin && p.PriceValue <= rangeMax
                select new
                {
                     priceID = p.PriceID,
                     station = p.Station,
                     fuelType = p.FuelType,
                     priceValue = p.PriceValue
                 })
            .ToListAsync();

            if (alldata == null || !alldata.Any())
                return new NotFoundObjectResult("No result found. Please try a different search");

            result = alldata;
        }
        else
        {
            if (stationID <= 0 || fuelTypeID <= 0) return new BadRequestObjectResult("Station ID and Fuel Type ID must be provided for an exact search.");
            var pdata = await
                (from p in _dbContext.Prices
                 where
                    p.Station == stationID && p.FuelType == fuelTypeID
                    && p.PriceValue >= rangeMin && p.PriceValue <= rangeMax
                 select p)
             .FirstOrDefaultAsync();

            if (pdata == null) return new NotFoundObjectResult("Price data not found for the given station and fuel type");
            result = new
            {
                priceID = pdata.PriceID,
                station = pdata.Station,
                fuelType = pdata.FuelType,
                priceValue = pdata.PriceValue
            };
        }
        return new OkObjectResult(result);
    }
}

public class PriceRequest
{
    public string? Station { get; set; }
    public string? FuelType { get; set; }
    public decimal? RangeMin { get; set; }
    public decimal? RangeMax { get; set; }
    public bool ShowAll { get; set; }
}