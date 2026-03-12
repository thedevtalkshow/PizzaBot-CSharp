# PizzaApi

A local ASP.NET Core Web API that implements the **Contoso Pizza** ordering backend — the same backend the workshop's hosted MCP server connects to. Running this locally gives you a self-contained pizza ordering service you can inspect and experiment with.

## Running the API

No configuration required. The API loads menu data from `data/pizzas.json` and `data/toppings.json` at startup.

```bash
dotnet run
```

The API listens on **`http://localhost:7071`** by default.

## Endpoints

| Method | Path | Description |
|---|---|---|
| `GET` | `/api` | Service status and order summary |
| `GET` | `/api/pizzas` | Full pizza menu |
| `GET` | `/api/pizzas/{id}` | Single pizza by ID |
| `GET` | `/api/toppings` | All toppings (optional `?category=` filter) |
| `GET` | `/api/toppings/categories` | Distinct topping categories |
| `GET` | `/api/toppings/{id}` | Single topping by ID |
| `GET` | `/api/orders` | All orders (optional `?userId=`, `?status=`, `?last=1h` filters) |
| `GET` | `/api/orders/{id}` | Single order by ID |
| `POST` | `/api/orders` | Place a new order |
| `DELETE` | `/api/orders/{id}?userId=` | Cancel a pending order |

A `PizzaApi.http` file is included for quick endpoint testing from Visual Studio or VS Code (REST Client extension).

## Order Status

Orders advance through statuses automatically via a background service:

`pending` → `in-preparation` → `ready` → `completed`

Once the MCP server and order dashboard are added to this solution, you'll be able to point them at your local API and run the full workshop stack locally.

## Libraries

| Library | Description | Docs |
|---|---|---|
| `Microsoft.AspNetCore` (built-in) | ASP.NET Core Minimal APIs | [Learn](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis) |
| `Microsoft.Azure.Cosmos` | Azure Cosmos DB client — included for a future persistent data service | [Learn](https://learn.microsoft.com/en-us/azure/cosmos-db/nosql/sdk-dotnet-v3) |
| `Azure.Identity` | Passwordless authentication via `DefaultAzureCredential` | [Learn](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/identity-readme) |
