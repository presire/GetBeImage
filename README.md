# GetBeImage
<br>

URL : https://github.com/presire/GetBeImage  
<br>

# はじめに  
GetBeImageは、<a href="https://ame.hacca.jp">薄荷飴</a>において、BE番号が立てた全てのスレッドに貼られた画像を一括取得するソフトウェアです。  
<br>

**※注意**  
**5chのleiaサーバがダウンしている場合は、leiaサーバをmaguroサーバに置き換える必要があります。**  
<br>

SLE、Debian GNU/Linux、Manjaro ARM、Windowsにて動作確認をしております。  
<br>
<br>

# 1. ビルドに必要なライブラリをインストール  
<br>

* .NET 8以降のランタイムまたはSDK  
  https://dotnet.microsoft.com/ja-jp/download/dotnet  
<br>
<br>

# 2. ビルドを行う場合

実行バイナリファイルは、execディレクトリ内の以下に示すディレクトリに存在します。  
そのため、ソースコードから実行バイナリファイルを生成する必要はありません。  
* Linux-x64ディレクトリ（Linux向け）  
* Windows-x64ディレクトリ（Windows向け）  
<br>

もし、ソースコードから実行バイナリファイルを生成する場合は、以下の手順に従います。  
<br>

まず、GitHubからGetBeImageのソースコードをダウンロードします。  

    git clone https://github.com/presire/GetBeImage.git GetBeImage  

    cd GetBeImage  
<br>

次に、GetBeImageをソースコードからビルドします。  
ビルドするには、<code>dotnet</code>コマンドを使用します。  
<br>

    # Linux x64向け  
    dotnet publish -c:Release -r:linux-x64 -p:PublishReadyToRun=false -p:PublishSingleFile=true --self-contained:false  
    
    # Linux Arch64向け (PinePhone、Raspberry Pi等)  
    dotnet publish -c:Release -r:linux-arm64 -p:PublishReadyToRun=false -p:PublishSingleFile=true --self-contained:false  
    
    # Windows x64向け  
    dotnet publish -c:Release -r:win-x64 -p:PublishReadyToRun=false -p:PublishSingleFile=true --self-contained:false  
<br>
<br>

# 3. 設定ファイル  

本ソフトウェアの設定ファイルは、実行バイナリと同階層のディレクトリにGetBeImage.jsonファイルとして保存されます。  
メイン画面の[設定を保存]ボタンを押下することにより、現在の設定を保存することが可能です。  

* Be  
  デフォルト値 : 空欄  
  検索するBe番号が保存されます。  
  <br>
* Dir  
  デフォルト値 : 空欄  
  ダウンロードした画像を保存するディレクトリのパスが保存されます。  
  <br>
* Maguro  
  デフォルト値 : <code>true</code>  
  スレッドのURLにおいて、leiaサーバ (leia.2ch, leia.5ch) をmaguro.2ch.scドメインに置き換えます。  
  <br>
  leiaサーバはダウンしていることが多いため、この設定は<code>true</code>となっています。  
  <br>
* Skip  
  デフォルト値 : 空欄  
  検索をスキップするスレッドURLのドメインが保存されます。  
  <br>
* Maximize  
  デフォルト値 : <code>false</code>  
  ウインドウが最大化されているかどうかが保存されます。  
  <br>
* WindowSize  
  デフォルト値 : <code>["1024", "768"]</code>  
  ウインドウのサイズが保存されます。  
  <br>
