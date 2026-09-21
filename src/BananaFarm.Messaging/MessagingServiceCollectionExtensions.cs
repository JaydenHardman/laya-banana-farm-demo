using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BananaFarm.Messaging;

/// <summary>Registers the shared RabbitMQ connection and publisher.</summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="RabbitMqConnectionProvider"/> and <see cref="IMessagePublisher"/>,
    /// bound to the <c>RabbitMq</c> configuration section.
    /// </summary>
    public static IServiceCollection AddBananaMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();

        return services;
    }
}
