using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO.Abstractions;
using System.Security.Principal;
using System.Text;
using HtmlAgilityPack;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace GetBeImage;


public partial class MainWindow : Window
{
    // 通常のHTTPリクエスト用クライアント
    private static readonly HttpClient HttpClient;

    // 画像ダウンロード専用クライアント
    private static readonly HttpClient ImageHttpClient;

    // leia (maguro.2ch.sc) で取得する画像の拡張子
    private readonly string[] _imageExtensions =
    [
        ".jpg",
        ".jpeg",
        ".png",
        ".gif",
        ".bmp",
        ".webp",
        ".tiff",
        ".svg"
    ];
    
    // 静的コンストラクタ
    static MainWindow()
    {
        // 通常のHTTPリクエスト用のセットアップ
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        
        HttpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        // 画像ダウンロード用のセットアップ
        var imageHandler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        
        ImageHttpClient = new HttpClient(imageHandler)
        {
            // 画像ダウンロードは長めのタイムアウトを設定
            Timeout = TimeSpan.FromMinutes(2)
        };
    }
    
    public MainWindow()
    {
        InitializeComponent();
        
#if DEBUG
        this.AttachDevTools();
#endif
        
        // 設定の読み込みと適用
        LoadConfigurationAsync();
    }
    
    private async void LoadConfigurationAsync()
    {
        try
        {
            var config = await ConfigManager.Instance.GetConfigAsync();

            // ウインドウの最大化の設定
            if (config.Maximize)
            {
                WindowState = WindowState.Maximized;
            }

            // ウインドウのサイズ
            if (config.WindowSize.Count >= 2)
            {
                Width = config.WindowSize[0];
                Height = config.WindowSize[1];
            }
            
            // 各コントロールへの値の設定
            var inputBeText = this.FindControl<TextBox>("InputBeText");
            if (inputBeText != null)
            {
                inputBeText.Text = config.Be;
            }

            var skipBox = this.FindControl<TextBox>("SkipBox");
            if (skipBox != null && config.Skip.Count > 0)
            {
                skipBox.Text = string.Join(Environment.NewLine, config.Skip);
            }
            else if (skipBox != null)
            {
                skipBox.Text = string.Empty;
            }
            
            var replaceCheck = this.FindControl<CheckBox>("ReplaceCheck");
            if (replaceCheck != null)
            {
                replaceCheck.IsChecked = config.Maguro;
            }
            
            var dir = this.FindControl<TextBox>("DirectoryText");
            if (dir != null)
            {
                dir.Text = config.Dir;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Configuration error: {ex.Message}");
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
  
        var statusListBox = this.FindControl<ListBox>("StatusListBox");
        if (statusListBox == null) return;
        statusListBox.Items.Clear();

        // OSによりListBoxコントロールのプロパティを付加する
        // IssueはListBoxOptionメソッドを参照する
        ListBoxOption();
        
        var inputBeText = this.FindControl<TextBox>("InputBeText");
        if (inputBeText == null) return;
        inputBeText.Text = "";
        
        var serverCombo = this.FindControl<ComboBox>("ServerCombo");
        if (serverCombo == null) return;
        serverCombo.SelectedIndex = 0;
        
        var skipBox = this.FindControl<TextBox>("SkipBox");
        if (skipBox == null) return;
        skipBox.Text = "";
    }

    public void addToSkip_Click(object sender, RoutedEventArgs e)
    {
        var serverCombo = this.FindControl<ComboBox>("ServerCombo");
        if (serverCombo == null) return;
        
        var skipBox = this.FindControl<TextBox>("SkipBox");
        if (skipBox == null) return;
        
        var replaceCheck = this.FindControl<CheckBox>("ReplaceCheck");
        if (replaceCheck == null) return;
        
        // 選択したサーバをスキップするように設定
        var leiaUrl = $@"http://leia.2ch.net/{Environment.NewLine}http://leia.5ch.net/";
        if (replaceCheck.IsChecked == true) leiaUrl = $@"https://maguro.2ch.sc/";
        
        var serverUri = serverCombo.SelectedIndex == 0 ? leiaUrl : @"http://greta.5ch.net/";
        if (!string.IsNullOrEmpty(skipBox.Text)) skipBox.Text += Environment.NewLine; 
        skipBox.Text += serverUri;
    }

    private List<string> _urls = [];
    private string? DirPath { get; set; } = "";

    private CancellationTokenSource _cts = new CancellationTokenSource();
    
    // 入力値の検証
    private async Task<bool> ValidateInputs()
    {
        // BE番号の確認
        var inputBe = this.FindControl<TextBox>("InputBeText");
        if (inputBe == null) return false;

        var beid = inputBe.Text;
        if (string.IsNullOrEmpty(beid))
        {
            await MessageBoxManager.GetMessageBoxStandard("エラー", "Be番号の設定が異常です。",
                ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error).ShowAsync();
            return false;
        }

        // 保存先ディレクトリの確認
        var directoryText = this.FindControl<TextBox>("DirectoryText");
        if (directoryText == null) return false;

        DirPath = directoryText.Text;
        if (string.IsNullOrEmpty(DirPath))
        {
            await MessageBoxManager.GetMessageBoxStandard("保存先の確認",
                "保存先が未指定の場合はアプリケーションと同じディレクトリとなります。",
                ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Folder).ShowAsync();
            var utf8Bytes = Encoding.UTF8.GetBytes(Path.Combine(Directory.GetCurrentDirectory(), "image"));
            DirPath = Encoding.UTF8.GetString(utf8Bytes);

            // ディレクトリの作成
            try
            {   
                Directory.CreateDirectory(DirPath);
            }
            catch (Exception err)
            {
                await MessageBoxManager.GetMessageBoxStandard("エラー",
                    $"ディレクトリ作成時にエラーが発生しました。{Environment.NewLine}{Environment.NewLine}{err.Message}",
                    ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error).ShowAsync();
                return false;
            }
        }

        if (!Directory.Exists(DirPath))
        {   
            await MessageBoxManager.GetMessageBoxStandard("保存先の確認",
                "ディレクトリが存在していません。",
                ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error).ShowAsync();
            return false;
        }

        // 画像を保存するディレクトリの書き込み権限を確認
        if (!HasWritePermission(DirPath))
        {   
            await MessageBoxManager.GetMessageBoxStandard("パーミッションエラー",
                "指定したディレクトリに書き込み権限がありません。",
                ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error).ShowAsync();
            return false;
        }

        return true;
    }

    // UIの有効/無効を切り替える
    private void DisableUi()
    {
        var inputBe = this.FindControl<TextBox>("InputBeText");
        if (inputBe != null) inputBe.IsEnabled = false;

        var serverCombo = this.FindControl<ComboBox>("ServerCombo");
        if (serverCombo != null) serverCombo.IsEnabled = false;

        var btnSkip = this.FindControl<Button>("BtnSkip");
        if (btnSkip != null) btnSkip.IsEnabled = false;

        var skipBox = this.FindControl<TextBox>("SkipBox");
        if (skipBox != null) skipBox.IsEnabled = false;

        var replaceCheck = this.FindControl<CheckBox>("ReplaceCheck");
        if (replaceCheck != null) replaceCheck.IsEnabled = false;

        var btnDirectory = this.FindControl<Button>("BtnDirectory");
        if (btnDirectory != null) btnDirectory.IsEnabled = false;

        var directoryText = this.FindControl<TextBox>("DirectoryText");
        if (directoryText != null) directoryText.IsEnabled = false;

        var button = this.FindControl<Button>("BtnStart");
        if (button != null)
        {
            button.Content = @"実行中...";
            button.IsEnabled = false;
        }

        var stopBtn = this.FindControl<Button>("BtnStop");
        if (stopBtn != null) stopBtn.IsEnabled = true;
    }

    // スレッドURLの取得
    private async Task<List<string>> FetchThreadUrls(string beid, TextBox skipBox, CheckBox replaceCheck, ListBox statusListBox, CancellationToken token)
    {
        var urls = new List<string>();
        
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            statusListBox.Items.Insert(statusListBox.ItemCount, $"スレッドのURLを取得しています。");
            statusListBox.InvalidateVisual();
        }, DispatcherPriority.Send, token);

        var url = "https://ame.hacca.jp/sasss/log-be2.cgi?i=" + beid;
        var response = await HttpClient.GetAsync(url, token);
        response.EnsureSuccessStatusCode();

        // Shift_JISエンコーディングを指定してコンテンツを読み取り
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var enc = Encoding.GetEncoding("Shift_JIS");
        var htmlWeb = new HtmlWeb
        {
            OverrideEncoding = enc
        };

        htmlWeb.PreRequest += (request) =>
        {
            request.Timeout = 5000;
            return true;
        };
        
        var htmlDocument = htmlWeb.Load(url);  // HtmlDocumentを作成して文字列をロード
        
        const string xpathExpression = "//body//a/@href";
        var urlInnerText = htmlDocument.DocumentNode.SelectNodes(xpathExpression)?
            .Select(node => node.GetDirectInnerText())
            .ToList();

        if (urlInnerText == null)
        {
            await MessageBoxManager.GetMessageBoxStandard("終了",
                $"スレッドのURLは取得できませんでした。{Environment.NewLine}BE番号が間違っているかもしれません。",
                ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Warning).ShowAsync();
            return urls;
        }

        var result = urlInnerText.Where(x => x != string.Empty).ToList().Skip(2).ToList();
        result = result.Select(x => x).Where(x => Regex.IsMatch(x, @"^http.*")).ToList();

        token.ThrowIfCancellationRequested();

        if (replaceCheck.IsChecked != false)
        {
            result = result.Select(item => item.Contains(@"http://leia.2ch.net/") || item.Contains(@"http://leia.5ch.net/")
                    ? item.Replace(@"http://leia.2ch.net/", @"https://maguro.2ch.sc/")
                          .Replace(@"http://leia.5ch.net/", @"https://maguro.2ch.sc/")
                    : item)
                .ToList();
        }

        token.ThrowIfCancellationRequested();

        if (!string.IsNullOrEmpty(skipBox.Text))
        {
            var skipUrls = skipBox.Text?.Split(Environment.NewLine).ToList() ?? new List<string>();
            foreach (var skipUrl in skipUrls)
            {
                var pattern = @"^" + skipUrl;
                result.RemoveAll(s => Regex.IsMatch(s, pattern));
            }
        }

        urls.AddRange(result);

        if (urls.Count == 0)
        {
            await MessageBoxManager.GetMessageBoxStandard("終了",
                $"スレッドが見つかりません。{Environment.NewLine}スキップ設定等の確認をお願いします。",
                ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Stop).ShowAsync();
            return urls;
        }

        urls.Reverse();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            statusListBox.Items.Insert(statusListBox.ItemCount, "以下に示すスレッドのURLから画像をダウンロードします。");
        }, DispatcherPriority.Send, token);

        // 各URLを処理
        foreach (var threadUrl in urls)
        {
            // UIスレッドでの処理を待機
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                statusListBox.Items.Insert(statusListBox.ItemCount, threadUrl);
                statusListBox.ScrollIntoView(statusListBox.ItemCount - 1);
            }, DispatcherPriority.Send, token);
    
            // UIの更新が確実に反映されるように待機
            await Task.Delay(1, token);
        }

        return urls;
    }

    // 画像のダウンロード
    private async Task DownloadImages(ListBox statusListBox, CancellationToken token)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                statusListBox.Items.Insert(statusListBox.ItemCount, $"スレッドから画像のURLを抽出しています。");
                statusListBox.ScrollIntoView(statusListBox.ItemCount - 1);
                statusListBox.InvalidateVisual();
            });

            foreach (var url in _urls)
            {
                var response = await HttpClient.GetAsync(url, token);
                response.EnsureSuccessStatusCode();

                var htmlWeb = new HtmlWeb();
                
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                var enc = Encoding.GetEncoding("Shift_JIS");
                htmlWeb.OverrideEncoding = enc;
                
                htmlWeb.PreRequest += (request) =>
                {
                    request.Timeout = 5000;
                    return true;
                };
                
                var htmlDocument = htmlWeb.Load(url);
                
                await ProcessThreadImages(htmlDocument, url, statusListBox, token);
            }
        }
        catch (HttpRequestException err)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                statusListBox.Items.Insert(statusListBox.ItemCount, $"ダウンロード失敗 : {err.Message}");
                statusListBox.ScrollIntoView(statusListBox.ItemCount - 1);
                statusListBox.InvalidateVisual();
            }, DispatcherPriority.Send, token);
            
            throw;
        }
    }

    // スレッド内の画像ダウンロード
    private async Task ProcessThreadImages(HtmlDocument htmlDocument, string url, ListBox statusListBox, CancellationToken token)
    {
        var imageUrls = new HashSet<string>();
    
        // URLに応じてXPATHを切り替え
        var isMaguroSite = Regex.IsMatch(url, @"^https://maguro.2ch.sc/.*");
        var xpath = isMaguroSite
            ? "//dl[@class='thread']//dd"
            : "//div[@class='post-content']";
    
        var nodes = htmlDocument.DocumentNode.SelectNodes(xpath);
    
        if (nodes != null)
        {
            foreach (var node in nodes)
            {
                // img要素のsrc属性から画像URLを抽出
                var imgNodes = node.SelectNodes(".//img");
                if (imgNodes != null)
                {
                    foreach (var img in imgNodes)
                    {
                        var src = img.GetAttributeValue("src", "");
                        if (!string.IsNullOrEmpty(src) && 
                            !src.Contains("ad-stir") && 
                            !src.Contains("microad"))
                        {
                            imageUrls.Add(src);
                        }
                    }
                }

                // aタグのテキスト値から画像URLを抽出
                var linkNodes = node.SelectNodes(".//a");
                if (linkNodes == null) continue;
                
                foreach (var link in linkNodes)
                {
                    var linkText = link.InnerText.Trim();
                    if (!string.IsNullOrEmpty(linkText) &&
                        _imageExtensions.Any(ext => linkText.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    {
                        imageUrls.Add(linkText);
                    }
                }
            }
        }

        if (imageUrls.Count > 0)
        {
            var imageLists = FilterAndTransformImageUrls(imageUrls.ToList());
            await DownloadImagesFromUrls(imageLists, statusListBox, token);
        }
    }

    // URLのフィルタリングと変換
    private static List<string> FilterAndTransformImageUrls(List<string> urls)
    {
        urls = urls.Where(x => Regex.IsMatch(x, @"(.*.png|.*.jpg|.*.jpeg|.*.gif)")).ToList();

        var transformations = new Dictionary<string, string>
        {
            {@"sssp://o.8ch.net/", @"https://o.5ch.net/"},
            {@"//o.8ch.net/", @"https://o.5ch.net/"},
            {@"//o.5ch.net/", @"https://o.5ch.net/"},
            {@"o.8ch.net/", @"o.5ch.net/"},
            {@"http://o.5ch.net/", @"https://o.5ch.net/"}
        };

        foreach (var transformation in transformations)
        {
            urls = urls.Select(x =>
                Regex.IsMatch(x, $@"({transformation.Key}.*.png|{transformation.Key}.*.jpg|{transformation.Key}.*.jpeg|{transformation.Key}.*.gif)") ?
                    x.Replace(transformation.Key, transformation.Value) : x)
                .ToList();
        }

        return urls;
    }

    // URLリストから画像をダウンロード
    private async Task DownloadImagesFromUrls(List<string> imageUrls, ListBox statusListBox, CancellationToken token)
    {
        foreach (var imageUrl in imageUrls)
        {
            // 画像URLが空の場合およびBe番号のアイコンは除外
            if (string.IsNullOrEmpty(imageUrl) || imageUrl.Contains("//img.5ch.net/ico/"))
                continue;

            if (IsValidUrl(imageUrl))
            {
                var fileName = new string(imageUrl.Reverse().TakeWhile(c => c != '/').Reverse().ToArray());
                if (!string.IsNullOrEmpty(fileName))
                {
                    var decImageUrl = System.Net.WebUtility.UrlDecode(imageUrl);
                    fileName              = System.Net.WebUtility.UrlDecode(fileName);
                    var localFilePath     = Path.Combine(DirPath ?? throw new InvalidOperationException(), fileName);

                    // ファイルの存在確認 (既にダウンロード済みの画像ファイルは無視する)
                    string? statusMsg;
                    if (File.Exists(localFilePath))
                    {
                        statusMsg = $"ダウンロード済みのため無視 : {decImageUrl}";
                        
                        // リストボックスの更新
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            statusListBox.Items.Insert(statusListBox.ItemCount, statusMsg);
                            statusListBox.ScrollIntoView(statusListBox.ItemCount - 1);
                            statusListBox.InvalidateVisual();
                        }, DispatcherPriority.Send, token);
                        continue;
                    }

                    // 画像のダウンロード
                    var (bResult, errMsg) = await DownloadImageAsync(decImageUrl, localFilePath);
                    
                    // ダウンロード結果の取得
                    statusMsg = bResult ? $"ダウンロード完了 : {decImageUrl}" : $"ダウンロード失敗 : {decImageUrl}\t{errMsg}";
                    
                    // リストボックスの更新
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        statusListBox.Items.Insert(statusListBox.ItemCount, statusMsg);
                        statusListBox.ScrollIntoView(statusListBox.ItemCount - 1);
                        statusListBox.InvalidateVisual();
                    }, DispatcherPriority.Send, token);
                }
            }
            
            token.ThrowIfCancellationRequested();
        }
    }

    // メインの処理メソッド
    public async void btnStart_Click(object sender, RoutedEventArgs e)
    {
        // 各プロパティを初期化
        _urls.Clear();
        DirPath = string.Empty;
        _cts = new CancellationTokenSource();

        var statusListBox = this.FindControl<ListBox>("StatusListBox");
        if (statusListBox == null) return;
        statusListBox.Items.Clear();

        var scrollViewer = statusListBox.FindDescendantOfType<ScrollViewer>();
        if (scrollViewer == null) return;
        scrollViewer.Offset = new Vector(0, 0);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            statusListBox.InvalidateVisual();
            scrollViewer.InvalidateVisual();
        }, DispatcherPriority.Send);

        // 入力値の検証
        if (!await ValidateInputs())
        {
            return;
        }

        // UIの無効化
        DisableUi();

        // CancellationTokenSourceの生成
        using (_cts = new CancellationTokenSource())
        {
            var token = _cts.Token;
            token.Register(() => { });

            var inputBe      = this.FindControl<TextBox>("InputBeText");
            var be     = inputBe?.Text ?? string.Empty;
            var skipBox      = this.FindControl<TextBox>("SkipBox");
            var replaceCheck = this.FindControl<CheckBox>("ReplaceCheck");

            try
            {
                // スレッドURLの取得
                if (skipBox != null && replaceCheck != null)
                    _urls = await FetchThreadUrls(be, skipBox, replaceCheck, statusListBox, token);
                
                if (_urls.Count == 0)
                {
                    UiEnable();
                    return;
                }

                // 画像のダウンロード処理
                await DownloadImages(statusListBox, token);
            }
            catch (HttpRequestException err)
            {
                await MessageBoxManager.GetMessageBoxStandard("エラー", $"エラーが発生しました。\n\n{err.Message}",
                    ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error).ShowAsync();
            }
            catch (OperationCanceledException)
            {
                await MessageBoxManager.GetMessageBoxStandard("キャンセル", $"処理がキャンセルされました。",
                    ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Stop).ShowAsync();
            }
            finally
            {
                UiEnable();
            }
        }
    }
    
    public void btnStop_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
    }
    
    // 画像の保存
    [GeneratedRegex(@"(i.imgur.com/|imgur.com/a/)")]
    private static partial Regex ImgurRegex();

    private static async Task<(bool, string)> DownloadImageAsync(string url, string destinationPath)
    {
        // imgurの場合は別途HttpClientを作成
        if (ImgurRegex().IsMatch(url))
        {
            using var imgurHandler = new HttpClientHandler();
            imgurHandler.AllowAutoRedirect = true;
            using var imgurClient  = new HttpClient(imgurHandler);
            
            // imgur向けの処理
            return await ProcessImgurDownload(imgurClient, url, destinationPath);
        }

        // 通常の画像ダウンロード
        try
        {
            var response = await ImageHttpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
        
            var content = await response.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(destinationPath, content);
        }
        catch (HttpRequestException err)
        {
            return (false, err.Message);
        }
        
        return (true, string.Empty);
    }
    
    private static async Task<(bool, string)> ProcessImgurDownload(HttpClient client, string url, string destinationPath)
    {
        try
        {
            // HTTP GETリクエストを送信
            var response = await client.GetAsync(url);
        
            // リダイレクト先の最終的な画像のURLを取得
            var finalUrl = response.RequestMessage?.RequestUri?.ToString();
            if (string.IsNullOrEmpty(finalUrl))
                return (false, $"リダイレクト先の画像ファイルのURLが不明です。");
        
            // リダイレクト先の画像ファイル名を取得
            var fileName = new string(finalUrl.Reverse().TakeWhile(c => c != '/').Reverse().ToArray());

            // 画像ファイルの削除の可否
            var bRemoved = Regex.IsMatch(fileName, @"(removed.png|removed.jpg|removed.jpeg|removed.gif)");
        
            // 削除されている場合はエラー
            if (bRemoved) 
                return (false, $"画像ファイルは削除されています。");

            // リクエストを確認
            response.EnsureSuccessStatusCode();

            // 画像ファイルを読み込む
            var content = await response.Content.ReadAsByteArrayAsync();

            // 画像ファイルを指定したディレクトリに書き込む
            await File.WriteAllBytesAsync(destinationPath, content);
        
            return (true, string.Empty);
        }
        catch (HttpRequestException err)
        {
            return (false, err.Message);
        }
    }

    private static bool IsValidUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uriResult) &&
               (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    }
    
