using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using FocusCoach.Core;

namespace FocusCoach.App;

/// <summary>
/// XAML から文言の辞書を引くための束縛元。言語が替わると「Item[]」の変更通知を出し、{loc:T キー} の文字がその場で替わる
/// (ウィンドウは作り直さない)。docs/review/design-0.13A-i18n.md §2.2。
/// </summary>
public sealed class LocSource : INotifyPropertyChanged
{
    public static LocSource Instance { get; } = new();

    private LocSource() => Loc.Changed += () =>
    {
        // Loc.Changed は UI スレッドとは限らない。束縛の更新は UI スレッドで行う
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) Raise(); else d.BeginInvoke(Raise);
    };

    public string this[string key] => Loc.T(key);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
}

/// <summary>XAML の文言: <c>Text="{loc:T main.start}"</c>。辞書のキーを渡す。キーが無ければ ⟦キー⟧ が出る。</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension(string key) => Key = key;
    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding { Source = LocSource.Instance, Path = new PropertyPath($"[{Key}]"), Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}

/// <summary>言語ごとの書体(英語は幅の狭い Segoe UI 系、日本語は今までどおり)。docs/review/design-0.13A-i18n.md §2.4。</summary>
internal static class LangFonts
{
    private static readonly FontFamily JaFont = new("BIZ UDPGothic, Yu Gothic UI, Meiryo UI");
    private static readonly FontFamily EnFont = new("Segoe UI Variable Text, Segoe UI, BIZ UDPGothic, Yu Gothic UI");

    public static FontFamily For(string language) => language == Loc.En ? EnFont : JaFont;

    /// <summary>アプリ全体の UiFont(DynamicResource で参照している)を、今の言語の書体にする。</summary>
    public static void Apply()
    {
        if (Application.Current is { } app) app.Resources["UiFont"] = For(Loc.Language);
    }
}
