using WPSupportTools.Core.Modules.Printing.Labels;
using WPSupportTools.Core.Platform;

namespace WPSupportTools.Core.Tests;

public sealed class LabelModelTests : IDisposable
{
    private const string Zd230 = "ZDesigner ZD230-203dpi ZPL";
    private readonly FakePlatform _fake = new();

    public void Dispose() => _fake.Dispose();

    private (IReadOnlyList<LabelPrinterModel> Models, IReadOnlyList<LabelPrinterModelCapture.Failure> Failures) Capture(
        Func<string, byte[]?>? printerDefault = null)
    {
        var platform = _fake.Build();
        return LabelPrinterModelCapture.Capture(platform.Printers, platform.Preferences,
            _ => "8.6.4.23827", printerDefault ?? (_ => null), new DateTimeOffset(2026, 11, 6, 17, 0, 0, TimeSpan.FromHours(-3)),
            "PC-TECNICO", "0.6.3");
    }

    [Fact]
    public void Captures_every_zebra_with_full_driver_data_and_ignores_other_printers()
    {
        _fake.Printers.Add(new InstalledPrinter(Zd230, Zd230, "USB001", false, false));
        _fake.Printers.Add(new InstalledPrinter("Brother", "Brother DCP-T820DW Printer", "WSD", false, true));
        var devMode = FakePreferences.NewDevMode(Zd230, 508, 254);
        _fake.Preferences.DevModes[Zd230] = devMode;

        var (models, failures) = Capture(_ => devMode);

        Assert.Empty(failures);
        var model = Assert.Single(models);
        Assert.Equal(Zd230, model.Driver);
        Assert.Equal("8.6.4.23827", model.DriverVersion);
        Assert.Equal(devMode, Convert.FromBase64String(model.UserDevMode));   // inclusive o bloco interno do driver
        Assert.Equal(devMode, Convert.FromBase64String(model.PrinterDefaultDevMode!));
        Assert.Contains("16 bytes", model.Summary);
        Assert.Empty(_fake.Preferences.Writes);                                // captura não altera nada
    }

    [Fact]
    public void Model_file_round_trips_and_has_a_safe_name()
    {
        _fake.Printers.Add(new InstalledPrinter(Zd230, Zd230, "USB001", false, false));
        _fake.Preferences.DevModes[Zd230] = FakePreferences.NewDevMode(Zd230, 508, 254);

        var model = Assert.Single(Capture().Models);
        var back = LabelPrinterModel.FromJson(model.ToJson());

        Assert.Equal(model, back);
        Assert.Equal("ZDesigner ZD230-203dpi ZPL 8.6.4.23827.wpst-modelo.json", model.FileName());
        Assert.Contains("\"preferencias_usuario\"", model.ToJson());
    }

    [Fact]
    public void Printer_that_cannot_be_read_is_reported_without_stopping_the_others()
    {
        _fake.Printers.Add(new InstalledPrinter("Zebra quebrada", Zd230, "USB002", true, false));
        _fake.Printers.Add(new InstalledPrinter(Zd230, Zd230, "USB001", false, false));
        _fake.Preferences.DevModes[Zd230] = FakePreferences.NewDevMode(Zd230, 508, 254);
        _fake.Preferences.DevModes["Zebra quebrada"] = new byte[10];   // DEVMODE inválido

        var (models, failures) = Capture();

        Assert.Single(models);
        Assert.Equal("Zebra quebrada", Assert.Single(failures).Printer);
    }
}