#pragma warning disable 0414
    private void CheckBox_CheckChanged(object sender, RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        
        var skipBox = this.FindControl<TextBox>("SkipBox");
        if (skipBox == null) return;

        var replaceCheck = (CheckBox)sender;
        if (string.IsNullOrEmpty(skipBox.Text)) return;
        
        skipBox.Text = replaceCheck.IsChecked != false ?
            skipBox.Text.Replace(@"http://leia.2ch.net/", @"https://maguro.2ch.sc/").
                Replace(@"http://leia.5ch.net/", @"https://maguro.2ch.sc/") :
            skipBox.Text.Replace(@"https://maguro.2ch.sc/", $"http://leia.2ch.net/{Environment.NewLine}http://leia.5ch.net/");

        skipBox.Text = string.Join(Environment.NewLine, skipBox.Text.Split(Environment.NewLine).Distinct().ToList());
    }
    
    private async void btnSave_Click(object sender, RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(e);
        
        var saveDirectory = this.FindControl<TextBox>("DirectoryText");
        if (saveDirectory == null)    return;
        
        // ディレクトリ選択ダイアログ (旧)
        /*var dirDialog = new OpenFolderDialog();
        var selectedFolder = await dirDialog.ShowAsync(this) ?? "";
        if (!string.IsNullOrEmpty(selectedFolder))
        {
            saveDirectory.Text = selectedFolder;
        }*/
        
        // ディレクトリ選択ダイアログ
        var storage = new Window().StorageProvider;
        var directory = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions {
            Title = $"画像を保存するディレクトリを選択",
            SuggestedStartLocation = null,
        });
        
        if (directory.Count != 0) saveDirectory.Text = System.Net.WebUtility.UrlDecode(directory.ToList().First().Path.AbsolutePath);
    }
