using Microsoft.AspNetCore.Html;

namespace SwarmUI.WebAPI;

/// <summary>
/// Registry class for API keys for upstream APIs, for which the keys are tracked per-user.
/// </summary>
public static class UserUpstreamApiKeys
{
    /// <summary>Info about a valid API Key type.</summary>
    /// <param name="KeyType">Short ID for data storage, eg 'stability_api'. Keep it very simple.</param>
    /// <param name="JSPrefix">JavaScript input prefix, eg 'stability'. Keep it very simple.</param>
    /// <param name="Title">Clear simple user-readable title, eg 'Stability AI'.</param>
    /// <param name="CreateLink">Link for where to create the API key, or to docs about it.</param>
    /// <param name="InfoHtml">Info about the key. Specify what it's for, when the user would want to use it, etc.</param>
    public record class ApiKeyInfo(string KeyType, string JSPrefix, string Title, string CreateLink, HtmlString InfoHtml);

    /// <summary>The actual registry.</summary>
    public static ConcurrentDictionary<string, ApiKeyInfo> KeysByType = [];

    /// <summary>Register an API key.</summary>
    public static void Register(ApiKeyInfo keyInfo)
    {
        if (!KeysByType.TryAdd(keyInfo.KeyType, keyInfo))
        {
            throw new ArgumentException($"Key with type '{keyInfo.KeyType}' already registered.");
        }
    }

    static UserUpstreamApiKeys()
    {
        Register(new("civitai_api", "civitai", "Civitai", "https://civitai.com/user/account", new("<span class=\"translate\">If you plan to use the</span> <a href=\"#\" onclick=\"getRequiredElementById('utilitiestabbutton').click();getRequiredElementById('modeldownloadertabbutton').click();\"><span class=\"translate\">Model Downloader</span></a> <span class=\"translate\">utility to download content from</span> <a href=\"https://civitai.com/\" target=\"_blank\" rel=\"noreferrer noopener\">Civitai</a> <span class=\"translate\">you will want to set your Civitai API key below.</span><br><span class=\"translate\">This will allow you to download gated or early-access content that your Civitai account has access to.</span>")));
        Register(new("huggingface_api", "huggingface", "Hugging Face", "https://huggingface.co/settings/tokens", new("<span class=\"translate\">If you plan to use the</span> <a href=\"#\" onclick=\"getRequiredElementById('utilitiestabbutton').click();getRequiredElementById('modeldownloadertabbutton').click();\"><span class=\"translate\">Model Downloader</span></a> <span class=\"translate\">utility to download content from</span> <a href=\"https://huggingface.co\" target=\"_blank\" rel=\"noreferrer noopener\">Hugging Face</a> <span class=\"translate\">you may want to set your Hugging Face API key below.</span><br><span class=\"translate\">This will allow you to download gated or private content that your HuggingFace account has access to.</span>")));
    }
}
