using Application.Interfaces;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using crypto_investment_project.Server.Helpers;
using Domain.DTOs;
using Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
MongoDbIdentityConfigurationHelper.Configure(builder);
builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("Mongo"));
builder.Services.Configure<BinanceSettings>(builder.Configuration.GetSection("Binance"));

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

builder.Services.AddSingleton<IExchangeService, ExchangeService>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("/index.html");

app.Run();
