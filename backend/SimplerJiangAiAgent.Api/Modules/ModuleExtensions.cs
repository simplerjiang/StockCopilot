using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SimplerJiangAiAgent.Api.Modules.Llm;
using SimplerJiangAiAgent.Api.Modules.Market;
using SimplerJiangAiAgent.Api.Modules.Stocks;

namespace SimplerJiangAiAgent.Api.Modules;

public static class ModuleExtensions
{
    public static IServiceCollection AddModules(this IServiceCollection services, IConfiguration configuration)
    {
        // 模块清单（后续按需扩展）
        var modules = new List<IModule>
        {
            new StocksModule(),
            new MarketModule(),
            new LlmModule()
        };

        foreach (var module in modules)
        {
            module.Register(services, configuration);
        }

        services.AddSingleton<IEnumerable<IModule>>(modules);
        return services;
    }

    public static IEndpointRouteBuilder MapModules(this IEndpointRouteBuilder app)
    {
        var modules = app.ServiceProvider.GetRequiredService<IEnumerable<IModule>>();
        foreach (var module in modules)
        {
            module.MapEndpoints(app);
        }

        return app;
    }
}
