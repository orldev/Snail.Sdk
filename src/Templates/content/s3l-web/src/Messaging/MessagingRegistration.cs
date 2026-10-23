using Snail.Toolkit.RabbitMQ.Exchanges.Extensions;
using Snail.Toolkit.RabbitMQ.Extensions;
using Snail.Toolkit.RabbitMQ.Producers.Extensions;

namespace Snail.Web.Template.Messaging;

/// <summary>The messages the application publishes, through RabbitMQ.</summary>
internal static class MessagingRegistration
{
    /// <summary>The application's exchange, named after it.</summary>
    public const string Exchange = "tmplimage";

    extension(IServiceCollection services)
    {
        /// <summary>The connection the RabbitMQ section names, the application's exchange and a producer of its messages.</summary>
        public IServiceCollection AddMessaging(IConfiguration configuration) =>
            services.AddRabbit(configuration, rabbit => rabbit.AddConnection(connection =>
            {
                var exchange = connection.AddDirectExchange(Exchange).Durable();

                connection.AddProducer<GreetingSent>(exchange).Persistent();
            }));
    }
}
