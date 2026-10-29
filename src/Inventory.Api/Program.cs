using Inventory.Api.Repositories;
using Inventory.Api.Services;
using Test3.Common;
var builder = WebApplication.CreateBuilder(args);
builder.AddApi("Inventory", "Inventory");
if (ApiSetup.Required(builder.Configuration, "InternalStock:Key").Length < 32)
    throw new InvalidOperationException("InternalStock:Key must contain at least 32 characters.");
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<ProductService>();
var app = builder.Build();
app.UseApi();
app.Run();
