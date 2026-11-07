using GaaS_API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace GaaS_API.Key
{
    internal class ValidateKey
    {
        private readonly DatabaseContext _dbContext;
        public ValidateKey(DatabaseContext dbContext)
        {
            _dbContext = dbContext;
        }

        public bool IsValidKey(string keyValue)
        {
            var apiKey = _dbContext.ApiKeys.FirstOrDefault(k => k.KeyValue == Convert.ToHexString(SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(keyValue))));
            if (apiKey == null) return false;
            return apiKey != null;
        }

        public bool GetPermissionLevel(string keyValue)
        {
            var apiKey = _dbContext.ApiKeys.FirstOrDefault(k => k.KeyValue == Convert.ToHexString(SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(keyValue))));
            if (apiKey == null) return false;
            return apiKey.PermissionLevel;
        }

        public async Task<IActionResult?> ValidateAdminAccessAsync(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return new BadRequestObjectResult("API Key is required");
            if (!IsValidKey(key)) return new UnauthorizedObjectResult("Invalid API Key");

            await _dbContext.ApiKeys.Where(k => k.KeyValue == Convert.ToHexString(SHA512.Create().ComputeHash(Encoding.UTF8.GetBytes(key)))).ExecuteUpdateAsync(setters => setters.SetProperty(k => k.KeyCallCount, k => k.KeyCallCount + 1));

            if (!GetPermissionLevel(key)) return new ObjectResult("Admin permission required") { StatusCode = StatusCodes.Status403Forbidden };
            return null;
        }
    }
}
