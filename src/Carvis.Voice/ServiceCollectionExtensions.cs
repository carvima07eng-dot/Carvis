using Carvis.Core.Voice;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Carvis.Voice;

public static class ServiceCollectionExtensions
{
    /// <summary>Whisper for speech recognition.</summary>
    public static IServiceCollection AddCarvisVoice(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<ISpeechToText, WhisperSpeechToText>());
        return services;
    }
}
