using Aquifer.Data;
using Aquifer.Migrations.Configuration;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration.Get<ConfigurationOptions>();

builder.Services
    .AddDbContext<AquiferDbContext>(options =>
        options.UseAzureSql(
            configuration?.ConnectionStrings?.AquiferDb,
            providerOptions =>
            {
                providerOptions.EnableRetryOnFailure(3);
                providerOptions.CommandTimeout(600);
            }));

var app = builder.Build();

app.Run();