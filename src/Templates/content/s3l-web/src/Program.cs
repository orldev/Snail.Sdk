using Snail.Web.Template.Extensions;

var builder = WebApplication.CreateBuilder(args);

#if (vault)
builder.Configuration.AddVaultWhenConfigured();
#endif
builder.Services.AddApplication(builder.Configuration);

var app = builder.Build();

app.UseApplication();
app.MapApplication();

await app.RunAsync();

/// <summary>The application's entry point, public so its tests can host it.</summary>
public partial class Program;
