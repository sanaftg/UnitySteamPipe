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

### macOS

macOS版ではDeveloper ID Application証明書と、`notarytool store-credentials` で作成したKeychain Profileが必要です。付属の `Editor/Steam/Steam.entitlements` を初期値として使用します。

## Requirements

- Unity 2022.3 or later
- Steamworks SDK（SteamPipeアップロード時）
- macOS signing/notarization tools（macOS版の署名・公証時）

## License

MIT License
