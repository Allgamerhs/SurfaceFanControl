using System.Globalization;
using System.Threading;
using SurfaceProFanControl.Core;

namespace SurfaceProFanControl.Infrastructure
{
    public static class LocalizationService
    {
        public static string CurrentLanguageCode { get; private set; } = "en";
        public static SupportedLanguage CurrentLanguage => LocalizationCatalog.Language(CurrentLanguageCode);

        public static void Use(string? languageCode)
        {
            CurrentLanguageCode = LocalizationCatalog.NormalizeLanguageCode(languageCode);
            var cultureName = CurrentLanguageCode switch
            {
                "tr" => "tr-TR",
                "de" => "de-DE",
                "it" => "it-IT",
                "es" => "es-ES",
                "ar" => "ar-SA",
                "zh-Hans" => "zh-CN",
                "ja" => "ja-JP",
                "ru" => "ru-RU",
                "pl" => "pl-PL",
                _ => "en-US"
            };
            var culture = CultureInfo.GetCultureInfo(cultureName);
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }

        public static string Text(string key) => LocalizationCatalog.Get(CurrentLanguageCode, key);
    }
}
