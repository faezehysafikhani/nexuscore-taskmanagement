namespace Chat.Api.Endpoints;

public static class ChatEndpointExtensions
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapConversationEndpoints();
        app.MapMessageEndpoints();

        return app;
    }
}