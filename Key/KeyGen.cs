using GaaS_API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace GaaS_API.Key;

public class KeyGen
{
    private readonly ILogger<KeyGen> _logger;
    private readonly DatabaseContext _dbContext;

    public KeyGen(ILogger<KeyGen> logger, DatabaseContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    [Function("KeyGen")]
    async public Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "api/genkey")] HttpRequest req)
    {
        // Establish variables to hold the query parameters
        string KeyName;
        int Permission;

        // Determine if the request is a GET or POST and extract the parameters accordingly
        if (req.Method == "GET")
        {
            string __KeyName = req.Query["KeyName"].ToString();
            string __Permission = req.Query["Permission"].ToString();
            if (string.IsNullOrEmpty(__KeyName) && string.IsNullOrEmpty(__Permission)) return new BadRequestObjectResult("Parameter must be provided");
            if (string.IsNullOrEmpty(__Permission)) return new BadRequestObjectResult("Permission parameter is required");
            KeyName =  __KeyName ?? "GaaS_API_KEY";
            Permission = int.TryParse(__Permission, out int permissionValue)? permissionValue : 0;
        }
        else if (req.Method == "POST")
        {
            var data = req.ReadFromJsonAsync<Dictionary<string, object>>().Result;
            if (data == null) return new BadRequestObjectResult("Invalid request body");
            string __KeyName = data["KeyName"].ToString();
            string __Permission = data["Permission"].ToString();
            if (string.IsNullOrEmpty(__KeyName) && string.IsNullOrEmpty(__Permission)) return new BadRequestObjectResult("Parameter must be provided");
            KeyName = __KeyName?.ToString() ?? "GaaS_API_KEY";
            if (__Permission == null || string.IsNullOrEmpty(__Permission?.ToString())) return new BadRequestObjectResult("Permission parameter is required");
            Permission = int.TryParse(__Permission?.ToString(), out int permissionValue) ? permissionValue : 0;
        }
        // If the request method is neither GET nor POST, return a bad request response
        else
        {
            return new BadRequestObjectResult("Invalid request method");
        }

        if (Permission != 0 && Permission != 1) return new BadRequestObjectResult("Invalid Permission value");

        string apiKey;
        string HashedKey;
        do
        {
            string randomText = Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(24))
                .Replace('+', '-')
                .Replace('/', '_');

            string suffix = Permission == 1 ? "1-as0x" : "0-as00";
            apiKey = $"ga_{randomText}-{suffix}";
            HashedKey = Convert.ToHexString(SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(apiKey)));
        } while (await _dbContext.ApiKeys.AnyAsync(k => k.KeyValue == HashedKey));
        ApiKey generatedAPIKey = new ApiKey
            { 
                KeyName = KeyName,
                KeyValue = apiKey,
                PermissionLevel = Permission != 0 ? true : false
            };
        await _dbContext.ApiKeys.AddAsync(HashedKey);
        await _dbContext.SaveChangesAsync();
        return new OkObjectResult(new { generatedAPIKey, message = "Copy or store the API Key now as you won't be able to see it again."});
    }
}

public class APIKeyRequestParams
{
    public string? KeyName { get; set; }
    public int Permission { get; set; }
}