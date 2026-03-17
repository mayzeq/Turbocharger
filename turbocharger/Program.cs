using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.EntityFrameworkCore;
using Turbocharger.Storage;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Turbocharger;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

        builder.Services.AddControllers(options =>
        {
            options.OutputFormatters.Clear();
            options.OutputFormatters.Add(new SystemTextJsonOutputFormatter(new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                TypeInfoResolver = new DefaultJsonTypeInfoResolver()
            }));
        });

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            // РАСКОММЕНТИРУЙТЕ после того, как XML файл начнет генерироваться
            var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);

            // Добавляем XML-комментарии только если файл существует
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }

            options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "Turbocharger MRP API",
                Version = "v1",
                Description = "API для управления структурой турбокомпрессора",
                Contact = new Microsoft.OpenApi.Models.OpenApiContact { Name = "Turbocharger System" }
            });
        });

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Turbocharger MRP API v1");
                c.DocumentTitle = "Turbocharger MRP API Documentation";
                c.DefaultModelsExpandDepth(-1);
            });
        }

        app.UseHttpsRedirection();
        app.MapControllers();
        app.Run();
    }
}