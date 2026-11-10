# GaaS API

GaaS API is a backend service built with .NET 10 and Azure Functions. It provides API functionality for a separate ASP.NET Core MVC web application, allowing the application to communicate with the backend and access its services.

## Technologies

- .NET 10
- Azure Functions v4
- Entity Framework Core 10
- Microsoft SQL Server
- Newtonsoft.Json
- Azure Monitor and OpenTelemetry

## Project Structure

| Directory / File | Description |
|---|---|
| `Key/` | API key-related functionality |
| `Models/` | Data models and database context |
| `Notification/` | Notification-related functionality |
| `Price/` | Pricing-related functionality |
| `Station/` | Station-related functionality |
| `Program.cs` | Application configuration and service registration |
| `GaaS-API.csproj` | Project dependencies and configuration |

## Requirements

- .NET 10 SDK
- Azure Functions Core Tools v4
- Microsoft SQL Server

For local development, configure this value in the local environment or the Azure Functions local settings file.

## Integration

This API is intended to serve as the backend for a separate ASP.NET Core MVC web application.

The MVC application will consume the API through HTTP requests rather than directly accessing the API's database or internal implementation.

## License

No license has been specified for this repository.
