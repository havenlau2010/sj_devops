using BuildServer.Core;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Initialize BuildManager as Singleton
// We use a shared log action that writes to Console
builder.Services.AddSingleton<BuildManager>(sp => 
{
    var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
    return new BuildManager(configPath, msg => Console.WriteLine($"[BuildServer] {msg}"));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
