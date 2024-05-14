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


namespace GetBeImage;


public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
  
        var statusListBox = this.FindControl<ListBox>("StatusListBox");
        if (statusListBox == null) return;
        statusListBox.Items.Clear();

        // OSによりListBoxコントロールのプロパティを付加する
        // IsshueはListBoxOptionメソッドを参照する
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
        var leiaUrl = @"http://leia.2ch.net/" + Environment.NewLine + @"http://leia.5ch.net/";
        if (replaceCheck.IsChecked == true) leiaUrl = @"https://maguro.2ch.sc/";
        
        var serverUri = serverCombo.SelectedIndex == 0 ? leiaUrl : @"http://greta.5ch.net/";
        if (!string.IsNullOrEmpty(skipBox.Text)) skipBox.Text += Environment.NewLine; 
        skipBox.Text += serverUri;
    }

    private List<string> _urls = new List<string>();
    private string? DirPath { get; set; } = "";

    private CancellationTokenSource _cts = new CancellationTokenSource();
    
    public async void btnStart_Click(object sender, RoutedEventArgs e)
    {
        // 各プロパティを初期化
        _urls.Clear();
        DirPath = string.Empty;
        _cts = new CancellationTokenSource();
        
        //　ステータスをクリア
        var statusListBox = this.FindControl<ListBox>("StatusListBox");
        if (statusListBox == null) return;
        statusListBox.Items.Clear();
        
        // ステータスのスクロールバーの位置を元に戻す (最上部に移動)
        var scrollViewer = statusListBox.FindDescendantOfType<ScrollViewer>();
        if (scrollViewer == null) return;
        scrollViewer.Offset = new Vector(0, 0);
        
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            statusListBox.InvalidateVisual();
            scrollViewer.InvalidateVisual();
        }, DispatcherPriority.Send);
        
        // BE番号の確認
        var inputBe = this.FindControl<TextBox>("InputBeText");
        if (inputBe == null)    return;

        var beid = inputBe.Text;
        if (string.IsNullOrEmpty(beid))
        {
            await MessageBoxManager.GetMessageBoxStandard("エラー", "Be番号の設定が異常です。",
                ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error).ShowAsync();
            return;
        }
        
        // 保存先ディレクトリの確認
        var directoryText = this.FindControl<TextBox>("DirectoryText");
        if (directoryText == null) return;

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
                return;
            }
        }
        
        if (!Directory.Exists(DirPath))
        {   // 画像を保存するディレクトリが存在しない場合はエラー
            await MessageBoxManager.GetMessageBoxStandard("保存先の確認",
                "ディレクトリが存在していません。",
                ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error).ShowAsync();
            return;
        }
        
        // 画像を保存するディレクトリの書き込み権限を確認
        if (!HasWritePermission(DirPath))
        {   // 書き込み権限がない場合はエラー
            await MessageBoxManager.GetMessageBoxStandard("パーミッションエラー",
                "指定したディレクトリに書き込み権限がありません。",
                ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error).ShowAsync();
            return;
        }
        
        // 実行時に不要なUIを無効化
        //// Be番号の入力テキストUIの無効化
        inputBe.IsEnabled = false;
        
        //// プルダウンの無効化
        var serverCombo = this.FindControl<ComboBox>("ServerCombo");
        if (serverCombo == null) return;
        serverCombo.IsEnabled = false;
        
        //// [スキップ]ボタンの無効化
        var btnSkip = this.FindControl<Button>("BtnSkip");
        if (btnSkip == null) return;
        btnSkip.IsEnabled = false;
        
        //// 無視するサーバテキストUIの無効化
        var skipBox = this.FindControl<TextBox>("SkipBox");
        if (skipBox == null) return;
        skipBox.IsEnabled = false;
        
        //// チェックボックスUIを無効化
        var replaceCheck = this.FindControl<CheckBox>("ReplaceCheck");
        if (replaceCheck == null) return;
        replaceCheck.IsEnabled = false;
        
        //// [保存先]ボタンの無効化
        var btnDirectory = this.FindControl<Button>("BtnDirectory");
        if (btnDirectory == null) return;
        btnDirectory.IsEnabled = false;
        
        //// 保存先テキストUIの無効化
        directoryText.IsEnabled = false;
        
        // [開始]ボタンの無効化
        var button = this.FindControl<Button>("BtnStart");
        if (button is null) return;
        button.Content = @"実行中...";
        button.IsEnabled = false;
        
        // [停止]ボタンの有効化
        var stopBtn = this.FindControl<Button>("BtnStop");
        if (stopBtn == null)    return;
        stopBtn.IsEnabled = true;

        // CancellationTokenSourceの生成
        using (_cts = new CancellationTokenSource())
        {
            // キャンセルトークンの取得
            var token = _cts.Token;
            
            // タスクをキャンセルした場合のアクションを登録
            token.Register(() =>
            {
            });

            // スレッドのURL群を取得
            var urlHandler = new HttpClientHandler() {AllowAutoRedirect = false,};  // 自動リダイレクトしない
            using (var client = new HttpClient(urlHandler))
            {
                client.Timeout = TimeSpan.FromMilliseconds(15000);
                
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        statusListBox.Items.Insert(statusListBox.ItemCount, $"スレッドのURLを取得しています。");
                        statusListBox.InvalidateVisual();
                    }, DispatcherPriority.Send);
                    
                    // 該当するBe番号のスレッド一覧のURLを指定
                    var url = "https://ame.hacca.jp/sasss/log-be2.cgi?i=" + beid;

                    // HTTPリクエストを送信してレスポンスを取得
                    var response = await client.GetAsync(url, token);

                    // 取得に失敗した場合、GetStringAsyncメソッドのときと同じ例外を投げる
                    response.EnsureSuccessStatusCode();

                    // WebサイトからHTMLを取得
                    var htmlWeb = new HtmlWeb();
                    
                    // PreRequestイベントを購読してタイムアウトを設定
                    htmlWeb.PreRequest += (request) =>
                    {
                        // タイムアウトを[mS]で設定 (15[秒])
                        request.Timeout = 15000;
                        return true;
                    };
                    
                    var htmlDocument = htmlWeb.Load(url);

                    // XPathを使用して<a>タグのhref要素のスレッドURLを取得
                    var xpathExpression = "//body//a/@href";
                    var urlInnerText = htmlDocument.DocumentNode.SelectNodes(xpathExpression)?
                        .Select(node => node.GetDirectInnerText())
                        .ToList();
                    if (urlInnerText == null)
                    {
                        await MessageBoxManager.GetMessageBoxStandard("終了",
                            $"スレッドのURLは取得できませんでした。{Environment.NewLine}BE番号が間違っているかもしれません。",
                            ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Warning).ShowAsync();
                        UiEnable();
                        
                        return;
                    }
                    
                    var result = urlInnerText.Where(x => x != string.Empty).ToList().Skip(2).ToList();
                    result     = result.Select(x => x).Where(x => Regex.IsMatch(x, @"^http.*")).ToList();

                    // キャンセルが要求されていたら、OperationCanceledException例外を発生させる
                    token.ThrowIfCancellationRequested();
                    
                    // leiaサーバをmaguroサーバ置換する場合
                    if (replaceCheck.IsChecked != false)
                    {   // URLが"http://leia.2ch.net/" または "http://leia.5ch.net/" の場合は "https://maguro.2ch.sc/"へ置換する
                        result = result.Select(item => item.Contains(@"http://leia.2ch.net/") || item.Contains(@"http://leia.5ch.net/") ?
                                item.Replace(@"http://leia.2ch.net/", @"https://maguro.2ch.sc/")
                                    .Replace(@"http://leia.5ch.net/", @"https://maguro.2ch.sc/") : item)
                            .ToList();
                    }
                    
                    // キャンセルが要求されていたら、OperationCanceledException例外を発生させる
                    token.ThrowIfCancellationRequested();
                    
                    // 特定のサーバにあるスレッドURLを読み飛ばす
                    if (!string.IsNullOrEmpty(skipBox.Text))
                    {
                        var skipUrls = skipBox.Text?.Split(Environment.NewLine).ToList() ?? new List<string>();
                        foreach (var skipUrl in skipUrls)
                        {   // プルダウン等で指定した特定のURLを含む全ての要素を削除
                            var pattern = @"^" + skipUrl;
                            result.RemoveAll(s => Regex.IsMatch(s, pattern));
                        }
                        
                        _urls.AddRange(result);
                    }
                    else
                    {
                        _urls.AddRange(result);
                    }
                    
                    // キャンセルが要求されていたら、OperationCanceledException例外を発生させる
                    token.ThrowIfCancellationRequested();
                    
                    // スレッドが見つからない場合は処理を終了
                    if (_urls.Count == 0)
                    {
                        await MessageBoxManager.GetMessageBoxStandard("終了", $"スレッドが見つかりません。" +
                                                                            $"{Environment.NewLine}" +
                                                                            $"スキップ設定等の確認をお願いします。",
                            ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Stop).ShowAsync();
                        UiEnable();
                
                        return;
                    }
            
                    // URLの並びにおいて、最も過去のスレッドURLを先頭にする (時系列順)
                    _urls.Reverse();
                    
                    // これから読み込むスレッドのURLを表示
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        statusListBox.Items.Insert(statusListBox.ItemCount, $"以下に示すスレッドのURLから画像をダウンロードします。");
                        statusListBox.InvalidateVisual();
                    }, DispatcherPriority.Send);
                    
                    foreach (var threadUrl in _urls)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            statusListBox.Items.Insert(statusListBox.ItemCount, $"{threadUrl}");
                            statusListBox.ScrollIntoView(statusListBox.ItemCount - 1);
                            //statusListBox.ScrollIntoView(statusListBox.Items[statusListBox.Items.Count - 1]);
                            
                            statusListBox.InvalidateVisual();
                        }, DispatcherPriority.Send);
                    }
                }
                catch (HttpRequestException err)
                {
                    await MessageBoxManager.GetMessageBoxStandard("エラー", $"エラーが発生しました。\n\n{err.Message}",
                        ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error).ShowAsync();
                    UiEnable();
                    
                    return;
                }
                catch (OperationCanceledException)
                {
                    await MessageBoxManager.GetMessageBoxStandard("キャンセル", $"処理がキャンセルされました。",
                        ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Stop).ShowAsync();
                    UiEnable();

                    return;
                }
            }
            
            // 画像ファイルの確認およびダウンロード
            var imgHandler = new HttpClientHandler() {AllowAutoRedirect = false,};  // 自動リダイレクトしない
            using (var client = new HttpClient(imgHandler))
            {
                // 画像のダウンロードは時間が掛かるため、タイムアウトを設定しない
                //client.Timeout = TimeSpan.FromMilliseconds(60000);
                
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        statusListBox.Items.Insert(statusListBox.ItemCount, $"スレッドから画像のURLを抽出しています。");
                        statusListBox.ScrollIntoView(statusListBox.ItemCount - 1);
                        //statusListBox.ScrollIntoView(statusListBox.Items[statusListBox.Items.Count - 1]);
                        statusListBox.InvalidateVisual();
                    });
                    
                    foreach (var url in _urls)
                    {
                        // 該当スレッドのURLへHTTPリクエストを送信し、レスポンスを取得
                        var response = await client.GetAsync(url, token);

                        // 取得に失敗した場合、GetStringAsyncメソッドのときと同じ例外を投げる
                        response.EnsureSuccessStatusCode();

                        // WebサイトからHTMLを取得
                        var htmlWeb = new HtmlWeb();
                        
                        // 文字コード : Shift-JIS
                        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);  // need .NET 5 later
                        var enc = Encoding.GetEncoding("Shift_JIS");
                        htmlWeb.OverrideEncoding = enc;
                        
                        // PreRequestイベントを購読してタイムアウトを設定
                        htmlWeb.PreRequest += (request) =>
                        {
                            // タイムアウトを[mS]で設定 (5000[mS])
                            request.Timeout = 5000;
                            return true;
                        };
                        
                        var htmlDocument = htmlWeb.Load(url);
                        
                        // 画像のURLを取得
                        var imageUrls = new List<string>();
                        
                        // imgタグのsrc要素およびaタグの画像を全て検出
                        var xpath = new Func<string, string>(x =>
                            !Regex.IsMatch(x, @"^https://maguro.2ch.sc/.*")
                                ? "//body//div[@id='maincontent']//div//div[@id='thread']//article//section[@class='post-content']" // gretaサーバ
                                : "//body//dl[@class='thread']//dd")(url); 
                        var objNewsDataNodeCollection = htmlDocument.DocumentNode.SelectNodes(xpath);

                        foreach (var objNewsDataNode in objNewsDataNodeCollection)
                        {
                            // レスのimgタグを全て取得
                            var imgTags = objNewsDataNode.SelectNodes("img")?
                                .Select(node => node)
                                .ToList() ?? new ();
                            
                            // レスのimgタグのsrc要素を全て取得
                            //var srcElements = new List<string>();
                            //foreach (var imgTag in imgTags)
                            //{
                            //    srcElements.Add(imgTag.GetAttributeValue("src", ""));
                            //}
                            var srcElements = imgTags.Select(tag => tag.GetAttributeValue("src", "")).ToList();
                            
                            // src要素からPNG形式, JPG(JPEG)形式, GIF形式のみを抽出
                            srcElements = srcElements.Select(x => x).Where(x => Regex.IsMatch(x, @"(.*.png|.*.jpg|.*.jpeg|.*.gif)")).ToList();
                            
                            // src要素から以下に示すURLを変換
                            // "sssp://o.8ch.net/"  -->  "https://o.8ch.net/"
                            // "//o.8ch.net/"  -->  "https://o.8ch.net/"
                            // "//o.5ch.net/"  -->  "https://o.5ch.net/"
                            // "o.8ch.net/"  -->  "o.5ch.net/"
                            srcElements = srcElements.Select(x => 
                                        Regex.IsMatch(x, @"(sssp://o.8ch.net/.*.png|sssp://o.8ch.net/.*.jpg|sssp://o.8ch.net/.*.jpeg|sssp://o.8ch.net/.*.gif)") ?
                                        x.Replace(@"sssp://o.8ch.net/", @"https://o.5ch.net/") : x)
                                .ToList();
                            
                            srcElements = srcElements.Select(x => 
                                    Regex.IsMatch(x, @"(^//o.8ch.net/.*.png|^//o.8ch.net/.*.jpg|^//o.8ch.net/.*.jpeg|^//o.8ch.net/.*.gif)") ?
                                        x.Replace(@"//o.8ch.net/", @"https://o.5ch.net/") : x)
                                .ToList();
                            
                            srcElements = srcElements.Select(x => 
                                    Regex.IsMatch(x, @"(^//o.5ch.net/.*.png|^//o.5ch.net/.*.jpg|^//o.5ch.net/.*.jpeg|^//o.5ch.net/.*.gif)") ?
                                        x.Replace(@"//o.5ch.net/", @"https://o.5ch.net/") : x)
                                .ToList();
                            
                            srcElements = srcElements.Select(x => 
                                    Regex.IsMatch(x, @"(o.8ch.net/.*.png|o.8ch.net/.*.jpg|o.8ch.net/.*.jpeg|o.8ch.net/.*.gif)") ?
                                        x.Replace(@"o.8ch.net/", @"o.5ch.net/") : x)
                                .ToList();
                            
                            srcElements = srcElements.Select(x => 
                                    Regex.IsMatch(x, @"(http://o.5ch.net/.*.png|http://o.5ch.net/.*.jpg|http://o.5ch.net/.*.jpeg|http://o.5ch.net/.*.gif)") ?
                                        x.Replace(@"http://o.5ch.net/", @"https://o.5ch.net/") : x)
                                .ToList();
                            
                            imageUrls.AddRange(srcElements);
                            
                            // レスのaタグを全て取得
                            var aTags = objNewsDataNode.SelectNodes("a")?
                                .Select(node => node)
                                .ToList() ?? new ();
                            
                            // レスのaタグの値を全て取得
                            var hrefElements = aTags.Select(tag => tag.InnerText).ToList();
                            
                            // href要素からPNG形式, JPG(JPEG)形式, GIF形式のみを抽出
                            hrefElements = hrefElements.Select(x => x).Where(x => Regex.IsMatch(x, @"(.*.png|.*.jpg|.*.jpeg|.*.gif)")).ToList();
                            
                            // href要素から以下に示すURLを変換
                            // "sssp://o.8ch.net/"  -->  "https://o.8ch.net/"
                            // "//o.8ch.net/"  -->  "https://o.8ch.net/"
                            // "//o.5ch.net/"  -->  "https://o.5ch.net/"
                            // "o.8ch.net/"  -->  "o.5ch.net/"
                            hrefElements = hrefElements.Select(x => 
                                        Regex.IsMatch(x, @"(sssp://o.8ch.net/.*.png|sssp://o.8ch.net/.*.jpg|sssp://o.8ch.net/.*.jpeg|sssp://o.8ch.net/.*.gif)") ?
                                        x.Replace(@"sssp://o.8ch.net/", @"https://o.5ch.net/") : x)
                                .ToList();
                            
                            hrefElements = hrefElements.Select(x => 
                                    Regex.IsMatch(x, @"(^//o.8ch.net/.*.png|^//o.8ch.net/.*.jpg|^//o.8ch.net/.*.jpeg|^//o.8ch.net/.*.gif)") ?
                                        x.Replace(@"//o.8ch.net/", @"https://o.5ch.net/") : x)
                                .ToList();
                            
                            hrefElements = hrefElements.Select(x => 
                                    Regex.IsMatch(x, @"(^//o.5ch.net/.*.png|^//o.5ch.net/.*.jpg|^//o.5ch.net/.*.jpeg|^//o.5ch.net/.*.gif)") ?
                                        x.Replace(@"//o.5ch.net/", @"https://o.5ch.net/") : x)
                                .ToList();
                            
                            hrefElements = hrefElements.Select(x => 
                                    Regex.IsMatch(x, @"(o.8ch.net/.*.png|o.8ch.net/.*.jpg|o.8ch.net/.*.jpeg|o.8ch.net/.*.gif)") ?
                                        x.Replace(@"o.8ch.net/", @"o.5ch.net/") : x)
                                .ToList();
                            
                            hrefElements = hrefElements.Select(x => 
                                    Regex.IsMatch(x, @"(http://o.5ch.net/.*.png|http://o.5ch.net/.*.jpg|http://o.5ch.net/.*.jpeg|http://o.5ch.net/.*.gif)") ?
                                        x.Replace(@"http://o.5ch.net/", @"https://o.5ch.net/") : x)
                                .ToList();
                            
                            imageUrls.AddRange(hrefElements);
                        }
                        
                        // XPathを使用してスレッドに書き込まれている全てのレス内容を取得
                        /*var xpathExpression = new Func<string, string>(x =>
                            !Regex.IsMatch(x, @"^https://maguro.2ch.sc/.*")
                                ? "//article//section[@class='post-content']//a[contains(., '.jpg') or contains(., '.png') or contains(., '.gif')]/@href"
                                : "//body//dl[@class='thread']/dd/a[contains(., '.jpg') or contains(., '.png') or contains(., '.gif')]/@href")(url);*/
                        var xpathExpression = new Func<string, string>(x =>
                            !Regex.IsMatch(x, @"^https://maguro.2ch.sc/.*")
                                ? "//body//div[@id='maincontent']//div//div[@id='thread']//article//section[@class='post-content']//text()" // gretaサーバ
                                : "//body//dl[@class='thread']//dd//text()")(url);                                                          // maguroサーバ
                        
                        var allResponses = htmlDocument.DocumentNode.SelectNodes(xpathExpression)?
                            .Select(node => node.GetDirectInnerText())
                            .ToList() ?? new List<string>();
                        
                        foreach (var res in allResponses)
                        {
                            var resList = res.Split(Environment.NewLine).ToList();
                            var imgList = resList.Select(x => x).Where(x => Regex.IsMatch(x, @"(.*.png|.*.jpg|.*.jpeg|.*.gif)")).ToList();
                            
                            // "sssp://o.8ch.net/"  -->  "https://o.8ch.net/"
                            // "//o.8ch.net/"  -->  "https://o.8ch.net/"
                            // "//o.5ch.net/"  -->  "https://o.5ch.net/"
                            // "o.8ch.net/"  -->  "o.5ch.net/"
                            imgList = imgList.Select(x =>
                                Regex.IsMatch(x, @"(sssp://o.8ch.net/.*.png|sssp://o.8ch.net/.*.jpg|sssp://o.8ch.net/.*.gif)") ?
                                    x.Replace(@"sssp://o.8ch.net/", @"https://o.5ch.net/") : x)
                                .ToList();
                            
                            imgList = imgList.Select(x => 
                                    Regex.IsMatch(x, @"(^//o.8ch.net/.*.png|^//o.8ch.net/.*.jpg|^//o.8ch.net/.*.jpeg|^//o.8ch.net/.*.gif)") ?
                                        x.Replace(@"//o.8ch.net/", @"https://o.5ch.net/") : x)
                                .ToList();
                            
                            imgList = imgList.Select(x => 
                                    Regex.IsMatch(x, @"(^//o.5ch.net/.*.png|^//o.5ch.net/.*.jpg|^//o.5ch.net/.*.jpeg|^//o.5ch.net/.*.gif)") ?
                                        x.Replace(@"//o.5ch.net/", @"https://o.5ch.net/") : x)
                                .ToList();
                            
                            imgList = imgList.Select(x => 
                                    Regex.IsMatch(x, @"(o.8ch.net/.*.png|o.8ch.net/.*.jpg|o.8ch.net/.*.jpeg|o.8ch.net/.*.gif)") ?
                                        x.Replace(@"o.8ch.net/", @"o.5ch.net/") : x)
                                .ToList();
                            
                            imgList = imgList.Select(x => 
                                    Regex.IsMatch(x, @"(http://o.5ch.net/.*.png|http://o.5ch.net/.*.jpg|http://o.5ch.net/.*.jpeg|http://o.5ch.net/.*.gif)") ?
                                        x.Replace(@"http://o.5ch.net/", @"https://o.5ch.net/") : x)
                                .ToList();
                            
                            imageUrls.AddRange(imgList);
                        }
                        
                        // 重複している画像のURLを削除
                        imageUrls = imageUrls.Distinct().ToList();

                        // 取得したURLから画像を保存
                        if (imageUrls.Count != 0)
                        {
                            foreach (var imageUrl in imageUrls)
                            {
                                // Beアイコンの画像を無視する
                                if (string.IsNullOrEmpty(imageUrl)) continue;
                                if (imageUrl.Contains("//img.5ch.net/ico/")) continue;

                                // 画像のURLの生存を確認
                                if (IsValidUrl(imageUrl))
                                {
                                    // 画像ファイル名
                                    var fileName =
                                        new string(imageUrl.Reverse().TakeWhile(c => c != '/').Reverse().ToArray());
                                    if (!string.IsNullOrEmpty(fileName))
                                    {
                                        var decImageUrl = System.Net.WebUtility.UrlDecode(imageUrl);
                                        fileName = System.Net.WebUtility.UrlDecode(fileName);
                                        var (bResult, errMsg) = await DownloadImageAsync(decImageUrl, Path.Combine(this.DirPath, fileName));
                                        
                                        // ダウンロードした画像ファイルのURLを表示
                                        var statusMsg = bResult ? $"ダウンロード完了 : {decImageUrl}" : $"ダウンロード失敗 : {decImageUrl}\t{errMsg}";
                                        
                                        await Dispatcher.UIThread.InvokeAsync(() =>
                                        {
                                            statusListBox.Items.Insert(statusListBox.ItemCount, statusMsg);
                                            statusListBox.ScrollIntoView(statusListBox.ItemCount - 1);
                                            //statusListBox.ScrollIntoView(statusListBox.Items[statusListBox.Items.Count - 1]);
                                            statusListBox.InvalidateVisual();
                                        }, DispatcherPriority.Send);
                                    }
                                }
                                
                                // キャンセルが要求されていたら、OperationCanceledException例外を発生させる
                                token.ThrowIfCancellationRequested();
                            }
                        }
                    }

                    // Character Encoding : Shift-JIS
                    // Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);  // need .NET 5 later
                    // var enc = Encoding.GetEncoding("Shift_JIS");

                    // 文字コードを指定してストリームから読み込む
                    // String hoge = "";
                    // using (var stream = (await response.Content.ReadAsStreamAsync()))
                    // using (var reader = (new StreamReader(stream, enc, true)) as TextReader)
                    // {
                    //     hoge = await reader.ReadToEndAsync();
                    // }
                    //
                    // // .jpg、.png、.gif拡張子がある画像のURLを抽出
                    // var imageUrls = htmlDocument.DocumentNode
                    //     .Descendants()
                    //     .Where(node =>
                    //     {
                    //         var src = node.Attributes["src"]?.Value;
                    //         var style = node.Attributes["style"]?.Value;
                    //         return src != null && (src.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    //                                src.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    //                                src.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                    //                                (style != null && (style.Contains(".jpg") || style.Contains(".png") || style.Contains(".gif"))));
                    //     })
                    //     .Select(node =>
                    //     {
                    //         var src = node.Attributes["src"]?.Value;
                    //         if (src != null)　return src;
                    //
                    //         var style = node.Attributes["style"]?.Value;
                    //         // ここでstyle属性からURLを抽出する処理を追加する
                    //
                    //         return null;
                    //     })
                    //     .Where(url => url != null);
                    //
                    // foreach (var imageUrl in imageUrls)
                    // {
                    // }
                }
                catch (HttpRequestException err)
                {
                    // ダウンロードに失敗した画像ファイルのURLを表示
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        statusListBox.Items.Insert(statusListBox.ItemCount, $"ダウンロード失敗 : {err.Message}");
                        statusListBox.ScrollIntoView(statusListBox.ItemCount - 1);
                        statusListBox.InvalidateVisual();
                    }, DispatcherPriority.Send);
                }
                catch (OperationCanceledException)
                {
                    await MessageBoxManager.GetMessageBoxStandard("キャンセル", $"処理がキャンセルされました。",
                        ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Stop).ShowAsync();
                    UiEnable();
                }
            }
        }

        UiEnable();
    }
    
    public void btnStop_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
    }
    
    // 画像の保存
    private static async Task<(bool, string)> DownloadImageAsync(string url, string destinationPath)
    {
        var urlHandler = Regex.IsMatch(url, @"(i.imgur.com/|imgur.com/a/)")
            ? new HttpClientHandler() {AllowAutoRedirect = true,}   // 自動リダイレクトする
            : new HttpClientHandler() {AllowAutoRedirect = false,}; // 自動リダイレクトしない
        
        using (var client = new HttpClient(urlHandler))
        {
            try
            {
                // HTTP GETリクエストを送信
                var response = await client.GetAsync(url);
                
                // imgurにある画像ファイルの場合はリダイレクト
                if (urlHandler.AllowAutoRedirect)
                {
                    // リダイレクト先の最終的な画像のURLを取得
                    var finalUrl = response.RequestMessage?.RequestUri?.ToString();
                    if (string.IsNullOrEmpty(finalUrl)) return (false, $"リダイレクト先の画像ファイルのURLが不明です。");
                    
                    // リダイレクト先の画像ファイル名を取得
                    var fileName = new string(finalUrl.Reverse().TakeWhile(c => c != '/').Reverse().ToArray());

                    // 画像ファイルの削除の可否
                    var bRemoved = Regex.IsMatch(fileName, @"(removed.png|removed.jpg|removed.jpeg|removed.gif)");
                    
                    // 削除されている場合はエラー
                    if (bRemoved) return (false, $"画像ファイルは削除されています。");
                }

                // リクエストを確認
                response.EnsureSuccessStatusCode();

                // 画像ファイルを読み込む
                var content = await response.Content.ReadAsByteArrayAsync();

                // 画像ファイルを指定したディレクトリに書き込む
                await File.WriteAllBytesAsync(destinationPath, content);
            }
            catch (HttpRequestException err)
            {
                return (false, err.Message);
            }
        }

        return (true, string.Empty);
    }

    private static bool IsValidUrl(string url)
    {
        Uri? uriResult;
        return Uri.TryCreate(url, UriKind.Absolute, out uriResult) &&
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
        if (inputBeText == null)    return;
            
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
    
    private void OpenAboutDialog_Click(object? sender, PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(e);
        
        // アバウトダイアログを表示
        var aboutDialog = new AboutDialog();
        {
            aboutDialog.Show();
        }
    }
#pragma warning disable 0414
    
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
    // Isshue :
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
        
        //// [スキップ]ボタンの有効化
        var btnSkip = this.FindControl<Button>("BtnSkip");
        if (btnSkip == null) return;
        btnSkip.IsEnabled = true;
        
        //// 無視するサーバテキストUIの有効化
        var skipBox = this.FindControl<TextBox>("SkipBox");
        if (skipBox == null) return;
        skipBox.IsEnabled = true;
        
        //// チェックボックUIの有効化
        var replaceCheck = this.FindControl<CheckBox>("ReplaceCheck");
        if (replaceCheck == null) return;
        replaceCheck.IsEnabled = true;
        
        //// [保存先]ボタンの有効化
        var btnDirectory = this.FindControl<Button>("BtnDirectory");
        if (btnDirectory == null) return;
        btnDirectory.IsEnabled = true;
        
        //// 保存先テキストUIの有効化
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
}

