using System.ClientModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using HoneyGrid.Contracts;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace HoneyGrid.Functions.Ai;

/// <summary>
/// Klasyfikator oparty na Azure OpenAI (gpt-4o-mini). Bezkluczowo
/// (DefaultAzureCredential → rola „Cognitive Services OpenAI User").
///
/// Włączany konfiguracją: gdy brak <c>OpenAIEndpoint</c>, klasyfikator jest
/// wyłączony (<see cref="Enabled"/> = false) i <see cref="ClassifyAsync"/> zwraca
/// same wartości null — wołający (ClassifyEvents) podstawia wtedy stub. Dzięki
/// temu pipeline działa od razu, a model jest ulepszeniem opcjonalnym.
///
/// Odporność: prosty ponowny zapis z wykładniczym opóźnieniem na 429/5xx; każdy
/// twardy błąd → null (fallback na stub), nigdy wyjątek na zewnątrz.
/// </summary>
public sealed class OpenAiClassifier
{
    private const int MaxAttempts = 3;

    private readonly ChatClient? _chat;
    private readonly ILogger<OpenAiClassifier> _logger;

    public OpenAiClassifier(IConfiguration config, ILogger<OpenAiClassifier> logger)
    {
        _logger = logger;

        var endpoint = config["OpenAIEndpoint"];
        var deployment = config["OpenAIDeployment"] ?? "gpt-4o-mini";

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            _chat = null; // wyłączony — pipeline użyje stub-klasyfikatora
            return;
        }

        var client = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential());
        _chat = client.GetChatClient(deployment);
    }

    /// <summary>Czy klasyfikator AI jest skonfigurowany i aktywny.</summary>
    public bool Enabled => _chat is not null;

    /// <summary>
    /// Klasyfikuje wsad zdarzeń. Zwraca listę wyrównaną indeksami do wejścia;
    /// pozycje, których model nie zwrócił/nie dało się sparsować, są null.
    /// </summary>
    public async Task<IReadOnlyList<ClassificationInfo?>> ClassifyAsync(
        IReadOnlyList<HoneypotEvent> batch, CancellationToken cancellationToken)
    {
        if (_chat is null || batch.Count == 0)
        {
            return new ClassificationInfo?[batch.Count];
        }

        var messages = new ChatMessage[]
        {
            new SystemChatMessage(ClassificationPrompt.System),
            new UserChatMessage(ClassificationPrompt.BuildUser(batch)),
        };
        var options = new ChatCompletionOptions { Temperature = 0f };

        var text = await CompleteWithRetryAsync(messages, options, cancellationToken);
        return ClassificationResponseParser.Parse(text, batch.Count);
    }

    private async Task<string?> CompleteWithRetryAsync(
        ChatMessage[] messages, ChatCompletionOptions options, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                ClientResult<ChatCompletion> result = await _chat!.CompleteChatAsync(messages, options, ct);
                var parts = result.Value.Content;
                return parts.Count > 0 ? parts[0].Text : null;
            }
            catch (ClientResultException ex) when (IsTransient(ex.Status) && attempt < MaxAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(400 * Math.Pow(2, attempt - 1));
                _logger.LogWarning(ex,
                    "OpenAI: błąd przejściowy (status {Status}), próba {Attempt}/{Max}, ponawiam za {Delay} ms.",
                    ex.Status, attempt, MaxAttempts, delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OpenAI: klasyfikacja nieudana — fallback na stub.");
                return null;
            }
        }

        return null;
    }

    private static bool IsTransient(int status) => status == 429 || status >= 500;
}
