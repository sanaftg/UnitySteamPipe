# UnitySteamPipe

Unity EditorからWindows/macOSプレイヤーのビルド、SteamPipeへのアップロード、macOSの署名・公証を行うUPMパッケージです。

## Installation

Unityの `Window > Package Manager` から **Add package from git URL...** を選び、次を入力します。

```text
https://github.com/sanaftg/UnitySteamPipe.git
```

または `Packages/manifest.json` に追加します。

```json
"net.f-tg.unity-steampipe": "https://github.com/sanaftg/UnitySteamPipe.git"
```

## Usage

1. Unityメニューの `Tools > SteamPipe` を開きます。
2. App ID、Depot ID、Steamログイン名、Steamworks SDKパス、ビルド出力先を設定します。
3. PlayerのビルドまたはSteamPipeアップロードを実行します。

設定値はUnityの `EditorPrefs` に保存されます。Steamのパスワードは保存しません。

### LAN Remote

同じLANでSteamPipeウィンドウを開いている別端末へ、Git Fetch、Git Pull、Full Pipelineを依頼できます。Full Pipelineはビルドからアップロードまでを実行し、macOSでは署名・公証を含み、Windowsではそれらを自動的にスキップします。

1. 受信側で `Allow Remote Requests` を有効にします。
2. 両端末の `Shared Key` を同じ値に設定します。
3. 送信側で `Refresh` を押し、対象端末を選んで操作を送信します。

受信は初期状態では無効です。探索にはUDP `43817`、操作要求には既定でTCP `43818`を使用します。OSのファイアウォールでUnity Editorのローカルネットワーク通信を許可してください。

LAN経由のGit操作は非対話実行のためGitHub SSH認証を使用します。各受信端末でSSHキーをGitHubへ登録し、`ssh -T git@github.com`が成功する状態にしてください。リポジトリの`origin`設定自体は変更しません。

選択中の端末のSteamPipeログは`Remote Log`へ表示され、実行中は1秒間隔で更新されます。ログ取得にもShared Key認証が必要です。 ローカルログとリモートログは高さ固定のスクロール欄で、追加された最新行へ自動追従します。

Git Pull成功後はUnityのAssetDatabaseを強制更新します。Full Pipelineはアセットインポートとスクリプトコンパイルの完了を待ってからビルドを開始します。

### macOS

macOS版ではDeveloper ID Application証明書と、`notarytool store-credentials` で作成したKeychain Profileが必要です。付属の `Editor/Steam/Steam.entitlements` を初期値として使用します。

## Requirements

- Unity 2022.3 or later
- Steamworks SDK（SteamPipeアップロード時）
- macOS signing/notarization tools（macOS版の署名・公証時）

## License

MIT License
