using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WessTools.Models;

namespace WessTools.Services;

public sealed class OpenAiChatService
{
    private const string SystemPrompt =
        """
        Your name is Wes. You are the built-in assistant inside the desktop app Wes's Tools.
        Respond warmly, clearly, and conversationally, similar to a supportive desktop copilot.
        You can help with Windows apps, services, optimization ideas, everyday questions, and explaining what this app does.
        You must not build apps, write code, generate scripts, produce installers, debug software, or provide step-by-step app-building instructions from inside Wes's Tools.
        If the user asks for coding or app-building help, politely refuse and explain that Wes AI inside this app is chat-only.
        Keep answers practical and concise unless the user asks for more depth.
        """;

    private static readonly HttpClient HttpClient = new()
    {
        BaseAddress = new Uri("https://api.openai.com/")
    };

    public async Task<string> SendMessageAsync(
        string apiKey,
        string model,
        IReadOnlyList<ChatMessage> history,
        string userMessage,
        bool rememberHistory,
        CancellationToken cancellationToken)
    {
        if (LooksLikeBuildRequest(userMessage))
        {
            return "I can help with questions and recommendations here, but Wes AI inside Wes's Tools cannot build apps, write code, or generate installers.";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var messages = new List<object>
        {
            new { role = "system", content = SystemPrompt }
        };

        if (rememberHistory)
        {
            foreach (var message in history.Where(message => !string.IsNullOrWhiteSpace(message.Content)))
            {
                messages.Add(new
                {
                    role = message.IsUser ? "user" : "assistant",
                    content = message.Content
                });
            }
        }

        messages.Add(new
        {
            role = "user",
            content = userMessage
        });

        var payload = new
        {
            model,
            messages
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ReadErrorMessage(json));
        }

        using var document = JsonDocument.Parse(json);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return string.IsNullOrWhiteSpace(content)
            ? "Wes did not return any text for that message."
            : content.Trim();
    }

    private static bool LooksLikeBuildRequest(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var lowered = message.ToLowerInvariant();
        var buildWords = new[] { "build", "make", "create", "code", "script", "program", "app", "application", "website", "installer", "debug" };
        return buildWords.Count(lowered.Contains) >= 2;
    }

    private static string ReadErrorMessage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var errorElement) &&
                errorElement.TryGetProperty("message", out var messageElement))
            {
                return messageElement.GetString() ?? "OpenAI returned an unknown error.";
            }
        }
        catch
        {
        }

        return "OpenAI returned an unknown error.";
    }
}
