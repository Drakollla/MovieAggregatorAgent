using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Runtime.CompilerServices;

namespace MovieAgentCLI.LLM
{
    public class MockProvider : ILLMProvider
    {
        public string Name => "Mock";

        public void Register(IKernelBuilder builder, IConfiguration config)
        {
            builder.Services.AddSingleton<IChatCompletionService, MockChatCompletionService>();
        }
    }

    public class MockChatCompletionService : IChatCompletionService
    {
        private static readonly Dictionary<string, string> _mockResponses = new()
        {
            { "похож", "Рекомендации Кинопоиска! Фильмы в духе «Матрица»:\n\n1. Джон Уик (2014)\n   Где смотреть: https://www.kinopoisk.ru/film/453399/\n\n2. Бегущий по лезвию (1982)\n   Где смотреть: https://www.kinopoisk.ru/film/409417/\n\n3. Тёмный рыцарь (2008)\n   Где смотреть: https://www.kinopoisk.ru/film/258721/\n\n[MOVIES:453399,409417,258721]" },
            { "найди", "ДАННЫЕ ИЗ БАЗЫ: Название: Матрица (1999). Рейтинг: 8.7. Сюжет: Хакер Нео узнаёт, что его мир — иллюзия, созданная машинами. Ссылка: https://www.kinopoisk.ru/film/64187/\n[MOVIE:64187]" },
            { "критериям", "ДАННЫЕ ИЗ БАЗЫ:\n<MOVIE>\nНазвание: Интерстеллар (2014)\nРейтинг: 8.6\nСюжет: Когда засуха приводит человечество к продовольственному кризису...\nГде смотреть: https://www.kinopoisk.ru/film/258687/\n</MOVIE>\n\n[MOVIES:258687]" },
            { "календарь", "SYSTEM: Success! The Calendar URL is generated.\nCRITICAL INSTRUCTION: You MUST output this exact Markdown link in your final response: [Добавить в Google Календарь](https://calendar.google.com/calendar/render?action=TEMPLATE&text=test)\nDo not hide or modify the link!" },
        };

        public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

        public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(500, cancellationToken);

            var lastUserMessage = chatHistory.LastOrDefault(m => m.Role == AuthorRole.User)?.Content?.ToLower() ?? "";

            string response = "Я mock-модель. Задайте мне вопрос о фильмах — я отвечу заглушкой.";

            foreach (var (keyword, mockResponse) in _mockResponses)
            {
                if (lastUserMessage.Contains(keyword))
                {
                    response = mockResponse;
                    break;
                }
            }

            return new List<ChatMessageContent>
            {
                new(AuthorRole.Assistant, response)
            };
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var result = await GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);

            foreach (var msg in result)
                yield return new StreamingChatMessageContent(AuthorRole.Assistant, msg.Content);
        }
    }
}
