using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Reflection;
using Avalonia;

namespace GetBeImage;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        
#if DEBUG
        this.AttachDevTools();
#endif
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        
        // 現在のアセンブリを取得
        var assembly = Assembly.GetExecutingAssembly();

        // アセンブリのバージョン情報を取得
        var version = assembly.GetName().Version;
        
        var asmText = this.FindControl<TextBlock>("AsmText");
        if (asmText == null || version == null) return;
        asmText.Text = string.Format($"GetBeImage   ver. {version.Major.ToString()}.{version.Minor.ToString()}.{version.Build.ToString()}");
    }

    private void btnOk_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}