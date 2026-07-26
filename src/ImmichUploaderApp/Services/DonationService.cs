using System.Diagnostics;

namespace ImmichUploaderApp.Services;

public static class DonationService
{
    public const string KoFiUrl = "https://ko-fi.com/lafkato";

    public static void ShowPrompt(IWin32Window? owner = null)
    {
        var (title, message) = Loc.Language switch
        {
            "en" => ("Support Immich Uploader", "Do you enjoy Immich Uploader? You can optionally support its development with a small Ko-fi tip."),
            "sv" => ("Stöd Immich Uploader", "Gillar du Immich Uploader? Du kan frivilligt stödja utvecklingen med en liten Ko-fi-donation."),
            "de" => ("Immich Uploader unterstützen", "Gefällt dir Immich Uploader? Du kannst die Entwicklung freiwillig mit einer kleinen Ko-fi-Spende unterstützen."),
            _ => ("Tue Immich Uploaderia", "Pidätkö Immich Uploaderista? Voit halutessasi tukea sen kehitystä pienellä Ko-fi-lahjoituksella."),
        };

        if (MessageBox.Show(owner, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;
        try { Process.Start(new ProcessStartInfo(KoFiUrl) { UseShellExecute = true }); }
        catch (Exception ex) { AppLogger.Log($"VAROITUS: Ko-fi-sivun avaaminen epaonnistui: {ex.Message}"); }
    }

    public static string MenuText => Loc.Language switch
    {
        "en" => "Support development (Ko-fi)",
        "sv" => "Stöd utvecklingen (Ko-fi)",
        "de" => "Entwicklung unterstützen (Ko-fi)",
        _ => "Tue kehitystä (Ko-fi)",
    };
}
