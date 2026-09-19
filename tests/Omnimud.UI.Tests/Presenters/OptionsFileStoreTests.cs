using System.Globalization;
using Omnimud.Core.Options;
using Omnimud.Data;
using Omnimud.Data.Exchange;
using Omnimud.Data.Migrations;
using Omnimud.Data.Options;
using Omnimud.Data.Repositories;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Tests.Presenters;

public sealed class OptionsFileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "omnimud-optfiles-" + Guid.NewGuid().ToString("N"));

    public OptionsFileStoreTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("es");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string InDir(string name) => Path.Combine(_dir, name);

    private (ExchangeService Exchange, OptionsService Options) RealServices()
    {
        var factory = new SqliteConnectionFactory(InDir("store.db"));
        using (var connection = factory.Create())
            MigrationRunner.RunAsync(connection).GetAwaiter().GetResult();
        var options = new OptionsService(new SqliteOptionRepository(factory));
        return (new ExchangeService(factory, options), options);
    }

    [Fact]
    public async Task SaveThenLoad_ReturnsTheSameOptions_WithoutTheExchangeService()
    {
        var store = new ExchangeOptionsFileStore();
        var sample = OptionsSamples.AllNonDefault();

        await store.SaveAsync(InDir("a.omnimud"), sample);

        (await store.LoadAsync(InDir("a.omnimud"))).Should().Be(sample with { ProxyPasswordProtected = null }, "everything but the proxy password travels");
        var text = await File.ReadAllTextAsync(InDir("a.omnimud"));
        text.Should().Contain("\"format\": \"omnimud\"").And.Contain("\"kind\": \"options\"");
        text.Should().NotContain(nameof(OmnimudOptions.ProxyPasswordProtected)).And.NotContain(sample.ProxyPasswordProtected!);
    }

    [Fact]
    public async Task SaveThenLoad_ThroughTheRealExchangeService()
    {
        var store = new ExchangeOptionsFileStore(RealServices().Exchange);
        var sample = OptionsSamples.AllNonDefault();

        await store.SaveAsync(InDir("b.omnimud"), sample);

        (await store.LoadAsync(InDir("b.omnimud"))).Should().Be(sample with { ProxyPasswordProtected = null });
        (await File.ReadAllTextAsync(InDir("b.omnimud"))).Should().NotContain(sample.ProxyPasswordProtected!);
    }

    [Fact]
    public async Task FilesWrittenByTheExchangeService_AreUnderstood_AndTheOtherWayRound()
    {
        var (exchange, options) = RealServices();
        var sample = OptionsSamples.AllNonDefault();
        await options.SaveAsync(OptionScope.Global, null, sample);
        var store = new ExchangeOptionsFileStore(exchange);

        await exchange.SaveToFileAsync(InDir("c.omnimud"), await exchange.ExportOptionsAsync(OptionScope.Global, null));
        (await store.LoadAsync(InDir("c.omnimud"))).Should().Be(sample with { ProxyPasswordProtected = null });
        (await File.ReadAllTextAsync(InDir("c.omnimud"))).Should().NotContain(sample.ProxyPasswordProtected!, "the service does not export the proxy password either");

        // And a file written by the dialog can be imported by the service.
        await store.SaveAsync(InDir("d.omnimud"), sample with { Volume = 33 });
        var analysis = await exchange.AnalyzeAsync(await exchange.LoadFromFileAsync(InDir("d.omnimud")), ImportTarget.None);
        await exchange.ApplyAsync(analysis, analysis.Items.ToDictionary(i => i.Key, _ => ImportDecision.Overwrite));
        (await options.ResolveAsync(null, null)).Should().Be(sample with { Volume = 33 }, "the import keeps the proxy password the scope already had");
    }

    [Theory]
    [InlineData("", "El fichero está dañado o no es un fichero de Omnimud.")]
    [InlineData("esto no es json", "El fichero está dañado o no es un fichero de Omnimud.")]
    [InlineData("[1,2]", "El fichero está dañado o no es un fichero de Omnimud.")]
    [InlineData("{\"format\":\"otro\",\"formatVersion\":1,\"kind\":\"options\",\"options\":{\"Volume\":\"5\"}}", "El fichero está dañado o no es un fichero de Omnimud.")]
    [InlineData("{\"format\":\"omnimud\",\"kind\":\"options\",\"options\":{\"Volume\":\"5\"}}", "El fichero está dañado o no es un fichero de Omnimud.")]
    [InlineData("{\"format\":\"omnimud\",\"formatVersion\":99,\"kind\":\"options\",\"cosas\":[]}", "El fichero lo ha escrito una versión más reciente de Omnimud.")]
    [InlineData("{\"format\":\"omnimud\",\"formatVersion\":1,\"kind\":\"aliases\",\"aliases\":[]}", "El fichero no contiene opciones.")]
    [InlineData("{\"format\":\"omnimud\",\"formatVersion\":1,\"kind\":\"options\",\"options\":{}}", "El fichero no contiene opciones.")]
    [InlineData("{\"format\":\"omnimud\",\"formatVersion\":1,\"kind\":\"options\",\"options\":[1]}", "El fichero está dañado o no es un fichero de Omnimud.")]
    public async Task BadFiles_AreRejectedWithALocalizedMessage(string content, string message)
    {
        await File.WriteAllTextAsync(InDir("bad.omnimud"), content);

        foreach (var store in new[] { new ExchangeOptionsFileStore(), new ExchangeOptionsFileStore(RealServices().Exchange) })
        {
            var load = () => store.LoadAsync(InDir("bad.omnimud"));
            (await load.Should().ThrowAsync<OptionsFileException>()).WithMessage(message);
        }
    }

    [Fact]
    public async Task UnknownKeysAndCorruptValues_FallBackToDefaults()
    {
        await File.WriteAllTextAsync(InDir("odd.omnimud"),
            "{\"format\":\"omnimud\",\"formatVersion\":1,\"kind\":\"options\",\"options\":{\"Volume\":\"900\",\"NoExiste\":\"x\",\"HistorySize\":\"12\"}}");

        var options = await new ExchangeOptionsFileStore().LoadAsync(InDir("odd.omnimud"));

        options.Should().Be(OmnimudOptions.Default with { HistorySize = 12 });
    }

    [Fact]
    public async Task OptionsOfACharacterFile_AreAccepted()
    {
        await File.WriteAllTextAsync(InDir("char.omnimud"),
            "{\"format\":\"omnimud\",\"formatVersion\":1,\"kind\":\"character\",\"character\":{\"name\":\"Aldara\",\"options\":{\"Volume\":\"7\"}}}");

        (await new ExchangeOptionsFileStore().LoadAsync(InDir("char.omnimud"))).Volume.Should().Be(7);
    }

    [Fact]
    public async Task MissingFile_AndUnwritablePath_BecomeOptionsFileExceptions()
    {
        var store = new ExchangeOptionsFileStore();

        var load = () => store.LoadAsync(InDir("no-existe.omnimud"));
        var save = () => store.SaveAsync(Path.Combine(_dir, "no", "existe", "x.omnimud"), OmnimudOptions.Default);

        await load.Should().ThrowAsync<OptionsFileException>();
        await save.Should().ThrowAsync<OptionsFileException>();
    }
}
