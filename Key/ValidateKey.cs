using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Generic;
using System.Text;
using GaaS_API.Models;

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
            var apiKey = _dbContext.ApiKeys.FirstOrDefault(k => k.KeyValue == keyValue);
            if (apiKey == null) return false;
            return apiKey != null;
        }

        public bool GetPermissionLevel(string keyValue)
        {
            var apiKey = _dbContext.ApiKeys.FirstOrDefault(k => k.KeyValue == keyValue);
            if (apiKey == null) return false;
            return apiKey.PermissionLevel;
        }
    }
}
