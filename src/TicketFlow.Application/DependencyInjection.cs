using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using TicketFlow.Application.Auth;
using TicketFlow.Application.Events;
using TicketFlow.Application.Orders;
using TicketFlow.Application.Tickets;
using TicketFlow.Application.Users;

namespace TicketFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<EventService>();
        services.AddScoped<OrderService>();
        services.AddScoped<OrderExpirationService>();
        services.AddScoped<TicketService>();
        services.AddScoped<UserService>();

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        return services;
    }
}
