namespace SmartSql.LocalAssistant;

public sealed class LocalAssistantService(ILanguageModel languageModel)
{
    public async Task<AssistantDecision> InterpretAsync(
        AssistantRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RussianRequest))
        {
            return new RefusedDecision("Запрос пуст.");
        }

        if (!request.Permissions.CanView)
        {
            return new RefusedDecision("У пользователя нет права на просмотр таблицы.");
        }

        if (request.Permissions.MaximumRows <= 0)
        {
            return new RefusedDecision("Для пользователя не задан допустимый лимит строк.");
        }

        var prompts = PromptBuilder.Build(request);
        var response = await languageModel.CompleteAsync(prompts.System, prompts.User, cancellationToken);
        return IntentParser.ParseAndValidate(response, request);
    }
}