#pragma warning disable 0414
    
#pragma warning disable 0414
    private void InputBeText_OnTextChanging(object? sender, TextChangingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(e);

        var inputBeText = this.FindControl<TextBox>("InputBeText");
        if (inputBeText == null) return;
            
        // 入力制限
        if (!string.IsNullOrEmpty(inputBeText.Text))
            inputBeText.Text = Regex.Replace(inputBeText.Text, @"[^0-9]", "");
    }
    
    private void DirectoryText_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        
        if (sender is null) return;
        var dirText = (TextBox)sender;
        
        DirPath = dirText.Text;
    }
    
    private void OpenAboutDialog_Click(object? sender, TappedEventArgs tappedEventArgs)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(tappedEventArgs);
        
        // アバウトダイアログを表示
        var aboutDialog = new AboutDialog();
        {
            aboutDialog.ShowDialog(this);
        }
    }
    
    private void OpenBrowser_Click(object sender, PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(e);
        
        // デフォルトのWebブラウザを起動して薄荷飴(Webサイト)を閲覧
        Process.Start(new ProcessStartInfo
                     {
                         FileName = @"https://ame.hacca.jp/ttp.html",
                         UseShellExecute = true
                     });
    }
    
    // ListBoxのオプションを各OSにより分ける
    // Issue :
    // https://github.com/AvaloniaUI/Avalonia/issues/12744
    // https://github.com/AvaloniaUI/Avalonia/issues/13607
    // https://github.com/AvaloniaUI/Avalonia/pull/13765
    private void ListBoxOption()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
        {   // Linuxの場合
            var statusListBox = this.FindControl<ListBox>("StatusListBox");
            if (statusListBox is not null)
            {
                statusListBox.UseLayoutRounding = false;
            }
        }
    }
    
    // 指定したディレクトリの書き込み権限を確認する
    private static bool HasWritePermission(string directoryPath)
    {
        // 各プラットフォームごとに対応
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {   // Windowsの場合
            var fileSystem = new FileSystem();
            try
            {
                // ディレクトリの書き込み権限を確認
                var directorySecurity = fileSystem.Directory.GetAccessControl(directoryPath);

                // 実行しているユーザの識別子を取得
                var windowsIdentity = WindowsIdentity.GetCurrent();

                // 実行しているユーザにおいて、指定したディレクトリの書き込み権限を確認
                var rules = directorySecurity.GetAccessRules(true, true, typeof(SecurityIdentifier));
                foreach (System.Security.AccessControl.FileSystemAccessRule rule in rules)
                {
                    if (rule.IdentityReference.Equals(windowsIdentity.User) &&
                        (rule.FileSystemRights & System.Security.AccessControl.FileSystemRights.WriteData) == System.Security.AccessControl.FileSystemRights.WriteData)
                    {
                        return true;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {   // アクセスが拒否された場合
                return false;
            }
        }
        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
        {   // Linuxの場合
            // ディレクトリの情報を取得
            DirectoryInfo directoryInfo = new DirectoryInfo(directoryPath);

            // 書き込み権限の確認
            if (directoryInfo.Exists && (directoryInfo.Attributes & FileAttributes.ReadOnly) == 0) return true;
        }

        // ディレクトリの書き込み権限が無い場合
        return false;
    }
    
    private void UiEnable()
    {
        // Be番号テキストUIの有効化
        var inputBe = this.FindControl<TextBox>("InputBeText");
        if (inputBe == null)    return;
        inputBe.IsEnabled = true;
        
        // プルダウンの有効化
        var serverCombo = this.FindControl<ComboBox>("ServerCombo");
        if (serverCombo == null) return;
        serverCombo.IsEnabled = true;
        
        // [スキップ]ボタンの有効化
        var btnSkip = this.FindControl<Button>("BtnSkip");
        if (btnSkip == null) return;
        btnSkip.IsEnabled = true;
        
        // 無視するサーバテキストUIの有効化
        var skipBox = this.FindControl<TextBox>("SkipBox");
        if (skipBox == null) return;
        skipBox.IsEnabled = true;
        
        // チェックボックUIの有効化
        var replaceCheck = this.FindControl<CheckBox>("ReplaceCheck");
        if (replaceCheck == null) return;
        replaceCheck.IsEnabled = true;
        
        // [保存先]ボタンの有効化
        var btnDirectory = this.FindControl<Button>("BtnDirectory");
        if (btnDirectory == null) return;
        btnDirectory.IsEnabled = true;
        
        // 保存先テキストUIの有効化
        var directoryText = this.FindControl<TextBox>("DirectoryText");
        if (directoryText == null) return;
        directoryText.IsEnabled = true;
        
        // [開始]ボタンの有効化
        var startBtn = this.FindControl<Button>("BtnStart");
        if (startBtn == null)    return;
        startBtn.Content = @"開始";
        startBtn.IsEnabled = true;
        
        // [停止]ボタンの無効化
        var stopBtn = this.FindControl<Button>("BtnStop");
        if (stopBtn == null)    return;
        stopBtn.IsEnabled = false;
    }

    // ステータス情報のコンテキストメニュー
    private async void MenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(e);
    
        var statusListBox = this.FindControl<ListBox>("StatusListBox");

        // ReSharper disable once UseNullPropagation
        if (statusListBox == null) return;

        // 選択した行のテキストをクリップボードへコピー
        if (statusListBox.SelectedItem is not string selectedItem) return;
        if ( Clipboard is not null) await Clipboard.SetTextAsync(selectedItem);
    }
    
    // [設定を保存]ボタン
    private async void btnSaveSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btnSaveSettings)
        {
            try
            {
                // [設定を保存]ボタンを一時的に無効化
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    btnSaveSettings.IsEnabled = false;
                });
                
                await UpdateSettingAsync();

                var window = (Window)this.VisualRoot!;
                await MessageBoxManager.GetMessageBoxStandard("保存完了", "各種設定を保存しました。",
                    ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Success).ShowWindowDialogAsync(window);
            }
            catch (Exception)
            {
                var window = (Window)this.VisualRoot!;
                await MessageBoxManager.GetMessageBoxStandard("保存の失敗", "各種設定の保存に失敗しました。",
                    ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error).ShowWindowDialogAsync(window);
            }
            finally
            {
                // [設定を保存]ボタンを有効化
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    btnSaveSettings.IsEnabled = true;
                });
            }
        }
    }
    
    // 設定ファイルの更新
    private async Task UpdateSettingAsync()
    {
        await ConfigManager.Instance.UpdateConfigAsync(config =>
        {
            // Be番号
            var inputBeText = this.FindControl<TextBox>("InputBeText");
            if (inputBeText != null)
            {
                config.Be = inputBeText.Text;
            }
            
            // スキップするURL
            var skipBox = this.FindControl<TextBox>("SkipBox");
            config.Skip = skipBox?.Text?
                .Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList() ?? [];
            
            // Maguroチェックボックス
            var replaceCheck = this.FindControl<CheckBox>("ReplaceCheck");
            if (replaceCheck != null)
            {
                config.Maguro = (bool) replaceCheck.IsChecked!;
            }
            
            // 保存先ディレクトリ
            var directoryText = this.FindControl<TextBox>("DirectoryText");
            if (directoryText != null)
            {
                config.Dir = directoryText.Text;
            }
            
            // 現在の最大化状態を保存
            config.Maximize = WindowState == WindowState.Maximized;
            
            // 現在のウインドウサイズを保存 (最大化されている場合は保存しない)
            if (WindowState != WindowState.Normal) return;
            config.WindowSize.Clear();
            config.WindowSize.Add(Width);
            config.WindowSize.Add(Height);
        });
    }
}

