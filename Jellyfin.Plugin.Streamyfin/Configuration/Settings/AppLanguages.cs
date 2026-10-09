using System.Collections.Generic;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// The languages the app can be shown in, as its own language picker offers them.
/// </summary>
/// <remarks>
/// <c>APP_LANGUAGES</c> in the app's <c>i18n.ts</c>, value for value and label for label:
/// a choice stores what the app stores and is named the way the app's picker names it,
/// in its own language. A copy drifts, which is what <c>SettingsParityTests</c> is for. It
/// reads the list the manifest records from the app and fails the day a language is
/// added, dropped or renamed there and not here.
///
/// <para>
/// The order is the one the app shows on a device set to English. The app sorts by label
/// in the collation of each device's language, so there is no single order to hold this
/// to, and the test compares the languages rather than their order.
/// </para>
/// </remarks>
public static class AppLanguages
{
    /// <summary>
    /// Gets the choice that stores nothing, so each device follows its own language.
    /// </summary>
    /// <remarks>
    /// What the app's picker calls System and lists first. It writes no language, and the
    /// app then follows the one the device is set to.
    /// </remarks>
    public static SettingsChoice DeviceLanguage { get; } = new(null, "Device language");

    /// <summary>
    /// Gets every language the app offers.
    /// </summary>
    public static IReadOnlyList<SettingsChoice> Languages { get; } =
    [
        new("ca", "Catalan"),
        new("cs", "Čeština"),
        new("da", "Dansk"),
        new("de", "Deutsch"),
        new("en", "English"),
        new("es", "Español"),
        new("eo", "Esperanto"),
        new("fr", "Français"),
        new("it", "Italiano"),
        new("tlh", "Klingon"),
        new("lb", "Lëtzebuergesch"),
        new("hu", "Magyar"),
        new("nl", "Nederlands"),
        new("nb", "Norsk Bokmål"),
        new("nn", "Norsk Nynorsk"),
        new("pl", "Polski"),
        new("pt-BR", "Português (Brasil)"),
        new("pt", "Português (Portugal)"),
        new("ro", "Română"),
        new("sq", "Shqip"),
        new("fi", "Suomi"),
        new("sv", "Svenska"),
        new("vi", "Tiếng Việt"),
        new("tr", "Türkçe"),
        new("el", "Ελληνικά"),
        new("ru", "Русский"),
        new("uk", "Українська"),
        new("he", "עברית"),
        new("ar", "العربية"),
        new("th", "ไทย"),
        new("ko", "한국어"),
        new("ja", "日本語"),
        new("zh-CN", "简体中文"),
        new("zh-TW", "繁體中文"),
    ];

    /// <summary>
    /// Gets what the dropdown offers: the device's own language first, as the app's picker
    /// has it, then every language.
    /// </summary>
    public static IReadOnlyList<SettingsChoice> Choices { get; } = [DeviceLanguage, .. Languages];
}
