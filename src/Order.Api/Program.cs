using Order.Api.Repositories;
using Order.Api.Services;
using Test3.Common;
var builder = WebApplication.CreateBuilder(args);
builder.AddApi("Order", "Orders");
if (ApiSetup.Required(builder.Configuration, "InternalStock:Key").Length < 32)
    throw new InvalidOperationException("InternalStock:Key must contain at least 32 characters.");
var inventoryUrl = new Uri(ApiSetup.Required(builder.Configuration, "InventoryService:BaseUrl"));
if (!inventoryUrl.IsAbsoluteUri || !inventoryUrl.AbsoluteUri.EndsWith('/'))
    throw new InvalidOperationException("InventoryService:BaseUrl must be an absolute URL ending with '/'.");
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<OperationProcessor>();
builder.Services.AddHostedService<RecoveryWorker>();
builder.Services.AddHttpClient<IInventoryClient, InventoryClient>(client =>
{
    client.BaseAddress = inventoryUrl;
    client.Timeout = TimeSpan.FromSeconds(5);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
var app = builder.Build();
app.UseApi();
app.Run();
