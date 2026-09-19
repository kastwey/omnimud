using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Omnimud.Core.Options;
using Omnimud.Data.Exchange;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Presenters;

/// <summary>A problem reading or writing an options file, with a message already localized for the user.</summary>
public sealed class OptionsFileException : Exception
{
    public OptionsFileException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Options ↔ .omnimud file. The dialog exports what is on screen and imports INTO the screen
/// (nothing is stored until OK), so it cannot use the database-to-database import of the exchange service.</summary>
public interface IOptionsFileStore
{
    /// <exception cref="OptionsFileException"/>
    Task SaveAsync(string path, OmnimudOptions options, CancellationToken ct = default);

    /// <exception cref="OptionsFileException"/>
    Task<OmnimudOptions> LoadAsync(string path, CancellationToken ct = default);
}

/// <summary>
/// Writes and reads an .omnimud document of kind "options" (the same format <see cref="IExchangeService"/>
/// produces). When the exchange service is available, file access and the validation of the document
/// (size, header, version, limits) go through it.
/// </summary>
public sealed class ExchangeOptionsFileStore : IOptionsFileStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IExchangeService? _exchange;

    public ExchangeOptionsFileStore(IExchangeService? exchange = null) => _exchange = exchange;

    public async Task SaveAsync(string path, OmnimudOptions options, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);

        var document = new ExchangeDocument
        {
            Kind = ExchangeKind.Options,
            ExportedAt = DateTime.UtcNow,
            // Without the secrets: the protected proxy password stays in this installation.
            Options = new Dictionary<string, string>(OptionsSerializer.SerializeForExport(options))
        };
        var json = JsonSerializer.Serialize(document, Json);

        try
        {
            if (_exchange is not null)
                await _exchange.SaveToFileAsync(path, json, ct).ConfigureAwait(false);
            else
                await File.WriteAllTextAsync(path, json, new UTF8Encoding(false), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new OptionsFileException(ex.Message, ex);
        }
    }

    public async Task<OmnimudOptions> LoadAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json;
        try
        {
            if (_exchange is not null)
            {
                json = await _exchange.LoadFromFileAsync(path, ct).ConfigureAwait(false);
            }
            else
            {
                if (new FileInfo(path).Length > IExchangeService.MaxDocumentLength * 4L)
                    throw new OptionsFileException(Strings.Options_ErrFileTooLarge);
                json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct).ConfigureAwait(false);
            }
        }
        catch (ExchangeFormatException ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new OptionsFileException(ex.Message, ex);
        }

        return await ParseAsync(json, ct).ConfigureAwait(false);
    }

    /// <summary>The options inside the text of an .omnimud document.</summary>
    public async Task<OmnimudOptions> ParseAsync(string json, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new OptionsFileException(Strings.Options_ErrFileInvalid);
        if (json.Length > IExchangeService.MaxDocumentLength)
            throw new OptionsFileException(Strings.Options_ErrFileTooLarge);

        CheckHeader(json);

        ExchangeDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<ExchangeDocument>(json, Json);
        }
        catch (JsonException ex)
        {
            throw new OptionsFileException(Strings.Options_ErrFileInvalid, ex);
        }
        if (document is null)
            throw new OptionsFileException(Strings.Options_ErrFileInvalid);

        // A character file carries the options of the character: accept them too.
        var values = document.Options ?? document.Character?.Options;
        if (values is null || values.Count == 0)
            throw new OptionsFileException(Strings.Options_ErrFileNoOptions);
        if (values.Count > IExchangeService.MaxListItems)
            throw new OptionsFileException(Strings.Options_ErrFileInvalid);

        if (_exchange is not null && document.Options is not null)
        {
            // Full validation of the document by its owner. Writes nothing.
            try
            {
                await _exchange.AnalyzeAsync(json, ImportTarget.None, ct).ConfigureAwait(false);
            }
            catch (ExchangeFormatException ex)
            {
                throw Translate(ex);
            }
        }

        // A file never brings secrets (exports leave them out; a hand-made one would belong to another key).
        return OptionsSerializer.Deserialize(OptionsSerializer.WithoutSecrets(values));
    }

    /// <summary>Format and version are read from the raw JSON, so a file of a newer version is reported as such
    /// even if its structure no longer matches.</summary>
    private static void CheckHeader(string json)
    {
        try
        {
            using var dom = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            if (dom.RootElement.ValueKind != JsonValueKind.Object)
                throw new OptionsFileException(Strings.Options_ErrFileInvalid);

            string? format = null;
            int? version = null;
            foreach (var property in dom.RootElement.EnumerateObject())
            {
                if (property.Name.Equals("format", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                    format = property.Value.GetString();
                else if (property.Name.Equals("formatVersion", StringComparison.OrdinalIgnoreCase)
                         && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var v))
                    version = v;
            }

            if (!string.Equals(format, ExchangeDocument.FormatName, StringComparison.OrdinalIgnoreCase) || version is null or < 1)
                throw new OptionsFileException(Strings.Options_ErrFileInvalid);
            if (version > ExchangeDocument.CurrentFormatVersion)
                throw new OptionsFileException(Strings.Options_ErrFileNewer);
        }
        catch (JsonException ex)
        {
            throw new OptionsFileException(Strings.Options_ErrFileInvalid, ex);
        }
    }

    private static OptionsFileException Translate(ExchangeFormatException ex) => new(ex.Error switch
    {
        ExchangeError.TooLarge => Strings.Options_ErrFileTooLarge,
        ExchangeError.UnsupportedVersion => Strings.Options_ErrFileNewer,
        ExchangeError.InvalidContent => Strings.Options_ErrFileNoOptions,
        _ => Strings.Options_ErrFileInvalid
    }, ex);
}
